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

    public double Transmission { get; set; }

    public double Ior { get; set; } = 1.45d;

    // Displacement height scale for the Displacement texture slot. Defaults to the contract default
    // (1.0); the collector overrides it from the material's displacement amount when present.
    public double DisplacementScale { get; set; } = 1d;

    public bool BackfaceCull { get; set; }

    public MaxSceneColorSnapshotData EmissionColor { get; set; } = new() { R = 0d, G = 0d, B = 0d, A = 1d };

    public double EmissionStrength { get; set; }

    public List<MaxSceneTextureSlotSnapshotData> TextureSlots { get; set; } = [];

    #endregion
}
