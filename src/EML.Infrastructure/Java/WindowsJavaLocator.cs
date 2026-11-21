using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text.RegularExpressions;
using EML.Core.Java;
using EML.Infrastructure.Minecraft.Importing;
using Microsoft.Win32;

namespace EML.Infrastructure.Java;

/// <summary>发现 Windows 已安装和常见位置中的 Java；不扫描整块硬盘。</summary>
public sealed class WindowsJavaLocator : IJavaLocator
{
    private readonly string? _launcherRoot;
    private readonly string? _managedRuntimeRoot;
    private static readonly string[] JavaVendorDirectories = ["Java", "Eclipse Adoptium", "AdoptOpenJDK", "Microsoft", "Amazon Corretto", "Zulu", "BellSoft", "Semeru"];
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private IReadOnlyList<JavaInstallation>? _lastKnownInstallations;

    /// <summary>提供上次已完成的不可变检测快照，启动模块仍需实时查询选中的 Java。</summary>
    internal IReadOnlyList<JavaInstallation>? LastKnownInstallations => Volatile.Read(ref _lastKnownInstallations);

    /// <summary>启动器数据目录不是搜索根，仅允许其中已发布的 EML Java。</summary>
    public WindowsJavaLocator(string? launcherRoot = null, string? managedRuntimeRoot = null)
    {
        _launcherRoot = Normalize(launcherRoot ?? AppContext.BaseDirectory);
        _managedRuntimeRoot = Normalize(managedRuntimeRoot);
    }

    /// <summary>后台发现并验证 Java，去重后按主版本降序返回；最多同时验证四项。</summary>
    public async Task<IReadOnlyList<JavaInstallation>> FindAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("此检测器用于 Windows。");

