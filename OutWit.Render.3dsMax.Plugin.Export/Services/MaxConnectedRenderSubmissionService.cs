using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Submits prepared launch packages and refreshes connected render jobs through the configured transport.
/// </summary>
public sealed class MaxConnectedRenderSubmissionService
{
    #region Fields

    private readonly IMaxConnectedRenderSubmissionTransport m_transport;

    #endregion

    #region Constructors

    public MaxConnectedRenderSubmissionService(IMaxConnectedRenderSubmissionTransport transport)
    {
        m_transport = transport;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Submits the prepared launch package and returns a trackable connected render job state.
    /// </summary>
    /// <param name="request">The launch request the package was prepared from.</param>
    /// <param name="package">The prepared launch package.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>The trackable connected render job state.</returns>
    public Task<MaxConnectedRenderJobState> SubmitAsync(MaxSceneLaunchPackageRequest request, MaxSceneLaunchPackageResult package, CancellationToken cancellationToken = default)
    {
        return m_transport.SubmitAsync(request, package, cancellationToken);
    }

    /// <summary>
    /// Refreshes one previously submitted connected render job through the configured transport.
    /// </summary>
    /// <param name="jobState">The job state to refresh.</param>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>The refreshed job state.</returns>
    public Task<MaxConnectedRenderJobState> RefreshAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default)
    {
        return m_transport.RefreshAsync(jobState, cancellationToken);
    }

    #endregion
}
