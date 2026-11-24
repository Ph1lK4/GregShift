using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.Controls;

/// <summary>只解码宿主交付的 PNG 字节；没有 URI 加载器，不访问网络或任意本地文件。</summary>
internal sealed class GtnhFlowIconRenderer
{
    private readonly Dictionary<string, BitmapSource?> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Address, int Frames, int Index), BitmapSource> _frames = [];

    internal GtnhFlowIconRenderer(IReadOnlyDictionary<string, byte[]>? data)
    {
        if (data is null) return;
        // 字节仍需在原生解码前检查；不能仅相信缓存或调用方。
        ulong decodedPixels = 0;
        long encodedBytes = 0;
        foreach (var pair in data.Take(2048))
        {
            var bytes = pair.Value;
            if (bytes is null || bytes.Length is < 24 or > 2 * 1024 * 1024 ||
                !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) continue;
            var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
            if (width is < 1 or > 16384 || height is < 1 or > 512 || (ulong)width * height > 4 * 1024 * 1024) continue;
            if (decodedPixels + (ulong)width * height > 16 * 1024 * 1024 || encodedBytes + bytes.Length > 32 * 1024 * 1024) continue;
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count != 1) continue;
                var image = decoder.Frames[0];
                if (image.PixelWidth != width || image.PixelHeight != height) continue;
                image.Freeze(); _images[pair.Key] = image;
                decodedPixels += (ulong)width * height; encodedBytes += bytes.Length;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
            { /* 损坏或不支持的图标仍保留文字；不得令整张图加载失败。 */ }
        }
    }

    internal int Count => _images.Count;

    internal BitmapSource? Frame(GtnhFlowIcon? icon, double seconds = 0)
    {
        if (icon is null || !_images.TryGetValue(icon.Address, out var image) || image is null ||
            icon.Frames is < 1 or > 512 || !double.IsFinite(icon.FrameSeconds) || icon.FrameSeconds <= 0) return null;
        if (icon.Frames == 1) return image;
        if (image.PixelWidth != image.PixelHeight * icon.Frames) return null;
        var index = (int)(Math.Max(0, double.IsFinite(seconds) ? seconds : 0) / Math.Max(.02, icon.FrameSeconds) % icon.Frames);
        var key = (icon.Address, icon.Frames, index);
        if (!_frames.TryGetValue(key, out var frame))
        {
            if (_frames.Count >= 2048) _frames.Clear();
            frame = new CroppedBitmap(image, new Int32Rect(index * image.PixelHeight, 0, image.PixelHeight, image.PixelHeight));
            frame.Freeze(); _frames.Add(key, frame);
        }
        return frame;
    }

    internal bool Draw(DrawingContext context, GtnhFlowIcon? icon, Rect bounds, double seconds = 0)
    {
        if (Frame(icon, seconds) is not { } frame) return false;
        context.DrawImage(frame, bounds);
        return true;
    }
}
