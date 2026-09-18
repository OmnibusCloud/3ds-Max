namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Resolves the frame a still render uses. A still used to render the FIRST frame of the scene's range,
/// whatever frame the artist was looking at; it now renders the time slider's frame, bounded by the
/// scene's animation range — the capture samples animated channels across exactly that range, so a frame
/// outside it would silently render the pose held at the nearest end.
/// </summary>
public static class MaxStillFrameResolver
{
    #region Functions

    /// <summary>
    /// The still frame for a requested (or slider) frame: clamped into the scene range, or the range
    /// start when there is no frame at all (no host).
    /// </summary>
    /// <param name="frame">The requested frame, or null when unknown.</param>
    /// <param name="rangeStart">First frame of the scene's animation range.</param>
    /// <param name="rangeEnd">Last frame of the scene's animation range.</param>
    /// <returns>A frame within the range.</returns>
    public static int Resolve(int? frame, int rangeStart, int rangeEnd)
    {
        // A degenerate range (end before start) collapses onto its start rather than throwing.
        var end = Math.Max(rangeStart, rangeEnd);

        if (frame is not { } value)
            return rangeStart;

        return Math.Clamp(value, rangeStart, end);
    }

    /// <summary>True when the frame lies within the scene's animation range.</summary>
    /// <param name="frame">The frame to check.</param>
    /// <param name="rangeStart">First frame of the scene's animation range.</param>
    /// <param name="rangeEnd">Last frame of the scene's animation range.</param>
    /// <returns>True when <paramref name="frame"/> is inside the range, inclusive.</returns>
    public static bool IsWithinRange(int frame, int rangeStart, int rangeEnd) =>
        frame >= rangeStart && frame <= Math.Max(rangeStart, rangeEnd);

    #endregion
}
