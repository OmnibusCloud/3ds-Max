namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// The persisted part of the plugin's OmnibusCloud session: enough to silently restore
/// sign-in across 3ds Max restarts (the access token itself is never persisted).
/// </summary>
public sealed class MaxStoredSession
{
    #region Properties

    public string RefreshToken { get; set; } = string.Empty;

    public string TokenEndpoint { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string LastLoginUtc { get; set; } = string.Empty;

    #endregion
}
