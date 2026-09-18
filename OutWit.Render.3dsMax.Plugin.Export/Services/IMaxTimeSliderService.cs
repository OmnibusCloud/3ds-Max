namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// The host's time slider: the frame 3ds Max currently shows. A still render follows it both ways,
/// the way the Blender addon's Frame field follows <c>scene.frame_current</c> — the artist scrubs to
/// the frame they want and renders what the viewport shows. The host implementation talks to
/// <c>IInterface.Time</c> / <c>SetTime</c>; the null implementation is used in tests and when no Max
/// host is present, so ViewModels can depend on this unconditionally.
/// </summary>
/// <remarks>
/// Frames use Max's own numbering (time in ticks divided by ticks per frame, no shift) — the same
/// numbering the scene capture samples, so frame N here is the instant Max renders at frame N.
/// </remarks>
public interface IMaxTimeSliderService
{
    /// <summary>
    /// Raised when the host's current frame changes (scrubbing, playback, keyboard stepping). Changes
    /// made through <see cref="SetCurrentFrame"/> are not echoed back. Raised on the host UI thread.
    /// </summary>
    event Action<int>? FrameChanged;

    /// <summary>Moves the host time slider to the frame and redraws the viewports.</summary>
    /// <param name="frame">The frame, in Max's own numbering.</param>
    void SetCurrentFrame(int frame);

    /// <summary>The frame the host time slider is on, or null when there is no host.</summary>
    int? CurrentFrame { get; }
}
