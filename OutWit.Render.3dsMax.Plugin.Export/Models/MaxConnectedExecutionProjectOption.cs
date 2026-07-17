namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Describes one project (campaign) the signed-in user may launch into — the project-kind
/// counterpart of <see cref="MaxConnectedExecutionGroupOption"/> in the unified Target list.
/// </summary>
public sealed class MaxConnectedExecutionProjectOption
{
    #region Properties

    public string ProjectId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    #endregion
}
