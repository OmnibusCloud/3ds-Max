namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneColorKeyframeSnapshotData
{
    #region Properties

    public int Frame { get; set; }

    public MaxSceneColorSnapshotData Color { get; set; } = new();

    #endregion
}
