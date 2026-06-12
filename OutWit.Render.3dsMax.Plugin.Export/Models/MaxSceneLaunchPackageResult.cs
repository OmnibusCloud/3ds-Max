using OutWit.Controller.Render.Dcc.Model;

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

    /// <summary>
    /// The exported neutral scene payload (in-memory only — what the package artifacts were written from).
    /// </summary>
    public DccSceneData? Scene { get; set; }

    /// <summary>
    /// The source .max file path; used to resolve relative texture paths during attachment upload.
    /// </summary>
    public string SceneFilePath { get; set; } = string.Empty;

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    #endregion
}
