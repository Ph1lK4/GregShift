using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using EML.Core.Minecraft.Gtnh.Tools;
using EML.Infrastructure.Minecraft.Importing;

namespace EML.Infrastructure.Minecraft.Gtnh;

/// <summary>图标使用独立有界缓存；来源声明和 PNG 尺寸都必须通过验证。</summary>
internal sealed class GtnhFlowIconCache(string root, HttpClient http)
{
    private const int MaximumImageBytes = 2 * 1024 * 1024;
    private const int MaximumTotalBytes = 32 * 1024 * 1024;
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    internal async Task<GtnhFlowChartContent> PopulateAsync(GtnhFlowChartContent content, bool download, CancellationToken token)
    {
        var icons = GtnhFlowMediaParser.Icons(content.Document).Distinct().ToArray();
        if (icons.Length == 0) return content;
        var groups = icons.GroupBy(icon => icon.Address).ToArray();
        var bytes = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var failed = groups.Length > 2048 ? 1 : 0;
        var total = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await Parallel.ForEachAsync(groups.Take(2048), new ParallelOptions
            { MaxDegreeOfParallelism = 4, CancellationToken = deadline.Token }, async (group, currentToken) =>
            {
                var icon = group.First();
                try
                {
                    if (Volatile.Read(ref total) >= MaximumTotalBytes) { Interlocked.Increment(ref failed); return; }
                    var data = await ReadAsync(icon, download, count =>
                    {
                        if (Interlocked.Add(ref total, count) > MaximumTotalBytes) throw new InvalidDataException("本次图标读取总量达到上限。");
                    }, currentToken).ConfigureAwait(false);
                    if (data is null || group.Any(descriptor => !ValidPng(data, descriptor))) { Interlocked.Increment(ref failed); return; }
                    bytes.TryAdd(icon.Address, data);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException)
                { Interlocked.Increment(ref failed); }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { failed++; }
        token.ThrowIfCancellationRequested();
        var warning = content.Warning;
        if (failed > 0) warning = (warning is null ? "" : warning + " ") + "部分图标暂不可用，仍可查看配方文字。";
        return content with { IconData = bytes, Warning = warning };
    }

    private async Task<byte[]?> ReadAsync(GtnhFlowIcon icon, bool download, Action<int> account, CancellationToken token)
    {
        if (!GtnhFlowMediaParser.IsAllowedIconAddress(icon.Address)) return null;
        var identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(icon.Address)));
        var path = Path.Combine(root, identity + ".png");
        var gate = Gates[(uint)StringComparer.Ordinal.GetHashCode(identity) % (uint)Gates.Length];
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            InstanceImportPaths.EnsureNoLinks(path);
            if (File.Exists(path))
            {
                try
                {
                    await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, FileOptions.Asynchronous);
                    var cached = await ReadBoundedAsync(file, account, token).ConfigureAwait(false);
                    if (ValidPng(cached, icon)) return cached;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { }
            }
            if (!download) return null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, icon.Address);
            request.Headers.Accept.ParseAdd("image/png");
            request.Headers.UserAgent.ParseAdd("GregShift/1.0 (+https://github.com/Ph1lK4/GregShift)");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } actual && actual.AbsoluteUri != icon.Address || response.Content.Headers.ContentLength > MaximumImageBytes ||
                response.Content.Headers.ContentType?.MediaType is { } type && !type.Equals("image/png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("图标响应不是允许的 PNG。");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var data = await ReadBoundedAsync(stream, account, deadline.Token).ConfigureAwait(false);
            if (!ValidPng(data, icon)) throw new InvalidDataException("图标尺寸或动画帧数据无效。");
            try { await WriteAsync(path, data, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 当前阅读不依赖写缓存成功。 */ }
            return data;
        }
        finally { gate.Release(); }
    }

    private async Task WriteAsync(string path, byte[] data, CancellationToken token)
    {
        InstanceImportPaths.EnsureNoLinks(root);
        Directory.CreateDirectory(root);
        InstanceImportPaths.EnsureNoLinks(path);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, FileOptions.Asynchronous))
            { await file.WriteAsync(data, token).ConfigureAwait(false); await file.FlushAsync(token).ConfigureAwait(false); }
            token.ThrowIfCancellationRequested();
            InstanceImportPaths.EnsureNoLinks(path);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) { InstanceImportPaths.EnsureNoLinks(temporary); File.Delete(temporary); } }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, Action<int> account, CancellationToken token)
    {
        using var memory = new MemoryStream();
        var chunk = new byte[16384];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, token).ConfigureAwait(false);
            if (count == 0) break;
            if (memory.Length + count > MaximumImageBytes) throw new InvalidDataException("图标数据过大。");
            account(count);
            memory.Write(chunk, 0, count);
        }
        return memory.ToArray();
    }

    internal static bool ValidPng(byte[] data, GtnhFlowIcon icon)
    {
        if (data.Length is < 45 or > MaximumImageBytes || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)) != 13 || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
        if (!(width is > 0 and <= 16384 && height is > 0 and <= 512 && (ulong)width * height <= 4 * 1024 * 1024 &&
            icon.Frames is > 0 and <= 512 && width == height * icon.Frames) || data[26] != 0 || data[27] != 0 || data[28] > 1) return false;
        var imageData = false;
        for (var position = 8; position <= data.Length - 12;)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4));
            if (length > (uint)(data.Length - position - 12)) return false;
            var type = data.AsSpan(position + 4, 4);
            if (type.SequenceEqual("IDAT"u8) && length > 0) imageData = true;
            if (type.SequenceEqual("IEND"u8)) return length == 0 && imageData && position + 12 == data.Length;
            position += (int)length + 12;
        }
        return false;
    }
}
