using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Owns the connected-render boundary for the 3ds Max plugin: preflight, launch-package
/// preparation, and submission through the configured transport.
/// </summary>
public sealed class MaxConnectedRenderService
{
    #region Fields

    private readonly MaxSceneLaunchPreparationService m_launchPreparationService;

    private readonly MaxConnectedRenderPreflightService m_preflightService;

    private readonly MaxConnectedRenderSubmissionService m_submissionService;

    #endregion

    #region Constructors

    public MaxConnectedRenderService(MaxSceneLaunchPreparationService launchPreparationService, MaxConnectedRenderPreflightService preflightService, MaxConnectedRenderSubmissionService submissionService)
    {
        m_launchPreparationService = launchPreparationService;
        m_preflightService = preflightService;
        m_submissionService = submissionService;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Launches a connected render: runs preflight, prepares the launch package, and submits it through the configured transport.
    /// Must be called on the 3ds Max main thread — preflight and preparation capture the scene through
    /// the single-threaded Max SDK synchronously before the submission awaits the network.
    /// </summary>
    /// <param name="request">The launch request.</param>
    /// <param name="cancellationToken">Cancels the launch.</param>
    /// <returns>The trackable connected render job state.</returns>
    public async Task<MaxConnectedRenderJobState> LaunchRenderAsync(MaxSceneLaunchPackageRequest request, CancellationToken cancellationToken = default)
    {
        var preflight = m_preflightService.Run(request);
        var now = DateTime.UtcNow;

        if (!preflight.CanLaunch)
        {
            return new MaxConnectedRenderJobState
            {
                JobId = $"blocked-{Guid.NewGuid():N}",
                StatusText = preflight.StatusText,
                ProgressPercent = 0d,
                IsCompleted = false,
                IsPlaceholderLocalSubmission = true,
                SubmittedUtc = now,
                UpdatedUtc = now,
                Diagnostics = [.. preflight.Diagnostics]
            };
        }

        var package = m_launchPreparationService.Prepare(request);
        return await m_submissionService.SubmitAsync(request, package, cancellationToken);
    }

    /// <summary>
    /// Refreshes one previously launched connected-render job through the configured transport.
    /// </summary>
    /// <param name="jobState">The job state to refresh.</param>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>The refreshed job state.</returns>
    public Task<MaxConnectedRenderJobState> RefreshJobAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobState);
        return m_submissionService.RefreshAsync(jobState, cancellationToken);
    }

    /// <summary>
    /// Requests cancellation of one previously launched connected-render job on the farm.
    /// </summary>
    /// <param name="jobState">The job state to cancel.</param>
    /// <param name="cancellationToken">Cancels the cancel request itself.</param>
    /// <returns>The updated job state.</returns>
    public Task<MaxConnectedRenderJobState> CancelJobAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobState);
        return m_submissionService.CancelAsync(jobState, cancellationToken);
    }

    #endregion
}
