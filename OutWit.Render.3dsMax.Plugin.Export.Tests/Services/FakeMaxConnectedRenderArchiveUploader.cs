using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal sealed class FakeMaxConnectedRenderArchiveUploader : IMaxConnectedRenderArchiveUploader
{
    #region Properties

    public Guid UploadedBlobId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public MaxConnectedRenderUploadRequest? LastRequest { get; private set; }

    #endregion

    #region IMaxConnectedRenderArchiveUploader

    public Guid UploadArchive(MaxConnectedRenderUploadRequest request)
    {
        LastRequest = new MaxConnectedRenderUploadRequest
        {
            CloudUrl = request.CloudUrl,
            IdentityUrl = request.IdentityUrl,
            ApiKey = request.ApiKey,
            PackageArchivePath = request.PackageArchivePath
        };

        return UploadedBlobId;
    }

    #endregion
}
