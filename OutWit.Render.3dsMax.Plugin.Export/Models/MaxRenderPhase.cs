namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Render/connection lifecycle phase, ported from the Blender bridge status model
/// (<c>bridge_status.py</c>) and adapted for the in-process 3ds Max plugin (no separate bridge
/// process, so no <c>BridgeMissing</c>). The source of truth for the active-job phases is the
/// server job status, not a local guess.
/// </summary>
public enum MaxRenderPhase
{
    /// <summary>No cloud connection has been attempted.</summary>
    Disconnected,

    /// <summary>A connection / session restore is in progress.</summary>
    Connecting,

    /// <summary>The cloud endpoint could not be reached.</summary>
    CloudUnreachable,

    /// <summary>No user session — sign-in required before Render/Export.</summary>
    SignedOut,

    /// <summary>Connected and signed in; a render can be submitted.</summary>
    Ready,

    /// <summary>A single actionable blocker prevents rendering (e.g. unsaved scene).</summary>
    Blocked,

    /// <summary>A job is being submitted to the farm.</summary>
    Submitting,

    /// <summary>The scene payload is uploading (carries a progress fraction).</summary>
    Uploading,

    /// <summary>The job is rendering (carries frames completed / total).</summary>
    Running,

    /// <summary>The job has finished rendering and is finalizing the result.</summary>
    Finalizing,

    /// <summary>A cancel was requested and is propagating (transitional).</summary>
    Cancelling,

    /// <summary>Terminal: the job completed successfully.</summary>
    Completed,

    /// <summary>Terminal: the job failed.</summary>
    Failed,

    /// <summary>Terminal: the job was cancelled.</summary>
    Cancelled
}
