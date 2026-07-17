namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Persisted metadata for the first local launch package prepared by the 3ds Max plugin.
/// </summary>
public sealed class MaxSceneLaunchPackageManifest
{
    #region Properties

    public string PackageId { get; set; } = string.Empty;

    public DateTime PreparedUtc { get; set; }

    public string CloudUrl { get; set; } = string.Empty;

    public string IdentityUrl { get; set; } = string.Empty;

    public string RenderMode { get; set; } = string.Empty;

    public int ResolutionX { get; set; }

    public int ResolutionY { get; set; }

    public int FrameStart { get; set; }

    public int FrameEnd { get; set; }

    public int Samples { get; set; }

    public bool UseAllClients { get; set; }

    public string SelectedGroupName { get; set; } = string.Empty;

    public string SelectedProjectName { get; set; } = string.Empty;

    public string JsonArtifactPath { get; set; } = string.Empty;

    public string MemoryPackGzipArtifactPath { get; set; } = string.Empty;

    public string PackageArchivePath { get; set; } = string.Empty;

    #endregion
}
