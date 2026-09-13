using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

/// <summary>
/// Recording status-bar service: keeps every prompt-line report so a test can assert that progress
/// keeps flowing to the host with no dialog in the picture.
/// </summary>
internal sealed class FakeMaxStatusBarService : IMaxStatusBarService
{
    #region IMaxStatusBarService

    public void Report(MaxRenderStatus status)
    {
        Reports.Add(status);
    }

    public void Clear()
    {
        ClearCount++;
    }

    #endregion

    #region Properties

    public List<MaxRenderStatus> Reports { get; } = [];

    public int ClearCount { get; private set; }

    #endregion
}
