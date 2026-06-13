using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxSceneCameraFramingTests
{
    #region Camera Math Tests

    [Test]
    public void BuildLookAtNodeRotationProducesGeneratorForwardAlongRequestedDirectionTest()
    {
        var forward = MaxCameraMath.Normalize((1d, -2d, 3d));

        var nodeRotation = MaxCameraMath.BuildLookAtNodeRotation(forward, (0d, 0d, 1d));
        var generatorForward = MaxCameraMath.ComputeGeneratorForward(nodeRotation);

        Assert.Multiple(() =>
        {
            Assert.That(generatorForward.X, Is.EqualTo(forward.X).Within(1e-9));
            Assert.That(generatorForward.Y, Is.EqualTo(forward.Y).Within(1e-9));
            Assert.That(generatorForward.Z, Is.EqualTo(forward.Z).Within(1e-9));
        });
    }

    #endregion

    #region Synthesizer Framing Tests

    [Test]
    public void ValidateCurrentSceneFramesGeometryWhenActiveViewportFacesAwayTest()
    {
        // Mesh near origin; viewport camera 200 units down -Z with identity rotation, which the
        // generator points along -Y (away from the +Z geometry) — the headless batch failure mode.
        var snapshot = CreateViewportSnapshot(
            cameraTranslation: (0d, 0d, -200d),
            cameraRotation: (W: 1d, X: 0d, Y: 0d, Z: 0d));

        var result = MaxSceneExportTestData.CreateService(snapshot).ValidateCurrentScene();

        var cameraNode = result.Scene!.Nodes.Single(me => me.Kind == DccNodeKind.Camera);
        var forward = MaxCameraMath.ComputeGeneratorForward((
            cameraNode.LocalTransform.Rotation.W,
            cameraNode.LocalTransform.Rotation.X,
            cameraNode.LocalTransform.Rotation.Y,
            cameraNode.LocalTransform.Rotation.Z));
        var toCenter = MaxCameraMath.Normalize((
            0d - cameraNode.LocalTransform.Translation.X,
            0d - cameraNode.LocalTransform.Translation.Y,
            0d - cameraNode.LocalTransform.Translation.Z));

        Assert.Multiple(() =>
        {
            Assert.That(result.Summary.UsesSyntheticViewportCamera, Is.True);
            // The synthesized framing camera must look at the geometry.
            Assert.That(MaxCameraMath.Dot(forward, toCenter), Is.GreaterThan(0.95d));
            // And it must no longer sit on the original viewport's -Z position.
            Assert.That(cameraNode.LocalTransform.Translation.Z, Is.GreaterThan(0d));
        });
    }

    [Test]
    public void ValidateCurrentSceneKeepsActiveViewportWhenItAlreadyFramesGeometryTest()
    {
        // A viewport camera placed up-front and already looking at the origin geometry.
        var cameraPosition = (X: 0d, Y: -200d, Z: 0d);
        var forward = MaxCameraMath.Normalize((0d - cameraPosition.X, 0d - cameraPosition.Y, 0d - cameraPosition.Z));
        var rotation = MaxCameraMath.BuildLookAtNodeRotation(forward, (0d, 0d, 1d));

        var snapshot = CreateViewportSnapshot(
            cameraTranslation: (cameraPosition.X, cameraPosition.Y, cameraPosition.Z),
            cameraRotation: rotation);

        var result = MaxSceneExportTestData.CreateService(snapshot).ValidateCurrentScene();

        var cameraNode = result.Scene!.Nodes.Single(me => me.Kind == DccNodeKind.Camera);

        Assert.Multiple(() =>
        {
            Assert.That(result.Summary.UsesSyntheticViewportCamera, Is.True);
            // Kept the user's viewport framing verbatim.
            Assert.That(cameraNode.LocalTransform.Translation.X, Is.EqualTo(cameraPosition.X).Within(1e-9));
            Assert.That(cameraNode.LocalTransform.Translation.Y, Is.EqualTo(cameraPosition.Y).Within(1e-9));
            Assert.That(cameraNode.LocalTransform.Translation.Z, Is.EqualTo(cameraPosition.Z).Within(1e-9));
        });
    }

    #endregion

    #region Tools

    private static MaxSceneSnapshotData CreateViewportSnapshot(
        (double X, double Y, double Z) cameraTranslation,
        (double W, double X, double Y, double Z) cameraRotation)
    {
        return new MaxSceneSnapshotData
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
                Translation = new MaxSceneVector3SnapshotData { X = cameraTranslation.X, Y = cameraTranslation.Y, Z = cameraTranslation.Z },
                Rotation = new MaxSceneQuaternionSnapshotData { W = cameraRotation.W, X = cameraRotation.X, Y = cameraRotation.Y, Z = cameraRotation.Z },
                Scale = new MaxSceneVector3SnapshotData { X = 1d, Y = 1d, Z = 1d }
            },
            FrameStart = 1,
            FrameEnd = 1,
            RenderWidth = 320,
            RenderHeight = 240,
            Nodes =
            [
                new MaxSceneNodeSnapshotData
                {
                    Id = "node:mesh",
                    Name = "Box",
                    Kind = DccNodeKind.Mesh,
                    MeshId = "mesh:box",
                    LocalTransform = new MaxSceneTransformSnapshotData
                    {
                        Translation = new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 0d },
                        Rotation = new MaxSceneQuaternionSnapshotData { W = 1d },
                        Scale = new MaxSceneVector3SnapshotData { X = 1d, Y = 1d, Z = 1d }
                    },
                    Visible = true,
                    Renderable = true
                }
            ],
            Meshes =
            [
                new MaxSceneMeshSnapshotData
                {
                    Id = "mesh:box",
                    Name = "BoxMesh",
                    Positions =
                    [
                        new MaxSceneVector3SnapshotData { X = -10d, Y = -10d, Z = -10d },
                        new MaxSceneVector3SnapshotData { X = 10d, Y = -10d, Z = -10d },
                        new MaxSceneVector3SnapshotData { X = 0d, Y = 10d, Z = 10d }
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
                    MaterialIndices = []
                }
            ],
            NodesCount = 1,
            MeshesCount = 1
        };
    }

    #endregion
}
