namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Result of running the first local connected-render preflight for the current 3ds Max scene and launch settings.
/// </summary>
public sealed class MaxConnectedRenderPreflightResult
{
    #region Properties

    public bool CanLaunch { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    #endregion
}
