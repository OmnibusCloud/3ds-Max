namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Result of running one real connected-render smoke flow from a live 3ds Max scene through OmnibusCloud.
/// </summary>
public sealed class MaxConnectedRenderLiveSmokeResult
{
    #region Properties

    public bool IsSuccess { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public string JobId { get; set; } = string.Empty;

    public string FinalJobStatus { get; set; } = string.Empty;

    public double OverallProgress { get; set; }

    public Guid ResultBlobId { get; set; }

    public string DownloadedFilePath { get; set; } = string.Empty;

    public string TraceLogPath { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    #endregion
}
