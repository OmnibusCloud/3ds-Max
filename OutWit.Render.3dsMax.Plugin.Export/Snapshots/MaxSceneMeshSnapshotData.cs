namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneMeshSnapshotData
{
    #region Properties

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<MaxSceneVector3SnapshotData> Positions { get; set; } = [];

    public List<MaxSceneVector3SnapshotData> Normals { get; set; } = [];

    public List<MaxSceneVector2SnapshotData> Uv0 { get; set; } = [];

    public List<int> TriangleIndices { get; set; } = [];

    public List<int> MaterialIndices { get; set; } = [];

    #endregion
}
