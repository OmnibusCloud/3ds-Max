using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Loads the first plugin-side execution scope options while the real authenticated OmnibusCloud scope query is still being phased in.
/// </summary>
public sealed class MaxConnectedExecutionScopeService
{
    #region Functions

    /// <summary>
    /// Loads execution scope options for the current connected plugin shell.
    /// </summary>
    public MaxConnectedExecutionScopeResult Load(MaxConnectedExecutionScopeRequest request)
    {
        var diagnostics = new List<MaxSceneDiagnosticItem>();

        if (string.IsNullOrWhiteSpace(request.CloudUrl) && string.IsNullOrWhiteSpace(request.IdentityUrl))
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "OmnibusCloud URL or Identity URL is required before loading execution scope options."));
            return CreateFailureResult("Execution scope load failed. Cloud endpoint is missing.", diagnostics);
        }

        if (string.IsNullOrWhiteSpace(request.SessionStatusText) || request.SessionStatusText.Equals("Signed out", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Start browser sign-in before loading execution scope options."));
            return CreateFailureResult("Execution scope load failed. Sign-in has not started yet.", diagnostics);
        }

        var userLabel = BuildUserDisplayName(request.CloudUrl, request.IdentityUrl);
        diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, "Loaded placeholder execution scope options from the local 3ds Max plugin shell."));

        return new MaxConnectedExecutionScopeResult
        {
            IsSuccess = true,
            StatusText = "Execution scope options loaded locally.",
            UserDisplayName = userLabel,
            SessionStatusText = "Scope options loaded (local placeholder)",
            CanRunOnAllClients = true,
            Groups =
            [
                new MaxConnectedExecutionGroupOption
                {
                    GroupId = "group-artists",
                    Name = "Artists",
                    Description = "Primary render group for artist-initiated launches."
                },
                new MaxConnectedExecutionGroupOption
                {
                    GroupId = "group-preview",
                    Name = "Preview",
                    Description = "Smaller preview group for quick validation renders."
                }
            ],
            Diagnostics = diagnostics
        };
    }

    private static string BuildUserDisplayName(string cloudUrl, string identityUrl)
    {
        var source = !string.IsNullOrWhiteSpace(cloudUrl) ? cloudUrl : identityUrl;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return "OmnibusCloud User (placeholder)";

        return $"OmnibusCloud User @ {uri.Host}";
    }

    private static MaxConnectedExecutionScopeResult CreateFailureResult(string statusText, List<MaxSceneDiagnosticItem> diagnostics)
    {
        return new MaxConnectedExecutionScopeResult
        {
            IsSuccess = false,
            StatusText = statusText,
            SessionStatusText = "Scope load failed",
            Diagnostics = diagnostics
        };
    }

    private static MaxSceneDiagnosticItem CreateDiagnostic(MaxSceneDiagnosticSeverity severity, string message)
    {
        return new MaxSceneDiagnosticItem
        {
            Severity = severity,
            Message = message
        };
    }

    #endregion
}
