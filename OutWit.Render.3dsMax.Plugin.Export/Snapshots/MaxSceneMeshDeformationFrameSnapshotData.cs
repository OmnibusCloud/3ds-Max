namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

/// <summary>
/// One baked deformation frame: the full set of object-space vertex positions (aligned 1:1 with the
/// mesh base <see cref="MaxSceneMeshSnapshotData.Positions"/>) at a given frame. Carries
/// skin/morph/cloth/sim deformation as a vertex cache so the neutral payload represents the result
/// of arbitrary deformation independent of the source rig.
/// </summary>
public sealed class MaxSceneMeshDeformationFrameSnapshotData
{
    #region Properties

    public int Frame { get; set; }

    public List<MaxSceneVector3SnapshotData> Positions { get; set; } = [];

    #endregion
}
