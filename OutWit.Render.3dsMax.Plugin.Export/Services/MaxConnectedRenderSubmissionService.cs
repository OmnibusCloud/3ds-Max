using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Records the first local submission receipt for the phased 3ds Max connected render flow before real OmnibusCloud transport is wired.
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
    /// Records a local placeholder submission receipt for the prepared launch package and returns a trackable connected render job state.
    /// </summary>
    public MaxConnectedRenderJobState Submit(MaxSceneLaunchPackageRequest request, MaxSceneLaunchPackageResult package)
    {
        return m_transport.Submit(request, package);
    }

    /// <summary>
    /// Refreshes one previously submitted connected render job through the configured transport.
    /// </summary>
    public MaxConnectedRenderJobState Refresh(MaxConnectedRenderJobState jobState)
    {
        return m_transport.Refresh(jobState);
    }

    #endregion
}
