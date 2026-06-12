namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

public sealed class MaxSceneDiagnosticItem
{
    #region Properties

    public MaxSceneDiagnosticSeverity Severity { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? ContextName { get; set; }

    public string? SuggestedAction { get; set; }

    #endregion
}
