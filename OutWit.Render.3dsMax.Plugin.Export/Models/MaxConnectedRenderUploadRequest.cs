namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Request for uploading a prepared 3ds Max connected-render package archive to OmnibusCloud.
/// </summary>
public sealed class MaxConnectedRenderUploadRequest
{
    #region Properties

    public string CloudUrl { get; set; } = string.Empty;

    public string IdentityUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string PackageArchivePath { get; set; } = string.Empty;

    #endregion
}
