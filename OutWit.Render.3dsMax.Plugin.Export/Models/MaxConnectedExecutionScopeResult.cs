namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Result of loading the current execution scope options for the first connected 3ds Max plugin shell.
/// </summary>
public sealed class MaxConnectedExecutionScopeResult
{
    #region Properties

    public bool IsSuccess { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public string UserDisplayName { get; set; } = string.Empty;

    public string SessionStatusText { get; set; } = string.Empty;

    public bool CanRunOnAllClients { get; set; }

    public List<MaxConnectedExecutionGroupOption> Groups { get; set; } = [];

    /// <summary>Projects (campaigns) the user may launch into — listed BEFORE groups in the Target UI.</summary>
    public List<MaxConnectedExecutionProjectOption> Projects { get; set; } = [];

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    #endregion
}
