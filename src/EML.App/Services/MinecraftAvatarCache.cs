using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace EML.App.Services;

/// <summary>从 Mojang 公开档案与纹理主机读取皮肤；展示失败只回退，不触碰账号凭证。</summary>
public sealed class MinecraftAvatarCache : IMinecraftAvatarCache, IDisposable
{
    private const int MaximumProfileBytes = 64 * 1024;
    private const int MaximumSkinBytes = 1024 * 1024;
    private const int MaximumAvatarBytes = 32 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(1);
    private readonly string _root;
    private readonly Func<byte[], byte[]?> _decodeSkin;
    private readonly TimeProvider _clock;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _lifecycle = new();
    private readonly Dictionary<string, Entry> _memory = new(StringComparer.Ordinal);
    private bool _disposed;
    private int _operations;

    /// <summary>皮肤解码器由 WPF 宿主提供；传输可替换用于无网络测试，外部 handler 不归本服务释放。</summary>
    public MinecraftAvatarCache(string launcherRoot, Func<byte[], byte[]?> decodeSkin,
        HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(decodeSkin);
        _root = Path.Combine(Path.GetFullPath(launcherRoot), ".eml-runtime", "account-cache", "avatars");
        _decodeSkin = decodeSkin; _clock = clock ?? TimeProvider.System;
        if (handler is HttpClientHandler http) { http.AllowAutoRedirect = false; http.UseCookies = false; }
        if (handler is SocketsHttpHandler sockets) { sockets.AllowAutoRedirect = false; sockets.UseCookies = false; }
        _client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }, handler is null)
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>离线与无效 UUID 不联网；有效缓存优先，刷新失败时保留已验证的旧头像。</summary>
    public async Task<byte[]?> GetAsync(string? accountId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!Guid.TryParse(accountId, out var uuid) || uuid == Guid.Empty) return null;
        CancellationToken lifetime;
        lock (_lifecycle)
        {
            if (_disposed) return null;
            ++_operations; lifetime = _lifetime.Token;
        }
        var entered = false;
        byte[]? fallback = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
            var key = uuid.ToString("N");
            var now = _clock.GetUtcNow();
            if (_memory.TryGetValue(key, out var known))
            {
                fallback = known.Avatar;
                if (known.RefreshAfter > now) return Copy(fallback);
            }
            var destination = Path.Combine(_root, key + ".png");
            try
            {
                EnsureNoLinks(destination);
                var info = new FileInfo(destination);
                if (info.Exists && info.Length is > 0 and <= MaximumSkinBytes)
                {
                    var cached = await ReadFileAsync(destination, deadline.Token).ConfigureAwait(false);
                    var avatar = Decode(cached);
                    if (avatar is not null)
                    {
                        fallback = avatar;
                        var age = now - info.LastWriteTimeUtc;
                        if (age >= TimeSpan.Zero && age < CacheLifetime)
                        {
                            Remember(key, avatar, now + CacheLifetime - age);
                            return Copy(avatar);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* 缓存损坏或不可读时仍可查询公开皮肤，不覆盖其他文件。 */ }

            try
            {
                var profile = await DownloadAsync(new Uri("https://sessionserver.mojang.com/session/minecraft/profile/" + key),
                    MaximumProfileBytes, deadline.Token).ConfigureAwait(false);
                var texture = TextureAddress(profile, uuid);
                if (texture is null) throw new InvalidDataException("公开档案没有可用的官方皮肤。");
                var skin = await DownloadAsync(texture, MaximumSkinBytes, deadline.Token).ConfigureAwait(false);
                var avatar = Decode(skin);
                if (avatar is null) throw new InvalidDataException("皮肤无法安全解码。");
                deadline.Token.ThrowIfCancellationRequested();
                // 缓存不可写不应隐藏已经下载并安全解码的头像。
                try { await SaveSkinAsync(destination, skin, deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { }
                Remember(key, avatar, _clock.GetUtcNow() + CacheLifetime);
                return Copy(avatar);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                Remember(key, fallback, _clock.GetUtcNow() + FailureLifetime);
                return Copy(fallback);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return Copy(fallback); }
        finally
        {
            if (entered) _gate.Release();
            lock (_lifecycle)
            {
                --_operations;
                if (_disposed && _operations == 0) DisposeResources();
            }
        }
    }

    /// <summary>每次读取都限制实际流长度，不信任服务器或本机缓存声明的文件大小。</summary>
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > maximum) throw new InvalidDataException("皮肤数据超过大小限制。");
            buffer.Write(chunk, 0, count);
        }
    }

    /// <summary>只请求调用方内部构造并验证的官方地址，禁止自动跳转和登录凭据。</summary>
    private async Task<byte[]> DownloadAsync(Uri address, int maximum, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd("EML/1.0 (player-avatars)");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("皮肤数据超过大小限制。");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await ReadBoundedAsync(stream, maximum, token).ConfigureAwait(false);
    }

    /// <summary>档案必须属于请求的 UUID；只信任官方纹理主机及固定哈希路径，不执行任意 URI。</summary>
    private static Uri? TextureAddress(byte[] profileBytes, Guid expected)
    {
        using var profile = JsonDocument.Parse(profileBytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = profile.RootElement;
        if (!root.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out var actual) || actual != expected ||
            !root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array || properties.GetArrayLength() > 64)
            return null;
        string? value = null;
        foreach (var property in properties.EnumerateArray())
            if (property.TryGetProperty("name", out var name) && name.GetString() == "textures")
            {
                if (value is not null || !property.TryGetProperty("value", out var contents)) return null;
                value = contents.GetString();
            }
        if (string.IsNullOrEmpty(value) || value.Length > MaximumProfileBytes) return null;
        using var textures = JsonDocument.Parse(Convert.FromBase64String(value), new JsonDocumentOptions { MaxDepth = 16 });
        var textureRoot = textures.RootElement;
        if (textureRoot.TryGetProperty("profileId", out var textureId) &&
            (!Guid.TryParse(textureId.GetString(), out var textureUuid) || textureUuid != expected)) return null;
        if (!textureRoot.TryGetProperty("textures", out var textureMap) || !textureMap.TryGetProperty("SKIN", out var skin) ||
            !skin.TryGetProperty("url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https") || !address.IsDefaultPort || address.UserInfo.Length != 0 ||
            address.Host != "textures.minecraft.net" || address.Query.Length != 0 || address.Fragment.Length != 0)
            return null;
        const string prefix = "/texture/";
        var path = address.AbsolutePath;
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length != prefix.Length + 64 ||
            path.AsSpan(prefix.Length).ContainsAnyExcept("0123456789abcdefABCDEF")) return null;
        return new Uri("https://textures.minecraft.net" + path);
    }

    /// <summary>可信解码器拒绝坏图；输出另设小上限，避免错误实现占用过多展示内存。</summary>
    private byte[]? Decode(byte[] skin)
    {
        var avatar = _decodeSkin(skin);
        return avatar is { Length: > 0 and <= MaximumAvatarBytes } ? avatar : null;
    }

    /// <summary>缓存原始皮肤，每次复用仍经过同一尺寸和解码校验。</summary>
    private static async Task<byte[]> ReadFileAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        return await ReadBoundedAsync(input, MaximumSkinBytes, token).ConfigureAwait(false);
    }

    /// <summary>缓存只写入固定 UUID 文件名，先写独立临时文件，再原子替换自己的缓存。</summary>
    private async Task SaveSkinAsync(string destination, byte[] bytes, CancellationToken token)
    {
        EnsureNoLinks(destination); Directory.CreateDirectory(_root); EnsureNoLinks(destination);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); EnsureNoLinks(destination);
            File.Move(temporary, destination, overwrite: true);
            File.SetLastWriteTimeUtc(destination, _clock.GetUtcNow().UtcDateTime);
        }
        finally
        {
            try { EnsureNoLinks(temporary); File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>内存条目有上限，失败也短暂记忆，避免列表重建反复请求同一账号。</summary>
    private void Remember(string key, byte[]? avatar, DateTimeOffset refreshAfter)
    {
        if (_memory.Count >= 128 && !_memory.ContainsKey(key)) _memory.Clear();
        _memory[key] = new(Copy(avatar), refreshAfter);
    }

    /// <summary>返回独立数组，调用者修改图片缓冲不会污染其他位置共享的缓存。</summary>
    private static byte[]? Copy(byte[]? bytes) => bytes?.ToArray();

    /// <summary>缓存及祖先不得通过链接重定向到实例、凭据或任意其他本机文件。</summary>
    private static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("头像缓存路径不能使用链接。");
    }

    /// <summary>先取消在途展示请求，最后一个请求退出后再释放连接与信号量。</summary>
    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true; _lifetime.Cancel();
            if (_operations == 0) DisposeResources();
        }
    }

    /// <summary>只在生命周期锁内且无在途请求时执行，避免 Release 与 Dispose 竞态。</summary>
    private void DisposeResources() { _client.Dispose(); _gate.Dispose(); _lifetime.Dispose(); _memory.Clear(); }

    private sealed record Entry(byte[]? Avatar, DateTimeOffset RefreshAfter);
}
