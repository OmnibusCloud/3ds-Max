using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Upload boundary for sending a prepared connected-render archive to OmnibusCloud.
/// </summary>
public interface IMaxConnectedRenderArchiveUploader
{
    #region Functions

    /// <summary>
    /// Uploads one prepared connected-render package archive and returns the created blob identifier.
    /// </summary>
    Guid UploadArchive(MaxConnectedRenderUploadRequest request);

    #endregion
}
