using OutWit.Controller.Render.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

/// <summary>
/// The image formats and video presets the farm actually renders (wire enums <see cref="RenderFormat"/>
/// and <see cref="VideoFormat"/>) — the single source for the Render dialog, the Settings defaults and
/// the submission transport, so the UI can never offer an output the server would ignore.
/// </summary>
public static class MaxRenderOutputCatalog
{
    #region Constants

    public static readonly IReadOnlyList<string> ImageFormats = ["PNG", "JPEG", "EXR", "TIFF", "WEBP"];

    /// <summary>
    /// The subset a TILED still may use. The server stitches tiles through an 8-bit ffmpeg crop/pad
    /// pipeline and rejects anything else outright ("Render.CollectTiles bootstrap implementation
    /// currently supports PNG and JPEG only"), so offering the rest for a tiled launch only ever
    /// produced a job that failed on the farm.
    /// </summary>
    public static readonly IReadOnlyList<string> TiledImageFormats = ["PNG", "JPEG"];

    /// <summary>Canonical persisted preset keys with the display labels the dialogs show.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> VideoPresets =
    [
        new("mp4-h264", "MP4 · H.264"),
        new("mp4-h265", "MP4 · H.265"),
        new("webm-vp9", "WebM · VP9"),
        new("mov-prores", "MOV · ProRes 422 HQ")
    ];

    #endregion

    #region Functions

    /// <summary>Canonical preset key from a persisted value (accepts the legacy mp4/mov/webm container names).</summary>
    public static string NormalizeVideoPresetKey(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "mp4-h265" or "h265" => "mp4-h265",
            "webm" or "webm-vp9" => "webm-vp9",
            "mov" or "mov-prores" => "mov-prores",
            _ => "mp4-h264"
        };
    }

    public static string VideoPresetDisplay(string? key)
    {
        var normalized = NormalizeVideoPresetKey(key);
        return VideoPresets.First(me => me.Key == normalized).Value;
    }

    public static string VideoPresetKeyFromDisplay(string? display)
    {
        return VideoPresets.FirstOrDefault(me => me.Value == display, VideoPresets[0]).Key;
    }

    public static string NormalizeImageFormat(string? value)
    {
        var upper = value?.ToUpperInvariant();
        return ImageFormats.Contains(upper ?? string.Empty) ? upper! : "PNG";
    }

    /// <summary>True when the format can carry a tiled still through the server's stitcher.</summary>
    public static bool IsTiledImageFormat(string? value) =>
        TiledImageFormats.Contains(value?.ToUpperInvariant() ?? string.Empty);

    /// <summary>
    /// The format a tiled still will actually render in: the requested one when the stitcher supports
    /// it, PNG otherwise. Falls back rather than failing — a tiled launch the farm cannot collect is
    /// never the artist's intent.
    /// </summary>
    public static string NormalizeTiledImageFormat(string? value) =>
        IsTiledImageFormat(value) ? value!.ToUpperInvariant() : "PNG";

    public static RenderFormat ParseImageFormat(string? value)
    {
        return NormalizeImageFormat(value) switch
        {
            "JPEG" => RenderFormat.JPEG,
            "EXR" => RenderFormat.EXR,
            "TIFF" => RenderFormat.TIFF,
            "WEBP" => RenderFormat.WEBP,
            _ => RenderFormat.PNG
        };
    }

    public static VideoFormat ParseVideoPreset(string? key)
    {
        return NormalizeVideoPresetKey(key) switch
        {
            "mp4-h265" => VideoFormat.Mp4H265,
            "webm-vp9" => VideoFormat.WebMVp9,
            "mov-prores" => VideoFormat.MovProres422Hq,
            _ => VideoFormat.Mp4H264
        };
    }

    #endregion
}
