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

    public MaxSceneSnapshotData Capture()
    {
        return Snapshot;
    }

    #endregion

    #region Properties

    public MaxSceneSnapshotData Snapshot { get; }

    #endregion
}
