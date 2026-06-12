namespace OutWit.Render.ThreeDsMax.Plugin.Services;

internal sealed class MaxMaterialBindingMap
{
    #region Properties

    public List<string> MaterialIds { get; set; } = [];

    public Dictionary<int, int> CompactMaterialIndexByRawIndex { get; set; } = [];

    #endregion
}
