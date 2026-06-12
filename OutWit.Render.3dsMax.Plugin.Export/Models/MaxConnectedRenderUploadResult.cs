namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// Result of uploading a prepared connected-render package archive to OmnibusCloud.
/// </summary>
public sealed class MaxConnectedRenderUploadResult
{
    #region Properties

    public bool IsSuccess { get; set; }

    public string StatusText { get; set; } = string.Empty;

    public Guid UploadedBlobId { get; set; }

    public string PackageArchivePath { get; set; } = string.Empty;

    public string UploadReceiptPath { get; set; } = string.Empty;

    public List<MaxSceneDiagnosticItem> Diagnostics { get; set; } = [];

    #endregion
}
