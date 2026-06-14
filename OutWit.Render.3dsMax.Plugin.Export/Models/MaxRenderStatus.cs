using System;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Immutable render/connection status snapshot: a phase, a human status line, and optional progress.
/// Drives the status bar (MX-5/6) and the gated Render/Export actions (MX-17). Factory helpers build
/// the canonical states so callers never set inconsistent fields. Mirrors the Blender bridge status
/// view; the active-job states are produced from server job status, not local guesses.
/// </summary>
public sealed class MaxRenderStatus
{
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

    /// <summary>Rendering, with frames completed / total.</summary>
    public static MaxRenderStatus Running(int framesCompleted, int framesTotal) =>
        new()
        {
            Phase = MaxRenderPhase.Running,
            FramesCompleted = framesCompleted,
            FramesTotal = framesTotal,
            Progress = framesTotal > 0 ? Clamp01((double)framesCompleted / framesTotal) : null,
            StatusLine = framesTotal > 0 ? $"Rendering {framesCompleted}/{framesTotal}" : "Rendering…"
        };

    /// <summary>Finalizing the result after rendering.</summary>
    public static MaxRenderStatus Finalizing() =>
        new() { Phase = MaxRenderPhase.Finalizing, StatusLine = "Finalizing…" };

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

    /// <summary>Progress fraction 0..1 (upload % or frame fraction); null when indeterminate / none.</summary>
    public double? Progress { get; init; }

    /// <summary>Frames completed when rendering, else null.</summary>
    public int? FramesCompleted { get; init; }

    /// <summary>Total frames when rendering, else null.</summary>
    public int? FramesTotal { get; init; }

    /// <summary>True while a farm job is active (submit → finalize / cancelling).</summary>
    public bool IsActiveJob => Phase is MaxRenderPhase.Submitting or MaxRenderPhase.Uploading
        or MaxRenderPhase.Running or MaxRenderPhase.Finalizing or MaxRenderPhase.Cancelling;

    /// <summary>True in a terminal job state.</summary>
    public bool IsTerminal => Phase is MaxRenderPhase.Completed or MaxRenderPhase.Failed or MaxRenderPhase.Cancelled;

    /// <summary>True when a render can be submitted.</summary>
    public bool IsReady => Phase == MaxRenderPhase.Ready;

    /// <summary>True when the status bar should show a determinate progress bar.</summary>
    public bool HasDeterminateProgress => Progress is >= 0d;

    #endregion
}
