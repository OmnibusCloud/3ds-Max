using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneLightSnapshotData
{
    #region Properties

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DccLightKind Kind { get; set; } = DccLightKind.Point;

    public MaxSceneColorSnapshotData Color { get; set; } = new();

    public double Intensity { get; set; } = 1d;

    public double Range { get; set; } = 10d;

    public double SpotAngleDegrees { get; set; } = 45d;

    #endregion
}
