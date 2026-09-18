namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// No-op <see cref="IMaxTimeSliderService"/> used in tests and when the plugin runs without a Max host:
/// there is no slider to read, so a still falls back to the start of the scene's range. Stateless
/// singleton.
/// </summary>
public sealed class MaxTimeSliderServiceNull : IMaxTimeSliderService
{
    #region Fields

    public static readonly MaxTimeSliderServiceNull Instance = new();

    #endregion

    #region IMaxTimeSliderService

    public event Action<int>? FrameChanged
    {
        add { }
        remove { }
    }

    public void SetCurrentFrame(int frame)
    {
    }

    public int? CurrentFrame => null;

    #endregion
}
