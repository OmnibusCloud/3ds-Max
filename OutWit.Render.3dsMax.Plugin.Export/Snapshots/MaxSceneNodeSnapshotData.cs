using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneNodeSnapshotData
{
    #region Properties

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? ParentId { get; set; }

    public DccNodeKind Kind { get; set; }

    public MaxSceneTransformSnapshotData LocalTransform { get; set; } = new();

    public List<MaxSceneTransformKeyframeSnapshotData> TransformKeyframes { get; set; } = [];

    public string? MeshId { get; set; }

    public string? CameraId { get; set; }

    public string? LightId { get; set; }

    public string? MaterialBindingId { get; set; }

    public bool Visible { get; set; } = true;

    public bool Renderable { get; set; } = true;

    #endregion
}
