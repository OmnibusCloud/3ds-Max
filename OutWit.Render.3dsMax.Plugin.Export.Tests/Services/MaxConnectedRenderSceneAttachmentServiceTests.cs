using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxConnectedRenderSceneAttachmentServiceTests
{
    #region Fields

    private string m_tempDirectoryPath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_tempDirectoryPath = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.SceneAttachments.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_tempDirectoryPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_tempDirectoryPath))
            Directory.Delete(m_tempDirectoryPath, true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task UploadImageAssetAttachmentsAsyncUploadsReferencedImageAssetTest()
    {
        var service = new MaxConnectedRenderSceneAttachmentService();
        var exportService = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var scene = exportService.ValidateCurrentScene().Scene!;
        var texturePath = Path.Combine(m_tempDirectoryPath, "floor_albedo.png");
        File.WriteAllBytes(texturePath, [1, 2, 3, 4]);
        scene.ImageAssets[0].SourcePath = texturePath;

        var uploadedFilePaths = new List<string>();

        var diagnostics = await service.UploadImageAssetAttachmentsAsync(
            scene,
            string.Empty,
            (filePath, _) =>
            {
                uploadedFilePaths.Add(filePath);
                return Task.FromResult(Guid.Parse("11111111-1111-1111-1111-111111111111"));
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(uploadedFilePaths, Is.EqualTo(new[] { texturePath }));
            Assert.That(scene.AttachedFiles.Count, Is.EqualTo(1));
            Assert.That(scene.AttachedFiles[0].BlobId, Is.EqualTo(Guid.Parse("11111111-1111-1111-1111-111111111111")));
            Assert.That(scene.AttachedFiles[0].OriginalPath, Is.EqualTo(texturePath));
            Assert.That(scene.AttachedFiles[0].RelativePath, Is.EqualTo("textures/floor_albedo.png"));
            Assert.That(diagnostics.Any(me => me.Message.Contains("Uploaded scene image asset", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task UploadImageAssetAttachmentsAsyncSkipsExistingAttachmentTest()
    {
        var service = new MaxConnectedRenderSceneAttachmentService();
        var exportService = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var scene = exportService.ValidateCurrentScene().Scene!;
        var texturePath = Path.Combine(m_tempDirectoryPath, "floor_albedo.png");
        File.WriteAllBytes(texturePath, [1, 2, 3, 4]);
        scene.ImageAssets[0].SourcePath = texturePath;
        scene.AttachedFiles.Add(new OutWit.Controller.Render.Model.RenderSceneAttachmentRefData
        {
            Kind = "ImageAsset",
            BlobId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            OriginalPath = texturePath,
            RelativePath = "textures/floor_albedo.png",
            PackagingStrategy = "SceneAttachmentBlob"
        });

        var uploadCount = 0;

        var diagnostics = await service.UploadImageAssetAttachmentsAsync(
            scene,
            string.Empty,
            (_, _) =>
            {
                uploadCount++;
                return Task.FromResult(Guid.NewGuid());
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(uploadCount, Is.EqualTo(0));
            Assert.That(scene.AttachedFiles.Count, Is.EqualTo(1));
            Assert.That(diagnostics, Is.Empty);
        });
    }

    [Test]
    public void UploadImageAssetAttachmentsAsyncRejectsMissingImageAssetFileTest()
    {
        var service = new MaxConnectedRenderSceneAttachmentService();
        var exportService = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var scene = exportService.ValidateCurrentScene().Scene!;
        scene.ImageAssets[0].SourcePath = Path.Combine(m_tempDirectoryPath, "missing.png");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.UploadImageAssetAttachmentsAsync(
                scene,
                string.Empty,
                (_, _) => Task.FromResult(Guid.NewGuid()),
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("source file was not found"));
    }

    [Test]
    public async Task UploadImageAssetAttachmentsAsyncResolvesRelativeImagePathAgainstSceneAncestorsTest()
    {
        var service = new MaxConnectedRenderSceneAttachmentService();
        var exportService = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var scene = exportService.ValidateCurrentScene().Scene!;
        var dataRootDirectoryPath = Path.Combine(m_tempDirectoryPath, "@Data", "3ds_max");
        var sceneDirectoryPath = Path.Combine(dataRootDirectoryPath, "Scenes", "Raytrace", "AdvancedExamples");
        Directory.CreateDirectory(sceneDirectoryPath);
        var texturePath = Path.Combine(sceneDirectoryPath, "floor_albedo.png");
        File.WriteAllBytes(texturePath, [1, 2, 3, 4]);
        scene.ImageAssets[0].SourcePath = Path.Combine("Scenes", "Raytrace", "AdvancedExamples", "floor_albedo.png");
        var sceneFilePath = Path.Combine(sceneDirectoryPath, "Demo.max");
        File.WriteAllText(sceneFilePath, string.Empty);

        var uploadedFilePaths = new List<string>();

        await service.UploadImageAssetAttachmentsAsync(
            scene,
            sceneFilePath,
            (filePath, _) =>
            {
                uploadedFilePaths.Add(filePath);
                return Task.FromResult(Guid.Parse("33333333-3333-3333-3333-333333333333"));
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(uploadedFilePaths, Is.EqualTo(new[] { texturePath }));
            Assert.That(scene.AttachedFiles[0].OriginalPath, Is.EqualTo(texturePath));
            Assert.That(scene.AttachedFiles[0].BlobId, Is.EqualTo(Guid.Parse("33333333-3333-3333-3333-333333333333")));
        });
    }

    #endregion
}
