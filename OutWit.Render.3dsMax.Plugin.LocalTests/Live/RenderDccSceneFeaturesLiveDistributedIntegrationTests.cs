using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.LocalTests.Live;

/// <summary>
/// Live distributed integration tests for the Dcc 1.4 features the plugin now populates (baked
/// deformation, light/camera property animation, vertex colours, motion blur). Each test builds the
/// neutral <see cref="DccSceneData"/> the plugin exporter produces and submits it as a REAL job to the
/// deployed OmnibusCloud instance, pinned to a REAL client group (resolved from the API-key user's
/// execution scope or <c>OMNIBUSCLOUD_GROUP_ID</c>), so it renders on real connected node clients —
/// the same principle as the Blender bridge distribution tests.
///
/// These exercise the deployed 1.4.0 GENERATOR + farm with the new payloads (no 3ds Max needed). The
/// full Max-side path (a real .max scene → collector → cloud via 3dsmaxbatch.exe) is covered by the
/// MaxBatch*IntegrationTests and needs feature-bearing .max assets.
/// </summary>
[TestFixture]
[Explicit("Live external distributed integration test for Dcc 1.4 features against the deployed OmnibusCloud instance and real connected node clients on a real client group. Set OMNIBUSCLOUD_API_KEY (and optionally OMNIBUSCLOUD_GROUP_ID).")]
[Category("Live")]
[NonParallelizable]
public sealed class RenderDccSceneFeaturesLiveDistributedIntegrationTests : LiveDistributedIntegrationTestBase
{
    #region Constants

    private static readonly TimeSpan TIMEOUT = TimeSpan.FromMinutes(15);

    #endregion

    #region Tests

    [Test]
    public async Task BakedDeformationFramesProduceDifferentPosesOnRealGroupTest()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        using var cts = new CancellationTokenSource(TIMEOUT);
        var client = Client;
        var groupId = await ResolveTargetGroupOrIgnoreAsync(client, cts.Token);

        // Frame 1 = full rest quad, frame 2 = the same mesh baked down to 30% — the silhouette/lit area
        // changes dramatically, proving the per-frame shape-key deformation actually drives geometry.
        var scene = CreateBaseScene();
        scene.RenderSettings!.FrameEnd = 2;
        scene.Meshes[0].DeformationFrames =
        [
            new DccMeshDeformationFrameData { Frame = 1, Positions = QuadPositions(1d) },
            new DccMeshDeformationFrameData { Frame = 2, Positions = QuadPositions(0.3d) }
        ];

        var frames = await RenderFramesAsync(client, scene, 1, 2, groupId, solutionRoot!, "Deformation", cts.Token);

