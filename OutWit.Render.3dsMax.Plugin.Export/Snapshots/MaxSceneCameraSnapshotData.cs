namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneCameraSnapshotData
{
    #region Properties

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double VerticalFovDegrees { get; set; } = 45d;

    public List<MaxSceneScalarKeyframeSnapshotData> VerticalFovKeyframes { get; set; } = [];

    public double NearClip { get; set; } = 0.1d;

    public List<MaxSceneScalarKeyframeSnapshotData> NearClipKeyframes { get; set; } = [];

    public double FarClip { get; set; } = 1000d;

    public List<MaxSceneScalarKeyframeSnapshotData> FarClipKeyframes { get; set; } = [];

    public bool IsPerspective { get; set; } = true;

    public bool EnableDepthOfField { get; set; }

    public double FocusDistance { get; set; }

    public double FStop { get; set; } = 2.8d;

    // The authored EV100 of a physical camera (stock Physical / VRayPhysicalCamera); null for
    // ordinary cameras. The mapper carries its deviation from a photographic reference into the
    // render exposure.
    public double? ExposureEv { get; set; }

    #endregion
}
