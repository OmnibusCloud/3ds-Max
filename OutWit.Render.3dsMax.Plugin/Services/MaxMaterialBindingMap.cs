namespace OutWit.Render.ThreeDsMax.Plugin.Services;

internal sealed class MaxMaterialBindingMap
{
    #region Properties

    public List<string> MaterialIds { get; set; } = [];

    public Dictionary<int, int> CompactMaterialIndexByRawIndex { get; set; } = [];

    // Sub-material slot count of the source Multi material (>= MaterialIds.Count when some
    // sub-materials failed to resolve). Face MtlIDs wrap modulo this count, matching Max.
    public int RawSubMaterialCount { get; set; } = 1;

    #endregion
}
