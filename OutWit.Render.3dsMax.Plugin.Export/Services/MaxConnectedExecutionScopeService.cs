using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Loads the execution scope options (groups / all-clients permission) the signed-in
/// user may launch on, through the authenticated OmnibusCloud connection.
/// </summary>
public sealed class MaxConnectedExecutionScopeService
{
    #region Fields

    private readonly IMaxCloudSessionService m_sessionService;

    private readonly IMaxCloudConnectionService m_connectionService;

    #endregion

    #region Constructors

    public MaxConnectedExecutionScopeService(IMaxCloudSessionService sessionService, IMaxCloudConnectionService connectionService)
    {
        m_sessionService = sessionService;
        m_connectionService = connectionService;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Loads execution scope options for the signed-in user.
    /// </summary>
    /// <param name="request">The scope request with the engine endpoint.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The execution scope load result.</returns>
    public async Task<MaxConnectedExecutionScopeResult> LoadAsync(MaxConnectedExecutionScopeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<MaxSceneDiagnosticItem>();

        if (string.IsNullOrWhiteSpace(request.CloudUrl))
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "OmnibusCloud URL is required before loading execution scope options."));
            return CreateFailureResult("Execution scope load failed. Cloud endpoint is missing.", diagnostics);
        }

        var sessionState = m_sessionService.GetState();
        if (!sessionState.IsSignedIn)
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Sign in before loading execution scope options."));
            return CreateFailureResult("Execution scope load failed. No signed-in session.", diagnostics);
        }

        try
        {
            var client = await m_connectionService.GetClientAsync(request.CloudUrl, cancellationToken);
            if (client == null)
            {
                diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, $"Could not connect to OmnibusCloud at '{request.CloudUrl}'."));
                return CreateFailureResult("Execution scope load failed. Cloud connection unavailable.", diagnostics);
            }

            var scope = await client.GetExecutionScopeOptionsAsync(cancellationToken);

            diagnostics.Add(CreateDiagnostic(
                MaxSceneDiagnosticSeverity.Info,
                $"Loaded execution scope: {scope.Groups.Length} groups, all-clients={scope.CanRunOnAllClients}."));

            return new MaxConnectedExecutionScopeResult
            {
                IsSuccess = true,
                StatusText = "Execution scope options loaded.",
                UserDisplayName = sessionState.DisplayName,
                SessionStatusText = $"Signed in as {sessionState.DisplayName}",
                CanRunOnAllClients = scope.CanRunOnAllClients,
                Groups = scope.Groups
                    .Select(me => new MaxConnectedExecutionGroupOption
                    {
                        GroupId = me.GroupId?.ToString() ?? string.Empty,
                        Name = me.Name,
                        Description = me.Description
                    })
                    .ToList(),
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, $"Execution scope query failed: {ex.Message}"));
            return CreateFailureResult("Execution scope load failed.", diagnostics);
        }
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
