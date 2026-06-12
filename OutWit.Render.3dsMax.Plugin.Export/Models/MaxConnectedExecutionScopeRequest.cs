namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Request for loading the first plugin-side execution scope options after browser sign-in is initiated.
/// </summary>
public sealed class MaxConnectedExecutionScopeRequest
{
    #region Properties

    public string CloudUrl { get; set; } = string.Empty;

    public string IdentityUrl { get; set; } = string.Empty;

    public string SessionStatusText { get; set; } = string.Empty;

    #endregion
}
