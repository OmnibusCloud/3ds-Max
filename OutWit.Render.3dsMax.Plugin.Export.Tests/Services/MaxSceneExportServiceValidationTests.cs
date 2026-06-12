using System.Reflection;
using System.Text.Json;
using OutWit.Controller.Render.Dcc.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxSceneExportServiceValidationTests
{
    #region Tests

    [Test]
    public void ValidateCurrentSceneFailsWhenMeshNodeUsesMaterialBindingWithPerTriangleMaterialsTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Meshes[0].MaterialIndices = [0, 1];
        snapshot.Materials.Add(new MaxSceneMaterialSnapshotData { Id = "material:secondary", Name = "Secondary" });
        snapshot.MaterialNames.Add("Secondary");
        snapshot.MaterialsCount = 2;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("MaterialBindingId", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneFailsWhenMaterialContainsDuplicateTextureSlotsTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Materials[0].TextureSlots.Add(new MaxSceneTextureSlotSnapshotData
        {
            Slot = OutWit.Controller.Render.Dcc.Model.DccTextureSlotKind.BaseColor,
            ImageAssetId = "image:floor_albedo_2"
        });
        snapshot.ImageAssets.Add(new MaxSceneImageAssetSnapshotData
        {
            Id = "image:floor_albedo_2",
            Name = "FloorAlbedo2",
            SourcePath = @"C:\Demo\floor_albedo_2.png",
            RelativePath = "textures/floor_albedo_2.png",
            AssetKind = "ImageAsset"
        });

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("duplicate texture slot", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneFailsWhenMeshContainsOutOfRangeMaterialIndexTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Meshes[0].MaterialIndices = [4];

        var service = MaxSceneExportTestData.CreateService(snapshot);
        var result = service.ValidateCurrentScene();

        Assert.That(result.IsSuccess, Is.True, "Export-side metadata validation is expected to succeed before build-input validation is applied.");
        Assert.That(result.Scene, Is.Not.Null);

        var exception = Assert.Throws<InvalidOperationException>(() => ValidateDccBuildInput(result.Scene!));

        Assert.That(exception!.Message, Does.Contain("out-of-range material index"));
    }

    [Test]
    public void ExportCurrentSceneSerializesMappedNodesMaterialsAndImagesTest()
    {
        var service = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.Export.ValidationTests.{Guid.NewGuid():N}");

        try
        {
            var result = service.ExportCurrentScene(outputDirectory, MaxSceneExportOutputFormat.Json);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.OutputPath, Is.Not.Null.And.Not.Empty);

            var json = File.ReadAllText(result.OutputPath!);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("Nodes").GetArrayLength(), Is.EqualTo(3));
                Assert.That(root.GetProperty("Materials").GetArrayLength(), Is.EqualTo(1));
                Assert.That(root.GetProperty("ImageAssets").GetArrayLength(), Is.EqualTo(1));
                Assert.That(root.GetProperty("Materials")[0].GetProperty("TextureSlots")[0].GetProperty("ImageAssetId").GetString(), Is.EqualTo("image:floor_albedo"));
            });
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public void ValidateCurrentSceneSucceedsForMetadataOnlySceneTest()
    {
        var service = MaxSceneExportTestData.CreateService(new MaxSceneSnapshotData
        {
            SceneName = "MetadataOnly",
            SceneFilePath = @"C:\Demo\MetadataOnly.max",
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            FrameStart = 1,
            FrameEnd = 1,
            RenderWidth = 1920,
            RenderHeight = 1080
        });

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Nodes, Is.Empty);
            Assert.That(result.Scene.Meshes, Is.Empty);
            Assert.That(result.Scene.Materials, Is.Empty);
            Assert.That(result.Scene.ImageAssets, Is.Empty);
        });
    }

    [Test]
    public void ValidateCurrentSceneSynthesizesCameraFromActivePerspectiveViewportTest()
    {
        var service = MaxSceneExportTestData.CreateService(new MaxSceneSnapshotData
        {
            SceneName = "ViewportScene",
            SceneFilePath = @"C:\Demo\ViewportScene.max",
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            HasActiveViewportRenderFallbackCandidate = true,
            ActiveViewportType = "ViewPerspectiveUser",
            ActiveViewportIsPerspective = true,
            ActiveViewportVerticalFovDegrees = 45d,
            ActiveViewportTransform = new MaxSceneTransformSnapshotData
            {
                Translation = new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = -170d },
                Rotation = new MaxSceneQuaternionSnapshotData { X = 0d, Y = 0d, Z = 0d, W = 1d },
                Scale = new MaxSceneVector3SnapshotData { X = 1d, Y = 1d, Z = 1d }
            },
            FrameStart = 1,
            FrameEnd = 1,
            RenderWidth = 320,
            RenderHeight = 240
        });

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Summary.UsesSyntheticViewportCamera, Is.True);
            Assert.That(result.Summary.CamerasCount, Is.EqualTo(1));
            Assert.That(result.Summary.NodesCount, Is.EqualTo(1));
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Cameras, Has.Count.EqualTo(1));
            Assert.That(result.Scene.Nodes.Count(me => me.Kind == OutWit.Controller.Render.Dcc.Model.DccNodeKind.Camera), Is.EqualTo(1));
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("Synthesized render camera", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneDoesNotSynthesizeCameraFromNonPerspectiveViewportTest()
    {
        var service = MaxSceneExportTestData.CreateService(new MaxSceneSnapshotData
        {
            SceneName = "OrthoViewportScene",
            SceneFilePath = @"C:\Demo\OrthoViewportScene.max",
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            HasActiveViewportRenderFallbackCandidate = true,
            ActiveViewportType = "ViewTop",
            ActiveViewportIsPerspective = false,
            ActiveViewportVerticalFovDegrees = 45d,
            ActiveViewportTransform = new MaxSceneTransformSnapshotData(),
            FrameStart = 1,
            FrameEnd = 1,
            RenderWidth = 320,
            RenderHeight = 240
        });

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Summary.UsesSyntheticViewportCamera, Is.False);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Cameras, Is.Empty);
            Assert.That(result.Scene.Nodes.Any(me => me.Kind == OutWit.Controller.Render.Dcc.Model.DccNodeKind.Camera), Is.False);
        });
    }

    [Test]
    public void ValidateCurrentSceneFailsWhenNodeReferencesMissingMaterialTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Nodes[0].MaterialBindingId = "material:missing";

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("missing material", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentScenePreservesParentChildNodeHierarchyTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Nodes[1].ParentId = snapshot.Nodes[0].Id;
        snapshot.Nodes[2].ParentId = snapshot.Nodes[0].Id;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Nodes.Single(me => me.Id == "node:camera").ParentId, Is.EqualTo("node:mesh"));
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:light").ParentId, Is.EqualTo("node:mesh"));
        });
    }

    [Test]
    public void ValidateCurrentSceneMapsMultipleMaterialsAndImagesTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Nodes.Add(new MaxSceneNodeSnapshotData
        {
            Id = "node:mesh2",
            Name = "MeshNode2",
            Kind = OutWit.Controller.Render.Dcc.Model.DccNodeKind.Mesh,
            MeshId = "mesh:demo2",
            MaterialBindingId = "material:wall"
        });
        snapshot.Meshes.Add(new MaxSceneMeshSnapshotData
        {
            Id = "mesh:demo2",
            Name = "DemoMesh2",
            Positions =
            [
                new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d },
                new MaxSceneVector3SnapshotData { X = 1d, Y = 0d, Z = 1d },
                new MaxSceneVector3SnapshotData { X = 0d, Y = 1d, Z = 1d }
            ],
            Normals =
            [
                new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d },
                new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d },
                new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d }
            ],
            Uv0 =
            [
                new MaxSceneVector2SnapshotData { X = 0d, Y = 0d },
                new MaxSceneVector2SnapshotData { X = 1d, Y = 0d },
                new MaxSceneVector2SnapshotData { X = 0d, Y = 1d }
            ],
            TriangleIndices = [0, 1, 2],
            MaterialIndices = [0]
        });
        snapshot.Materials.Add(new MaxSceneMaterialSnapshotData
        {
            Id = "material:wall",
            Name = "Wall",
            TextureSlots =
            [
                new MaxSceneTextureSlotSnapshotData
                {
                    Slot = OutWit.Controller.Render.Dcc.Model.DccTextureSlotKind.BaseColor,
                    ImageAssetId = "image:wall_albedo"
                }
            ]
        });
        snapshot.ImageAssets.Add(new MaxSceneImageAssetSnapshotData
        {
            Id = "image:wall_albedo",
            Name = "WallAlbedo",
            SourcePath = @"C:\Demo\wall_albedo.png",
            RelativePath = "textures/wall_albedo.png",
            AssetKind = "ImageAsset"
        });
        snapshot.MaterialNames.Add("Wall");
        snapshot.TextureNames.Add("WallAlbedo");
        snapshot.NodesCount = 4;
        snapshot.MeshesCount = 2;
        snapshot.MaterialsCount = 2;
        snapshot.TexturesCount = 2;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Materials.Count, Is.EqualTo(2));
            Assert.That(result.Scene.ImageAssets.Count, Is.EqualTo(2));
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:mesh2").MaterialBindingId, Is.EqualTo("material:wall"));
            Assert.That(result.Scene.Materials.Single(me => me.Id == "material:wall").TextureSlots.Single().ImageAssetId, Is.EqualTo("image:wall_albedo"));
        });
    }

    #endregion

    #region Helpers

    private static void ValidateDccBuildInput(OutWit.Controller.Render.Dcc.Model.DccSceneData scene)
    {
        var buildInputFactoryType = typeof(DccSceneValidationService).Assembly.GetType("OutWit.Controller.Render.Dcc.Services.DccSceneBuildInputFactory")
                                   ?? throw new InvalidOperationException("Failed to resolve DccSceneBuildInputFactory type for export regression tests.");
        var createMethod = buildInputFactoryType.GetMethod("Create", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                           ?? throw new InvalidOperationException("Failed to resolve DccSceneBuildInputFactory.Create method for export regression tests.");

        try
        {
            createMethod.Invoke(null, [scene]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    #endregion
}
