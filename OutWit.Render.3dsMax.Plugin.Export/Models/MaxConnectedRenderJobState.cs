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
    /// Together with <see cref="ImageFormat"/> / <see cref="VideoPreset"/> it names the downloaded result.
    /// </summary>
    public string RenderMode { get; set; } = string.Empty;

    /// <summary>
    /// The image format the artist chose (stills, tiled stills, frame sequences), e.g. "JPEG". The result
    /// used to be saved as .png whatever was chosen — a JPEG, EXR, TIFF or WEBP under a .png name.
    /// Empty on records written before it was kept; the downloaded bytes then decide.
    /// </summary>
    public string ImageFormat { get; set; } = string.Empty;

    /// <summary>
    /// The video preset key the artist chose (e.g. "webm-vp9"); a video used to be saved as .mp4 whatever
    /// the container. Empty on older records.
    /// </summary>
    public string VideoPreset { get; set; } = string.Empty;

    /// <summary>
    /// The folder the artist chose for the result ("Save to"). Kept on the job and its persisted record
    /// because the result arrives long after the dialog that launched it may have closed — even after a
    /// 3ds Max restart. Empty keeps the result where it was downloaded (older records, batch flows).
    /// </summary>
    public string ResultFolder { get; set; } = string.Empty;

    /// <summary>The name the result is saved under (the scene name); see <see cref="ResultFolder"/>.</summary>
    public string ResultName { get; set; } = string.Empty;

    public string StatusText { get; set; } = string.Empty;

    /// <summary>
    /// Coarse engine progress (0..100): processed activities over the job's stage count. The whole
    /// distributed render is ONE opaque stage, so this sits flat (typically at 50%) while the farm
    /// renders — <see cref="DistributedProgressPercent"/> is the axis that moves.
    /// </summary>
    public double ProgressPercent { get; set; }

    /// <summary>
    /// Fine-grained distributed progress (0..100): completed sub-tasks over assigned sub-tasks across
    /// the job's node assignments, fed by the node progress heartbeat. 0 while the job has reported no
    /// distributed work yet (and for jobs that never distribute any).
    /// </summary>
    public double DistributedProgressPercent { get; set; }

    public bool IsCompleted { get; set; }

    /// <summary>
    /// True once the farm reports the job as cancelled (terminal, distinct from failure).
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// True once the farm reports the job as failed (terminal). Reported by the transport from the
    /// server job status, so a restored job does not have to sniff <see cref="StatusText"/>.
    /// </summary>
    public bool IsFailed { get; set; }

    /// <summary>
    /// The raw server job status name (e.g. Pending / Processing / Completed); empty before the first
    /// refresh. Kept for the diagnostics log and the restored-job view.
    /// </summary>
    public string ServerStatus { get; set; } = string.Empty;

    /// <summary>
    /// Number of tiles the job was submitted with (TilesX * TilesY) for a tiled still, else 0. One
    /// distributed sub-task is one tile, so this makes the computation bar countable.
    /// </summary>
    public int TileCount { get; set; }

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
