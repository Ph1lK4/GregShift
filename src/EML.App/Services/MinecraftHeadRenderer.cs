using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EML.App.Services;

/// <summary>只解码标准皮肤并合成正面与帽子层；输出固定八像素头像，不加载远程图片控件。</summary>
internal static class MinecraftHeadRenderer
{
    internal static BitmapSource Steve { get; } = CreateSteve();

    /// <summary>先检查 PNG 尺寸再调用系统解码器，拒绝大图、动画和非标准皮肤。</summary>
    internal static byte[]? DecodeSkin(byte[] bytes)
    {
        if (!HasPngSize(bytes, 64, 32) && !HasPngSize(bytes, 64, 64)) return null;
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            var decoder = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1) return null;
            var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            if (source.PixelWidth != 64 || source.PixelHeight is not (32 or 64)) return null;
            var pixels = new byte[64 * source.PixelHeight * 4];
            source.CopyPixels(pixels, 64 * 4, 0);
            var head = new byte[8 * 8 * 4];
            // 老版 64×32 皮肤会用全不透明右半区表示未绘制帽子，遵循该格式的透明层规则。
            var useHat = source.PixelHeight != 32 || HasTransparentLegacyOverlay(pixels);
            for (var y = 0; y < 8; ++y)
            for (var x = 0; x < 8; ++x)
            {
                var face = ((y + 8) * 64 + x + 8) * 4;
                var hat = ((y + 8) * 64 + x + 40) * 4;
                var target = (y * 8 + x) * 4;
                var alpha = useHat ? pixels[hat + 3] : 0;
                for (var channel = 0; channel < 3; ++channel)
                    head[target + channel] = (byte)((pixels[face + channel] * (255 - alpha) + pixels[hat + channel] * alpha + 127) / 255);
                head[target + 3] = 255;
            }
            var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, head, 8 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream(); encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>缓存服务返回的头像也检查尺寸，避免无约束图像进入界面。</summary>
    internal static BitmapSource? ReadHead(byte[] bytes)
    {
        if (!HasPngSize(bytes, 8, 8)) return null;
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            var decoder = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1) return null;
            var bitmap = decoder.Frames[0]; bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>检查签名、首个 IHDR 与实际像素尺寸，解压前即限制解码开销。</summary>
    private static bool HasPngSize(byte[] bytes, int width, int height) => bytes.Length is >= 33 and <= 1048576 &&
        bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8, 4)) == 13 &&
        bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) &&
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) == width &&
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) == height;

    /// <summary>旧格式右上半区有透明像素才将第二层视为有效帽子。</summary>
    private static bool HasTransparentLegacyOverlay(byte[] pixels)
    {
        for (var y = 0; y < 32; ++y)
        for (var x = 32; x < 64; ++x)
            if (pixels[(y * 64 + x) * 4 + 3] < 128) return true;
        return false;
    }

    /// <summary>内置 Steve 正面像素图，不因离线玩家名或 UUID 联网，不需要额外素材文件。</summary>
    private static BitmapSource CreateSteve()
    {
        string[] rows = ["HHHHHHHH", "HHHHHHHH", "HSSSSSSH", "SSSSSSSS", "SWESSEWS", "SSSNNSSS", "SSDDDDSS", "SSDBBDSS"];
        var colors = new Dictionary<char, Color>
        {
            ['H'] = Color.FromRgb(53, 35, 23), ['S'] = Color.FromRgb(174, 126, 97),
            ['W'] = Color.FromRgb(225, 225, 225), ['E'] = Color.FromRgb(72, 61, 139),
            ['N'] = Color.FromRgb(137, 83, 65), ['D'] = Color.FromRgb(82, 49, 32), ['B'] = Color.FromRgb(127, 64, 52)
        };
        var pixels = new byte[8 * 8 * 4];
        for (var y = 0; y < 8; ++y)
        for (var x = 0; x < 8; ++x)
        {
            var color = colors[rows[y][x]]; var index = (y * 8 + x) * 4;
            pixels[index] = color.B; pixels[index + 1] = color.G; pixels[index + 2] = color.R; pixels[index + 3] = 255;
        }
        var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 32);
        bitmap.Freeze(); return bitmap;
    }
}
