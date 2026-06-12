using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneTextureSlotSnapshotData
{
    #region Properties

    public DccTextureSlotKind Slot { get; set; }

    public string ImageAssetId { get; set; } = string.Empty;

    #endregion
}
