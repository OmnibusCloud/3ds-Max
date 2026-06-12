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
    MaxConnectedRenderJobState Submit(MaxSceneLaunchPackageRequest request, MaxSceneLaunchPackageResult package);

    /// <summary>
    /// Refreshes one previously submitted connected render job through the current transport implementation.
    /// </summary>
    MaxConnectedRenderJobState Refresh(MaxConnectedRenderJobState jobState);

    #endregion
}
