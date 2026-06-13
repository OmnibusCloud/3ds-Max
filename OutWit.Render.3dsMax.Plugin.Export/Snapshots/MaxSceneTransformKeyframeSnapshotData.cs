namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

/// <summary>
/// One sampled node-transform keyframe (frame + local transform) for transform animation.
/// </summary>
public sealed class MaxSceneTransformKeyframeSnapshotData
{
    #region Properties

    public int Frame { get; set; }

    public MaxSceneTransformSnapshotData Transform { get; set; } = new();

    #endregion
}
