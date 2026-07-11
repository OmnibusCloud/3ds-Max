using OutWit.Common.Settings.Aspects;
using OutWit.Common.Settings.Configuration;
using OutWit.Common.Settings.Interfaces;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

/// <summary>
/// Per-OS-user plugin preferences (MX-16), persisted via <see cref="OutWit.Common.Settings"/> to a
/// JSON file in the user's <c>%APPDATA%</c> (not synced). These are the sticky working habits with no
/// other source of truth — theme choice, export defaults, the "remember last render settings" buckets,
/// and the last render target. Frame range / resolution / fps are NOT here (they are read live from the
/// scene), and the OIDC refresh token is NOT here (it stays in its own DPAPI store).
/// </summary>
public class MaxPluginSettings : SettingsContainer
{
    #region Constructors

    public MaxPluginSettings(ISettingsManager settingsManager) : base(settingsManager)
    {
    }

    #endregion

    #region General

    /// <summary>UI theme: "FollowMax" (default), "Dark", or "Light" (MX-14).</summary>
    [Setting("General")]
    public virtual string ThemeMode { get; set; } = null!;

    #endregion

    #region Output

    /// <summary>Default export target: "Blend" (server convert) or "DccJson" (local) (MX-18).</summary>
    [Setting("Output")]
    public virtual string ExportTarget { get; set; } = null!;

    /// <summary>Last-used export output folder.</summary>
    [Setting("Output")]
    public virtual string OutputFolder { get; set; } = null!;

    /// <summary>Open the output folder after a successful export.</summary>
    [Setting("Output")]
    public virtual bool OpenFolderAfterExport { get; set; }

    /// <summary>Default still/frames image format: "PNG" (default) / "JPEG" / "EXR" / "TIFF" / "WEBP".</summary>
    [Setting("Output")]
    public virtual string ImageFormat { get; set; } = null!;

    #endregion

    #region Render

    /// <summary>Master toggle — when off, the plugin stops seeding/sticky-writing render prefs (MX-16).</summary>
    [Setting("Render")]
    public virtual bool RememberLastRenderSettings { get; set; }

    /// <summary>Last render mode: "RenderStill" / "RenderStillTiled" / "RenderFrames" / "RenderVideo" (MX-9).</summary>
    [Setting("Render")]
    public virtual string LastRenderMode { get; set; } = null!;

    /// <summary>Image output: split a single frame across machines (tiled still).</summary>
    [Setting("Render")]
    public virtual bool SplitFrame { get; set; }

    /// <summary>Opt-in local V-Ray render-to-texture of scanned (.vrscan) materials before upload.</summary>
    [Setting("Render")]
    public virtual bool BakeVRayScannedMaterials { get; set; }

    [Setting("Render")]
    public virtual int TilesX { get; set; }

    [Setting("Render")]
    public virtual int TilesY { get; set; }

    [Setting("Render")]
    public virtual int TileOverlap { get; set; }

    [Setting("Render")]
    public virtual string VideoContainer { get; set; } = null!;

    [Setting("Render")]
    public virtual string VideoCodec { get; set; } = null!;

    /// <summary>CRF quality for the CRF-driven video presets.</summary>
    [Setting("Render")]
    public virtual int VideoCrf { get; set; }

    #endregion

    #region Target

    /// <summary>Run on the whole network when allowed (MX-10); when off, the last group is used.</summary>
    [Setting("Target")]
    public virtual bool UseAllClients { get; set; }

    /// <summary>Last selected render target group id (empty = unset). Sticky fallback.</summary>
    [Setting("Target")]
    public virtual string LastGroupId { get; set; } = null!;

    /// <summary>Display name of the last target group, so the UI can show it even if it disappears.</summary>
    [Setting("Target")]
    public virtual string LastGroupName { get; set; } = null!;

    #endregion

    #region Diagnostics

    /// <summary>Minimum log level shown/written: "Information" (default), "Debug", "Warning", "Error".</summary>
    [Setting("Diagnostics")]
    public virtual string LogLevel { get; set; } = null!;

    #endregion
}
