using System.Windows.Threading;
using Autodesk.Max;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

/// <summary>
/// The 3ds Max time slider behind <see cref="IMaxTimeSliderService"/>: reads the current frame from
/// <see cref="IInterface.Time"/>, moves it with <see cref="IInterface.SetTime"/>, and reports changes
/// by sampling the current time on the Max UI thread while anyone is listening.
/// </summary>
/// <remarks>
/// Sampling, not the SDK's <c>RegisterTimeChangeCallback</c>: the managed wrapper exposes
/// <c>ITimeChangeCallback</c> only as an interface with no managed base class to derive from, and
/// handing a plain C# implementation to the native registry is not something the wrapper promises to
/// marshal. A quarter-second sample costs one property read, runs only while a Render dialog is open,
/// and stops the moment the last listener leaves. Every call is guarded: a slider hiccup must never
/// break the dialog.
/// </remarks>
public sealed class MaxTimeSliderService : IMaxTimeSliderService
{
    #region Constants

    /// <summary>How often the current time is sampled while someone listens.</summary>
    private static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromMilliseconds(250);

    #endregion

    #region Fields

    private readonly IGlobal m_global;

    private readonly IInterface m_coreInterface;

    private Action<int>? m_frameChanged;

    private DispatcherTimer? m_timer;

    private int? m_lastFrame;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the service over the running 3ds Max session.
    /// </summary>
    /// <param name="global">The Max global interface (ticks per frame).</param>
    /// <param name="coreInterface">The Max core interface (current time).</param>
    public MaxTimeSliderService(IGlobal global, IInterface coreInterface)
    {
        m_global = global;
        m_coreInterface = coreInterface;
    }

    #endregion

    #region Tools

    private void UpdatePolling()
    {
        if (m_frameChanged is null)
        {
            m_timer?.Stop();
            m_timer = null;
            return;
        }

        if (m_timer is not null)
            return;

        // Subscribed from a dialog built on the Max main thread, so this is the dispatcher Max pumps.
        // Background priority: a sample must never compete with input or with Max's own playback.
        m_lastFrame = CurrentFrame;
        m_timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
        {
            Interval = POLL_INTERVAL
        };
        m_timer.Tick += OnPollTick;
        m_timer.Start();
    }

    private int ResolveTicksPerFrame() => MaxHostApplicationService.ResolveTicksPerFrame(m_global);

    #endregion

    #region Event Handlers

    private void OnPollTick(object? sender, EventArgs e)
    {
        var frame = CurrentFrame;
        if (frame is not { } value || frame == m_lastFrame)
            return;

        m_lastFrame = value;
        m_frameChanged?.Invoke(value);
    }

    #endregion

    #region IMaxTimeSliderService

    public event Action<int>? FrameChanged
    {
        add
        {
            m_frameChanged += value;
            UpdatePolling();
        }
        remove
        {
            m_frameChanged -= value;
            UpdatePolling();
        }
    }

    public void SetCurrentFrame(int frame)
    {
        try
        {
            var ticksPerFrame = ResolveTicksPerFrame();
            if (ticksPerFrame <= 0)
                return;

            m_coreInterface.SetTime(frame * ticksPerFrame, true);

            // Our own move is not news to the listener that asked for it.
            m_lastFrame = frame;
        }
        catch
        {
            // Best-effort: the render still carries the frame even if the slider could not follow.
        }
    }

    public int? CurrentFrame
    {
        get
        {
            try
            {
                var ticksPerFrame = ResolveTicksPerFrame();
                if (ticksPerFrame <= 0)
                    return null;

                // Floor, not truncation: a sub-frame time just below zero is frame -1 in Max, not 0.
                return (int)Math.Floor(m_coreInterface.Time / (double)ticksPerFrame);
            }
            catch
            {
                return null;
            }
        }
    }

    #endregion
}