        AssertImagesDiffer(frames[0], frames[1], "Baked deformation frame 1 vs 2");
    }

    [Test]
    public async Task LightIntensityAnimationProducesDifferentBrightnessOnRealGroupTest()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        using var cts = new CancellationTokenSource(TIMEOUT);
        var client = Client;
        var groupId = await ResolveTargetGroupOrIgnoreAsync(client, cts.Token);

        // Frame 1 dim, frame 2 ~12x brighter via intensity keyframes — the lit quad must get visibly
        // brighter, proving the property-animation channels are sampled and keyframed.
        var scene = CreateBaseScene();
        scene.RenderSettings!.FrameEnd = 2;
        scene.Lights[0].Intensity = 200d;
        scene.Lights[0].IntensityKeyframes =
        [
            new DccScalarKeyframeData { Frame = 1, Value = 200d, InterpolationMode = DccKeyframeInterpolationMode.Linear },
            new DccScalarKeyframeData { Frame = 2, Value = 2500d, InterpolationMode = DccKeyframeInterpolationMode.Linear }
        ];

        var frames = await RenderFramesAsync(client, scene, 1, 2, groupId, solutionRoot!, "LightIntensityAnimation", cts.Token);

        AssertImagesDiffer(frames[0], frames[1], "Light intensity frame 1 vs 2");
        var dim = MeasureMeanLuminance(frames[0]);
        var bright = MeasureMeanLuminance(frames[1]);
        Assert.That(bright, Is.GreaterThan(dim * 1.2d),
            $"Light intensity animation did not brighten the render: frame1 luminance {dim:F2}, frame2 {bright:F2}.");
    }

    [Test]
    public async Task VertexColouredMeshRendersOnRealGroupTest()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        using var cts = new CancellationTokenSource(TIMEOUT);
        var client = Client;
        var groupId = await ResolveTargetGroupOrIgnoreAsync(client, cts.Token);

        // A vertex-coloured mesh must be accepted (Colors aligned 1:1 with Positions, a 1.4.0 guard) and
        // render. The generator does not yet wire the colour attribute into the BSDF, so this proves the
        // payload is accepted + renders, not a colour-vs-no-colour visual difference.
        var scene = CreateBaseScene();
        scene.Meshes[0].Colors =
        [
            new DccColorData { R = 1d, G = 0d, B = 0d, A = 1d },
            new DccColorData { R = 0d, G = 1d, B = 0d, A = 1d },
            new DccColorData { R = 0d, G = 0d, B = 1d, A = 1d },
            new DccColorData { R = 1d, G = 1d, B = 1d, A = 1d }
        ];

        var image = await RenderStillAsync(client, scene, groupId, solutionRoot!, "VertexColours", cts.Token);

        AssertImageIsNotSolidBlack(image, "Vertex-coloured mesh");
    }

    [Test]
    public async Task MotionBlurFramesRenderOnRealGroupTest()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        using var cts = new CancellationTokenSource(TIMEOUT);
        var client = Client;
        var groupId = await ResolveTargetGroupOrIgnoreAsync(client, cts.Token);

        // Motion blur on + a moving object across frames. Proves the deployed generator accepts the new
        // RenderSettings.MotionBlur/Shutter flags and still renders (a blur-on vs blur-off image diff is
        // a heavier follow-up).
        var scene = CreateBaseScene();
        scene.RenderSettings!.FrameEnd = 2;
        scene.RenderSettings.MotionBlur = true;
        scene.RenderSettings.MotionBlurShutter = 0.5d;
        var meshNode = scene.Nodes.First(me => me.Kind == DccNodeKind.Mesh);
        meshNode.TransformKeyframes =
        [
            new DccTransformKeyframeData { Frame = 1, Transform = Translation(0d), InterpolationMode = DccKeyframeInterpolationMode.Linear },
            new DccTransformKeyframeData { Frame = 2, Transform = Translation(1.5d), InterpolationMode = DccKeyframeInterpolationMode.Linear }
        ];

        var frames = await RenderFramesAsync(client, scene, 1, 2, groupId, solutionRoot!, "MotionBlur", cts.Token);

        foreach (var frame in frames)
            AssertImageIsNotSolidBlack(frame, "Motion-blur frame");
    }

    [Test]
    public async Task RealGroupResolutionDiagnosticsTest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var client = Client;

        var scope = await client.GetExecutionScopeOptionsAsync(cts.Token);
        WriteExecutionScopeDiagnostics(scope);

        var groupId = await ResolveLiveGroupIdAsync(client, cts.Token);
        TestContext.Progress.WriteLine($"Resolved live group id: {(groupId?.ToString() ?? "<none>")}; CanRunOnAllClients={scope.CanRunOnAllClients}");

        Assert.That(groupId != null || scope.CanRunOnAllClients, Is.True,
            "The API key can target neither a named group nor all clients — live feature tests would have nothing to run on.");
    }

    #endregion

    #region Tools

    private async Task<Guid?> ResolveTargetGroupOrIgnoreAsync(WitCloudClient client, CancellationToken cancellationToken)
    {
        var groupId = await ResolveLiveGroupIdAsync(client, cancellationToken);
        if (groupId != null)
        {
            TestContext.Progress.WriteLine($"Submitting to real client group: {groupId}");
            return groupId;
        }

        var scope = await client.GetExecutionScopeOptionsAsync(cancellationToken);
        if (scope.CanRunOnAllClients)
        {
            TestContext.Progress.WriteLine("No named client group resolved; running on all clients (CanRunOnAllClients).");
            return null;
        }

        Assert.Ignore("No client group resolvable and the API key cannot run on all clients. Set OMNIBUSCLOUD_GROUP_ID or grant the key a group.");
        return null;
    }

    private async Task<string> RenderStillAsync(WitCloudClient client, DccSceneData scene, Guid? groupId, string solutionRoot, string label, CancellationToken cancellationToken)
    {
        var submission = new WitJobSubmission
        {
            ScriptName = "RenderDccSceneStill",
            Parameters = JobParametersSnapshot.Create(scene, 1, CreateRenderOptions()),
            ClientGroupId = groupId
        };

        var handle = await client.Scripts.SubmitAsync(submission, cancellationToken);
        TestContext.Progress.WriteLine($"Live {label} still submitted: JobId={handle.JobId}; Group={(groupId?.ToString() ?? "all-clients")}");

        var waitResult = await handle.WaitAsync<Guid>(pollInterval: TimeSpan.FromSeconds(2), ct: cancellationToken);
        AssertCompletedOrIgnoreExternalCapacity(waitResult, $"Live distributed {label}", "RenderDccSceneStill");

        var resultBlobId = waitResult.Result;
        if (resultBlobId == Guid.Empty)
            resultBlobId = await handle.GetResultAsync<Guid>(ct: cancellationToken);

        Assert.That(resultBlobId, Is.Not.EqualTo(Guid.Empty), $"{label} did not return a result blob id.");

        var outputDirectory = GetPersistentOutputDirectory(solutionRoot, $"Features_{label}", handle.JobId, resultBlobId);
        var localImagePath = Path.Combine(outputDirectory, $"{label.ToLowerInvariant()}-still.png");
        await client.Blobs.DownloadBlobToFileAsync(resultBlobId, localImagePath, ct: cancellationToken);

        Assert.That(File.Exists(localImagePath), Is.True, $"Downloaded result was not found at {localImagePath}");
        Assert.That(new FileInfo(localImagePath).Length, Is.GreaterThan(0), "Downloaded result file is empty.");
        TestContext.Progress.WriteLine($"Live {label} still saved to: {localImagePath}");
        return localImagePath;
    }

    private async Task<string[]> RenderFramesAsync(WitCloudClient client, DccSceneData scene, int startFrame, int endFrame, Guid? groupId, string solutionRoot, string label, CancellationToken cancellationToken)
    {
        var submission = new WitJobSubmission
        {
            ScriptName = "RenderDccSceneFrames",
            Parameters = JobParametersSnapshot.Create(scene, startFrame, endFrame, CreateRenderOptions()),
            ClientGroupId = groupId
        };

        var handle = await client.Scripts.SubmitAsync(submission, cancellationToken);
        TestContext.Progress.WriteLine($"Live {label} frames submitted: JobId={handle.JobId}; Group={(groupId?.ToString() ?? "all-clients")}");

        var waitResult = await handle.WaitAsync<Guid?[]>(pollInterval: TimeSpan.FromSeconds(2), ct: cancellationToken);
        AssertCompletedOrIgnoreExternalCapacity(waitResult, $"Live distributed {label}", "RenderDccSceneFrames");

        var frameBlobIds = await GetFrameBlobIdsAsync(handle, waitResult.Result, cancellationToken);
        var expected = endFrame - startFrame + 1;
        Assert.That(frameBlobIds.Length, Is.EqualTo(expected), $"{label} did not return the expected {expected} frame blob ids.");

        var outputDirectory = GetPersistentOutputDirectory(solutionRoot, $"Features_{label}", handle.JobId, frameBlobIds[0]);
        var paths = new string[frameBlobIds.Length];
        for (var index = 0; index < frameBlobIds.Length; index++)
        {
            paths[index] = Path.Combine(outputDirectory, $"frame_{index + 1:D4}.png");
            await client.Blobs.DownloadBlobToFileAsync(frameBlobIds[index], paths[index], ct: cancellationToken);
            Assert.That(File.Exists(paths[index]), Is.True, $"Downloaded frame was not found at {paths[index]}");
            Assert.That(new FileInfo(paths[index]).Length, Is.GreaterThan(0), $"Downloaded frame file is empty: {paths[index]}");
        }

        TestContext.Progress.WriteLine($"Live {label} frames saved to: {outputDirectory}");
        return paths;
    }

    private static async Task<Guid[]> GetFrameBlobIdsAsync(WitJobHandle handle, Guid?[]? waitResult, CancellationToken cancellationToken)
    {
        if (waitResult is { Length: > 0 })
            return waitResult.Where(me => me.HasValue).Select(me => me!.Value).ToArray();

        try
        {
            var result = await handle.GetResultAsync<Guid?[]>(ct: cancellationToken);
            if (result is { Length: > 0 })
                return result.Where(me => me.HasValue).Select(me => me!.Value).ToArray();
        }
        catch
        {
        }

        return [];
    }

    #endregion

    #region Scene Builders

    private static DccSceneData CreateBaseScene()
    {
        return new DccSceneData
        {
            SceneName = "LiveDccFeatureScene",
            SourceApplication = new DccApplicationData { ApplicationFamily = "3dsMax", ApplicationVersion = "2027", ExporterVersion = "1.0.0" },
            Units = new DccUnitSettingsData { LinearUnit = "centimeter", UnitsPerMeter = 100d },
            AxisSystem = new DccAxisSystemData { Handedness = "right", UpAxis = "Z", ForwardAxis = "Y" },
            RenderSettings = new DccRenderSettingsData
            {
                ResolutionX = 480,
                ResolutionY = 480,
                FrameStart = 1,
                FrameEnd = 1,
                Fps = 24,
                TargetEngine = RenderEngine.Cycles,
                Samples = 32
            },
            Nodes =
            [
                new DccNodeData
                {
                    Id = "node:mesh",
                    Name = "FeatureQuad",
                    Kind = DccNodeKind.Mesh,
                    MeshId = "mesh:quad",
                    MaterialBindingId = "material:quad",
                    LocalTransform = Translation(0d),
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
                    Id = "mesh:quad",
                    Name = "FeatureQuadMesh",
                    Positions = QuadPositions(1d),
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
                new DccCameraData { Id = "camera:main", Name = "Camera001", VerticalFovDegrees = 45d, NearClip = 0.1d, FarClip = 500d, IsPerspective = true }
            ],
            Lights =
            [
                new DccLightData
                {
                    Id = "light:key",
                    Name = "KeyLight",
                    Kind = DccLightKind.Point,
                    Color = new DccColorData { R = 1d, G = 0.95d, B = 0.85d, A = 1d },
                    Intensity = 1500d,
                    Range = 25d,
                    SpotAngleDegrees = 45d
                }
            ],
            Materials =
            [
                new DccMaterialData
                {
                    Id = "material:quad",
                    Name = "QuadMaterial",
                    Kind = DccMaterialKind.PrincipledSurface,
                    BaseColor = new DccColorData { R = 0.8d, G = 0.75d, B = 0.7d, A = 1d },
                    AlphaMode = DccMaterialAlphaMode.Blend,
                    Opacity = 1d,
                    Metallic = 0d,
                    Roughness = 0.5d,
                    NormalStrength = 1d
                }
            ]
        };
    }

    private static List<DccVector3Data> QuadPositions(double half)
    {
        return
        [
            new DccVector3Data { X = -half, Y = -half, Z = 0d },
            new DccVector3Data { X = half, Y = -half, Z = 0d },
            new DccVector3Data { X = half, Y = half, Z = 0d },
            new DccVector3Data { X = -half, Y = half, Z = 0d }
        ];
    }

    private static DccTransformData Translation(double x)
    {
        return new DccTransformData
        {
            Translation = new DccVector3Data { X = x, Y = 0d, Z = 0d },
            Rotation = new DccQuaternionData { W = 1d },
            Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
        };
    }

    private static RenderOptionsData CreateRenderOptions()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 32,
            ResolutionX = 480,
            ResolutionY = 480,
            Denoise = true
        };
    }

    #endregion

    #region Assertions

    private static void AssertImageIsNotSolidBlack(string imagePath, string context)
    {
        using var image = Image.Load<Rgba32>(imagePath);

        long meaningfullyLitPixels = 0;
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

        Assert.That(meaningfullyLitPixels, Is.GreaterThan(0),
            $"{context}: rendered image contains only near-black pixels. Max channel value: {maxChannelValue}.");
    }

    private static void AssertImagesDiffer(string pathA, string pathB, string context)
    {
        using var a = Image.Load<Rgba32>(pathA);
        using var b = Image.Load<Rgba32>(pathB);

        Assert.That(a.Width, Is.EqualTo(b.Width), $"{context}: image widths differ.");
        Assert.That(a.Height, Is.EqualTo(b.Height), $"{context}: image heights differ.");

        long differing = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var pa = a[x, y];
                var pb = b[x, y];
                if (Math.Abs(pa.R - pb.R) > 16 || Math.Abs(pa.G - pb.G) > 16 || Math.Abs(pa.B - pb.B) > 16)
                    differing++;
            }
        }

        var total = (long)a.Width * a.Height;
        Assert.That(differing, Is.GreaterThan(total / 100),
            $"{context}: frames are nearly identical ({differing}/{total} px differ) — the per-frame change did not affect the render.");
    }

    private static double MeasureMeanLuminance(string imagePath)
    {
        using var image = Image.Load<Rgba32>(imagePath);

        double sum = 0d;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                sum += (pixel.R + pixel.G + pixel.B) / 3d;
            }
        }

        return sum / (image.Width * image.Height);
    }

    #endregion
}
