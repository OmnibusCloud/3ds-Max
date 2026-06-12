using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Provides a raw snapshot of the current 3ds Max scene for export-side processing.
/// </summary>
public interface IMaxSceneSnapshotProvider
{
    #region Functions

    /// <summary>
    /// Captures the current host scene into a snapshot model.
    /// </summary>
    MaxSceneSnapshotData Capture();

    #endregion
}
