namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Describes the first connected-launch package to prepare from the current 3ds Max scene.
/// </summary>
public sealed class MaxSceneLaunchPackageRequest
{
    #region Properties

    public string CloudUrl { get; set; } = string.Empty;

    public string IdentityUrl { get; set; } = string.Empty;

    public string RenderMode { get; set; } = "RenderStill";

    public int ResolutionX { get; set; } = 1920;

    public int ResolutionY { get; set; } = 1080;

    public int FrameStart { get; set; } = 1;

    public int FrameEnd { get; set; } = 1;

    public int Samples { get; set; } = 64;

    public bool UseAllClients { get; set; }

    public string SelectedGroupName { get; set; } = string.Empty;

    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>Still/frames image format ("PNG"/"JPEG"/"EXR"/"TIFF"/"WEBP"); empty falls back to PNG.</summary>
    public string ImageFormat { get; set; } = string.Empty;

    /// <summary>Tiled-still grid; non-positive values fall back to the transport defaults.</summary>
    public int TilesX { get; set; }

    public int TilesY { get; set; }

    public int TileOverlap { get; set; }

    /// <summary>Video preset key ("mp4-h264"/"mp4-h265"/"webm-vp9"/"mov-prores"); empty falls back to MP4/H.264.</summary>
    public string VideoPreset { get; set; } = string.Empty;

    /// <summary>CRF for the CRF-driven video presets; non-positive falls back to the transport default.</summary>
    public int VideoCrf { get; set; }

    // Opt-in local V-Ray render-to-texture of scanned (.vrscan) materials before upload; part
    // of the export runs on the user's machine, which the UI states explicitly.
    public bool BakeVRayScannedMaterials { get; set; }

    #endregion
}
