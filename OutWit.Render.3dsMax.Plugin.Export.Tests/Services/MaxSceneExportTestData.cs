using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal static class MaxSceneExportTestData
{
    #region Functions

    public static MaxSceneExportService CreateService(MaxSceneSnapshotData snapshot)
    {
        var snapshotProvider = new FakeMaxSceneSnapshotProvider(snapshot);
        return new MaxSceneExportService(new MaxSceneSummaryService(snapshotProvider));
    }

    public static MaxSceneLaunchPreparationService CreateLaunchPreparationService(MaxSceneSnapshotData snapshot)
    {
        return new MaxSceneLaunchPreparationService(CreateService(snapshot));
    }

    public static MaxConnectedRenderService CreateConnectedRenderService(MaxSceneSnapshotData snapshot)
    {
        return new MaxConnectedRenderService(CreateLaunchPreparationService(snapshot), CreateConnectedRenderPreflightService(snapshot), CreateConnectedRenderSubmissionService());
    }

    public static MaxConnectedRenderPreflightService CreateConnectedRenderPreflightService(MaxSceneSnapshotData snapshot)
    {
        return new MaxConnectedRenderPreflightService(CreateService(snapshot));
    }

    public static MaxConnectedExecutionScopeService CreateConnectedExecutionScopeService(
        FakeMaxCloudSessionService? sessionService = null,
        FakeMaxCloudConnectionService? connectionService = null)
    {
        return new MaxConnectedExecutionScopeService(
            sessionService ?? new FakeMaxCloudSessionService(),
            connectionService ?? new FakeMaxCloudConnectionService());
    }

    public static MaxConnectedRenderDownloadService CreateConnectedRenderDownloadService()
    {
        return new MaxConnectedRenderDownloadService();
    }

    public static MaxConnectedRenderPackageUploadService CreateConnectedRenderPackageUploadService(IMaxConnectedRenderArchiveUploader archiveUploader)
    {
        return new MaxConnectedRenderPackageUploadService(archiveUploader);
    }

    public static MaxConnectedRenderSubmissionService CreateConnectedRenderSubmissionService()
    {
        return new MaxConnectedRenderSubmissionService(CreateConnectedRenderSubmissionTransport());
    }

    public static IMaxConnectedRenderSubmissionTransport CreateConnectedRenderSubmissionTransport()
    {
        return new MaxConnectedRenderSubmissionTransportLocalPlaceholder();
    }

    public static MaxSceneSnapshotData CreateMinimalValidSceneSnapshot()
    {
        return new MaxSceneSnapshotData
        {
            SceneName = "DemoScene",
            SceneFilePath = @"C:\Demo\DemoScene.max",
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            ActiveRenderCameraName = "Camera001",
            CameraNames = ["Camera001"],
            LightNames = ["KeyLight"],
            MaterialNames = ["Floor"],
            TextureNames = ["FloorAlbedo"],
            Nodes =
            [
                new MaxSceneNodeSnapshotData
                {
                    Id = "node:mesh",
                    Name = "MeshNode",
                    Kind = OutWit.Controller.Render.Dcc.Model.DccNodeKind.Mesh,
                    LocalTransform = new MaxSceneTransformSnapshotData
                    {
                        Translation = new MaxSceneVector3SnapshotData { X = 1d, Y = 2d, Z = 3d },
                        Rotation = new MaxSceneQuaternionSnapshotData { X = 0d, Y = 0d, Z = 0d, W = 1d },
                        Scale = new MaxSceneVector3SnapshotData { X = 1.5d, Y = 1d, Z = 0.5d }
                    },
                    MeshId = "mesh:demo",
                    MaterialBindingId = "material:floor"
                },
                new MaxSceneNodeSnapshotData
                {
                    Id = "node:camera",
                    Name = "CameraNode",
                    Kind = OutWit.Controller.Render.Dcc.Model.DccNodeKind.Camera,
                    CameraId = "camera:main"
                },
                new MaxSceneNodeSnapshotData
                {
                    Id = "node:light",
                    Name = "LightNode",
                    Kind = OutWit.Controller.Render.Dcc.Model.DccNodeKind.Light,
                    LightId = "light:key"
                }
            ],
            Meshes =
            [
                new MaxSceneMeshSnapshotData
                {
                    Id = "mesh:demo",
                    Name = "DemoMesh",
                    Positions =
                    [
                        new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 0d },
                        new MaxSceneVector3SnapshotData { X = 1d, Y = 0d, Z = 0d },
                        new MaxSceneVector3SnapshotData { X = 0d, Y = 1d, Z = 0d }
                    ],
                    Normals =
                    [
                        new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d },
                        new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d },
                        new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d }
                    ],
                    Uv0 =
                    [
                        new MaxSceneVector2SnapshotData { X = 0.1d, Y = 0.2d },
                        new MaxSceneVector2SnapshotData { X = 0.9d, Y = 0.2d },
                        new MaxSceneVector2SnapshotData { X = 0.1d, Y = 0.8d }
                    ],
                    TriangleIndices = [0, 1, 2],
                    MaterialIndices = [0]
                }
            ],
            Cameras =
            [
                new MaxSceneCameraSnapshotData
                {
                    Id = "camera:main",
                    Name = "Camera001",
                    VerticalFovDegrees = 45d,
                    NearClip = 0.1d,
                    FarClip = 500d,
                    IsPerspective = true
                }
            ],
            Lights =
            [
                new MaxSceneLightSnapshotData
                {
                    Id = "light:key",
                    Name = "KeyLight",
                    Kind = OutWit.Controller.Render.Dcc.Model.DccLightKind.Point,
                    Intensity = 2d,
                    Range = 20d
                }
            ],
            Materials =
            [
                new MaxSceneMaterialSnapshotData
                {
                    Id = "material:floor",
                    Name = "Floor",
                    TextureSlots =
                    [
                        new MaxSceneTextureSlotSnapshotData
                        {
                            Slot = OutWit.Controller.Render.Dcc.Model.DccTextureSlotKind.BaseColor,
                            ImageAssetId = "image:floor_albedo"
                        }
                    ]
                }
            ],
            ImageAssets =
            [
                new MaxSceneImageAssetSnapshotData
                {
                    Id = "image:floor_albedo",
                    Name = "FloorAlbedo",
                    SourcePath = @"C:\Demo\floor_albedo.png",
                    RelativePath = "textures/floor_albedo.png",
                    AssetKind = "ImageAsset"
                }
            ],
            NodesCount = 3,
            MeshesCount = 1,
            MaterialsCount = 1,
            TexturesCount = 1,
            CamerasCount = 1,
            LightsCount = 1,
            FrameStart = 1,
            FrameEnd = 10,
            RenderWidth = 1920,
            RenderHeight = 1080
        };
    }

    #endregion
}
