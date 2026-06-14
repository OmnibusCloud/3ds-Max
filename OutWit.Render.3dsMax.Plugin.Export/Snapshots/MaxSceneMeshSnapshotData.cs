namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneMeshSnapshotData
{
    #region Properties

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<MaxSceneVector3SnapshotData> Positions { get; set; } = [];

    public List<MaxSceneVector3SnapshotData> Normals { get; set; } = [];

    public List<MaxSceneVector2SnapshotData> Uv0 { get; set; } = [];

    public List<MaxSceneVector2SnapshotData> Uv1 { get; set; } = [];

    public List<int> TriangleIndices { get; set; } = [];

    public List<int> MaterialIndices { get; set; } = [];

    // Optional per-corner vertex colours, aligned 1:1 with Positions. Empty when the mesh has no
    // colour layer (the mapper then emits no colour attribute).
    public List<MaxSceneColorSnapshotData> Colors { get; set; } = [];

    // Optional baked per-frame deformation (skin/morph/cloth/sim). Empty for static meshes; each
    // frame's Positions count matches the base Positions count.
    public List<MaxSceneMeshDeformationFrameSnapshotData> DeformationFrames { get; set; } = [];

    #endregion
}
