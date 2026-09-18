namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Reads what a downloaded result actually IS from its leading bytes. The requested format normally
/// names the file (the farm honours it), but the bytes are the truth: they also name results whose
/// record predates the format being kept, and they keep a file from ever being labelled as something
/// it is not. Only unambiguous signatures are recognised; anything else defers to the request.
/// </summary>
public static class MaxRenderResultSniffer
{
    #region Constants

    /// <summary>Enough for every signature below (the ISO-BMFF major brand ends at byte 12).</summary>
    private const int HEADER_LENGTH = 16;

    private static readonly byte[] PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] JPEG_SIGNATURE = [0xFF, 0xD8, 0xFF];

    private static readonly byte[] EXR_SIGNATURE = [0x76, 0x2F, 0x31, 0x01];

    private static readonly byte[] TIFF_LITTLE_ENDIAN_SIGNATURE = [0x49, 0x49, 0x2A, 0x00];

    private static readonly byte[] TIFF_BIG_ENDIAN_SIGNATURE = [0x4D, 0x4D, 0x00, 0x2A];

    private static readonly byte[] EBML_SIGNATURE = [0x1A, 0x45, 0xDF, 0xA3];

    #endregion

    #region Functions

    /// <summary>The extension the file's own bytes declare, or null when unrecognised or unreadable.</summary>
    /// <param name="filePath">The downloaded file.</param>
    /// <returns>The extension including the dot, or null.</returns>
    public static string? DetectExtension(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[HEADER_LENGTH];
            var read = stream.ReadAtLeast(header, HEADER_LENGTH, throwOnEndOfStream: false);
            return DetectExtension(header[..read]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The extension a header declares, or null when it matches no known result type.</summary>
    /// <param name="header">The first bytes of the file (16 are enough).</param>
    /// <returns>The extension including the dot, or null.</returns>
    public static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(PNG_SIGNATURE))
            return ".png";

        if (header.StartsWith(JPEG_SIGNATURE))
            return ".jpg";

        if (header.StartsWith(EXR_SIGNATURE))
            return ".exr";

        if (header.StartsWith(TIFF_LITTLE_ENDIAN_SIGNATURE) || header.StartsWith(TIFF_BIG_ENDIAN_SIGNATURE))
            return ".tif";

        if (header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return ".webp";

        // EBML: the plugin only ever requests WebM from the Matroska family.
        if (header.StartsWith(EBML_SIGNATURE))
            return ".webm";

        if (header.StartsWith("BLENDER"u8))
            return ".blend";

        return DetectIsoMediaExtension(header);
    }

    /// <summary>
    /// ISO base media (<c>ftyp</c> box at offset 4): QuickTime's "qt  " brand is a .mov, the MPEG-4
    /// brands an .mp4. An unknown brand stays unrecognised rather than guessed.
    /// </summary>
    private static string? DetectIsoMediaExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length < 12 || !header[4..8].SequenceEqual("ftyp"u8))
            return null;

        var brand = header[8..12];
        if (brand.SequenceEqual("qt  "u8))
            return ".mov";

        if (brand.SequenceEqual("isom"u8) || brand.SequenceEqual("iso2"u8) || brand.SequenceEqual("iso4"u8)
            || brand.SequenceEqual("iso5"u8) || brand.SequenceEqual("iso6"u8) || brand.SequenceEqual("mp41"u8)
            || brand.SequenceEqual("mp42"u8) || brand.SequenceEqual("avc1"u8) || brand.SequenceEqual("hvc1"u8)
            || brand.SequenceEqual("dash"u8))
        {
            return ".mp4";
        }

        return null;
    }

    #endregion
}
