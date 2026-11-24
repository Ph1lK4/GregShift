using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EML.Core.Minecraft.Gtnh.Tools;
using EML.Infrastructure.Minecraft.Importing;

namespace EML.Infrastructure.Minecraft.Gtnh;

/// <summary>按需读取流程图数据并独立缓存；不保存网页脚本、账号或游戏实例资料。</summary>
public sealed class GtnhFlowChartService : IGtnhFlowChartService, IDisposable
{
    private const int MaximumPageBytes = 8 * 1024 * 1024;
    private const int MaximumCacheBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 8 };
    private static readonly SemaphoreSlim[] CacheLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly string _cacheRoot;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly GtnhFlowIconCache _icons;
    private bool _disposed;

    public GtnhFlowChartService(string launcherRoot, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherRoot);
        _cacheRoot = Path.GetFullPath(Path.Combine(launcherRoot, "cache", "gtnh-tools", "flowcharts"));
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false });
        _icons = new(Path.Combine(_cacheRoot, "icons"), _http);
    }

    /// <summary>有效缓存优先；显式更新失败时保留旧副本，取消请求则不回退或发布结果。</summary>
    public async Task<GtnhFlowChartContent> LoadAsync(GtnhFlowChart chart, bool refresh = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var (address, path, gate) = GetCacheTarget(chart);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GtnhFlowChartContent? cached = null;
            string? cacheIssue = null;
            try { cached = await ReadCacheAsync(path, address.AbsoluteUri, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            { cacheIssue = "本地图表缓存不可用：" + ex.Message; }
            if (cached is not null && !refresh) return await _icons.PopulateAsync(cached, false, cancellationToken).ConfigureAwait(false);
            GtnhFlowChartContent content;
            try { content = await DownloadAsync(address, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new GtnhFlowBrowserRequiredException(cached is null ? null : await _icons.PopulateAsync(cached, false, cancellationToken).ConfigureAwait(false), ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (cached is not null) return await _icons.PopulateAsync(cached with { Warning = "联网更新失败，继续使用本地缓存：" + ex.Message }, false, cancellationToken).ConfigureAwait(false);
                throw new IOException("无法读取原站流程图数据。" + (cacheIssue is null ? "" : " " + cacheIssue) + " " + ex.Message, ex);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return await SaveCacheOrWarnAsync(path, content, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    /// <summary>从已验证原站页面读取的声明式数据，完整校验后保存，并仅按需缓存允许 CDN 上的图标。</summary>
    public async Task<GtnhFlowChartContent> ImportPageDataAsync(GtnhFlowChart chart, string payloadJson, long? revisionId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var (address, path, gate) = GetCacheTarget(chart);
        if (revisionId is <= 0) throw new InvalidDataException("流程图原站修订号无效。");
        var document = GtnhFlowDocumentParser.ParsePayload(payloadJson);
        cancellationToken.ThrowIfCancellationRequested();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = new GtnhFlowChartContent(document, payloadJson, address.AbsoluteUri, revisionId, DateTimeOffset.UtcNow, false)
                { HasMedia = GtnhFlowMediaParser.HasMedia(payloadJson) };
            return await SaveCacheOrWarnAsync(path, content, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private (Uri Address, string Path, SemaphoreSlim Gate) GetCacheTarget(GtnhFlowChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (!chart.IsCreated) throw new InvalidDataException("未创建，等待更新。");
        var address = chart.Address;
        if (!GtnhToolNavigationPolicy.TryGetFlowChartPageTitle(address, out var title) || title != chart.PageTitle || address.Fragment.Length > 0)
            throw new InvalidDataException("流程图来源地址无效。");
        var identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri)));
        return (address, Path.Combine(_cacheRoot, identity + ".json"),
            CacheLocks[(uint)StringComparer.Ordinal.GetHashCode(identity) % (uint)CacheLocks.Length]);
    }

    private async Task<GtnhFlowChartContent> SaveCacheOrWarnAsync(string path, GtnhFlowChartContent content, CancellationToken token)
    {
        try { await WriteCacheAsync(path, content, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { token.ThrowIfCancellationRequested(); content = content with { Warning = "图表已加载，但未能保存离线缓存：" + ex.Message }; }
        token.ThrowIfCancellationRequested();
        return await _icons.PopulateAsync(content, true, token).ConfigureAwait(false);
    }

    private async Task<GtnhFlowChartContent> DownloadAsync(Uri address, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd("GregShift/1.0 (+https://github.com/Ph1lK4/GregShift)");
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } actual && actual != address)
            throw new InvalidDataException("原站流程图发生了未允许的重定向。");
        if (response.Content.Headers.ContentType?.MediaType is { } media &&
            !media.Equals("text/html", StringComparison.OrdinalIgnoreCase) && !media.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("原站返回的不是流程图网页。");
        if (response.Content.Headers.ContentLength is > MaximumPageBytes) throw new InvalidDataException("原站流程图页面过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        var bytes = await ReadBoundedAsync(stream, MaximumPageBytes, deadline.Token).ConfigureAwait(false);
        var html = Encoding.UTF8.GetString(bytes);
        token.ThrowIfCancellationRequested();
        var payload = GtnhFlowDocumentParser.ExtractPayload(html);
        payload = GtnhFlowHtmlMedia.Enrich(payload, html);
        var document = GtnhFlowDocumentParser.ParsePayload(payload);
        return new(document, payload, address.AbsoluteUri, GtnhFlowDocumentParser.ReadRevisionId(html), DateTimeOffset.UtcNow, false)
            { HasMedia = GtnhFlowMediaParser.HasMedia(payload) };
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken token)
    {
        using var buffer = new MemoryStream(); var chunk = new byte[16384];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
            if (count == 0) break;
            if (buffer.Length + count > limit) throw new InvalidDataException("流程图数据超过大小限制。");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private async Task<GtnhFlowChartContent?> ReadCacheAsync(string path, string address, CancellationToken token)
    {
        InstanceImportPaths.EnsureNoLinks(path);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, FileOptions.Asynchronous);
        if (stream.Length > MaximumCacheBytes) throw new InvalidDataException("本地图表缓存过大。");
        var bytes = await ReadBoundedAsync(stream, MaximumCacheBytes, token).ConfigureAwait(false);
        var record = JsonSerializer.Deserialize<CacheRecord>(bytes, JsonOptions);
        if (record is null || record.Version != 1 || record.SourceAddress != address || record.PayloadJson is null ||
            record.RetrievedAt == default || record.RetrievedAt > DateTimeOffset.UtcNow.AddMinutes(5) || record.RevisionId is <= 0 ||
            record.License != GtnhFlowChartContent.LicenseName)
            throw new InvalidDataException("本地图表缓存的来源或记录无效。");
        var document = GtnhFlowDocumentParser.ParsePayload(record.PayloadJson);
        return new(document, record.PayloadJson, address, record.RevisionId, record.RetrievedAt, true)
            { HasMedia = GtnhFlowMediaParser.HasMedia(record.PayloadJson) };
    }

    private async Task WriteCacheAsync(string path, GtnhFlowChartContent content, CancellationToken token)
    {
        var record = new CacheRecord(1, content.SourceAddress, content.RevisionId, content.RetrievedAt, content.PayloadJson, GtnhFlowChartContent.LicenseName);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        if (bytes.Length > MaximumCacheBytes) throw new InvalidDataException("图表缓存超过大小限制。");
        InstanceImportPaths.EnsureNoLinks(_cacheRoot);
        Directory.CreateDirectory(_cacheRoot);
        InstanceImportPaths.EnsureNoLinks(_cacheRoot);
        InstanceImportPaths.EnsureNoLinks(path);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            InstanceImportPaths.EnsureNoLinks(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // 仅清理本次独占创建的随机临时文件；已有缓存始终保留到新文件完成。
            try { if (File.Exists(temporary)) { InstanceImportPaths.EnsureNoLinks(temporary); File.Delete(temporary); } }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record CacheRecord(int Version, string SourceAddress, long? RevisionId, DateTimeOffset RetrievedAt, string PayloadJson, string License);
    public void Dispose() { if (_disposed) return; _disposed = true; if (_ownsHttp) _http.Dispose(); }
}
