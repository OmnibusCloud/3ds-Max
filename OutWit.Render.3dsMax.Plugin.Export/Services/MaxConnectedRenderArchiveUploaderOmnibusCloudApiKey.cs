using OutWit.Cloud.SDK;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// API-key-based archive uploader for sending prepared connected-render packages to OmnibusCloud.
/// </summary>
public sealed class MaxConnectedRenderArchiveUploaderOmnibusCloudApiKey : IMaxConnectedRenderArchiveUploader
{
    #region Functions

    /// <summary>
    /// Uploads one prepared connected-render package archive to OmnibusCloud blob storage.
    /// </summary>
    public Guid UploadArchive(MaxConnectedRenderUploadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var cancellationToken = cancellationSource.Token;
        var client = new WitCloudClient(request.CloudUrl, request.IdentityUrl, request.ApiKey);

        try
        {
            client.ConnectAsync(cancellationToken).GetAwaiter().GetResult();
            return client.Blobs.UploadBlobFromFileAsync(request.PackageArchivePath, ct: cancellationToken).GetAwaiter().GetResult();
        }
        finally
        {
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    #endregion
}
