namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneTransformSnapshotData
{
    #region Properties

    public MaxSceneVector3SnapshotData Translation { get; set; } = new();

    public MaxSceneQuaternionSnapshotData Rotation { get; set; } = new();

    public MaxSceneVector3SnapshotData Scale { get; set; } = new() { X = 1d, Y = 1d, Z = 1d };

    #endregion
}
