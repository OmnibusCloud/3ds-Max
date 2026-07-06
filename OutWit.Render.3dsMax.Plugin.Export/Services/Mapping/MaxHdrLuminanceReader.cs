using System;
using System.IO;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

/// <summary>
/// Minimal Radiance RGBE (.hdr) reader that estimates the image's mean luminance. Used to
/// auto-expose an environment HDRI: authored HDRs span wildly different absolute levels (a bright
/// kitchen averages ~1.5, an overcast sky ~0.1), and emitting them at Strength=1 either blows the
/// render to white or leaves it black. Only the luminance statistic is needed, so scanlines are
/// subsampled and decode errors simply report failure (the world then keeps Strength=1).
/// </summary>
internal static class MaxHdrLuminanceReader
{
    #region Constants

    // Sample every Nth scanline — the mean over a few dozen rows converges well enough for exposure.
    private const int SCANLINE_STRIDE = 4;

    private const int MIN_RLE_WIDTH = 8;

    private const int MAX_RLE_WIDTH = 32767;

    #endregion

    #region Functions

    public static bool TryComputeMeanLuminance(string path, out double meanLuminance)
    {
        meanLuminance = 0d;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (!TryReadHeader(reader, out var width, out var height))
                return false;

            double sum = 0d;
            long count = 0;
            var scanline = new byte[width * 4];

            for (var row = 0; row < height; row++)
            {
                if (!TryReadScanline(reader, scanline, width))
                    return false;

                if (row % SCANLINE_STRIDE != 0)
                    continue;

                for (var x = 0; x < width; x++)
                {
                    var e = scanline[x * 4 + 3];
                    if (e == 0)
                    {
                        count++;
                        continue;
                    }

                    var factor = Math.Pow(2d, e - 136); // ldexp(1, e - (128 + 8))
                    var r = scanline[x * 4] * factor;
                    var g = scanline[x * 4 + 1] * factor;
                    var b = scanline[x * 4 + 2] * factor;
                    sum += 0.2126d * r + 0.7152d * g + 0.0722d * b;
                    count++;
                }
            }

            if (count == 0)
                return false;

            meanLuminance = sum / count;
            return meanLuminance > 0d && double.IsFinite(meanLuminance);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadHeader(BinaryReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;

        var first = ReadLine(reader);
        if (first == null || !first.StartsWith("#?", StringComparison.Ordinal))
            return false;

        // Header lines until the blank separator, then the resolution line ("-Y H +X W").
        string? line;
        while ((line = ReadLine(reader)) != null && line.Length > 0)
        {
        }

        var resolution = ReadLine(reader);
        if (resolution == null)
            return false;

        var parts = resolution.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "-Y" || parts[2] != "+X")
            return false;

        return int.TryParse(parts[1], out height) && int.TryParse(parts[3], out width)
               && width > 0 && height > 0;
    }

    private static string? ReadLine(BinaryReader reader)
    {
        var builder = new System.Text.StringBuilder();
        while (true)
        {
            var value = reader.BaseStream.ReadByte();
            if (value < 0)
                return builder.Length > 0 ? builder.ToString() : null;
            if (value == '\n')
                return builder.ToString();
            builder.Append((char)value);
        }
    }

    private static bool TryReadScanline(BinaryReader reader, byte[] scanline, int width)
    {
        var header = reader.ReadBytes(4);
        if (header.Length < 4)
            return false;

        // Adaptive-RLE scanline: 0x02 0x02 then the 16-bit width, four component planes follow.
        if (header[0] == 2 && header[1] == 2 && width >= MIN_RLE_WIDTH && width <= MAX_RLE_WIDTH
            && ((header[2] << 8) | header[3]) == width)
        {
            for (var component = 0; component < 4; component++)
            {
                var x = 0;
                while (x < width)
                {
                    var code = reader.ReadByte();
                    if (code > 128)
                    {
                        var value = reader.ReadByte();
                        for (var i = 0; i < code - 128; i++)
                            scanline[x++ * 4 + component] = value;
                    }
                    else
                    {
                        for (var i = 0; i < code; i++)
                            scanline[x++ * 4 + component] = reader.ReadByte();
                    }
                }
            }

            return true;
        }

        // Flat (old-style) scanline: the 4 bytes already read are the first pixel.
        scanline[0] = header[0];
        scanline[1] = header[1];
        scanline[2] = header[2];
        scanline[3] = header[3];
        var remaining = reader.ReadBytes((width - 1) * 4);
        if (remaining.Length < (width - 1) * 4)
            return false;

        Array.Copy(remaining, 0, scanline, 4, remaining.Length);
        return true;
    }

    #endregion
}
