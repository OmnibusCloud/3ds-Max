using System;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Immutable render/connection status snapshot: a phase, a human status line, and optional progress.
/// Drives the status bar (MX-5/6) and the gated Render/Export actions (MX-17). Factory helpers build
/// the canonical states so callers never set inconsistent fields. Mirrors the Blender bridge status
/// view; the active-job states are produced from server job status, not local guesses.
/// </summary>
/// <remarks>
/// An active farm job carries TWO independent progress axes, exactly as the server reports them
/// (<c>ProcessingJobInfo</c>): <see cref="Progress"/> is the engine's coarse stage fraction — the whole
/// distributed render is ONE opaque Grid.ForEach stage, so it sits flat (typically at 50%) for the
/// entire render — and <see cref="ComputationProgress"/> is the fine-grained "completed sub-tasks over
/// assigned sub-tasks" fraction fed by the node heartbeat. Collapsing the two into one bar is what made
/// the dialog look hung; the Blender addon draws both for the same reason.
/// </remarks>
public sealed class MaxRenderStatus
{
    #region Constants

    /// <summary>Unit name for frame-sequence jobs, where one distributed sub-task is one frame.</summary>
    public const string UNIT_FRAMES = "frames";

    /// <summary>Unit name for tiled stills, where one distributed sub-task is one tile.</summary>
    public const string UNIT_TILES = "tiles";

    #endregion

    #region Functions

    /// <summary>No cloud connection.</summary>
    public static MaxRenderStatus Disconnected() =>
        new() { Phase = MaxRenderPhase.Disconnected, StatusLine = "Disconnected" };

    /// <summary>Connecting / restoring a session.</summary>
    public static MaxRenderStatus Connecting() =>
        new() { Phase = MaxRenderPhase.Connecting, StatusLine = "Connecting…" };

    /// <summary>Cloud endpoint unreachable.</summary>
    public static MaxRenderStatus CloudUnreachable() =>
        new() { Phase = MaxRenderPhase.CloudUnreachable, StatusLine = "Cloud unreachable" };

    /// <summary>Sign-in required.</summary>
    public static MaxRenderStatus SignedOut() =>
        new() { Phase = MaxRenderPhase.SignedOut, StatusLine = "Sign in required" };

    /// <summary>Ready to render.</summary>
    public static MaxRenderStatus Ready() =>
        new() { Phase = MaxRenderPhase.Ready, StatusLine = "Ready" };

    /// <summary>A single actionable blocker.</summary>
    public static MaxRenderStatus Blocked(string message) =>
        new() { Phase = MaxRenderPhase.Blocked, StatusLine = string.IsNullOrWhiteSpace(message) ? "Blocked" : message };

    /// <summary>Submitting the job to the farm.</summary>
    public static MaxRenderStatus Submitting() =>
        new() { Phase = MaxRenderPhase.Submitting, StatusLine = "Submitting…" };

    /// <summary>Uploading the scene payload, with a 0..1 progress fraction.</summary>
    public static MaxRenderStatus Uploading(double fraction) =>
        new() { Phase = MaxRenderPhase.Uploading, Progress = Clamp01(fraction), StatusLine = $"Uploading {Percent(fraction)}%" };

    /// <summary>
    /// Rendering on the farm, carrying both server progress axes.
    /// </summary>
    /// <param name="overallFraction">The engine's coarse stage fraction (0..1).</param>
    /// <param name="computationFraction">
    /// The distributed sub-task fraction (0..1), or null when the server reports no distributed work yet
    /// — the computation bar then stays hidden instead of mirroring the coarse axis.
    /// </param>
    /// <param name="unitsCompleted">Completed distributed units (frames / tiles) when countable.</param>
    /// <param name="unitsTotal">Total distributed units when countable.</param>
    /// <param name="unitName">Plural noun for the units, e.g. <see cref="UNIT_FRAMES"/>.</param>
    /// <returns>The running status.</returns>
    public static MaxRenderStatus Running(
        double overallFraction,
        double? computationFraction = null,
        int? unitsCompleted = null,
        int? unitsTotal = null,
        string unitName = "")
    {
        var computation = computationFraction is { } value ? Clamp01(value) : (double?)null;
        var hasUnits = unitsCompleted is not null && unitsTotal is > 0 && !string.IsNullOrEmpty(unitName);

        return new MaxRenderStatus
        {
            Phase = MaxRenderPhase.Running,
            Progress = Clamp01(overallFraction),
            ComputationProgress = computation,
            UnitsCompleted = hasUnits ? unitsCompleted : null,
            UnitsTotal = hasUnits ? unitsTotal : null,
            UnitName = hasUnits ? unitName : string.Empty,
            StatusLine = hasUnits
                ? $"Rendering {unitsCompleted}/{unitsTotal}"
                : computation is { } fraction
                    ? $"Rendering {Percent(fraction)}%"
                    : "Rendering…"
        };
    }

