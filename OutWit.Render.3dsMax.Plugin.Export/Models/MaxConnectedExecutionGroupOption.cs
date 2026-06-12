namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Describes one execution group option visible to the first connected 3ds Max plugin shell.
/// </summary>
public sealed class MaxConnectedExecutionGroupOption
{
    #region Properties

    public string GroupId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    #endregion
}
