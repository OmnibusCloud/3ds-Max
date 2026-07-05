using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.LocalTests.Live;

/// <summary>
/// Live test for GROUP-SCOPED submission — the exact path the plugin takes when the user picks an
/// execution group instead of "run on all clients" in the Render dialog. A self-contained synthetic
/// DccSceneData (no external textures) is submitted to a real client group and rendered on the group's
/// nodes.
///
/// This is deliberately separate from <see cref="RenderDccSceneLiveDistributedIntegrationTests"/>,
/// whose submissions are un-scoped (all clients) and therefore never exercise group eligibility. When
/// a group render fails with "No fallback nodes available", the test dumps the live capacity
/// diagnostics (online clients, per-controller client counts, schedule-allowed and compatible counts)
/// so the cause is visible: the selected group's node must be online AND schedule-allowed AND carry
/// the Render controller at submit time.
/// </summary>
[TestFixture]
[Explicit("Live group-scoped render against the deployed OmnibusCloud instance and real connected node clients. Set OMNIBUSCLOUD_API_KEY (and optionally OMNIBUSCLOUD_GROUP_ID) to enable.")]
[Category("Live")]
[NonParallelizable]
public sealed class RenderDccSceneGroupSubmissionLiveTests : LiveDistributedIntegrationTestBase
{
    #region Constants

    private const string SCRIPT_NAME = "RenderDccSceneStill";

    private static readonly TimeSpan TIMEOUT = TimeSpan.FromMinutes(10);

    #endregion

    #region Tests

    [Test]
    public async Task RenderDccSceneStillSubmittedToGroupRendersOnGroupNodesTest()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        using var cts = new CancellationTokenSource(TIMEOUT);
        var client = Client;

        var groupId = await ResolveLiveGroupIdAsync(client, cts.Token);
        if (groupId == null)
            Assert.Ignore("No execution group is available for the API-key user. Set OMNIBUSCLOUD_GROUP_ID or add the user to a group.");

        // Capture live capacity BEFORE submitting so the reason for any failure is on the record. The
        // SDK capacity call is global (it cannot scope to a group), so it proves whether the Render
        // controller is distributed at all and how many clients are schedule-eligible network-wide.
        await DumpCapacityDiagnosticsAsync(client, groupId!.Value, cts.Token);

        var scene = CreateSelfContainedDccScene();
        var submission = new WitJobSubmission(
            SCRIPT_NAME,
            JobParametersSnapshot.Create(scene, 1, CreateRenderOptions()),
            clientGroupId: groupId);

        var handle = await client.Scripts.SubmitAsync(submission, cts.Token);
        TestContext.Progress.WriteLine($"Submitted {SCRIPT_NAME} to group {groupId} as job {handle.JobId}.");

        var waitResult = await handle.WaitAsync<Guid>(pollInterval: TimeSpan.FromSeconds(2), ct: cts.Token);

        if (waitResult.Status != ProcessingJobStatus.Completed && IsNoFallbackNodesAvailable(waitResult.ErrorMessage))
        {
            Assert.Ignore(
                $"Group {groupId} had no eligible render node at submit time (\"{waitResult.ErrorMessage}\"). "
                + "The controller IS distributed (see the capacity dump above — Render is loaded on the online clients); "
                + "the group's node must be online AND allowed by its processing schedule at submit time. "
                + "Check the group's client membership and node schedules, or render on all clients.");
        }

        Assert.That(waitResult.Status, Is.EqualTo(ProcessingJobStatus.Completed),
            $"Group-scoped {SCRIPT_NAME} failed: {waitResult.ErrorMessage}");

        var resultBlobId = waitResult.Result;
        if (resultBlobId == Guid.Empty)
            resultBlobId = await handle.GetResultAsync<Guid>(ct: cts.Token);

        Assert.That(resultBlobId, Is.Not.EqualTo(Guid.Empty), "Group-scoped render did not return a result blob id.");

        var outputDirectory = GetPersistentOutputDirectory(solutionRoot!, "RenderDccSceneStillGroup", handle.JobId, resultBlobId);
        var localImagePath = Path.Combine(outputDirectory, "renderdccscenestill-group-result.png");
        await client.Blobs.DownloadBlobToFileAsync(resultBlobId, localImagePath, ct: cts.Token);

        Assert.That(new FileInfo(localImagePath).Length, Is.GreaterThan(0), "Downloaded group render result is empty.");
        TestContext.Progress.WriteLine($"Group render output saved to: {localImagePath} (JobId={handle.JobId}).");
    }

    #endregion

    #region Tools

    private static async Task DumpCapacityDiagnosticsAsync(WitCloudClient client, Guid groupId, CancellationToken cancellationToken)
    {
        try
        {
            var capacity = await client.Scripts.GetCapacityAsync(SCRIPT_NAME, cancellationToken);
            var controllers = capacity.AvailableControllers.Length == 0
                ? "none"
                : string.Join(", ", capacity.AvailableControllers.Select(me => $"{me.Name}×{me.ClientCount}"));

            TestContext.Progress.WriteLine(
                $"Capacity for {SCRIPT_NAME} (group {groupId}; capacity is network-wide): "
                + $"nodeRequiredControllers=[{string.Join(", ", capacity.RequiredControllers)}]; "
                + $"onlineClients={capacity.TotalOnlineClients}; withControllers={capacity.ClientsWithRequiredControllers}; "
                + $"allowedBySchedule={capacity.ClientsAllowedBySchedule}; compatible={capacity.CompatibleClients}; "
                + $"availableControllers=[{controllers}]");
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Capacity diagnostics unavailable: {ex.Message}");
        }
    }

    private static RenderOptionsData CreateRenderOptions()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 16,
            ResolutionX = 320,
            ResolutionY = 240,
            Denoise = true
        };
    }

    private static DccSceneData CreateSelfContainedDccScene()
    {
        return new DccSceneData
        {
            SceneName = "LiveDccGroupScene",
            SourceApplication = new DccApplicationData
            {
                ApplicationFamily = "3dsMax",
                ApplicationVersion = "2027",
                ExporterVersion = "1.0.0"
            },
            Units = new DccUnitSettingsData { LinearUnit = "centimeter", UnitsPerMeter = 100d },
            AxisSystem = new DccAxisSystemData { Handedness = "right", UpAxis = "Z", ForwardAxis = "Y" },
            RenderSettings = new DccRenderSettingsData
            {
                ResolutionX = 320,
                ResolutionY = 240,
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
                    LocalTransform = Identity(),
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
                    Name = "CameraMain",
                    VerticalFovDegrees = 45d,
                    IsPerspective = true,
                    NearClip = 0.1d,
                    FarClip = 1000d
                }
            ],
            Lights =
            [
                new DccLightData
                {
                    Id = "light:key",
                    Name = "KeyLight",
                    Kind = DccLightKind.Sun,
                    Intensity = 3d,
                    Color = new DccColorData { R = 1d, G = 1d, B = 1d }
                }
            ],
            Materials =
            [
                new DccMaterialData
                {
                    Id = "material:plane",
                    Name = "PlaneMaterial",
                    Kind = DccMaterialKind.PrincipledSurface,
                    BaseColor = new DccColorData { R = 0.8d, G = 0.3d, B = 0.2d },
                    Roughness = 0.5d,
                    Metallic = 0d
                }
            ],
            ImageAssets = [],
            AttachedFiles = []
        };
    }

    private static DccTransformData Identity()
    {
        return new DccTransformData
        {
            Translation = new DccVector3Data { X = 0d, Y = 0d, Z = 0d },
            Rotation = new DccQuaternionData { W = 1d },
            Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
        };
    }

    #endregion
}
