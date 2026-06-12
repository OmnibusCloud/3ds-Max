namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Snapshot of the plugin's OmnibusCloud session for presentation.
/// </summary>
public sealed class MaxConnectedSessionState
{
    #region Properties

    public bool IsSignedIn { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;

    #endregion
}
