using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
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
    public async Task UploadImageAssetAttachmentsAsyncDegradesMissingImageAssetFileTest()
    {
        // A missing texture file must not fail the whole submission (3ds Max itself renders
        // with missing bitmaps): the asset and every reference to it drop out with a warning.
        var service = new MaxConnectedRenderSceneAttachmentService();
        var exportService = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var scene = exportService.ValidateCurrentScene().Scene!;
        var missingAssetId = scene.ImageAssets[0].Id;
        scene.ImageAssets[0].SourcePath = Path.Combine(m_tempDirectoryPath, "missing.png");

        var diagnostics = await service.UploadImageAssetAttachmentsAsync(
            scene,
            string.Empty,
            (_, _) => Task.FromResult(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Any(me => me.Severity == MaxSceneDiagnosticSeverity.Warning
                                              && me.Message.Contains("source file was not found")), Is.True);
            Assert.That(scene.ImageAssets.Any(me => me.Id == missingAssetId), Is.False, "the asset is removed");
            Assert.That(scene.Materials.SelectMany(me => me.TextureSlots).Any(me => me.ImageAssetId == missingAssetId), Is.False, "slot references are removed");
            Assert.That(scene.AttachedFiles, Is.Empty, "nothing was uploaded");
        });
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

    [Test]
    public async Task UploadImageAssetAttachmentsAsyncResolvesDriveRelativeImagePathAgainstSceneDirectoryTest()
    {
        // V-Ray sample scenes author their references drive-relative ('\Assets\Asphalt_Diffuse.jpg')
        // — rooted but with no drive letter. Such a path must resolve against the scene's own
        // folders instead of being rejected as an unresolvable absolute path.
        var service = new MaxConnectedRenderSceneAttachmentService();
        var exportService = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var scene = exportService.ValidateCurrentScene().Scene!;
        var sceneDirectoryPath = Path.Combine(m_tempDirectoryPath, "Automotive_Exterior");
        var assetsDirectoryPath = Path.Combine(sceneDirectoryPath, "Assets");
        Directory.CreateDirectory(assetsDirectoryPath);
        var texturePath = Path.Combine(assetsDirectoryPath, "Asphalt_Diffuse.jpg");
        File.WriteAllBytes(texturePath, [1, 2, 3, 4]);
        scene.ImageAssets[0].SourcePath = @"\Assets\Asphalt_Diffuse.jpg";
        var sceneFilePath = Path.Combine(sceneDirectoryPath, "Automotive_Exterior.max");
        File.WriteAllText(sceneFilePath, string.Empty);

        var uploadedFilePaths = new List<string>();

        await service.UploadImageAssetAttachmentsAsync(
            scene,
            sceneFilePath,
            (filePath, _) =>
            {
                uploadedFilePaths.Add(filePath);
                return Task.FromResult(Guid.Parse("44444444-4444-4444-4444-444444444444"));
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(uploadedFilePaths, Is.EqualTo(new[] { texturePath }));
            Assert.That(scene.AttachedFiles[0].OriginalPath, Is.EqualTo(texturePath));
            Assert.That(scene.AttachedFiles[0].BlobId, Is.EqualTo(Guid.Parse("44444444-4444-4444-4444-444444444444")));
        });
    }

    [Test]
    public async Task UploadImageAssetAttachmentsAsyncKeepsSameNamedTexturesFromDifferentFoldersDistinctTest()
    {
        // Two textures both named 'diffuse.png' from different folders used to collapse onto one
        // 'textures/diffuse.png' attachment: the second file was never uploaded and both
        // materials rendered with the FIRST texture, silently.
        var service = new MaxConnectedRenderSceneAttachmentService();
        var exportService = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var scene = exportService.ValidateCurrentScene().Scene!;

        var woodDirectory = Path.Combine(m_tempDirectoryPath, "wood");
        var metalDirectory = Path.Combine(m_tempDirectoryPath, "metal");
        Directory.CreateDirectory(woodDirectory);
        Directory.CreateDirectory(metalDirectory);
        var woodTexturePath = Path.Combine(woodDirectory, "diffuse.png");
        var metalTexturePath = Path.Combine(metalDirectory, "diffuse.png");
        File.WriteAllBytes(woodTexturePath, [1, 1, 1, 1]);
        File.WriteAllBytes(metalTexturePath, [2, 2, 2, 2]);

        scene.ImageAssets[0].SourcePath = woodTexturePath;
        scene.ImageAssets[0].RelativePath = "textures/diffuse.png";
        scene.ImageAssets.Add(new OutWit.Controller.Render.Dcc.Model.DccImageAssetData
        {
            Id = "image:metal_diffuse",
            Name = "diffuse",
            SourcePath = metalTexturePath,
            RelativePath = "textures/diffuse.png",
            AssetKind = "ImageAsset"
        });
        scene.Materials[0].TextureSlots.Add(new OutWit.Controller.Render.Dcc.Model.DccTextureSlotData
        {
            Slot = OutWit.Controller.Render.Dcc.Model.DccTextureSlotKind.Metallic,
            ImageAssetId = "image:metal_diffuse"
        });

        var uploadedFilePaths = new List<string>();

        await service.UploadImageAssetAttachmentsAsync(
            scene,
            string.Empty,
            (filePath, _) =>
            {
                uploadedFilePaths.Add(filePath);
                return Task.FromResult(Guid.NewGuid());
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(uploadedFilePaths, Is.EquivalentTo(new[] { woodTexturePath, metalTexturePath }), "both files must upload");
            Assert.That(scene.AttachedFiles, Has.Count.EqualTo(2));
            var relativePaths = scene.ImageAssets.Select(me => me.RelativePath).ToArray();
            Assert.That(relativePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(2), "colliding names must be uniquified");
            Assert.That(scene.ImageAssets[0].RelativePath, Is.EqualTo("textures/diffuse.png"), "first asset keeps its name");
            Assert.That(scene.ImageAssets[1].RelativePath, Does.Match("^textures/diffuse_[0-9a-f]{8}\\.png$"), "collision gains a stable path-derived suffix");
            Assert.That(scene.AttachedFiles.Select(me => me.RelativePath), Is.EquivalentTo(relativePaths), "attachments follow the uniquified paths");
        });
    }

    [Test]
    public async Task DegradingAMissingNormalTextureResetsTheOrphanedNormalStrengthTest()
    {
        // Removing the Normal slot used to leave NormalStrength ≠ 1 behind — a combination the
        // server-side contract rejects AFTER the whole upload ("custom normal strength only when
        // a normal texture slot is present"). The degraded scene must stay VALID.
        var service = new MaxConnectedRenderSceneAttachmentService();
        var exportService = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var scene = exportService.ValidateCurrentScene().Scene!;

        scene.ImageAssets[0].SourcePath = Path.Combine(m_tempDirectoryPath, "missing_bump.png"); // never written
        scene.Materials[0].TextureSlots.Add(new OutWit.Controller.Render.Dcc.Model.DccTextureSlotData
        {
            Slot = OutWit.Controller.Render.Dcc.Model.DccTextureSlotKind.Normal,
            ImageAssetId = scene.ImageAssets[0].Id
        });
        scene.Materials[0].NormalStrength = 0.3d;

        var diagnostics = await service.UploadImageAssetAttachmentsAsync(
            scene,
            string.Empty,
            (_, _) => Task.FromResult(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Materials[0].TextureSlots.Any(me => me.Slot == OutWit.Controller.Render.Dcc.Model.DccTextureSlotKind.Normal), Is.False);
            Assert.That(scene.Materials[0].NormalStrength, Is.EqualTo(1d), "orphaned normal strength resets to the contract default");
            Assert.That(diagnostics.Any(me => me.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.DoesNotThrow(() => OutWit.Controller.Render.Dcc.Services.DccSceneValidationService.Validate(scene), "the degraded scene must pass the server contract");
        });
    }

    [Test]
    public async Task UploadImageAssetAttachmentsAsyncStillSharesOneAttachmentForTheSameSourceFileTest()
    {
        // The SAME file referenced by two assets is a legitimate dedup — one upload, one attachment.
        var service = new MaxConnectedRenderSceneAttachmentService();
        var exportService = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var scene = exportService.ValidateCurrentScene().Scene!;
        var texturePath = Path.Combine(m_tempDirectoryPath, "shared.png");
        File.WriteAllBytes(texturePath, [1, 2, 3, 4]);

        scene.ImageAssets[0].SourcePath = texturePath;
        scene.ImageAssets[0].RelativePath = "textures/shared.png";
        scene.ImageAssets.Add(new OutWit.Controller.Render.Dcc.Model.DccImageAssetData
        {
            Id = "image:shared_again",
            Name = "shared",
            SourcePath = texturePath,
            RelativePath = "textures/shared.png",
            AssetKind = "ImageAsset"
        });
        scene.Materials[0].TextureSlots.Add(new OutWit.Controller.Render.Dcc.Model.DccTextureSlotData
        {
            Slot = OutWit.Controller.Render.Dcc.Model.DccTextureSlotKind.Metallic,
            ImageAssetId = "image:shared_again"
        });

        var uploadedFilePaths = new List<string>();

        await service.UploadImageAssetAttachmentsAsync(
            scene,
            string.Empty,
            (filePath, _) =>
            {
                uploadedFilePaths.Add(filePath);
                return Task.FromResult(Guid.NewGuid());
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(uploadedFilePaths, Is.EqualTo(new[] { texturePath }), "one upload for one file");
            Assert.That(scene.AttachedFiles, Has.Count.EqualTo(1));
            Assert.That(scene.ImageAssets.Select(me => me.RelativePath).Distinct().Count(), Is.EqualTo(1), "same file keeps one shared relative path");
        });
    }

    #endregion
}
