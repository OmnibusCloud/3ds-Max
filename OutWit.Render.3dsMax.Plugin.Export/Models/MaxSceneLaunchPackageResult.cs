namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Result of preparing a local launch package for later OmnibusCloud submission.
/// </summary>
public sealed class MaxSceneLaunchPackageResult
{
    #region Properties

    public bool IsSuccess { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string PackageFolderPath { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;

    public string PackageArchivePath { get; set; } = string.Empty;

    public string PrimaryArtifactPath { get; set; } = string.Empty;

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    #endregion
}
