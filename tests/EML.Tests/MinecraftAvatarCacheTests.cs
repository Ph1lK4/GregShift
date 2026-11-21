using System.Net;
using System.Text;
using System.Text.Json;
using EML.App.Services;

namespace EML.Tests;

public sealed class MinecraftAvatarCacheTests : IDisposable
{
    private const string Account = "7c4c182faeb34d6ab985f5aa6b9daf1e";
    private static readonly string Texture = "https://textures.minecraft.net/texture/" + new string('a', 64);
    private static readonly byte[] Skin = [1, 2, 3];
    private static readonly byte[] Avatar = [9, 8, 7];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EML-avatars-" + Guid.NewGuid().ToString("N"));
    private string CachePath => Path.Combine(_root, ".eml-runtime", "account-cache", "avatars", Account + ".png");

    /// <summary>只查询 UUID 对应的公开档案与官方纹理，不附带登录、API 或 Cookie 凭证。</summary>
    [Fact]
    public async Task DownloadsPublicProfileAndSkinWithoutCredentials()
    {
        using var transport = new Transport();
        using var cache = new MinecraftAvatarCache(_root, bytes => bytes.SequenceEqual(Skin) ? Avatar : null, transport);

        Assert.Equal(Avatar, await cache.GetAsync(Account));
        Assert.Equal(new[] { "https://sessionserver.mojang.com/session/minecraft/profile/" + Account, Texture }, transport.Addresses);
        Assert.Equal(Skin, await File.ReadAllBytesAsync(CachePath));
        Assert.True(transport.AllRequestsArePublicHttps);
    }

    /// <summary>公开档案中的旧式 HTTP 纹理地址在实际请求前升级为 HTTPS。</summary>
    [Fact]
    public async Task NormalizesOfficialHttpTextureToHttps()
    {
        using var transport = new Transport { Reply = (request, _) => Task.FromResult(
            request.RequestUri!.Host == "sessionserver.mojang.com" ? Bytes(Profile(Texture.Replace("https:", "http:"))) : Bytes(Skin)) };
        using var cache = new MinecraftAvatarCache(_root, bytes => bytes, transport);
        Assert.Equal(Skin, await cache.GetAsync(Account));
        Assert.Equal(Texture, transport.Addresses[1]);
        Assert.True(transport.AllRequestsArePublicHttps);
    }

    /// <summary>离线、空 UUID 与非法账号标识不查询远端，也不创建缓存目录。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Steve")]
    [InlineData("../private")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidAccountDoesNotUseNetworkOrDisk(string? account)
    {
        using var transport = new Transport();
        using var cache = new MinecraftAvatarCache(_root, bytes => bytes, transport);
        Assert.Null(await cache.GetAsync(account));
        Assert.Empty(transport.Addresses);
        Assert.False(Directory.Exists(_root));
    }

    /// <summary>UUID 的连字符和大小写不影响复用，返回缓冲也不会污染共享头像。</summary>
    [Fact]
    public async Task ReusesNormalizedMemoryAndValidatedDiskCache()
    {
        using var transport = new Transport();
        using (var cache = new MinecraftAvatarCache(_root, _ => Avatar, transport))
        {
            var first = await cache.GetAsync(Account);
            Assert.Equal(Avatar, first);
            first![0] = 0;
            Assert.Equal(Avatar, await cache.GetAsync(Guid.Parse(Account).ToString("D").ToUpperInvariant()));
        }
        using var other = new MinecraftAvatarCache(_root, _ => Avatar, transport);
        Assert.Equal(Avatar, await other.GetAsync(Account));
        Assert.Equal(2, transport.Addresses.Count);
    }

    /// <summary>六小时后刷新失败仍可显示已经通过相同解码验证的旧皮肤。</summary>
    [Fact]
    public async Task ExpiredCacheFallsBackToValidOldSkinOnRefreshFailure()
    {
        var clock = new TestClock();
        using var transport = new Transport();
        using var cache = new MinecraftAvatarCache(_root, _ => Avatar, transport, clock);
        Assert.Equal(Avatar, await cache.GetAsync(Account));
        clock.Advance(TimeSpan.FromHours(7));
        transport.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        Assert.Equal(Avatar, await cache.GetAsync(Account));
        Assert.Equal(Avatar, await cache.GetAsync(Account));
        Assert.Equal(3, transport.Addresses.Count);
        Assert.Equal(Skin, await File.ReadAllBytesAsync(CachePath));
    }

    /// <summary>坏磁盘缓存重新下载，通过解码后才替换自己的缓存文件。</summary>
    [Fact]
    public async Task InvalidDiskSkinIsDownloadedAgain()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllBytesAsync(CachePath, [0]);
        var decoded = 0;
        using var transport = new Transport();
        using var cache = new MinecraftAvatarCache(_root, bytes => { ++decoded; return bytes.SequenceEqual(Skin) ? Avatar : null; }, transport);
        Assert.Equal(Avatar, await cache.GetAsync(Account));
        Assert.Equal(2, decoded);
        Assert.Equal(2, transport.Addresses.Count);
        Assert.Equal(Skin, await File.ReadAllBytesAsync(CachePath));
    }

