using System.Diagnostics;
using System.Text.RegularExpressions;
using EML.Core.Downloads;

namespace EML.Infrastructure.Downloads;

public sealed partial class HttpFileDownloader : IDownloadCacheMaintenance
{
    internal const long DownloadCacheTargetBytes = 12L * 1024 * 1024 * 1024;
    private static readonly TimeSpan InactiveResumeLifetime = TimeSpan.FromDays(7);
    private static readonly Regex CacheFileName = new(
        @"\A(?<key>sha1-[0-9a-f]{40}|sha256-[0-9a-f]{64}|sha512-[0-9a-f]{128})(?<kind>\.blob(?:\.invalid-[0-9a-f]{32})?|\.part|\.resume\.json)\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private long _nextCacheMaintenance;

    public Task<long> GetCacheSizeAsync(CancellationToken token = default) => Task.Run(() =>
        ReadCache(token).Sum(item => item.Bytes), token);

    public Task<DownloadCacheCleanupResult> ClearCompletedCacheAsync(CancellationToken token = default) =>
        Task.Run(() => CleanCache(0, manual: true, token), token);

    /// <summary>限频整理，失败不影响已成功下载的内容；上限为软目标，不打断活跃下载和近期续传。</summary>
    private async Task MaintainCacheIfDueAsync(CancellationToken token)
    {
        var now = Stopwatch.GetTimestamp();
        var scheduled = Interlocked.Read(ref _nextCacheMaintenance);
        if (scheduled > now || Interlocked.CompareExchange(ref _nextCacheMaintenance,
                now + Stopwatch.Frequency * 60, scheduled) != scheduled) return;
        try { await Task.Run(() => CleanCache(DownloadCacheTargetBytes, manual: false, token), token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { }
    }

    internal DownloadCacheCleanupResult CleanCache(long targetBytes, bool manual, CancellationToken token)
    {
        var files = ReadCache(token);
        var remaining = files.Sum(item => item.Bytes);
        long removed = 0;
        var expiredBefore = DateTime.UtcNow - InactiveResumeLifetime;
        foreach (var item in files.OrderBy(item => item.LastUsedUtc))
        {
            token.ThrowIfCancellationRequested();
            if (item.IsResume ? manual || item.LastUsedUtc >= expiredBefore : !manual && remaining <= targetBytes) continue;
            var lockPath = Path.Combine(_options.CacheDirectory!, item.Key + ".lock");
            EnsureNoLinks(lockPath);
            FileStream entryLock;
            try { entryLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33) { continue; }
            using (entryLock)
            {
                EnsureNoLinks(item.Path);
                var file = new FileInfo(item.Path);
                // 另一个进程在快照之后使用或替换了此对象时，留给下次维护。
                if (!file.Exists || file.LastWriteTimeUtc != item.LastUsedUtc || file.Length != item.Bytes) continue;
                // 续传的片段和元数据共同判断；其中一项近期有变化就不动这一组。
                if (item.IsResume && HasRecentResumeSibling(item, expiredBefore)) continue;
                File.Delete(item.Path);
                remaining -= item.Bytes;
                removed += item.Bytes;
            }
        }
        return new(removed, ReadCache(token).Sum(item => item.Bytes));
    }

    private static bool HasRecentResumeSibling(CacheFile item, DateTime cutoff)
    {
        var sibling = Path.Combine(Path.GetDirectoryName(item.Path)!, item.Key +
            (item.Path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ? ".resume.json" : ".part"));
        EnsureNoLinks(sibling);
        return File.Exists(sibling) && File.GetLastWriteTimeUtc(sibling) >= cutoff;
    }

    private List<CacheFile> ReadCache(CancellationToken token)
    {
        var files = new List<CacheFile>();
        var root = _options.CacheDirectory;
        if (root is null) return files;
        EnsureNoLinks(root);
        if (!Directory.Exists(root)) return files;
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            token.ThrowIfCancellationRequested();
            var match = CacheFileName.Match(file.Name);
            if (!match.Success) continue;
            try
            {
                files.Add(new(file.FullName, match.Groups["key"].Value, file.Length, file.LastWriteTimeUtc,
                    !match.Groups["kind"].Value.StartsWith(".blob", StringComparison.OrdinalIgnoreCase)));
            }
            catch (FileNotFoundException) { } // 并行发布或另一个窗口刚清掉该项。
        }
        return files;
    }

    private sealed record CacheFile(string Path, string Key, long Bytes, DateTime LastUsedUtc, bool IsResume);
}
