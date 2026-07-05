namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Represents the current plugin-side connected render job state for the first local launch-package phase.
/// </summary>
public sealed class MaxConnectedRenderJobState
{
    #region Properties

    public string JobId { get; set; } = string.Empty;

    public string CloudUrl { get; set; } = string.Empty;

    /// <summary>
    /// The output mode the job was submitted with (e.g. RenderStill, RenderVideo, ExportBlend).
    /// Determines the result file extension when the result blob is downloaded.
    /// </summary>
    public string RenderMode { get; set; } = string.Empty;

    public string StatusText { get; set; } = string.Empty;

    public double ProgressPercent { get; set; }

    public bool IsCompleted { get; set; }

    /// <summary>
    /// True once the farm reports the job as cancelled (terminal, distinct from failure).
    /// </summary>
    public bool IsCancelled { get; set; }

    public bool IsPlaceholderLocalSubmission { get; set; }

    /// <summary>
    /// The submitted frame range; used to name per-frame results for frame-sequence jobs.
    /// </summary>
    public int FrameStart { get; set; }

    public int FrameEnd { get; set; }

    public DateTime SubmittedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public string PackageFolderPath { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;

    public string SubmissionReceiptPath { get; set; } = string.Empty;

    public string PackageArchivePath { get; set; } = string.Empty;

    public string PrimaryArtifactPath { get; set; } = string.Empty;

    public Guid? UploadedPackageBlobId { get; set; }

    public Guid? ResultBlobId { get; set; }

    /// <summary>
    /// Per-frame result blob ids for frame-sequence jobs (RenderFrames returns a blob collection).
    /// </summary>
    public List<Guid> ResultFrameBlobIds { get; set; } = [];

    public string UploadReceiptPath { get; set; } = string.Empty;

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    #endregion
}
