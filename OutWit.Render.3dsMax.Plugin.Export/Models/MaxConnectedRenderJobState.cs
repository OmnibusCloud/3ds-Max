namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Represents the current plugin-side connected render job state for the first local launch-package phase.
/// </summary>
public sealed class MaxConnectedRenderJobState
{
    #region Properties

    public string JobId { get; set; } = string.Empty;

    public string CloudUrl { get; set; } = string.Empty;

    public string StatusText { get; set; } = string.Empty;

    public double ProgressPercent { get; set; }

    public bool IsCompleted { get; set; }

    public bool IsPlaceholderLocalSubmission { get; set; }

    public DateTime SubmittedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public string PackageFolderPath { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;

    public string SubmissionReceiptPath { get; set; } = string.Empty;

    public string PackageArchivePath { get; set; } = string.Empty;

    public string PrimaryArtifactPath { get; set; } = string.Empty;

    public Guid? UploadedPackageBlobId { get; set; }

    public Guid? ResultBlobId { get; set; }

    public string UploadReceiptPath { get; set; } = string.Empty;

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    #endregion
}
