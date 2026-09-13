using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Maps a refreshed <see cref="MaxConnectedRenderJobState"/> onto the presentable
/// <see cref="MaxRenderStatus"/>. Lives next to the transport (not in a ViewModel) so the phase and
/// progress rules are unit-testable without a Max host or a WPF dispatcher.
/// </summary>
/// <remarks>
/// Both server axes are carried through: the coarse engine fraction drives the overall bar, and the
/// distributed fraction drives the computation bar and every number the user reads. For a job whose
/// sub-tasks are countable (frames of a sequence / tiles of a still) the distributed fraction is turned
/// back into a unit counter, floored — the last unit is never claimed before the farm reports it.
/// </remarks>
public static class MaxConnectedRenderJobStatusMapper
{
    #region Constants

    /// <summary>Below this coarse fraction, with no distributed work yet, the job is still starting.</summary>
    private const double SUBMITTING_CEILING = 0.1d;

    /// <summary>At or above this fraction an axis counts as done (float noise on 1.0).</summary>
    private const double DONE_FLOOR = 0.999d;

    /// <summary>Coarse fraction above which the engine is assembling the result, not rendering.</summary>
    private const double FINALIZING_FLOOR = 0.99d;

    #endregion

    #region Functions

    /// <summary>
    /// Maps the job state to a render status.
    /// </summary>
    /// <param name="jobState">The job state, as last refreshed from the server.</param>
    /// <param name="cancelRequested">True when a cancel was requested and has not landed yet.</param>
    /// <returns>The status to present.</returns>
    public static MaxRenderStatus Map(MaxConnectedRenderJobState jobState, bool cancelRequested = false)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        if (jobState.IsCancelled)
            return MaxRenderStatus.Cancelled();

        if (jobState.IsCompleted)
            return MaxRenderStatus.Completed();

        if (HasFailed(jobState))
            return MaxRenderStatus.Failed(jobState.StatusText);

        if (cancelRequested)
            return MaxRenderStatus.Cancelling();

        var overall = Clamp01(jobState.ProgressPercent / 100d);

        // Null, not zero: no distributed work reported yet means "no fine-grained data", and the
        // computation bar must stay hidden rather than mirror the coarse axis at 0.
        var computation = jobState.DistributedProgressPercent > 0d
            ? Clamp01(jobState.DistributedProgressPercent / 100d)
            : (double?)null;

        if (computation is null && overall < SUBMITTING_CEILING)
            return MaxRenderStatus.Submitting();

        // The farm finished the distributed work; what is left is stitching / encoding / uploading the
        // result. Reporting that honestly is what the old "stuck at 99%" was missing.
        if (computation >= DONE_FLOOR || overall >= FINALIZING_FLOOR)
            return MaxRenderStatus.Finalizing(overall, computation);

        var (unitsTotal, unitName) = ResolveUnits(jobState);
        var unitsCompleted = computation is { } fraction && unitsTotal > 0
            ? (int)Math.Floor(fraction * unitsTotal)
            : (int?)null;

        return MaxRenderStatus.Running(
            overall,
            computation,
            unitsCompleted,
            unitsTotal > 0 ? unitsTotal : null,
            unitsTotal > 0 ? unitName : string.Empty);
    }

    /// <summary>
    /// True when the job is in a terminal failure state.
    /// </summary>
    /// <param name="jobState">The job state to test.</param>
    /// <returns>True when the job failed.</returns>
    public static bool HasFailed(MaxConnectedRenderJobState jobState)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        if (jobState.IsCompleted || jobState.IsCancelled)
            return false;

        if (jobState.IsFailed)
            return true;

        // A job that reached the farm is failed ONLY when the server says so. Sniffing the status text
        // of such a job read a transient "Job refresh failed." (one dropped poll) as a dead render and
        // stopped tracking it; the farm kept rendering and the result was never collected.
        if (Guid.TryParse(jobState.JobId, out _))
            return false;

        // Nothing to poll: blocked preflight, a failed submission, or the local placeholder transport.
        // For those the status text IS the failure report.
        return !string.IsNullOrWhiteSpace(jobState.StatusText)
               && (jobState.StatusText.Contains("Failed", StringComparison.OrdinalIgnoreCase)
                   || jobState.StatusText.Contains("Cancelled", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Tools

    /// <summary>
    /// Resolves how many distributed sub-tasks the job carries and what to call them: one sub-task is
    /// one frame for a sequence or a video, one tile for a tiled still. A plain still distributes work
    /// the plugin cannot count, so it gets a percentage instead of a fake counter.
    /// </summary>
    private static (int UnitsTotal, string UnitName) ResolveUnits(MaxConnectedRenderJobState jobState)
    {
        switch (jobState.RenderMode)
        {
            case "RenderFrames":
            case "RenderVideo":
                var frames = jobState.FrameEnd - jobState.FrameStart + 1;
                return frames > 1 ? (frames, MaxRenderStatus.UNIT_FRAMES) : (0, string.Empty);

            case "RenderStillTiled":
                return jobState.TileCount > 1 ? (jobState.TileCount, MaxRenderStatus.UNIT_TILES) : (0, string.Empty);

            default:
                return (0, string.Empty);
        }
    }

    private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;

    #endregion
}
