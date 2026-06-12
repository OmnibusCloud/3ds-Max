namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Persisted receipt for one connected-render package archive upload to OmnibusCloud.
/// </summary>
public sealed class MaxConnectedRenderUploadReceipt
{
    #region Properties

    public Guid UploadedBlobId { get; set; }

    public DateTime UploadedUtc { get; set; }

    public string CloudUrl { get; set; } = string.Empty;

    public string IdentityUrl { get; set; } = string.Empty;

    public string PackageArchivePath { get; set; } = string.Empty;

    #endregion
}
