using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Transport boundary for submitting and refreshing connected render work from the 3ds Max plugin.
/// </summary>
public interface IMaxConnectedRenderSubmissionTransport
{
    #region Functions

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

    #endregion
}
