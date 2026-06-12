namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneMaterialSnapshotData
{
    #region Properties

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public MaxSceneColorSnapshotData BaseColor { get; set; } = new();

    public double Opacity { get; set; } = 1d;

    public double Metallic { get; set; }

    public double Roughness { get; set; } = 0.5d;

    public double NormalStrength { get; set; } = 1d;

    public List<MaxSceneTextureSlotSnapshotData> TextureSlots { get; set; } = [];

    #endregion
}
