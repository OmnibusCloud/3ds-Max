namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Names what the server is doing to a <c>ExportBlend</c> job, for the Export dialog's status line.
/// </summary>
/// <remarks>
/// The wire contract carries no activity name — <c>ProcessingJobInfo</c> has a status and a single
/// stage-based fraction (completed activities over the script's activity count). The script is ours
/// though, and fixed: <c>RenderDccSceneExportBlendPacked</c> unzips the scene, builds the .blend and
/// clears the scene, in that order. The fraction therefore says exactly which of the three is running
/// (0 = the first, 1/3 = the second, 2/3 = the third), which is a phase name rather than the frozen
/// "33%" the dialog used to show for the whole build. An unexpected fraction falls back to a plain
/// "working" line instead of naming the wrong step.
/// </remarks>
public static class MaxExportBlendPhase
{
    #region Constants

    /// <summary>The job script's activities, in the order the server runs them.</summary>
    private static readonly string[] SERVER_PHASES =
    [
        "Unpacking the scene on the server…",
        "Building the Blender scene…",
        "Finishing up on the server…"
    ];

    /// <summary>Shown when the fraction matches no known activity of the script.</summary>
    private const string SERVER_PHASE_UNKNOWN = "Working on the server…";

    #endregion

    #region Functions

    /// <summary>
    /// The status line for a submitted export.
    /// </summary>
    /// <param name="serverStatus">The job's server status (<c>ProcessingJobStatus</c> as text).</param>
    /// <param name="overallPercent">The job's stage-based progress, 0..100.</param>
    /// <returns>A phase name; never empty.</returns>
    public static string Describe(string? serverStatus, double overallPercent)
    {
        // Before it runs the server says so itself, and no stage has started yet.
        if (Equals(serverStatus, "Pending") || Equals(serverStatus, "Scheduled"))
            return "Queued on the server…";

        if (Equals(serverStatus, "Distributing"))
            return "Starting on the server…";

        if (overallPercent < 0d)
            return SERVER_PHASE_UNKNOWN;

        var stage = (int)Math.Round(overallPercent / 100d * SERVER_PHASES.Length, MidpointRounding.AwayFromZero);
        return stage < SERVER_PHASES.Length ? SERVER_PHASES[stage] : SERVER_PHASE_UNKNOWN;
    }

    private static bool Equals(string? serverStatus, string expected) =>
        string.Equals(serverStatus, expected, StringComparison.OrdinalIgnoreCase);

    #endregion
}
