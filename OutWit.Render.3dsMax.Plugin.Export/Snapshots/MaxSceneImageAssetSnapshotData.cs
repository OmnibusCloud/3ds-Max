namespace OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

public sealed class MaxSceneImageAssetSnapshotData
{
    #region Properties

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string AssetKind { get; set; } = "ImageAsset";

    #endregion
}
