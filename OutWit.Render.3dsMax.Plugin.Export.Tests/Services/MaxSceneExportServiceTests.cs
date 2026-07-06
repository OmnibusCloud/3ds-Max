using System.Text.Json;
using OutWit.Cloud.Data.Access;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxSceneExportServiceTests
{
    #region Tests

    [Test]
    public void CollectSummaryUsesSnapshotProviderDataTest()
    {
        var service = MaxSceneExportTestData.CreateService(new MaxSceneSnapshotData
        {
            SceneName = "DemoScene",
            SceneFilePath = @"C:\Demo\DemoScene.max",
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            ActiveRenderCameraName = "Camera001",
            CameraNames = ["Camera001"],
            LightNames = ["KeyLight"],
            MaterialNames = ["Floor", "Wall"],
            TextureNames = ["FloorAlbedo", "WallAlbedo"],
            Nodes =
            [
                new MaxSceneNodeSnapshotData
                {
                    Id = "node:mesh",
                    Name = "MeshNode",
                    Kind = OutWit.Controller.Render.Dcc.Model.DccNodeKind.Mesh,
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
                        new MaxSceneVector2SnapshotData { X = 0d, Y = 0d },
                        new MaxSceneVector2SnapshotData { X = 1d, Y = 0d },
                        new MaxSceneVector2SnapshotData { X = 0d, Y = 1d }
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
                    VerticalFovDegrees = 50d,
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
            NodesCount = 4,
            MeshesCount = 1,
            MaterialsCount = 1,
            TexturesCount = 1,
            CamerasCount = 1,
            LightsCount = 1,
            AnimatedChannelsCount = 0,
            FrameStart = 10,
            FrameEnd = 20,
            RenderWidth = 1280,
            RenderHeight = 720
        });

        var summary = service.CollectSummary();

        Assert.Multiple(() =>
        {
            Assert.That(summary.SceneName, Is.EqualTo("DemoScene"));
            Assert.That(summary.SceneFilePath, Is.EqualTo(@"C:\Demo\DemoScene.max"));
            Assert.That(summary.NodesCount, Is.EqualTo(4));
            Assert.That(summary.MeshesCount, Is.EqualTo(1));
            Assert.That(summary.CamerasCount, Is.EqualTo(1));
            Assert.That(summary.LightsCount, Is.EqualTo(1));
            Assert.That(summary.ActiveRenderCameraName, Is.EqualTo("Camera001"));
            Assert.That(summary.CameraNames, Is.EqualTo(new[] { "Camera001" }));
            Assert.That(summary.LightNames, Is.EqualTo(new[] { "KeyLight" }));
            Assert.That(summary.MaterialNames, Is.EqualTo(new[] { "Floor", "Wall" }));
            Assert.That(summary.TextureNames, Is.EqualTo(new[] { "FloorAlbedo", "WallAlbedo" }));
            Assert.That(summary.FrameStart, Is.EqualTo(10));
            Assert.That(summary.FrameEnd, Is.EqualTo(20));
            Assert.That(summary.RenderWidth, Is.EqualTo(1280));
            Assert.That(summary.RenderHeight, Is.EqualTo(720));
            Assert.That(summary.Nodes.Count, Is.EqualTo(3));
            Assert.That(summary.Meshes.Count, Is.EqualTo(1));
            Assert.That(summary.Cameras.Count, Is.EqualTo(1));
            Assert.That(summary.Lights.Count, Is.EqualTo(1));
            Assert.That(summary.ImageAssets.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void ValidateCurrentSceneBuildsValidDccSceneTest()
    {
        var service = MaxSceneExportTestData.CreateService(new MaxSceneSnapshotData
        {
            SceneName = "DemoScene",
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            MaterialNames = ["Floor", "Wall"],
            TextureNames = ["FloorAlbedo"],
            Nodes =
            [
                new MaxSceneNodeSnapshotData
                {
                    Id = "node:mesh",
                    Name = "MeshNode",
                    Kind = OutWit.Controller.Render.Dcc.Model.DccNodeKind.Mesh,
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
                        new MaxSceneVector2SnapshotData { X = 0d, Y = 0d },
                        new MaxSceneVector2SnapshotData { X = 1d, Y = 0d },
                        new MaxSceneVector2SnapshotData { X = 0d, Y = 1d }
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
                    VerticalFovDegrees = 55d,
                    NearClip = 0.1d,
                    FarClip = 750d,
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
                    Intensity = 3d,
                    Range = 25d
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
            NodesCount = 4,
            MeshesCount = 1,
            MaterialsCount = 1,
            TexturesCount = 1,
            CamerasCount = 1,
            LightsCount = 1,
            AnimatedChannelsCount = 0,
            FrameStart = 1,
            FrameEnd = 100,
            RenderWidth = 2560,
            RenderHeight = 1440
        });

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.SceneName, Is.EqualTo("DemoScene"));
            Assert.That(result.Scene.SourceApplication.ApplicationFamily, Is.EqualTo("3dsMax"));
            Assert.That(result.Scene.SourceApplication.ApplicationVersion, Is.EqualTo("2027"));
            Assert.That(result.Scene.RenderSettings.FrameStart, Is.EqualTo(1));
            Assert.That(result.Scene.RenderSettings.FrameEnd, Is.EqualTo(100));
            Assert.That(result.Scene.RenderSettings.ResolutionX, Is.EqualTo(2560));
            Assert.That(result.Scene.RenderSettings.ResolutionY, Is.EqualTo(1440));
            Assert.That(result.Scene.Nodes.Count, Is.EqualTo(3));
            Assert.That(result.Scene.Meshes.Count, Is.EqualTo(1));
            Assert.That(result.Scene.Cameras.Count, Is.EqualTo(1));
            Assert.That(result.Scene.Lights.Count, Is.EqualTo(1));
            Assert.That(result.Scene.Materials.Count, Is.EqualTo(1));
            Assert.That(result.Scene.ImageAssets.Count, Is.EqualTo(1));
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("Discovered materials", StringComparison.Ordinal)), Is.True);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("Discovered textures", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public void ExportCurrentSceneWritesJsonArtifactTest()
    {
        var service = MaxSceneExportTestData.CreateService(new MaxSceneSnapshotData
        {
            SceneName = "DemoScene",
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            FrameStart = 1,
            FrameEnd = 5
        });
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.Export.Tests.{Guid.NewGuid():N}");

        try
        {
            var result = service.ExportCurrentScene(outputDirectory, MaxSceneExportOutputFormat.Json);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.OutputPath, Is.Not.Null.And.Not.Empty);
                Assert.That(File.Exists(result.OutputPath!), Is.True);
            });

            var json = File.ReadAllText(result.OutputPath!);
            using var document = JsonDocument.Parse(json);

            Assert.That(document.RootElement.GetProperty("SceneName").GetString(), Is.EqualTo("DemoScene"));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public async Task LaunchRenderReturnsBlockedJobStateWhenPreflightFailsTest()
    {
        var service = MaxSceneExportTestData.CreateConnectedRenderService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.ConnectedRender.Blocked.Tests.{Guid.NewGuid():N}");

        try
        {
            var result = await service.LaunchRenderAsync(new MaxSceneLaunchPackageRequest
            {
                RenderMode = "RenderStill",
                ResolutionX = 1920,
                ResolutionY = 1080,
                FrameStart = 1,
                FrameEnd = 1,
                Samples = 64,
                OutputFolder = outputDirectory
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.JobId, Does.StartWith("blocked-"));
                Assert.That(result.ProgressPercent, Is.EqualTo(0d));
                Assert.That(result.ManifestPath, Is.Empty);
                Assert.That(result.PackageArchivePath, Is.Empty);
                Assert.That(result.Diagnostics.Any(me => me.Severity == MaxSceneDiagnosticSeverity.Error), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public void ConnectedRenderPreflightFailsWhenExecutionScopeAndEndpointsAreMissingTest()
    {
        var service = MaxSceneExportTestData.CreateConnectedRenderPreflightService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());

        var result = service.Run(new MaxSceneLaunchPackageRequest
        {
            RenderMode = "RenderStill",
            ResolutionX = 1920,
            ResolutionY = 1080,
            FrameStart = 1,
            FrameEnd = 1,
            Samples = 64,
            OutputFolder = Path.GetTempPath()
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.CanLaunch, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("OmnibusCloud URL or Identity URL", StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("execution group", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ConnectedRenderPreflightPassesForValidLocalLaunchSettingsTest()
    {
        var service = MaxSceneExportTestData.CreateConnectedRenderPreflightService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());

        var result = service.Run(new MaxSceneLaunchPackageRequest
        {
            CloudUrl = "https://omnibuscloud.local",
            IdentityUrl = "https://identity.omnibuscloud.local",
            RenderMode = "RenderFrames",
            ResolutionX = 1920,
            ResolutionY = 1080,
            FrameStart = 1,
            FrameEnd = 10,
            Samples = 64,
            SelectedGroupName = "Artists",
            OutputFolder = Path.GetTempPath()
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.CanLaunch, Is.True);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("preflight passed locally", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ConnectedRenderPreflightPassesForExportBlendWithoutGroupOrResolutionTest()
    {
        // ExportBlend builds the .blend host-side: no execution group, resolution, or frame range
        // applies, so preflight must pass with only the endpoint configured (regression for the
        // Export dialog's default target being blocked).
        var service = MaxSceneExportTestData.CreateConnectedRenderPreflightService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());

        var result = service.Run(new MaxSceneLaunchPackageRequest
        {
            CloudUrl = "https://omnibuscloud.local",
            IdentityUrl = "https://identity.omnibuscloud.local",
            RenderMode = "ExportBlend",
            OutputFolder = Path.GetTempPath()
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.CanLaunch, Is.True);
            Assert.That(result.Diagnostics.Any(me => me.Severity == MaxSceneDiagnosticSeverity.Error), Is.False);
        });
    }

    [Test]
    public async Task LoadExecutionScopeFailsWhenSignedOutTest()
    {
        var sessionService = new FakeMaxCloudSessionService
        {
            State = new MaxConnectedSessionState { IsSignedIn = false }
        };
        var service = MaxSceneExportTestData.CreateConnectedExecutionScopeService(sessionService);

        var result = await service.LoadAsync(new MaxConnectedExecutionScopeRequest
        {
            CloudUrl = "https://omnibuscloud.local"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("sign in", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task LoadExecutionScopeFailsWhenConnectionUnavailableTest()
    {
        var sessionService = new FakeMaxCloudSessionService
        {
            State = new MaxConnectedSessionState { IsSignedIn = true, DisplayName = "Artist One" }
        };
        var connectionService = new FakeMaxCloudConnectionService { Client = null };
        var service = MaxSceneExportTestData.CreateConnectedExecutionScopeService(sessionService, connectionService);

        var result = await service.LoadAsync(new MaxConnectedExecutionScopeRequest
        {
            CloudUrl = "https://omnibuscloud.local"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("connect", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task LoadExecutionScopeReturnsGroupsForSignedInUserTest()
    {
        var sessionService = new FakeMaxCloudSessionService
        {
            State = new MaxConnectedSessionState { IsSignedIn = true, DisplayName = "Artist One" }
        };
        var groupId = Guid.NewGuid();
        var connectionService = new FakeMaxCloudConnectionService
        {
            Client = new FakeWitCloudClient
            {
                ScopeOptions = new ExecutionScopeOptions
                {
                    CanRunOnAllClients = true,
                    Groups =
                    [
                        new ExecutionGroupOption { GroupId = groupId, Name = "Artists", Description = "Primary render group." },
                        new ExecutionGroupOption { Name = "Preview", Description = "Preview group." }
                    ]
                }
            }
        };
        var service = MaxSceneExportTestData.CreateConnectedExecutionScopeService(sessionService, connectionService);

        var result = await service.LoadAsync(new MaxConnectedExecutionScopeRequest
        {
            CloudUrl = "https://omnibuscloud.local"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.UserDisplayName, Is.EqualTo("Artist One"));
            Assert.That(result.SessionStatusText, Does.Contain("Artist One"));
            Assert.That(result.CanRunOnAllClients, Is.True);
            Assert.That(result.Groups.Select(me => me.Name), Is.EquivalentTo(new[] { "Artists", "Preview" }));
            Assert.That(result.Groups.Single(me => me.Name == "Artists").GroupId, Is.EqualTo(groupId.ToString()));
            Assert.That(connectionService.LastRequestedServerUrl, Is.EqualTo("https://omnibuscloud.local"));
        });
    }

    [Test]
    public void UploadConnectedRenderPackageFailsWhenApiKeyIsMissingTest()
    {
        var service = MaxSceneExportTestData.CreateConnectedRenderPackageUploadService(new FakeMaxConnectedRenderArchiveUploader());
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.ConnectedRender.Upload.MissingApiKey.Tests.{Guid.NewGuid():N}");

        try
        {
            var package = MaxSceneExportTestData.CreateLaunchPreparationService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot()).Prepare(new MaxSceneLaunchPackageRequest
            {
                CloudUrl = "https://omnibuscloud.local",
                IdentityUrl = "https://identity.omnibuscloud.local",
                RenderMode = "RenderStill",
                ResolutionX = 1920,
                ResolutionY = 1080,
                FrameStart = 1,
                FrameEnd = 1,
                Samples = 64,
                SelectedGroupName = "Artists",
                OutputFolder = outputDirectory
            });

            var result = service.Upload(new MaxConnectedRenderJobState
            {
                PackageArchivePath = package.PackageArchivePath,
                PrimaryArtifactPath = package.PrimaryArtifactPath
            }, new MaxConnectedRenderUploadRequest
            {
                CloudUrl = "https://omnibuscloud.local",
                IdentityUrl = "https://identity.omnibuscloud.local"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Diagnostics.Any(me => me.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public void UploadConnectedRenderPackageReturnsUploadedBlobIdTest()
    {
        var uploader = new FakeMaxConnectedRenderArchiveUploader
        {
            UploadedBlobId = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };
        var service = MaxSceneExportTestData.CreateConnectedRenderPackageUploadService(uploader);
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.ConnectedRender.Upload.Tests.{Guid.NewGuid():N}");

        try
        {
            var package = MaxSceneExportTestData.CreateLaunchPreparationService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot()).Prepare(new MaxSceneLaunchPackageRequest
            {
                CloudUrl = "https://omnibuscloud.local",
                IdentityUrl = "https://identity.omnibuscloud.local",
                RenderMode = "RenderStill",
                ResolutionX = 1920,
                ResolutionY = 1080,
                FrameStart = 1,
                FrameEnd = 1,
                Samples = 64,
                SelectedGroupName = "Artists",
                OutputFolder = outputDirectory
            });

            var result = service.Upload(new MaxConnectedRenderJobState
            {
                PackageArchivePath = package.PackageArchivePath,
                PrimaryArtifactPath = package.PrimaryArtifactPath
            }, new MaxConnectedRenderUploadRequest
            {
                CloudUrl = "https://omnibuscloud.local",
                IdentityUrl = "https://identity.omnibuscloud.local",
                ApiKey = "wit_sk_demo",
                PackageArchivePath = package.PackageArchivePath
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.UploadedBlobId, Is.EqualTo(Guid.Parse("22222222-2222-2222-2222-222222222222")));
                Assert.That(result.PackageArchivePath, Is.EqualTo(package.PackageArchivePath));
                Assert.That(File.Exists(result.UploadReceiptPath), Is.True);
                Assert.That(uploader.LastRequest, Is.Not.Null);
                Assert.That(uploader.LastRequest!.ApiKey, Is.EqualTo("wit_sk_demo"));
            });

            var uploadReceiptJson = File.ReadAllText(result.UploadReceiptPath);
            using var uploadReceiptDocument = JsonDocument.Parse(uploadReceiptJson);

            Assert.Multiple(() =>
            {
                Assert.That(uploadReceiptDocument.RootElement.GetProperty("UploadedBlobId").GetGuid(), Is.EqualTo(Guid.Parse("22222222-2222-2222-2222-222222222222")));
                Assert.That(uploadReceiptDocument.RootElement.GetProperty("PackageArchivePath").GetString(), Is.EqualTo(package.PackageArchivePath));
            });
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public async Task DownloadConnectedRenderArtifactCopiesPreparedArchiveTest()
    {
        var renderService = MaxSceneExportTestData.CreateConnectedRenderService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var downloadService = MaxSceneExportTestData.CreateConnectedRenderDownloadService();
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.ConnectedRender.Download.Tests.{Guid.NewGuid():N}");
        var downloadDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.ConnectedRender.Download.Output.{Guid.NewGuid():N}");

        try
        {
            var job = await renderService.LaunchRenderAsync(new MaxSceneLaunchPackageRequest
            {
                CloudUrl = "https://omnibuscloud.local",
                IdentityUrl = "https://identity.omnibuscloud.local",
                RenderMode = "RenderStill",
                ResolutionX = 1920,
                ResolutionY = 1080,
                FrameStart = 1,
                FrameEnd = 1,
                Samples = 64,
                SelectedGroupName = "Artists",
                OutputFolder = outputDirectory
            });

            var result = downloadService.Download(job, downloadDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(File.Exists(result.DownloadedFilePath), Is.True);
                Assert.That(result.DownloadedFilePath, Does.EndWith(".zip"));
                Assert.That(result.Diagnostics.Any(me => me.Message.Contains("placeholder", StringComparison.OrdinalIgnoreCase)), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);

            if (Directory.Exists(downloadDirectory))
                Directory.Delete(downloadDirectory, true);
        }
    }

    [Test]
    public async Task LaunchRenderWritesSubmissionReceiptJsonTest()
    {
        var service = MaxSceneExportTestData.CreateConnectedRenderService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.ConnectedRender.Receipt.Tests.{Guid.NewGuid():N}");

        try
        {
            var result = await service.LaunchRenderAsync(new MaxSceneLaunchPackageRequest
            {
                CloudUrl = "https://omnibuscloud.local",
                IdentityUrl = "https://identity.omnibuscloud.local",
                RenderMode = "RenderStill",
                ResolutionX = 1920,
                ResolutionY = 1080,
                FrameStart = 1,
                FrameEnd = 1,
                Samples = 64,
                SelectedGroupName = "Artists",
                OutputFolder = outputDirectory
            });

            var receiptJson = File.ReadAllText(result.SubmissionReceiptPath);
            using var receiptDocument = JsonDocument.Parse(receiptJson);

            Assert.Multiple(() =>
            {
                Assert.That(receiptDocument.RootElement.GetProperty("RenderMode").GetString(), Is.EqualTo("RenderStill"));
                Assert.That(receiptDocument.RootElement.GetProperty("SelectedGroupName").GetString(), Is.EqualTo("Artists"));
                Assert.That(receiptDocument.RootElement.GetProperty("PackageArchivePath").GetString(), Is.EqualTo(result.PackageArchivePath));
            });
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public void DownloadConnectedRenderArtifactFailsWhenArtifactIsMissingTest()
    {
        var service = MaxSceneExportTestData.CreateConnectedRenderDownloadService();
        var downloadDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.ConnectedRender.Download.Missing.{Guid.NewGuid():N}");

        try
        {
            var result = service.Download(new MaxConnectedRenderJobState
            {
                JobId = "local-missing",
                PrimaryArtifactPath = string.Empty,
                IsPlaceholderLocalSubmission = true
            }, downloadDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Diagnostics.Any(me => me.Message.Contains("artifact is missing", StringComparison.OrdinalIgnoreCase)), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(downloadDirectory))
                Directory.Delete(downloadDirectory, true);
        }
    }

    [Test]
    public async Task LaunchRenderCreatesTrackablePlaceholderJobStateTest()
    {
        var service = MaxSceneExportTestData.CreateConnectedRenderService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.ConnectedRender.Tests.{Guid.NewGuid():N}");

        try
        {
            var result = await service.LaunchRenderAsync(new MaxSceneLaunchPackageRequest
            {
                CloudUrl = "https://omnibuscloud.local",
                IdentityUrl = "https://identity.omnibuscloud.local",
                RenderMode = "RenderStill",
                ResolutionX = 1920,
                ResolutionY = 1080,
                FrameStart = 1,
                FrameEnd = 1,
                Samples = 64,
                SelectedGroupName = "Artists",
                OutputFolder = outputDirectory
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.JobId, Does.StartWith("local-"));
                Assert.That(result.IsPlaceholderLocalSubmission, Is.True);
                Assert.That(result.ProgressPercent, Is.GreaterThanOrEqualTo(15d));
                Assert.That(File.Exists(result.ManifestPath), Is.True);
                Assert.That(File.Exists(result.SubmissionReceiptPath), Is.True);
                Assert.That(File.Exists(result.PackageArchivePath), Is.True);
                Assert.That(File.Exists(result.PrimaryArtifactPath), Is.True);
                Assert.That(result.PrimaryArtifactPath, Does.EndWith(".zip"));
            });

            var refreshed = await service.RefreshJobAsync(result);

            Assert.Multiple(() =>
            {
                Assert.That(refreshed.ProgressPercent, Is.GreaterThanOrEqualTo(25d));
                Assert.That(refreshed.StatusText, Does.Contain("Local submission receipt is ready"));
            });
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public void PrepareLaunchPackageCreatesManifestAndArtifactsTest()
    {
        var service = MaxSceneExportTestData.CreateLaunchPreparationService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.LaunchPackage.Tests.{Guid.NewGuid():N}");

        try
        {
            var result = service.Prepare(new MaxSceneLaunchPackageRequest
            {
                CloudUrl = "https://omnibuscloud.local",
                IdentityUrl = "https://identity.omnibuscloud.local",
                RenderMode = "RenderStill",
                ResolutionX = 1280,
                ResolutionY = 720,
                FrameStart = 1,
                FrameEnd = 1,
                Samples = 32,
                SelectedGroupName = "Artists",
                OutputFolder = outputDirectory
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.PackageId, Is.Not.Empty);
                Assert.That(result.PackageFolderPath, Is.Not.Empty.And.Contain(outputDirectory));
                Assert.That(File.Exists(result.ManifestPath), Is.True);
                Assert.That(File.Exists(result.PackageArchivePath), Is.True);
                Assert.That(File.Exists(Path.Combine(result.PackageFolderPath, "dcc-scene.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(result.PackageFolderPath, "dcc-scene.mpack.gz")), Is.True);
                Assert.That(result.PrimaryArtifactPath, Does.EndWith(".zip"));
                Assert.That(result.Diagnostics.Any(me => me.Message.Contains("Prepared local launch package", StringComparison.OrdinalIgnoreCase)), Is.True);
            });

            MaxBatchLaunchPackageAssertions.AssertArchiveContainsExpectedArtifacts(
                result.PackageArchivePath,
                result.ManifestPath);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public void ExportCurrentSceneWritesSmallerCompressedMemoryPackArtifactThanCompressedJsonTest()
    {
        var service = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.Export.GZip.Tests.{Guid.NewGuid():N}");

        try
        {
            var jsonResult = service.ExportCurrentScene(outputDirectory, MaxSceneExportOutputFormat.Json);
            var memoryPackResult = service.ExportCurrentScene(outputDirectory, MaxSceneExportOutputFormat.MemoryPack);
            var jsonGzipResult = service.ExportCurrentScene(outputDirectory, MaxSceneExportOutputFormat.JsonGzip);
            var memoryPackGzipResult = service.ExportCurrentScene(outputDirectory, MaxSceneExportOutputFormat.MemoryPackGzip);

            var jsonLength = new FileInfo(jsonResult.OutputPath!).Length;
            var memoryPackLength = new FileInfo(memoryPackResult.OutputPath!).Length;
            var jsonGzipLength = new FileInfo(jsonGzipResult.OutputPath!).Length;
            var memoryPackGzipLength = new FileInfo(memoryPackGzipResult.OutputPath!).Length;

            Assert.Multiple(() =>
            {
                Assert.That(jsonGzipResult.IsSuccess, Is.True);
                Assert.That(memoryPackGzipResult.IsSuccess, Is.True);
                Assert.That(jsonGzipResult.OutputPath, Is.Not.Null.And.EndsWith(".json.gz"));
                Assert.That(memoryPackGzipResult.OutputPath, Is.Not.Null.And.EndsWith(".mpack.gz"));
                Assert.That(jsonGzipLength, Is.GreaterThan(0));
                Assert.That(memoryPackGzipLength, Is.GreaterThan(0));
                Assert.That(jsonGzipLength, Is.LessThan(jsonLength));
                Assert.That(memoryPackGzipLength, Is.LessThan(memoryPackLength));
                Assert.That(memoryPackGzipLength, Is.LessThan(jsonGzipLength));
            });
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public void ExportCurrentSceneWritesSmallerMemoryPackArtifactThanJsonTest()
    {
        var service = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"OutWit.3dsMax.Export.MemoryPack.Tests.{Guid.NewGuid():N}");

        try
        {
            var jsonResult = service.ExportCurrentScene(outputDirectory, MaxSceneExportOutputFormat.Json);
            var memoryPackResult = service.ExportCurrentScene(outputDirectory, MaxSceneExportOutputFormat.MemoryPack);

            var jsonLength = new FileInfo(jsonResult.OutputPath!).Length;
            var memoryPackLength = new FileInfo(memoryPackResult.OutputPath!).Length;

            Assert.Multiple(() =>
            {
                Assert.That(jsonResult.IsSuccess, Is.True);
                Assert.That(memoryPackResult.IsSuccess, Is.True);
                Assert.That(jsonResult.OutputPath, Is.Not.Null.And.EndsWith(".json"));
                Assert.That(memoryPackResult.OutputPath, Is.Not.Null.And.EndsWith(".mpack"));
                Assert.That(jsonLength, Is.GreaterThan(0));
                Assert.That(memoryPackLength, Is.GreaterThan(0));
                Assert.That(memoryPackLength, Is.LessThan(jsonLength));
            });
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    [Test]
    public void ValidateCurrentSceneAddsUnsavedSceneWarningTest()
    {
        var service = MaxSceneExportTestData.CreateService(new MaxSceneSnapshotData
        {
            SceneName = "UnsavedScene",
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            Nodes =
            [
                new MaxSceneNodeSnapshotData
                {
                    Id = "node:mesh",
                    Name = "MeshNode",
                    Kind = OutWit.Controller.Render.Dcc.Model.DccNodeKind.Mesh,
                    MeshId = "mesh:demo"
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
                        new MaxSceneVector2SnapshotData { X = 0d, Y = 0d },
                        new MaxSceneVector2SnapshotData { X = 1d, Y = 0d },
                        new MaxSceneVector2SnapshotData { X = 0d, Y = 1d }
                    ],
                    TriangleIndices = [0, 1, 2],
                    MaterialIndices = [0]
                }
            ],
            FrameStart = 1,
            FrameEnd = 1
        });

        var result = service.ValidateCurrentScene();

        Assert.That(result.Diagnostics.Any(me => me.Message.Contains("unsaved", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public void ValidateCurrentSceneAddsMissingRenderCameraInfoTest()
    {
        var service = MaxSceneExportTestData.CreateService(new MaxSceneSnapshotData
        {
            SceneName = "CameraScene",
            SourceApplicationLabel = "3ds Max 2027",
            SourceApplicationVersion = "2027",
            Nodes =
            [
                new MaxSceneNodeSnapshotData
                {
                    Id = "node:camera",
                    Name = "CameraNode",
                    Kind = OutWit.Controller.Render.Dcc.Model.DccNodeKind.Camera,
                    CameraId = "camera:main"
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
            CamerasCount = 1,
            FrameStart = 1,
            FrameEnd = 1
        });

        var result = service.ValidateCurrentScene();

        Assert.That(result.Diagnostics.Any(me => me.Message.Contains("active render camera", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public void CollectSummaryNormalizesMissingMetadataAndInvalidRangesTest()
    {
        var service = MaxSceneExportTestData.CreateService(new MaxSceneSnapshotData
        {
            SceneName = string.Empty,
            SourceApplicationLabel = string.Empty,
            FrameStart = 0,
            FrameEnd = -10,
            RenderWidth = 0,
            RenderHeight = 0
        });

        var summary = service.CollectSummary();

        Assert.Multiple(() =>
        {
            Assert.That(summary.SceneName, Is.EqualTo("3ds Max Scene"));
            Assert.That(summary.SourceApplicationLabel, Is.EqualTo("3ds Max 2027"));
            Assert.That(summary.FrameStart, Is.EqualTo(1));
            Assert.That(summary.FrameEnd, Is.EqualTo(1));
            Assert.That(summary.RenderWidth, Is.EqualTo(1920));
            Assert.That(summary.RenderHeight, Is.EqualTo(1080));
        });
    }

    [Test]
    public void ValidateCurrentSceneFailsWhenTextureSlotReferencesMissingImageAssetTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.ImageAssets.Clear();

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("missing image asset", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneMapsMaterialBindingAndTextureSlotTest()
    {
        var service = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Nodes.Single(me => me.Id == "node:mesh").MaterialBindingId, Is.EqualTo("material:floor"));
            Assert.That(result.Scene.Materials.Single().TextureSlots.Single().ImageAssetId, Is.EqualTo("image:floor_albedo"));
            Assert.That(result.Scene.ImageAssets.Single().RelativePath, Is.EqualTo("textures/floor_albedo.png"));
        });
    }

    [Test]
    public void ValidateCurrentSceneMapsNodeTransformAndMeshUvsTest()
    {
        var service = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Nodes.Single(me => me.Id == "node:mesh").LocalTransform.Translation.X, Is.EqualTo(1d));
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:mesh").LocalTransform.Translation.Y, Is.EqualTo(2d));
            // Mesh node scale is preserved (vertices are object-space; dropping the scale inflated
            // any scaled prop by its inverse factor).
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:mesh").LocalTransform.Scale.X, Is.EqualTo(1.5d));
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:mesh").LocalTransform.Scale.Z, Is.EqualTo(0.5d));
            Assert.That(result.Scene.Meshes.Single().Uv0[0].X, Is.EqualTo(0.1d));
            Assert.That(result.Scene.Meshes.Single().Uv0[0].Y, Is.EqualTo(0.2d));
        });
    }

    [Test]
    public void ValidateCurrentSceneMapsVisibilityAndRenderableFlagsTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Nodes[0].Visible = false;
        snapshot.Nodes[0].Renderable = false;
        snapshot.Nodes[2].Visible = false;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Nodes.Single(me => me.Id == "node:mesh").Visible, Is.False);
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:mesh").Renderable, Is.False);
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:camera").Visible, Is.True);
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:camera").Renderable, Is.True);
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:light").Visible, Is.False);
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:light").Renderable, Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneAppliesDccMapperNormalizationDefaultsTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.SourceApplicationVersion = string.Empty;
        snapshot.FrameStart = 4;
        snapshot.FrameEnd = 12;
        snapshot.FrameRate = 25;
        snapshot.RenderWidth = 2048;
        snapshot.RenderHeight = 858;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.SourceApplication.ApplicationFamily, Is.EqualTo("3dsMax"));
            Assert.That(result.Scene.SourceApplication.ApplicationVersion, Is.EqualTo("3ds Max 2027"));
            Assert.That(result.Scene.SourceApplication.ExporterVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Scene.Units.LinearUnit, Is.EqualTo("centimeter"));
            Assert.That(result.Scene.Units.UnitsPerMeter, Is.EqualTo(100d));
            Assert.That(result.Scene.AxisSystem.Handedness, Is.EqualTo("right"));
            Assert.That(result.Scene.AxisSystem.UpAxis, Is.EqualTo("Z"));
            Assert.That(result.Scene.AxisSystem.ForwardAxis, Is.EqualTo("Y"));
            Assert.That(result.Scene.RenderSettings.ResolutionX, Is.EqualTo(2048));
            Assert.That(result.Scene.RenderSettings.ResolutionY, Is.EqualTo(858));
            Assert.That(result.Scene.RenderSettings.FrameStart, Is.EqualTo(4));
            Assert.That(result.Scene.RenderSettings.FrameEnd, Is.EqualTo(12));
            Assert.That(result.Scene.RenderSettings.Fps, Is.EqualTo(25));
            Assert.That(result.Scene.RenderSettings.Samples, Is.EqualTo(64));
            Assert.That(result.Scene.RenderSettings.TargetEngine, Is.EqualTo(OutWit.Controller.Render.Model.RenderEngine.Cycles));
        });
    }

    [Test]
    public void ValidateCurrentSceneNormalizesInvalidCameraClipDistancesTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Cameras[0].NearClip = 500d;
        snapshot.Cameras[0].FarClip = 10d;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Cameras[0].NearClip, Is.EqualTo(0.1d));
            Assert.That(result.Scene.Cameras[0].FarClip, Is.EqualTo(1000d));
        });
    }

    [Test]
    public void ValidateCurrentSceneKeepsNonMeshTranslationsAndMeshScaleTest()
    {
        // Historical behaviour scaled camera/light translations by the average mesh scale to
        // compensate for the mesh scale being dropped. Mesh transforms now keep their true scale,
        // so every node stays at its raw Max world coordinates.
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Nodes.Single(me => me.Id == "node:mesh").LocalTransform.Scale.X = 0.1d;
        snapshot.Nodes.Single(me => me.Id == "node:mesh").LocalTransform.Scale.Y = 0.1d;
        snapshot.Nodes.Single(me => me.Id == "node:mesh").LocalTransform.Scale.Z = 0.1d;
        snapshot.Nodes.Single(me => me.Id == "node:camera").LocalTransform.Translation.X = 50d;
        snapshot.Nodes.Single(me => me.Id == "node:camera").LocalTransform.Translation.Y = -70d;
        snapshot.Nodes.Single(me => me.Id == "node:camera").LocalTransform.Translation.Z = 90d;
        snapshot.Nodes.Single(me => me.Id == "node:light").LocalTransform.Translation.X = 40d;
        snapshot.Nodes.Single(me => me.Id == "node:light").LocalTransform.Translation.Y = -60d;
        snapshot.Nodes.Single(me => me.Id == "node:light").LocalTransform.Translation.Z = 80d;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            // The light (not touched by the camera framer) keeps its raw translation; the mesh keeps
            // its true node scale. (The camera's translation is re-derived by the auto-framer here —
            // it does not face the subject — so it is not asserted.)
            Assert.That(result.Scene!.Nodes.Single(me => me.Id == "node:light").LocalTransform.Translation.X, Is.EqualTo(40d).Within(1e-9));
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:light").LocalTransform.Translation.Y, Is.EqualTo(-60d).Within(1e-9));
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:light").LocalTransform.Translation.Z, Is.EqualTo(80d).Within(1e-9));
            Assert.That(result.Scene.Nodes.Single(me => me.Id == "node:mesh").LocalTransform.Scale.X, Is.EqualTo(0.1d).Within(1e-9));
            // Light at (40,-60,80); scene centre at the mesh node (1,2,3): the characteristic
            // distance squared is 39^2 + 62^2 + 77^2 = 11294. The single light normalizes to a unit
            // multiplier, so power = 1200 * d^2 / 68.
            Assert.That(result.Scene.Lights.Single().Intensity, Is.EqualTo(1d * 1200d * 11294d / 68d).Within(1e-6));
            // The 20-unit cutoff no longer clears the light-to-subject distance (~106), so it is
            // dropped below the generator threshold (infinite range).
            Assert.That(result.Scene.Lights.Single().Range, Is.EqualTo(0.01d).Within(1e-9));
        });
    }

    [Test]
    public void ValidateCurrentSceneSynthesizesDefaultLightsWhenSceneHasNoneTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Lights.Clear();
        snapshot.LightNames.Clear();
        snapshot.LightsCount = 0;
        snapshot.Nodes.RemoveAll(me => me.Kind == OutWit.Controller.Render.Dcc.Model.DccNodeKind.Light);
        snapshot.NodesCount = snapshot.Nodes.Count;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Summary.UsesSyntheticDefaultLights, Is.True);
            // Scene had no lights → a default rig is synthesized (not left black) and reported as Info.
            Assert.That(result.Scene!.Lights, Has.Count.GreaterThanOrEqualTo(2));
            // Sun lights: irradiance is distance-independent, so the rig looks identical at any
            // scene scale (a point rig hit the wattage cap on large scenes and washed flat).
            Assert.That(result.Scene.Lights.All(me => me.Kind == OutWit.Controller.Render.Dcc.Model.DccLightKind.Sun), Is.True);
            // Key sun at the reference irradiance (multiplier 1 × 4 W/m²); ratios preserved below it.
            Assert.That(result.Scene.Lights.Max(me => me.Intensity), Is.EqualTo(4d).Within(1e-9));
            Assert.That(result.Scene.Lights.Min(me => me.Intensity), Is.GreaterThan(0d));
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("Synthesized default", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneWarnsWhenNoLightsAndNoGeometryToLightTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Lights.Clear();
        snapshot.LightNames.Clear();
        snapshot.LightsCount = 0;
        snapshot.Meshes.Clear();
        snapshot.MeshesCount = 0;
        snapshot.Nodes.RemoveAll(me => me.Kind != OutWit.Controller.Render.Dcc.Model.DccNodeKind.Camera);
        snapshot.NodesCount = snapshot.Nodes.Count;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Summary.UsesSyntheticDefaultLights, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("No explicit lights", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneAddsNearZeroLightRangeWarningTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Lights[0].Range = 0.01d;

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("near-zero attenuation range", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneMapsCameraAndLightPropertiesTest()
    {
        var service = MaxSceneExportTestData.CreateService(MaxSceneExportTestData.CreateMinimalValidSceneSnapshot());

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Scene, Is.Not.Null);
            Assert.That(result.Scene!.Cameras.Single().VerticalFovDegrees, Is.EqualTo(45d));
            Assert.That(result.Scene.Cameras.Single().NearClip, Is.EqualTo(0.1d));
            Assert.That(result.Scene.Cameras.Single().FarClip, Is.EqualTo(500d));
            Assert.That(result.Scene.Lights.Single().Kind, Is.EqualTo(OutWit.Controller.Render.Dcc.Model.DccLightKind.Point));
            // Light at origin; scene centre at the mesh node (1,2,3) → characteristic distance
            // sqrt(14). The single light is normalized to a unit multiplier by auto-exposure (its raw
            // intensity is the scene median), so power = 1200 * d^2 / 68.
            Assert.That(result.Scene.Lights.Single().Intensity, Is.EqualTo(1d * 1200d * 14d / 68d).Within(1e-6));
            Assert.That(result.Scene.Lights.Single().Range, Is.EqualTo(20d));
        });
    }

    [Test]
    public void ValidateCurrentSceneScalesPointLightPowerWithDistanceSquaredTest()
    {
        var nearSnapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        SetLightPosition(nearSnapshot, 10d, 0d, 0d);
        nearSnapshot.Lights[0].Intensity = 1d;
        nearSnapshot.Lights[0].Range = 0.01d;

        var farSnapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        SetLightPosition(farSnapshot, 30d, 0d, 0d);
        farSnapshot.Lights[0].Intensity = 1d;
        farSnapshot.Lights[0].Range = 0.01d;

        var nearResult = MaxSceneExportTestData.CreateService(nearSnapshot).ValidateCurrentScene();
        var farResult = MaxSceneExportTestData.CreateService(farSnapshot).ValidateCurrentScene();

        var nearIntensity = nearResult.Scene!.Lights.Single().Intensity;
        var farIntensity = farResult.Scene!.Lights.Single().Intensity;

        Assert.Multiple(() =>
        {
            // A native Max light multiplier of 1 must not render black: it scales to hundreds of watts.
            Assert.That(nearIntensity, Is.GreaterThan(50d));
            // Tripling the light distance must raise the power ~9x (inverse-square compensation).
            Assert.That(farIntensity / nearIntensity, Is.EqualTo(9d).Within(0.5d));
        });
    }

    [Test]
    public void ValidateCurrentSceneMapsSunLightToDistanceIndependentIrradianceTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Lights[0].Kind = OutWit.Controller.Render.Dcc.Model.DccLightKind.Sun;
        snapshot.Lights[0].Intensity = 1d;
        SetLightPosition(snapshot, 500d, 500d, 500d);

        var result = MaxSceneExportTestData.CreateService(snapshot).ValidateCurrentScene();

        // Sun strength is irradiance (W/m^2) and must not blow up with distance like point lights.
        Assert.That(result.Scene!.Lights.Single().Intensity, Is.EqualTo(4d).Within(1e-9));
    }

    [Test]
    public void ValidateCurrentSceneDropsLightCutoffThatWouldClipTheSubjectTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        SetLightPosition(snapshot, 100d, 0d, 0d);
        // A 5-unit cutoff on a light 100 units away would plunge the subject into darkness.
        snapshot.Lights[0].Range = 5d;

        var result = MaxSceneExportTestData.CreateService(snapshot).ValidateCurrentScene();

        Assert.That(result.Scene!.Lights.Single().Range, Is.EqualTo(0.01d));
    }

    [Test]
    public void ValidateCurrentSceneMapsTransformKeyframesWithNodeScaleRulesTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        var meshNode = snapshot.Nodes.Single(me => me.Kind == OutWit.Controller.Render.Dcc.Model.DccNodeKind.Mesh);
        meshNode.TransformKeyframes =
        [
            new MaxSceneTransformKeyframeSnapshotData
            {
                Frame = 1,
                Transform = new MaxSceneTransformSnapshotData
                {
                    Translation = new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 0d },
                    Rotation = new MaxSceneQuaternionSnapshotData { W = 1d },
                    Scale = new MaxSceneVector3SnapshotData { X = 2d, Y = 2d, Z = 2d }
                }
            },
            new MaxSceneTransformKeyframeSnapshotData
            {
                Frame = 10,
                Transform = new MaxSceneTransformSnapshotData
                {
                    Translation = new MaxSceneVector3SnapshotData { X = 5d, Y = 0d, Z = 0d },
                    Rotation = new MaxSceneQuaternionSnapshotData { W = 1d },
                    Scale = new MaxSceneVector3SnapshotData { X = 2d, Y = 2d, Z = 2d }
                }
            }
        ];

        var result = MaxSceneExportTestData.CreateService(snapshot).ValidateCurrentScene();

        var mappedMesh = result.Scene!.Nodes.Single(me => me.Kind == OutWit.Controller.Render.Dcc.Model.DccNodeKind.Mesh);
        Assert.Multiple(() =>
        {
            Assert.That(mappedMesh.TransformKeyframes, Has.Count.EqualTo(2));
            Assert.That(mappedMesh.TransformKeyframes[1].Frame, Is.EqualTo(10));
            Assert.That(mappedMesh.TransformKeyframes[1].Transform.Translation.X, Is.EqualTo(5d).Within(1e-9));
            // Mesh node scale is preserved — on keyframes too, not just the static transform.
            Assert.That(mappedMesh.TransformKeyframes[0].Transform.Scale.X, Is.EqualTo(2d).Within(1e-9));
        });
    }

    private static void SetLightPosition(MaxSceneSnapshotData snapshot, double x, double y, double z)
    {
        var lightNode = snapshot.Nodes.Single(me => me.Kind == OutWit.Controller.Render.Dcc.Model.DccNodeKind.Light);
        lightNode.LocalTransform.Translation.X = x;
        lightNode.LocalTransform.Translation.Y = y;
        lightNode.LocalTransform.Translation.Z = z;
    }

    [Test]
    public void ValidateCurrentSceneFailsWhenMeshNodeReferencesMissingMeshTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Nodes[0].MeshId = "mesh:missing";

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("missing mesh", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneFailsWhenCameraNodeReferencesMissingCameraTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Nodes[1].CameraId = "camera:missing";

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("missing camera", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public void ValidateCurrentSceneFailsWhenLightNodeReferencesMissingLightTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Nodes[2].LightId = "light:missing";

        var service = MaxSceneExportTestData.CreateService(snapshot);

        var result = service.ValidateCurrentScene();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Any(me => me.Message.Contains("missing light", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    #endregion

    #region Functions

    #endregion
}
