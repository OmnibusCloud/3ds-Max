using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Result of validating or exporting the current 3ds Max scene.
/// </summary>
public sealed class MaxSceneExportResult
{
    #region Properties

    public MaxSceneSummaryData Summary { get; set; } = new();

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    public string StatusText { get; set; } = string.Empty;

    public string? OutputPath { get; set; }

    public bool IsSuccess { get; set; }

    public DccSceneData? Scene { get; set; }

    #endregion
}
