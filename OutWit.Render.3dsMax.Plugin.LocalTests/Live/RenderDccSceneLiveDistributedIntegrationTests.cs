using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using OutWit.Cloud.SDK;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.LocalTests.Live;

/// <summary>
/// Live distributed integration tests for the neutral DCC-scene render path — the same
/// server-side pipeline the 3ds Max plugin submits into. A synthetic DccSceneData payload
/// (the shape the plugin exporter produces) is submitted to the deployed OmnibusCloud
/// instance and rendered by real connected node clients.
/// </summary>
[TestFixture]
[Explicit("Live external distributed integration test against the deployed OmnibusCloud instance and real connected node clients. Set OMNIBUSCLOUD_API_KEY to enable.")]
[Category("Live")]
[NonParallelizable]
public sealed class RenderDccSceneLiveDistributedIntegrationTests : LiveDistributedIntegrationTestBase
{
    #region Constants

    private static readonly TimeSpan TIMEOUT = TimeSpan.FromMinutes(10);

    #endregion

    #region Tests

    [Test]
    public async Task RenderDccSceneStillDistributedLiveRunProducesNonBlackImageTest()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        using var cts = new CancellationTokenSource(TIMEOUT);
        var client = Client;
        var scene = CreateDccScene();

        var handle = await client.Scripts.RunAsync("RenderDccSceneStill", scene, 1, CreateRenderOptions(), cts.Token);
        var waitResult = await handle.WaitAsync<Guid>(pollInterval: TimeSpan.FromSeconds(2), ct: cts.Token);

        AssertCompletedOrIgnoreExternalCapacity(waitResult, "Live distributed RenderDccSceneStill", "RenderDccSceneStill");

        var resultBlobId = waitResult.Result;
        if (resultBlobId == Guid.Empty)
            resultBlobId = await handle.GetResultAsync<Guid>(ct: cts.Token);

        Assert.That(resultBlobId, Is.Not.EqualTo(Guid.Empty),
            "RenderDccSceneStill did not return a result blob id.");

        var outputDirectory = GetPersistentOutputDirectory(solutionRoot, "RenderDccSceneStill", handle.JobId, resultBlobId);
        var localImagePath = Path.Combine(outputDirectory, "renderdccscenestill-live-result.png");
        await client.Blobs.DownloadBlobToFileAsync(resultBlobId, localImagePath, ct: cts.Token);

        Assert.That(File.Exists(localImagePath), Is.True, $"Downloaded result was not found at {localImagePath}");
        Assert.That(new FileInfo(localImagePath).Length, Is.GreaterThan(0), "Downloaded result file is empty.");

        TestContext.Progress.WriteLine($"Live RenderDccSceneStill output saved to: {localImagePath}");
        TestContext.Progress.WriteLine($"Live RenderDccSceneStill identifiers: JobId={handle.JobId}; ResultBlobId={resultBlobId}");

