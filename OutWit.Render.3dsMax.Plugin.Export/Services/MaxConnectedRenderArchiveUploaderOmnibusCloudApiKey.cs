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

        // The plugin runs on 3ds Max's main thread, which carries a UI
        // SynchronizationContext — blocking on async SDK calls there deadlocks
        // (the continuation can never get back onto the blocked thread). Run the
        // whole async flow on the thread pool and block on the outer task only.
        return Task.Run(async () =>
        {
            var client = new WitCloudClient(request.CloudUrl, request.IdentityUrl, request.ApiKey);

            try
            {
                await client.ConnectAsync(cancellationToken);
                return await client.Blobs.UploadBlobFromFileAsync(request.PackageArchivePath, ct: cancellationToken);
            }
            finally
            {
                await client.DisposeAsync();
            }
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    #endregion
}
