using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneTextureSlotSnapshotData
{
    #region Properties

    public DccTextureSlotKind Slot { get; set; }

    public string ImageAssetId { get; set; } = string.Empty;

    // Authored map tiling from the bitmap's Coordinates rollout (UVGen), separate from mesh UVs —
    // Butterfly's bark tiles 10×10 there. 1 = untiled.
    public double UvScaleX { get; set; } = 1d;

    public double UvScaleY { get; set; } = 1d;

    // Centre-anchor compensation: 3ds Max tiles around the UV centre (0.5), the generator's
    // Mapping node around the origin — without this the repeats of a fractional tiling land at
    // shifted phases (the dragon-sky seam). 0 = no compensation.
    public double UvOffsetX { get; set; }

    public double UvOffsetY { get; set; }

    #endregion
}
