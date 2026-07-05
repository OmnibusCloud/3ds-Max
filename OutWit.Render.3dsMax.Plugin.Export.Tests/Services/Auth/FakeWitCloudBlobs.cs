using OutWit.Cloud.SDK.Blobs;
using OutWit.Shared.Storage.Providers;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

/// <summary>
/// Fake blobs facet: a file download writes a marker file; the transport tests use nothing else.
/// </summary>
internal sealed class FakeWitCloudBlobs : IWitCloudBlobs
{
    #region IWitCloudBlobs

    public Task<Guid> UploadBlobAsync(byte[] data, string fileName, int chunkSize = IWitCloudBlobs.DEFAULT_CHUNK_SIZE, CancellationToken ct = default)
    {
        throw new NotSupportedException("Blob upload is not faked.");
    }

    public Task<Guid> UploadBlobFromFileAsync(string filePath, int chunkSize = IWitCloudBlobs.DEFAULT_CHUNK_SIZE, CancellationToken ct = default)
    {
        throw new NotSupportedException("Blob upload is not faked.");
    }

    public Task<byte[]> DownloadBlobAsync(Guid blobId, CancellationToken ct = default)
    {
        throw new NotSupportedException("In-memory blob download is not faked.");
    }

    public Task DownloadBlobToFileAsync(Guid blobId, string localPath, int chunkSize = IWitCloudBlobs.DEFAULT_CHUNK_SIZE, CancellationToken ct = default)
    {
        File.WriteAllText(localPath, blobId.ToString("D"));
        DownloadedBlobs.Add((blobId, localPath));
        return Task.CompletedTask;
    }

    public Task<BlobInfo> GetBlobInfoAsync(Guid blobId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Blob info is not faked.");
    }

    public Task DeleteBlobAsync(Guid blobId, CancellationToken ct = default)
    {
        throw new NotSupportedException("Blob delete is not faked.");
    }

    #endregion

    #region Properties

    public List<(Guid BlobId, string LocalPath)> DownloadedBlobs { get; } = [];

    #endregion
}
