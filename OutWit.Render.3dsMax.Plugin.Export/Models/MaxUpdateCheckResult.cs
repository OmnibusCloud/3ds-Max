namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>Outcome of one portal update check (Settings ▸ About "Check for updates").</summary>
public sealed class MaxUpdateCheckResult
{
    #region Properties

    public bool IsSuccess { get; init; }

    public bool UpdateAvailable { get; init; }

    public string CurrentVersion { get; init; } = string.Empty;

    public string LatestVersion { get; init; } = string.Empty;

    public string StatusText { get; init; } = string.Empty;

    #endregion

    #region Functions

    public static MaxUpdateCheckResult Failed(string currentVersion, string statusText) => new()
    {
        IsSuccess = false,
        CurrentVersion = currentVersion,
        StatusText = statusText
    };

    #endregion
}