        // 目录枚举和注册表访问也放到后台，避免阻塞 WPF 界面。
        var candidates = await Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("此检测器用于 Windows。");
            return Discover(cancellationToken);
        }, cancellationToken);
        var found = new ConcurrentDictionary<string, JavaInstallation>(PathComparer);
        await Parallel.ForEachAsync(candidates, new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken
        }, async (path, token) =>
        {
            if (!CanProbeAutomatically(path)) return;
            var installation = await InspectAsync(path, token);
            if (installation is not null && CanProbeAutomatically(installation.ExecutablePath))
                found.TryAdd(installation.ExecutablePath, installation);
        });

        var installations = found.Values
            .OrderByDescending(item => MajorVersion(item.Version))
            .ThenBy(item => item.ExecutablePath, PathComparer)
            .ToArray();
        Volatile.Write(ref _lastKnownInstallations, installations);
        return installations;
    }

    /// <summary>从环境变量、注册表和常见目录收集候选 java.exe，尚不验证能否运行。</summary>
    [SupportedOSPlatform("windows")]
    private string[] Discover(CancellationToken token)
    {
        var executables = new HashSet<string>(PathComparer);
        var roots = new Dictionary<string, int>(PathComparer);

        // 合并有效搜索目录；同一路径重复出现时保留较大的搜索深度。
        void AddRoot(string? path, int depth)
        {
            var normalized = Normalize(path);
            if (normalized is not null && Directory.Exists(normalized))
                roots[normalized] = Math.Max(roots.GetValueOrDefault(normalized), depth);
        }

        void AddJavaHome(string? path)
        {
            if (Normalize(path) is { } home) AddExecutable(Path.Combine(home, "bin"), executables);
        }

        foreach (var target in new[] { EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            foreach (var name in new[] { "JAVA_HOME", "JDK_HOME", "JRE_HOME" })
                AddJavaHome(Environment.GetEnvironmentVariable(name, target));

            foreach (var path in (Environment.GetEnvironmentVariable("PATH", target) ?? "")
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                AddExecutable(path, executables);
        }

        // 采用 Java 专用登记，不递归进入其他应用或用户数据目录。
        foreach (var path in RegistryLocations(token))
            AddJavaHome(path);
        foreach (var location in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var directory = Environment.GetFolderPath(location);
            if (directory.Length == 0) continue;
            foreach (var vendor in JavaVendorDirectories) AddRoot(Path.Combine(directory, vendor), 3);
        }
        AddRoot(_managedRuntimeRoot, 2);

        foreach (var (path, depth) in roots)
        {
            token.ThrowIfCancellationRequested();
            ScanRoot(path, depth, executables, token);
        }
        return executables.OrderBy(path => path, PathComparer).ToArray();
    }

    /// <summary>同样检查 PATH 直达项及启动时从旧检测快照取出的候选项。</summary>
    internal bool CanProbeAutomatically(string path)
    {
        if (Normalize(path) is not { } executable || !File.Exists(executable) ||
            IsManagedInstallationStagingPath(executable)) return false;
        if (_managedRuntimeRoot is not null && IsWithin(executable, _managedRuntimeRoot))
        {
            var parts = Path.GetRelativePath(_managedRuntimeRoot, executable).Split(Path.DirectorySeparatorChar);
            if (parts.Length != 3 || !PathComparer.Equals(parts[1], "bin") ||
                !PathComparer.Equals(parts[2], "java.exe")) return false;
            if (!HasNoLinks(executable) || !TemurinJavaRuntimeService.HasMatchingExecutableReceipt(executable, parts[0])) return false;
        }
        else if (_launcherRoot is not null && IsWithin(executable, _launcherRoot)) return false;
        return HasNoLinks(executable);
    }

    private static bool IsWithin(string path, string root) => PathComparer.Equals(path, root) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static bool HasNoLinks(string path)
    {
        try { InstanceImportPaths.EnsureNoLinks(path); return true; }
        catch (Exception ex) when (IsExpected(ex)) { return false; }
    }

    /// <summary>按深度和时间限制搜索目录，跳过无法访问的位置及链接子目录。</summary>
    internal static void ScanRoot(string root, int maxDepth,
        HashSet<string> executables, CancellationToken token)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = new HashSet<string>(PathComparer);
        var clock = Stopwatch.StartNew();
        // 每个来源最多访问 5000 个目录、耗时约 3 秒（不含单次系统 I/O）。
        while (queue.Count > 0 && visited.Count < 5000 && clock.Elapsed < TimeSpan.FromSeconds(3))
        {
            token.ThrowIfCancellationRequested();
            var (path, depth) = queue.Dequeue();
            if (!visited.Add(path) || IsManagedInstallationStagingPath(path)) continue;
            AddExecutable(path, executables);
            if (depth >= maxDepth) continue;
            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                };
                foreach (var child in Directory.EnumerateDirectories(path, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (queue.Count + visited.Count >= 5000 || clock.Elapsed >= TimeSpan.FromSeconds(3))
                        break;
                    queue.Enqueue((child, depth + 1));
                }
            }
            catch (Exception ex) when (IsExpected(ex)) { }
        }
    }

    /// <summary>将指定目录里存在的 java.exe 加入去重集合。</summary>
    internal static void AddExecutable(string? directory, HashSet<string> paths)
    {
        var normalized = Normalize(directory);
        if (normalized is null || IsManagedInstallationStagingPath(normalized)) return;
        // javaw 与 java 属于同一运行时；使用控制台入口可靠读取版本输出。
        var executable = Path.Combine(normalized, "java.exe");
        if (File.Exists(executable)) paths.Add(executable);
    }

    /// <summary>跳过 EML 尚未校验发布的安装目录，包含从父目录、PATH 或直接搜索根进入的情况。</summary>
    private static bool IsManagedInstallationStagingPath(string path)
    {
        // 只匹配安装器保留的精确目录结构，不排除其他位置的用户 Java 或普通 .install-* 目录。
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            var name = directory.Name;
            if (name.StartsWith(".install-", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(name[9..], "N", out _) &&
                string.Equals(directory.Parent?.Name, "java", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(directory.Parent?.Parent?.Name, "runtimes", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>从用户和系统的 32/64 位注册表收集 JavaHome。</summary>
    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> RegistryLocations(CancellationToken token)
    {
        var locations = new List<string>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var javaSoft = root.OpenSubKey(@"SOFTWARE\JavaSoft");
                if (javaSoft is not null) ReadJavaKeys(javaSoft, 4, locations, token);
            }
            catch (Exception ex) when (IsExpected(ex)) { }
        }
        return locations;
    }

    /// <summary>在限定深度内递归读取 JavaSoft 的 JavaHome，跳过无法读取的子键。</summary>
    [SupportedOSPlatform("windows")]
    private static void ReadJavaKeys(RegistryKey key, int remainingDepth,
        List<string> locations, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (key.GetValue("JavaHome") is string home) locations.Add(home);
            if (remainingDepth == 0) return;
            foreach (var name in key.GetSubKeyNames())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var child = key.OpenSubKey(name);
                    if (child is not null) ReadJavaKeys(child, remainingDepth - 1, locations, token);
                }
                catch (Exception ex) when (IsExpected(ex)) { }
            }
        }
        catch (Exception ex) when (IsExpected(ex)) { }
    }

    /// <summary>运行 Java 查询版本与架构；失败或超过五秒返回 null，并清理检测进程。</summary>
    private static async Task<JavaInstallation?> InspectAsync(string path, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-XshowSettings:properties");
        process.StartInfo.ArgumentList.Add("-version");
        // 检测时不执行用户为游戏或其他程序设置的 Java 代理等选项。
        foreach (var name in new[] { "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS" })
            process.StartInfo.Environment.Remove(name);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!process.Start()) return null;
            var stdout = ReadProbeOutputAsync(process.StandardOutput, timeout);
            var stderr = ReadProbeOutputAsync(process.StandardError, timeout);
            // Task.WhenAll 观察两个读取任务和进程退出，避免管道阻塞。
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0) return null;
            return ParseInstallation(path, stdout.Result + "\n" + stderr.Result);
        }
        catch (OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return null; // 单个运行时超时不影响其他结果。
        }
        catch (Exception ex) when (IsExpected(ex) || ex is Win32Exception)
        {
            return null;
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
        }
    }

    private static async Task<string> ReadProbeOutputAsync(StreamReader reader, CancellationTokenSource timeout)
    {
        const int maximumChars = 64 * 1024;
        var buffer = new char[4096];
        var text = new System.Text.StringBuilder();
        while (await reader.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false) is var count && count > 0)
        {
            if (text.Length + count > maximumChars)
            {
                await timeout.CancelAsync().ConfigureAwait(false);
                throw new IOException("Java 版本查询输出超过限制。");
            }
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    /// <summary>解析 Java 输出，优先用 java.home 下的入口合并重复检测结果。</summary>
    internal static JavaInstallation? ParseInstallation(string path, string output)
    {
        var version = ReadProperty(output, "java.version");
        if (string.IsNullOrWhiteSpace(version)) return null;
        var home = Normalize(ReadProperty(output, "java.home"));
        if (home is null) return null;
        var canonical = Path.Combine(home, "bin", "java.exe");
        if (!File.Exists(canonical)) canonical = path;
        // 相同 java.home 的 Oracle 入口、PATH 入口和实际文件合并成一条。
        var arch = ReadProperty(output, "os.arch").ToLowerInvariant();
        var bitness = ReadProperty(output, "sun.arch.data.model");
        var display = arch switch
        {
            "aarch64" or "arm64" => "ARM64（64 位）",
            "amd64" or "x86_64" => "x64（64 位）",
            "x86" or "i386" or "i486" or "i586" or "i686" => "x86（32 位）",
            _ => bitness is "32" or "64" ? $"{bitness} 位（{arch}）" : $"未知（{arch}）"
        };
        return new JavaInstallation(canonical, version, display);
    }

    /// <summary>按属性名读取 Java 输出；找不到时返回空字符串。</summary>
    private static string ReadProperty(string output, string name) =>
        Regex.Match(output, @"^\s*" + Regex.Escape(name) + @"\s*=\s*([^\r\n]+)",
            RegexOptions.Multiline).Groups[1].Value.Trim();

    /// <summary>提取排序用的主版本，例如 1.8 得到 8；解析失败返回 0。</summary>
    private static int MajorVersion(string version)
    {
        var match = Regex.Match(version, @"^(?:1\.)?(\d+)");
        return int.TryParse(match.Groups[1].Value, out var major) ? major : 0;
    }

    /// <summary>展开环境变量并规范化绝对路径；空值、相对路径或非法路径返回 null。</summary>
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return Path.IsPathFullyQualified(expanded) ? Path.GetFullPath(expanded) : null;
        }
        catch (Exception ex) when (IsExpected(ex)) { return null; }
    }

    /// <summary>限定可以跳过的文件、权限和路径异常；其余异常继续上报，避免把所有程序问题都吞掉。</summary>
    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException;
}
