using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal sealed class FakeMaxSceneSnapshotProvider : IMaxSceneSnapshotProvider
{
    #region Constructors

    public FakeMaxSceneSnapshotProvider(MaxSceneSnapshotData snapshot)
    {
        Snapshot = snapshot;
    }

    #endregion

    #region Functions

    public MaxSceneSnapshotData Capture(MaxSceneCaptureOptions captureOptions)
    {
        LastCaptureOptions = captureOptions;
        return Snapshot;
    }

    #endregion

    #region Properties

    public MaxSceneSnapshotData Snapshot { get; }

    public MaxSceneCaptureOptions? LastCaptureOptions { get; private set; }

    #endregion
}
