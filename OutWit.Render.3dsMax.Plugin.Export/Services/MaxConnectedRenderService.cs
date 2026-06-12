using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Owns the first connected-render boundary for the 3ds Max plugin while remote OmnibusCloud submission is still being phased in.
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
    /// Launches the first local connected-render step by preparing a submission package and returning a trackable job state.
    /// </summary>
    public MaxConnectedRenderJobState LaunchRender(MaxSceneLaunchPackageRequest request)
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
        return m_submissionService.Submit(request, package);
    }

    /// <summary>
    /// Refreshes the current placeholder connected-render state until real remote submission is wired.
    /// </summary>
    public MaxConnectedRenderJobState RefreshJob(MaxConnectedRenderJobState jobState)
    {
        ArgumentNullException.ThrowIfNull(jobState);
        return m_submissionService.Refresh(jobState);
    }

    #endregion
}
