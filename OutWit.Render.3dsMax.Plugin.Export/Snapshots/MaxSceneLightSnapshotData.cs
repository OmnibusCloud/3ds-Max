using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneLightSnapshotData
{
    #region Properties

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DccLightKind Kind { get; set; } = DccLightKind.Point;

    public MaxSceneColorSnapshotData Color { get; set; } = new();

    public List<MaxSceneColorKeyframeSnapshotData> ColorKeyframes { get; set; } = [];

    public double Intensity { get; set; } = 1d;

    // True for photometric (ILightscapeLight) lights, whose Intensity is a physical value (candela/lux)
    // that the mapper normalizes before power calibration so it does not overexpose the render.
    public bool IsPhotometric { get; set; }

    public List<MaxSceneScalarKeyframeSnapshotData> IntensityKeyframes { get; set; } = [];

    public double Range { get; set; } = 10d;

    public List<MaxSceneScalarKeyframeSnapshotData> RangeKeyframes { get; set; } = [];

    public double SpotAngleDegrees { get; set; } = 45d;

    // Spot edge softness [0,1] from the Max hotspot/falloff cone difference (0 = hard edge).
    public double SpotBlend { get; set; }

    public List<MaxSceneScalarKeyframeSnapshotData> SpotAngleKeyframes { get; set; } = [];

    public bool CastShadows { get; set; } = true;

    public double AreaWidth { get; set; } = 1d;

    public double AreaHeight { get; set; } = 1d;

    #endregion
}
