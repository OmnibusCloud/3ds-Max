using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// No-op <see cref="IMaxStatusBarService"/> used in tests and when the plugin runs without a Max host
/// (so ViewModels can report status unconditionally). Stateless singleton.
/// </summary>
public sealed class MaxStatusBarServiceNull : IMaxStatusBarService
{
    #region Fields

    public static readonly MaxStatusBarServiceNull Instance = new();

    #endregion

    #region IMaxStatusBarService

    public void Report(MaxRenderStatus status)
    {
    }

    public void Clear()
    {
    }

    #endregion
}