        AssertImageIsNotSolidBlack(localImagePath, "Live distributed RenderDccSceneStill");
    }

    [Test]
    public async Task RenderDccSceneStillTiledDistributedLiveRunProducesNonBlackImageTest()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        using var cts = new CancellationTokenSource(TIMEOUT);
        var client = Client;
        var scene = CreateDccScene();

        var handle = await client.Scripts.RunAsync("RenderDccSceneStillTiled", scene, 1, 2, 2, CreateRenderOptions(), CreateTileOptions(), cts.Token);
        var waitResult = await handle.WaitAsync<Guid>(pollInterval: TimeSpan.FromSeconds(2), ct: cts.Token);

        AssertCompletedOrIgnoreExternalCapacity(waitResult, "Live distributed RenderDccSceneStillTiled", "RenderDccSceneStillTiled");

        var resultBlobId = waitResult.Result;
        if (resultBlobId == Guid.Empty)
            resultBlobId = await handle.GetResultAsync<Guid>(ct: cts.Token);

        Assert.That(resultBlobId, Is.Not.EqualTo(Guid.Empty),
            "RenderDccSceneStillTiled did not return a result blob id.");

        var outputDirectory = GetPersistentOutputDirectory(solutionRoot, "RenderDccSceneStillTiled", handle.JobId, resultBlobId);
        var localImagePath = Path.Combine(outputDirectory, "renderdccscenestilltiled-live-result.png");
        await client.Blobs.DownloadBlobToFileAsync(resultBlobId, localImagePath, ct: cts.Token);

        Assert.That(File.Exists(localImagePath), Is.True, $"Downloaded result was not found at {localImagePath}");
        Assert.That(new FileInfo(localImagePath).Length, Is.GreaterThan(0), "Downloaded result file is empty.");

        TestContext.Progress.WriteLine($"Live RenderDccSceneStillTiled output saved to: {localImagePath}");
        TestContext.Progress.WriteLine($"Live RenderDccSceneStillTiled identifiers: JobId={handle.JobId}; ResultBlobId={resultBlobId}");

        AssertImageIsNotSolidBlack(localImagePath, "Live distributed RenderDccSceneStillTiled");
    }

    [Test]
    public async Task RenderDccSceneFramesDistributedLiveRunProducesExpectedFrameSetTest()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        using var cts = new CancellationTokenSource(TIMEOUT);
        var client = Client;
        var scene = CreateDccScene();
        scene.RenderSettings!.FrameEnd = 3;

        var handle = await client.Scripts.RunAsync("RenderDccSceneFrames", scene, 1, 3, CreateRenderOptions(), cts.Token);
        var waitResult = await handle.WaitAsync<Guid?[]>(pollInterval: TimeSpan.FromSeconds(2), ct: cts.Token);

        AssertCompletedOrIgnoreExternalCapacity(waitResult, "Live distributed RenderDccSceneFrames", "RenderDccSceneFrames");

        var frameBlobIds = await GetFrameBlobIdsAsync(handle, waitResult.Result, cts.Token);
        Assert.That(frameBlobIds.Length, Is.EqualTo(3), "RenderDccSceneFrames did not return the expected frame blob ids.");

        var outputDirectory = GetPersistentOutputDirectory(solutionRoot, "RenderDccSceneFrames", handle.JobId, frameBlobIds[0]);
        for (var index = 0; index < frameBlobIds.Length; index++)
        {
            var localImagePath = Path.Combine(outputDirectory, $"frame_{index + 1:D4}.png");
            await client.Blobs.DownloadBlobToFileAsync(frameBlobIds[index], localImagePath, ct: cts.Token);
            Assert.That(File.Exists(localImagePath), Is.True, $"Downloaded frame was not found at {localImagePath}");
            Assert.That(new FileInfo(localImagePath).Length, Is.GreaterThan(0), $"Downloaded frame file is empty: {localImagePath}");
            AssertImageIsNotSolidBlack(localImagePath, $"Live distributed RenderDccSceneFrames frame {index + 1}");
        }

        TestContext.Progress.WriteLine($"Live RenderDccSceneFrames output saved to: {outputDirectory}");
        TestContext.Progress.WriteLine($"Live RenderDccSceneFrames identifiers: JobId={handle.JobId}; FrameBlobIds={string.Join(", ", frameBlobIds)}");
    }

    [Test]
    public async Task RenderDccSceneVideoDistributedLiveRunProducesMp4BlobTest()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        using var cts = new CancellationTokenSource(TIMEOUT);
        var client = Client;
        var scene = CreateDccScene();
        scene.RenderSettings!.FrameEnd = 3;

        var handle = await client.Scripts.RunAsync("RenderDccSceneVideo", scene, 1, 3, CreateRenderOptions(), CreateVideoOptions(), cts.Token);
        var waitResult = await handle.WaitAsync<Guid>(pollInterval: TimeSpan.FromSeconds(2), ct: cts.Token);

        AssertCompletedOrIgnoreExternalCapacity(waitResult, "Live distributed RenderDccSceneVideo", "RenderDccSceneVideo");

        var resultBlobId = waitResult.Result;
        if (resultBlobId == Guid.Empty)
            resultBlobId = await handle.GetResultAsync<Guid>(ct: cts.Token);

        Assert.That(resultBlobId, Is.Not.EqualTo(Guid.Empty),
            "RenderDccSceneVideo did not return a result blob id.");

        var outputDirectory = GetPersistentOutputDirectory(solutionRoot, "RenderDccSceneVideo", handle.JobId, resultBlobId);
        var localVideoPath = Path.Combine(outputDirectory, "renderdccscenevideo-live-result.mp4");
        await client.Blobs.DownloadBlobToFileAsync(resultBlobId, localVideoPath, ct: cts.Token);

        Assert.That(File.Exists(localVideoPath), Is.True, $"Downloaded result was not found at {localVideoPath}");
        Assert.That(new FileInfo(localVideoPath).Length, Is.GreaterThan(0), "Downloaded result file is empty.");
        AssertFileLooksLikeMp4(localVideoPath, "Live distributed RenderDccSceneVideo");

        TestContext.Progress.WriteLine($"Live RenderDccSceneVideo output saved to: {localVideoPath}");
        TestContext.Progress.WriteLine($"Live RenderDccSceneVideo identifiers: JobId={handle.JobId}; ResultBlobId={resultBlobId}");
    }

    #endregion

    #region Tools

    private static DccSceneData CreateDccScene()
    {
        return new DccSceneData
        {
            SceneName = "LiveDccStillScene",
            SourceApplication = new DccApplicationData
            {
                ApplicationFamily = "3dsMax",
                ApplicationVersion = "2027",
                ExporterVersion = "1.0.0"
            },
            Units = new DccUnitSettingsData
            {
                LinearUnit = "centimeter",
                UnitsPerMeter = 100d
            },
            AxisSystem = new DccAxisSystemData
            {
                Handedness = "right",
                UpAxis = "Z",
                ForwardAxis = "Y"
            },
            RenderSettings = new DccRenderSettingsData
            {
                ResolutionX = 640,
                ResolutionY = 640,
                FrameStart = 1,
                FrameEnd = 1,
                Fps = 24,
                TargetEngine = RenderEngine.Cycles,
                Samples = 16
            },
            Nodes =
            [
                new DccNodeData
                {
                    Id = "node:mesh",
                    Name = "GroundPlane",
                    Kind = DccNodeKind.Mesh,
                    MeshId = "mesh:plane",
                    MaterialBindingId = "material:plane",
                    LocalTransform = new DccTransformData
                    {
                        Translation = new DccVector3Data { X = 0d, Y = 0d, Z = 0d },
                        Rotation = new DccQuaternionData { W = 1d },
                        Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
                    },
                    Visible = true,
                    Renderable = true
                },
                new DccNodeData
                {
                    Id = "node:camera",
                    Name = "CameraMain",
                    Kind = DccNodeKind.Camera,
                    CameraId = "camera:main",
                    LocalTransform = new DccTransformData
                    {
                        Translation = new DccVector3Data { X = 5d, Y = -5d, Z = 3d },
                        Rotation = new DccQuaternionData
                        {
                            X = 0.905348824282934d,
                            Y = 0.375007822359259d,
                            Z = 0.0762613084857334d,
                            W = 0.184111047727654d
                        },
                        Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
                    },
                    Visible = true,
                    Renderable = true
                },
                new DccNodeData
                {
                    Id = "node:light",
                    Name = "KeyLightNode",
                    Kind = DccNodeKind.Light,
                    LightId = "light:key",
                    LocalTransform = new DccTransformData
                    {
                        Translation = new DccVector3Data { X = 4d, Y = -4d, Z = 6d },
                        Rotation = new DccQuaternionData { W = 1d },
                        Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
                    },
                    Visible = true,
                    Renderable = true
                }
            ],
            Meshes =
            [
                new DccMeshData
                {
                    Id = "mesh:plane",
                    Name = "GroundPlaneMesh",
                    Positions =
                    [
                        new DccVector3Data { X = -1d, Y = -1d, Z = 0d },
                        new DccVector3Data { X = 1d, Y = -1d, Z = 0d },
                        new DccVector3Data { X = 1d, Y = 1d, Z = 0d },
                        new DccVector3Data { X = -1d, Y = 1d, Z = 0d }
                    ],
                    Normals =
                    [
                        new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
                        new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
                        new DccVector3Data { X = 0d, Y = 0d, Z = 1d },
                        new DccVector3Data { X = 0d, Y = 0d, Z = 1d }
                    ],
                    Uv0 =
                    [
                        new DccVector2Data { X = 0d, Y = 0d },
                        new DccVector2Data { X = 1d, Y = 0d },
                        new DccVector2Data { X = 1d, Y = 1d },
                        new DccVector2Data { X = 0d, Y = 1d }
                    ],
                    TriangleIndices = [0, 1, 2, 0, 2, 3],
                    MaterialIndices = [0, 0]
                }
            ],
            Cameras =
            [
                new DccCameraData
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
                new DccLightData
                {
                    Id = "light:key",
                    Name = "KeyLight",
                    Kind = DccLightKind.Point,
                    Color = new DccColorData { R = 1d, G = 0.95d, B = 0.85d, A = 1d },
                    Intensity = 1200d,
                    Range = 25d,
                    SpotAngleDegrees = 45d
                }
            ],
            Materials =
            [
                new DccMaterialData
                {
                    Id = "material:plane",
                    Name = "PlaneMaterial",
                    Kind = DccMaterialKind.PrincipledSurface,
                    BaseColor = new DccColorData { R = 0.8d, G = 0.7d, B = 0.6d, A = 1d },
                    AlphaMode = DccMaterialAlphaMode.Blend,
                    Opacity = 1d,
                    Metallic = 0d,
                    Roughness = 0.5d,
                    NormalStrength = 1d
                }
            ]
        };
    }

    private static RenderOptionsData CreateRenderOptions()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 16,
            ResolutionX = 640,
            ResolutionY = 640,
            Denoise = false
        };
    }

    private static TileOptionsData CreateTileOptions()
    {
        return new TileOptionsData
        {
            OverlapPx = 8,
            BlendMode = TileBlendMode.CenterPriorityCrop
        };
    }

    private static VideoOptionsData CreateVideoOptions()
    {
        return new VideoOptionsData
        {
            FrameRate = 24,
            ConstantRateFactor = 23
        };
    }

    private static async Task<Guid[]> GetFrameBlobIdsAsync(WitJobHandle handle, Guid?[]? waitResult, CancellationToken cancellationToken)
    {
        if (waitResult is { Length: > 0 })
            return waitResult.Where(me => me.HasValue).Select(me => me!.Value).ToArray();

        try
        {
            var result = await handle.GetResultAsync<Guid[]>(ct: cancellationToken);
            if (result is { Length: > 0 })
                return result.Where(me => me != Guid.Empty).ToArray();
        }
        catch
        {
        }

        try
        {
            var nullableResult = await handle.GetResultAsync<Guid?[]>(ct: cancellationToken);
            if (nullableResult is { Length: > 0 })
                return nullableResult.Where(me => me.HasValue).Select(me => me!.Value).ToArray();
        }
        catch
        {
        }

        try
        {
            var listResult = await handle.GetResultAsync<List<Guid?>>(ct: cancellationToken);
            if (listResult is { Count: > 0 })
                return listResult.Where(me => me.HasValue).Select(me => me!.Value).ToArray();
        }
        catch
        {
        }

        return [];
    }

    private static void AssertImageIsNotSolidBlack(string imagePath, string context)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);

        long meaningfullyLitPixels = 0;
        long totalPixels = (long)image.Width * image.Height;
        byte maxChannelValue = 0;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                var maxChannel = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                if (maxChannel > maxChannelValue)
                    maxChannelValue = maxChannel;

                if (pixel.R >= 8 || pixel.G >= 8 || pixel.B >= 8)
                    meaningfullyLitPixels++;
            }
        }

        Assert.That(totalPixels, Is.GreaterThan(0), $"{context}: image contains no pixels.");
        Assert.That(meaningfullyLitPixels, Is.GreaterThan(0),
            $"{context}: rendered image contains only near-black pixels. Max channel value: {maxChannelValue}.");
    }

    private static void AssertFileLooksLikeMp4(string filePath, string context)
    {
        using var stream = File.OpenRead(filePath);
        Assert.That(stream.Length, Is.GreaterThanOrEqualTo(12), $"{context}: mp4 file is too small to contain a valid header.");

        var header = new byte[12];
        var read = stream.Read(header, 0, header.Length);
        Assert.That(read, Is.EqualTo(header.Length), $"{context}: failed to read mp4 header.");

        var boxType = System.Text.Encoding.ASCII.GetString(header, 4, 4);
        Assert.That(boxType, Is.EqualTo("ftyp"), $"{context}: file does not look like an MP4 container.");
    }

    #endregion
}
