namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Request for loading the execution scope options of the signed-in user.
/// </summary>
public sealed class MaxConnectedExecutionScopeRequest
{
    #region Properties

    public string CloudUrl { get; set; } = string.Empty;

    public string IdentityUrl { get; set; } = string.Empty;

    #endregion
}
