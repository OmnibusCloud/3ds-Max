using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Transport boundary for submitting and refreshing connected render work from the 3ds Max plugin.
/// </summary>
public interface IMaxConnectedRenderSubmissionTransport
{
    #region Functions

    /// <summary>
    /// Cheap submission-readiness probe run BEFORE the heavy scene capture: an expired session
    /// must surface immediately, not after minutes of synchronous main-thread capture work.
    /// Transports without a session concept keep the default (never blocked).
    /// </summary>
    /// <param name="request">The launch request about to be prepared.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>Null when submission can proceed; a user-facing blocking reason otherwise.</returns>
    Task<string?> ProbeSubmissionBlockerAsync(MaxSceneLaunchPackageRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Submits one prepared launch package through the current transport implementation.
    /// </summary>
    /// <param name="request">The launch request the package was prepared from.</param>
    /// <param name="package">The prepared launch package.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>The trackable connected render job state.</returns>
    Task<MaxConnectedRenderJobState> SubmitAsync(MaxSceneLaunchPackageRequest request, MaxSceneLaunchPackageResult package, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes one previously submitted connected render job through the current transport implementation.
    /// </summary>
    /// <param name="jobState">The job state to refresh.</param>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>The refreshed job state.</returns>
    Task<MaxConnectedRenderJobState> RefreshAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation of one previously submitted connected render job on the farm.
    /// The job stays active until a later refresh observes the terminal cancelled status.
    /// </summary>
    /// <param name="jobState">The job state to cancel.</param>
    /// <param name="cancellationToken">Cancels the cancel request itself.</param>
    /// <returns>The updated job state.</returns>
    Task<MaxConnectedRenderJobState> CancelAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default);

    #endregion
}