    /// <summary>Finalizing the result after rendering (stitching / encoding / result upload).</summary>
    /// <param name="overallFraction">The engine's coarse stage fraction, when known.</param>
    /// <param name="computationFraction">The distributed fraction, when known (normally 1 by now).</param>
    /// <returns>The finalizing status.</returns>
    public static MaxRenderStatus Finalizing(double? overallFraction = null, double? computationFraction = null) =>
        new()
        {
            Phase = MaxRenderPhase.Finalizing,
            Progress = overallFraction is { } overall ? Clamp01(overall) : null,
            ComputationProgress = computationFraction is { } computation ? Clamp01(computation) : null,
            StatusLine = "Finalizing…"
        };

    /// <summary>Terminal: completed.</summary>
    public static MaxRenderStatus Completed() =>
        new() { Phase = MaxRenderPhase.Completed, StatusLine = "Completed" };

    /// <summary>Terminal: failed.</summary>
    public static MaxRenderStatus Failed(string message) =>
        new() { Phase = MaxRenderPhase.Failed, StatusLine = string.IsNullOrWhiteSpace(message) ? "Failed" : message };

    /// <summary>Cancel requested (transitional).</summary>
    public static MaxRenderStatus Cancelling() =>
        new() { Phase = MaxRenderPhase.Cancelling, StatusLine = "Cancelling…" };

    /// <summary>Terminal: cancelled.</summary>
    public static MaxRenderStatus Cancelled() =>
        new() { Phase = MaxRenderPhase.Cancelled, StatusLine = "Cancelled" };

    #endregion

    #region Tools

    private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;

    private static int Percent(double fraction) => (int)Math.Round(Clamp01(fraction) * 100d);

    #endregion

    #region Properties

    /// <summary>The lifecycle phase.</summary>
    public MaxRenderPhase Phase { get; init; }

    /// <summary>Human-readable single-line status for the status bar.</summary>
    public string StatusLine { get; init; } = string.Empty;

    /// <summary>
    /// Coarse progress fraction 0..1 (the upload fraction, or the engine's stage fraction for an active
    /// job); null when indeterminate / none. Flat for the whole distributed render — the fine-grained
    /// view is <see cref="ComputationProgress"/>.
    /// </summary>
    public double? Progress { get; init; }

    /// <summary>
    /// Fine-grained distributed "computation" fraction 0..1 (completed sub-tasks over assigned sub-tasks,
    /// fed by the node heartbeat); null when the job reports no distributed work.
    /// </summary>
    public double? ComputationProgress { get; init; }

    /// <summary>Completed distributed units (frames / tiles) when countable, else null.</summary>
    public int? UnitsCompleted { get; init; }

    /// <summary>Total distributed units when countable, else null.</summary>
    public int? UnitsTotal { get; init; }

    /// <summary>Plural noun for the distributed units; empty when the units are not countable.</summary>
    public string UnitName { get; init; } = string.Empty;

    /// <summary>Completed frames when the distributed units are frames, else null.</summary>
    public int? FramesCompleted => UnitName == UNIT_FRAMES ? UnitsCompleted : null;

    /// <summary>Total frames when the distributed units are frames, else null.</summary>
    public int? FramesTotal => UnitName == UNIT_FRAMES ? UnitsTotal : null;

    /// <summary>True while a farm job is active (submit → finalize / cancelling).</summary>
    public bool IsActiveJob => Phase is MaxRenderPhase.Submitting or MaxRenderPhase.Uploading
        or MaxRenderPhase.Running or MaxRenderPhase.Finalizing or MaxRenderPhase.Cancelling;

    /// <summary>True in a terminal job state.</summary>
    public bool IsTerminal => Phase is MaxRenderPhase.Completed or MaxRenderPhase.Failed or MaxRenderPhase.Cancelled;

    /// <summary>True when a render can be submitted.</summary>
    public bool IsReady => Phase == MaxRenderPhase.Ready;

    /// <summary>True when the status bar should show a determinate progress bar.</summary>
    public bool HasDeterminateProgress => Progress is >= 0d;

    /// <summary>True when the computation bar carries real server data (the job distributes work).</summary>
    public bool HasComputationProgress => ComputationProgress is >= 0d;

    #endregion
}