    /// <summary>解码器拒绝、抛错或生成过大头像时不写坏缓存，也不影响账号流程。</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("throw")]
    public async Task InvalidDecodedImageIsNotCached(string kind)
    {
        using var transport = new Transport();
        using var cache = new MinecraftAvatarCache(_root, _ => kind switch
        {
            "null" => null, "empty" => [], "oversized" => new byte[32 * 1024 + 1], _ => throw new InvalidDataException("bad skin")
        }, transport);
        Assert.Null(await cache.GetAsync(Account));
        Assert.False(File.Exists(CachePath));
        Assert.Equal(2, transport.Addresses.Count);
    }

    /// <summary>档案错误、账号标识不匹配和非法 base64 不触发纹理下载。</summary>
    [Theory]
    [InlineData("forbidden")]
    [InlineData("bad-json")]
    [InlineData("bad-base64")]
    [InlineData("wrong-id")]
    [InlineData("wrong-texture-id")]
    [InlineData("duplicate-textures")]
    [InlineData("missing-skin")]
    public async Task InvalidProfileIsRejected(string kind)
    {
        using var transport = new Transport { Reply = (_, _) => Task.FromResult(kind switch
        {
            "forbidden" => new HttpResponseMessage(HttpStatusCode.Forbidden),
            "bad-json" => Bytes(Encoding.UTF8.GetBytes("not json")),
            "bad-base64" => Bytes(JsonSerializer.SerializeToUtf8Bytes(new { id = Account, properties = new[] { new { name = "textures", value = "!bad!" } } })),
            "wrong-id" => Bytes(Profile(Texture, Guid.NewGuid().ToString("N"))),
            "wrong-texture-id" => Bytes(Profile(Texture, textureId: Guid.NewGuid().ToString("N"))),
            "duplicate-textures" => Bytes(JsonSerializer.SerializeToUtf8Bytes(new { id = Account, properties = new[] { new { name = "textures", value = "e30=" }, new { name = "textures", value = "e30=" } } })),
            _ => Bytes(JsonSerializer.SerializeToUtf8Bytes(new { id = Account, properties = new[] { new { name = "textures", value = "e30=" } } }))
        }) };
        using var cache = new MinecraftAvatarCache(_root, _ => Avatar, transport);
        Assert.Null(await cache.GetAsync(Account));
        Assert.Single(transport.Addresses);
        Assert.False(File.Exists(CachePath));
    }

    /// <summary>只允许固定官方纹理域和哈希路径，拒绝凭据、端口、查询参数及任意重定向入口。</summary>
    [Theory]
    [InlineData("https://evil.example/texture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://textures.minecraft.net.evil.example/texture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://secret@textures.minecraft.net/texture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://textures.minecraft.net:8443/texture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://textures.minecraft.net/texture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa?token=x")]
    [InlineData("https://textures.minecraft.net/texture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa#x")]
    [InlineData("https://textures.minecraft.net/texture/bad")]
    [InlineData("https://textures.minecraft.net/texture/gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("https://textures.minecraft.net/other/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("file:///C:/private.png")]
    public async Task UnsafeTextureAddressIsNeverRequested(string texture)
    {
        using var transport = new Transport { Reply = (_, _) => Task.FromResult(Bytes(Profile(texture))) };
        using var cache = new MinecraftAvatarCache(_root, _ => Avatar, transport);
        Assert.Null(await cache.GetAsync(Account));
        Assert.Single(transport.Addresses);
        Assert.False(File.Exists(CachePath));
    }

    /// <summary>档案或纹理的跳转响应均拒绝，不能跳转到任意其他地址。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RedirectIsNotFollowed(bool textureRedirect)
    {
        using var transport = new Transport { Reply = (request, _) => Task.FromResult(
            textureRedirect && request.RequestUri!.Host == "sessionserver.mojang.com" ? Bytes(Profile()) :
            new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example/private") } }) };
        using var cache = new MinecraftAvatarCache(_root, _ => Avatar, transport);
        Assert.Null(await cache.GetAsync(Account));
        Assert.Equal(textureRedirect ? 2 : 1, transport.Addresses.Count);
        Assert.DoesNotContain(transport.Addresses, address => address.Contains("evil.example", StringComparison.Ordinal));
    }

    /// <summary>同时限制声明大小与实际流大小；档案或皮肤超限均不能交给解码器。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OversizedResponsesAreRejected(bool oversizedSkin, bool declaredLength)
    {
        var decoded = false;
        using var transport = new Transport { Reply = (request, _) =>
        {
            if (oversizedSkin && request.RequestUri!.Host == "sessionserver.mojang.com") return Task.FromResult(Bytes(Profile()));
            var payload = new byte[(oversizedSkin ? 1024 * 1024 : 64 * 1024) + 1];
            HttpContent content = declaredLength ? new ByteArrayContent(payload) : new StreamContent(new UnknownLengthStream(payload));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        } };
        using var cache = new MinecraftAvatarCache(_root, _ => { decoded = true; return Avatar; }, transport);
        Assert.Null(await cache.GetAsync(Account));
        Assert.False(decoded);
        Assert.False(File.Exists(CachePath));
        Assert.Equal(oversizedSkin ? 2 : 1, transport.Addresses.Count);
    }

    /// <summary>失败短暂缓存防止 UI 重建重复请求，到期后允许重新查询。</summary>
    [Fact]
    public async Task NegativeCacheIsShortLived()
    {
        var clock = new TestClock();
        using var transport = new Transport { Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)) };
        using var cache = new MinecraftAvatarCache(_root, _ => Avatar, transport, clock);
        Assert.Null(await cache.GetAsync(Account));
        Assert.Null(await cache.GetAsync(Account));
        Assert.Single(transport.Addresses);
        clock.Advance(TimeSpan.FromMinutes(2));
        transport.Reply = null;
        Assert.Equal(Avatar, await cache.GetAsync(Account));
        Assert.Equal(3, transport.Addresses.Count);
    }

    /// <summary>并发展示同一账号只下载一次，不因多个入口产生重复请求。</summary>
    [Fact]
    public async Task ConcurrentRequestsShareCache()
    {
        using var transport = new Transport();
        using var cache = new MinecraftAvatarCache(_root, _ => Avatar, transport);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => cache.GetAsync(Account)));
        Assert.All(results, result => Assert.Equal(Avatar, result));
        Assert.Equal(2, transport.Addresses.Count);
    }

