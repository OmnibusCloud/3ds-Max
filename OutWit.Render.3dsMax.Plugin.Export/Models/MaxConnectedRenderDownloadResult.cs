namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Result of downloading the current connected render artifact for the first phased 3ds Max plugin flow.
/// </summary>
public sealed class MaxConnectedRenderDownloadResult
{
    #region Properties

    public bool IsSuccess { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public string DownloadedFilePath { get; set; } = string.Empty;

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    #endregion
}