    /// <summary>调用方提前取消或传输中取消时保留取消语义。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationIsPropagated(bool inFlight)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new Transport { Reply = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Bytes(Skin);
        } };
        using var cache = new MinecraftAvatarCache(_root, _ => Avatar, transport);
        using var cancellation = new CancellationTokenSource();
        if (!inFlight) cancellation.Cancel();
        var result = cache.GetAsync(Account, cancellation.Token);
        if (inFlight) { await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        Assert.Equal(inFlight ? 1 : 0, transport.Addresses.Count);
        Assert.False(File.Exists(CachePath));
    }

    /// <summary>释放服务会取消在途请求而不产生资源竞态，外部传输仍可供新服务使用。</summary>
    [Fact]
    public async Task DisposeSafelyCancelsRequestsAndPreservesExternalHandler()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new Transport { Reply = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Bytes(Skin);
        } };
        using var cache = new MinecraftAvatarCache(_root, _ => Avatar, transport);
        var running = cache.GetAsync(Account);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cache.Dispose();
        Assert.Null(await running);
        Assert.Null(await cache.GetAsync(Account));
        Assert.False(transport.Disposed);
        transport.Reply = null;
        using var replacement = new MinecraftAvatarCache(_root, _ => Avatar, transport);
        Assert.Equal(Avatar, await replacement.GetAsync(Account));
    }

    /// <summary>只清理本测试创建的随机临时目录。</summary>
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static byte[] Profile(string? texture = null, string? id = null, string? textureId = null)
    {
        var contents = new Dictionary<string, object> { ["textures"] = new { SKIN = new { url = texture ?? Texture } } };
        if (textureId is not null) contents["profileId"] = textureId;
        var value = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(contents));
        return JsonSerializer.SerializeToUtf8Bytes(new { id = id ?? Account, properties = new[] { new { name = "textures", value } } });
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    /// <summary>所有 HTTP 请求在内存处理；没有真实网络或账号凭证。</summary>
    private sealed class Transport : HttpMessageHandler
    {
        public List<string> Addresses { get; } = [];
        public bool AllRequestsArePublicHttps { get; private set; } = true;
        public bool Disposed { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Reply { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Addresses.Add(request.RequestUri!.AbsoluteUri);
            AllRequestsArePublicHttps &= request.RequestUri.Scheme == "https" && request.Headers.Authorization is null &&
                !request.Headers.Contains("x-api-key") && !request.Headers.Contains("Cookie");
            return Reply?.Invoke(request, token) ?? Task.FromResult(
                request.RequestUri.Host == "sessionserver.mojang.com" ? Bytes(Profile()) : Bytes(Skin));
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    /// <summary>模拟分块响应，不能通过 Content-Length 提前判断，必须限制实际读取量。</summary>
    private sealed class UnknownLengthStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => _inner.ReadAsync(buffer, token);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
