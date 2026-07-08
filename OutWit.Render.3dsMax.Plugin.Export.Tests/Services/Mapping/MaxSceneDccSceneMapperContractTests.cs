using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Mapping;

/// <summary>
/// Verifies the Dcc contract fields survive the snapshot -> summary -> neutral DccScene mapping:
/// the 1.3 set (area lights, camera depth of field, second UV set, scene world/environment colour)
/// and the 1.4 set (HDRI environment image, vertex colours, baked deformation, motion blur,
/// displacement scale, and light/camera property-animation keyframes).
/// </summary>
[TestFixture]
public sealed class MaxSceneDccSceneMapperContractTests
{
    #region Tools

    private static DccSceneData MapScene(MaxSceneSnapshotData snapshot)
    {
        var summary = MaxSceneExportTestData.CreateService(snapshot).CollectSummary();
        return MaxSceneDccSceneMapper.Create(summary);
    }

    #endregion

    #region Backface Cull Closed Mesh Tests

    [Test]
    public void BackfaceCullIsClearedForClosedMeshesTest()
    {
        // Culling is a no-op in Scanline on a watertight mesh (front always covers back), while
        // the backfacing→transparent emulation X-rays nested geometry (ape's eyeball turned
        // see-through and exposed the black iris disc). A closed tetrahedron must lose the flag.
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Materials[0].BackfaceCull = true;
        ReplaceMeshGeometry(snapshot, closed: true, outwardNormals: true);

        var scene = MapScene(snapshot);

        Assert.That(scene.Materials.First(me => me.Id == snapshot.Materials[0].Id).BackfaceCull, Is.False);
    }

    [Test]
    public void BackfaceCullSurvivesForOpenMeshesTest()
    {
        // An open shell (a wall) is exactly where Scanline culling changes the picture — the
        // camera must keep seeing through its backfaces.
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Materials[0].BackfaceCull = true;
        ReplaceMeshGeometry(snapshot, closed: false, outwardNormals: true);

        var scene = MapScene(snapshot);

        Assert.That(scene.Materials.First(me => me.Id == snapshot.Materials[0].Id).BackfaceCull, Is.True);
    }

    [Test]
    public void BackfaceCullIsClearedForSmallDetailMeshesTest()
    {
        // A culled cap smaller than the scene's typical mesh (the ape's iris) can never carry a
        // see-through composition — opaque is the truer Scanline read, while the transparent
        // emulation X-rays the assembly.
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Materials[0].BackfaceCull = true;
        // Small OPEN wall-like cap: perpendicular normals, well below the median mesh radius.
        ReplaceMeshGeometry(snapshot, closed: false, outwardNormals: true, scale: 0.001d);
        AddLargeCompanionMesh(snapshot, "mesh:big1");
        AddLargeCompanionMesh(snapshot, "mesh:big2");

        var scene = MapScene(snapshot);

        Assert.That(scene.Materials.First(me => me.Id == snapshot.Materials[0].Id).BackfaceCull, Is.False);
    }

    private static void AddLargeCompanionMesh(MaxSceneSnapshotData snapshot, string id)
    {
        var corners = new[]
        {
            new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 0d },
            new MaxSceneVector3SnapshotData { X = 100d, Y = 0d, Z = 0d },
            new MaxSceneVector3SnapshotData { X = 0d, Y = 100d, Z = 0d }
        };
        snapshot.Meshes.Add(new MaxSceneMeshSnapshotData
        {
            Id = id,
            Name = id,
            Positions = [.. corners],
            Normals = [.. corners.Select(_ => new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d })],
            Uv0 = [.. corners.Select(_ => new MaxSceneVector2SnapshotData { X = 0d, Y = 0d })],
            TriangleIndices = [0, 1, 2]
        });
        snapshot.Nodes.Add(new MaxSceneNodeSnapshotData
        {
            Id = $"node:{id}",
            Name = id,
            Kind = DccNodeKind.Mesh,
            MeshId = id,
            LocalTransform = new MaxSceneTransformSnapshotData
            {
                Translation = new MaxSceneVector3SnapshotData(),
                Rotation = new MaxSceneQuaternionSnapshotData { W = 1d },
                Scale = new MaxSceneVector3SnapshotData { X = 1d, Y = 1d, Z = 1d }
            }
        });
    }

    [Test]
    public void BackfaceCullSurvivesForInsideOutClosedMeshesTest()
    {
        // Interiors are routinely authored as inside-out closed boxes (normals pointing into the
        // room): there culling matters — the camera outside must keep seeing through the near
        // wall (Lighting-Vertex rendered a blank wall when the flag was cleared by closedness
        // alone).
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Materials[0].BackfaceCull = true;
        ReplaceMeshGeometry(snapshot, closed: true, outwardNormals: false);

        var scene = MapScene(snapshot);

        Assert.That(scene.Materials.First(me => me.Id == snapshot.Materials[0].Id).BackfaceCull, Is.True);
    }

    private static void ReplaceMeshGeometry(MaxSceneSnapshotData snapshot, bool closed, bool outwardNormals, double scale = 1d)
    {
        var mesh = snapshot.Meshes[0];
        var a = new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 0d };
        var b = new MaxSceneVector3SnapshotData { X = 10d * scale, Y = 0d, Z = 0d };
        var c = new MaxSceneVector3SnapshotData { X = 0d, Y = 10d * scale, Z = 0d };
        var d = new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 10d * scale };

        // Per-corner (unwelded) layout, like the collector emits.
        var corners = closed
            ? new[] { a, b, c, a, b, d, a, c, d, b, c, d }
            : new[] { a, b, c };

        var centroidX = corners.Average(me => me.X);
        var centroidY = corners.Average(me => me.Y);
        var centroidZ = corners.Average(me => me.Z);
        var sign = outwardNormals ? 1d : -1d;

        mesh.Positions = [.. corners.Select(me => new MaxSceneVector3SnapshotData { X = me.X, Y = me.Y, Z = me.Z })];
        // A wall shell carries normals perpendicular to the centroid direction (a flat plane's
        // normal never points away from its own centroid); a solid's normals point radially out.
        mesh.Normals = closed
            ? [.. corners.Select(me => new MaxSceneVector3SnapshotData
            {
                X = sign * (me.X - centroidX),
                Y = sign * (me.Y - centroidY),
                Z = sign * (me.Z - centroidZ)
            })]
            : [.. corners.Select(_ => new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d })];
        mesh.Uv0 = [.. corners.Select(_ => new MaxSceneVector2SnapshotData { X = 0d, Y = 0d })];
        mesh.TriangleIndices = [.. Enumerable.Range(0, corners.Length)];
        mesh.MaterialIndices = [];
    }

    #endregion

    #region Teleport Keyframe Tests

    [Test]
    public void TeleportKeyframesAreHeldConstantTest()
    {
        // Montage cuts arrive as adjacent samples far apart; the pre-cut key must hold CONSTANT
        // so linear interpolation (and motion blur over it) never sweeps through the scene.
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        var node = snapshot.Nodes.First(me => me.Kind == DccNodeKind.Mesh);
        node.TransformKeyframes =
        [
            CreateTranslationKeyframe(1, 0d),
            CreateTranslationKeyframe(2, 500d),
            CreateTranslationKeyframe(3, 501d)
        ];

        var mapped = MapScene(snapshot).Nodes.Single(me => me.Id == node.Id);

        Assert.Multiple(() =>
        {
            Assert.That(mapped.TransformKeyframes[0].InterpolationMode, Is.EqualTo(DccKeyframeInterpolationMode.Constant));
            Assert.That(mapped.TransformKeyframes[1].InterpolationMode, Is.EqualTo(DccKeyframeInterpolationMode.Linear));
        });
    }

    private static MaxSceneTransformKeyframeSnapshotData CreateTranslationKeyframe(int frame, double x)
    {
        return new MaxSceneTransformKeyframeSnapshotData
        {
            Frame = frame,
            Transform = new MaxSceneTransformSnapshotData
            {
                Translation = new MaxSceneVector3SnapshotData { X = x, Y = 0d, Z = 0d },
                Rotation = new MaxSceneQuaternionSnapshotData { X = 0d, Y = 0d, Z = 0d, W = 1d },
                Scale = new MaxSceneVector3SnapshotData { X = 1d, Y = 1d, Z = 1d }
            }
        };
    }

    #endregion

    #region Area Light Tests

    [Test]
    public void AreaLightDimensionsAndShadowFlagAreMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Lights[0].Kind = DccLightKind.Area;
        snapshot.Lights[0].AreaWidth = 2d;
        snapshot.Lights[0].AreaHeight = 3d;
        snapshot.Lights[0].CastShadows = false;

        var light = MapScene(snapshot).Lights.Single(me => me.Id == "light:key");

        Assert.That(light.Kind, Is.EqualTo(DccLightKind.Area));
        Assert.That(light.AreaWidth, Is.EqualTo(2d));
        Assert.That(light.AreaHeight, Is.EqualTo(3d));
        Assert.That(light.CastShadows, Is.False);
    }

    [Test]
    public void NonAreaLightKeepsDefaultShadowAndAreaDefaultsTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var light = MapScene(snapshot).Lights.Single(me => me.Id == "light:key");

        Assert.That(light.Kind, Is.EqualTo(DccLightKind.Point));
        Assert.That(light.CastShadows, Is.True);
        Assert.That(light.AreaWidth, Is.EqualTo(1d));
        Assert.That(light.AreaHeight, Is.EqualTo(1d));
    }

    #endregion

    #region Camera Depth Of Field Tests

    [Test]
    public void CameraDepthOfFieldIsMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Cameras[0].EnableDepthOfField = true;
        snapshot.Cameras[0].FocusDistance = 10d;
        snapshot.Cameras[0].FStop = 2d;

        var camera = MapScene(snapshot).Cameras.Single(me => me.Id == "camera:main");

        Assert.That(camera.EnableDepthOfField, Is.True);
        Assert.That(camera.FocusDistance, Is.EqualTo(10d));
        Assert.That(camera.FStop, Is.EqualTo(2d));
    }

    [Test]
    public void CameraWithoutDepthOfFieldStaysDisabledTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var camera = MapScene(snapshot).Cameras.Single(me => me.Id == "camera:main");

        Assert.That(camera.EnableDepthOfField, Is.False);
    }

    #endregion

    #region Second UV Set Tests

    [Test]
    public void SecondUvSetIsMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Meshes[0].Uv1 =
        [
            new MaxSceneVector2SnapshotData { X = 0.25d, Y = 0.75d },
            new MaxSceneVector2SnapshotData { X = 0.5d, Y = 0.5d },
            new MaxSceneVector2SnapshotData { X = 0.75d, Y = 0.25d }
        ];

        var mesh = MapScene(snapshot).Meshes.Single(me => me.Id == "mesh:demo");

        Assert.That(mesh.Uv1, Has.Count.EqualTo(3));
        Assert.That(mesh.Uv1[0].X, Is.EqualTo(0.25d));
        Assert.That(mesh.Uv1[0].Y, Is.EqualTo(0.75d));
        Assert.That(mesh.Uv1[2].X, Is.EqualTo(0.75d));
    }

    [Test]
    public void MeshWithoutSecondUvSetHasEmptyUv1Test()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var mesh = MapScene(snapshot).Meshes.Single(me => me.Id == "mesh:demo");

        Assert.That(mesh.Uv1, Is.Empty);
    }

    #endregion

    #region World / Environment Tests

    [Test]
    public void EnvironmentColourIsMappedToWorldTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.EnvironmentColor = new MaxSceneColorSnapshotData { R = 0.2d, G = 0.3d, B = 0.4d, A = 1d };

        var scene = MapScene(snapshot);

        Assert.That(scene.World, Is.Not.Null);
        // Max swatches are display (sRGB) values; the mapper linearizes them for the renderer.
        Assert.That(scene.World!.BackgroundColor.R, Is.EqualTo(SrgbToLinear(0.2d)).Within(1e-9));
        Assert.That(scene.World.BackgroundColor.G, Is.EqualTo(SrgbToLinear(0.3d)).Within(1e-9));
        Assert.That(scene.World.BackgroundColor.B, Is.EqualTo(SrgbToLinear(0.4d)).Within(1e-9));
    }

    private static double SrgbToLinear(double value)
    {
        return value <= 0.04045d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
    }

    [Test]
    public void NoEnvironmentColourLeavesWorldNullTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var scene = MapScene(snapshot);

        Assert.That(scene.World, Is.Null);
    }

    #endregion

    #region Environment Image (HDRI) Tests

    [Test]
    public void EnvironmentImageDrivesWorldWhenAssetPresentTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        // The minimal scene already carries an ImageAsset "image:floor_albedo"; reuse it as the HDRI.
        snapshot.EnvironmentImageId = "image:floor_albedo";
        snapshot.EnvironmentRotationDegrees = 90d;

        var scene = MapScene(snapshot);

        Assert.That(scene.World, Is.Not.Null);
        Assert.That(scene.World!.EnvironmentImageId, Is.EqualTo("image:floor_albedo"));
        Assert.That(scene.World.EnvironmentRotationDegrees, Is.EqualTo(90d));
    }

    [Test]
    public void EnvironmentImageIgnoredWhenAssetMissingTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        // An id that does not resolve to an ImageAsset must not produce an env-image world (a 1.4.0
        // contract guard); with no background colour either, the scene keeps an empty world.
        snapshot.EnvironmentImageId = "image:does_not_exist";

        var scene = MapScene(snapshot);

        Assert.That(scene.World, Is.Null);
    }

    [Test]
    public void EnvironmentImageTakesPriorityOverBackgroundColourTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.EnvironmentColor = new MaxSceneColorSnapshotData { R = 0.2d, G = 0.3d, B = 0.4d, A = 1d };
        snapshot.EnvironmentImageId = "image:floor_albedo";

        var scene = MapScene(snapshot);

        Assert.That(scene.World, Is.Not.Null);
        Assert.That(scene.World!.EnvironmentImageId, Is.EqualTo("image:floor_albedo"));
    }

    #endregion

    #region Vertex Colour Tests

    [Test]
    public void VertexColoursAreMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Meshes[0].Colors =
        [
            new MaxSceneColorSnapshotData { R = 1d, G = 0d, B = 0d, A = 1d },
            new MaxSceneColorSnapshotData { R = 0d, G = 1d, B = 0d, A = 1d },
            new MaxSceneColorSnapshotData { R = 0d, G = 0d, B = 1d, A = 1d }
        ];

        var mesh = MapScene(snapshot).Meshes.Single(me => me.Id == "mesh:demo");

        Assert.That(mesh.Colors, Has.Count.EqualTo(3));
        Assert.That(mesh.Colors[0].R, Is.EqualTo(1d));
        Assert.That(mesh.Colors[1].G, Is.EqualTo(1d));
        Assert.That(mesh.Colors[2].B, Is.EqualTo(1d));
    }

    [Test]
    public void MeshWithoutVertexColoursHasEmptyColoursTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var mesh = MapScene(snapshot).Meshes.Single(me => me.Id == "mesh:demo");

        Assert.That(mesh.Colors, Is.Empty);
    }

    #endregion

    #region Deformation Tests

    [Test]
    public void DeformationFramesAreMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        // Each deformation frame's position count must match the mesh corner count (3 here).
        snapshot.Meshes[0].DeformationFrames =
        [
            new MaxSceneMeshDeformationFrameSnapshotData
            {
                Frame = 2,
                Positions =
                [
                    new MaxSceneVector3SnapshotData { X = 0d, Y = 0d, Z = 1d },
                    new MaxSceneVector3SnapshotData { X = 1d, Y = 0d, Z = 1d },
                    new MaxSceneVector3SnapshotData { X = 0d, Y = 1d, Z = 1d }
                ]
            }
        ];

        var mesh = MapScene(snapshot).Meshes.Single(me => me.Id == "mesh:demo");

        Assert.That(mesh.DeformationFrames, Has.Count.EqualTo(1));
        Assert.That(mesh.DeformationFrames[0].Frame, Is.EqualTo(2));
        Assert.That(mesh.DeformationFrames[0].Positions, Has.Count.EqualTo(3));
        Assert.That(mesh.DeformationFrames[0].Positions[0].Z, Is.EqualTo(1d));
    }

    [Test]
    public void StaticMeshHasNoDeformationFramesTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var mesh = MapScene(snapshot).Meshes.Single(me => me.Id == "mesh:demo");

        Assert.That(mesh.DeformationFrames, Is.Empty);
    }

    #endregion

    #region Motion Blur Tests

    [Test]
    public void MotionBlurIsMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.MotionBlur = true;
        snapshot.MotionBlurShutter = 0.25d;

        var renderSettings = MapScene(snapshot).RenderSettings;

        Assert.That(renderSettings.MotionBlur, Is.True);
        Assert.That(renderSettings.MotionBlurShutter, Is.EqualTo(0.25d));
    }

    [Test]
    public void NoMotionBlurLeavesRenderSettingsDisabledTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var renderSettings = MapScene(snapshot).RenderSettings;

        Assert.That(renderSettings.MotionBlur, Is.False);
        Assert.That(renderSettings.MotionBlurShutter, Is.EqualTo(0.5d));
    }

    #endregion

    #region Displacement Tests

    [Test]
    public void DisplacementScaleIsMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Materials[0].DisplacementScale = 3d;

        var material = MapScene(snapshot).Materials.Single(me => me.Id == "material:floor");

        Assert.That(material.DisplacementScale, Is.EqualTo(3d));
    }

    [Test]
    public void DisplacementTextureSlotIsMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Materials[0].TextureSlots.Add(new MaxSceneTextureSlotSnapshotData
        {
            Slot = DccTextureSlotKind.Displacement,
            ImageAssetId = "image:floor_albedo"
        });

        var material = MapScene(snapshot).Materials.Single(me => me.Id == "material:floor");

        Assert.That(material.TextureSlots.Any(me => me.Slot == DccTextureSlotKind.Displacement), Is.True);
    }

    #endregion

    #region Light Property Animation Tests

    [Test]
    public void LightIntensityKeyframesUseTheSameCalibrationAsStaticIntensityTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        // Raw static intensity is 2; a keyframe at the same raw value must map to the static mapped
        // value, and a keyframe at double the raw value to double the mapped value.
        snapshot.Lights[0].IntensityKeyframes =
        [
            new MaxSceneScalarKeyframeSnapshotData { Frame = 1, Value = 2d },
            new MaxSceneScalarKeyframeSnapshotData { Frame = 5, Value = 4d }
        ];

        var light = MapScene(snapshot).Lights.Single(me => me.Id == "light:key");

        Assert.That(light.IntensityKeyframes, Has.Count.EqualTo(2));
        Assert.That(light.IntensityKeyframes[0].Value, Is.EqualTo(light.Intensity).Within(1e-6));
        Assert.That(light.IntensityKeyframes[1].Value, Is.EqualTo(light.Intensity * 2d).Within(1e-6));
        Assert.That(light.IntensityKeyframes[0].InterpolationMode, Is.EqualTo(DccKeyframeInterpolationMode.Linear));
    }

    [Test]
    public void LightColourKeyframesAreMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Lights[0].ColorKeyframes =
        [
            new MaxSceneColorKeyframeSnapshotData { Frame = 1, Color = new MaxSceneColorSnapshotData { R = 1d, G = 0d, B = 0d, A = 1d } },
            new MaxSceneColorKeyframeSnapshotData { Frame = 5, Color = new MaxSceneColorSnapshotData { R = 0d, G = 0d, B = 1d, A = 1d } }
        ];

        var light = MapScene(snapshot).Lights.Single(me => me.Id == "light:key");

        Assert.That(light.ColorKeyframes, Has.Count.EqualTo(2));
        Assert.That(light.ColorKeyframes[0].Color.R, Is.EqualTo(1d));
        Assert.That(light.ColorKeyframes[1].Color.B, Is.EqualTo(1d));
    }

    [Test]
    public void SpotAngleKeyframesAreMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Lights[0].Kind = DccLightKind.Spot;
        snapshot.Lights[0].SpotAngleKeyframes =
        [
            new MaxSceneScalarKeyframeSnapshotData { Frame = 1, Value = 30d },
            new MaxSceneScalarKeyframeSnapshotData { Frame = 5, Value = 60d }
        ];

        var light = MapScene(snapshot).Lights.Single(me => me.Id == "light:key");

        Assert.That(light.SpotAngleKeyframes, Has.Count.EqualTo(2));
        Assert.That(light.SpotAngleKeyframes[0].Value, Is.EqualTo(30d));
        Assert.That(light.SpotAngleKeyframes[1].Value, Is.EqualTo(60d));
    }

    [Test]
    public void LightWithoutKeyframesHasEmptyKeyframeListsTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var light = MapScene(snapshot).Lights.Single(me => me.Id == "light:key");

        Assert.That(light.IntensityKeyframes, Is.Empty);
        Assert.That(light.ColorKeyframes, Is.Empty);
        Assert.That(light.RangeKeyframes, Is.Empty);
        Assert.That(light.SpotAngleKeyframes, Is.Empty);
    }

    #endregion

    #region Camera Property Animation Tests

    [Test]
    public void CameraFovKeyframesAreMappedTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Cameras[0].VerticalFovKeyframes =
        [
            new MaxSceneScalarKeyframeSnapshotData { Frame = 1, Value = 45d },
            new MaxSceneScalarKeyframeSnapshotData { Frame = 5, Value = 30d }
        ];

        var camera = MapScene(snapshot).Cameras.Single(me => me.Id == "camera:main");

        Assert.That(camera.VerticalFovKeyframes, Has.Count.EqualTo(2));
        Assert.That(camera.VerticalFovKeyframes[0].Value, Is.EqualTo(45d));
        Assert.That(camera.VerticalFovKeyframes[1].Value, Is.EqualTo(30d));
        Assert.That(camera.VerticalFovKeyframes[0].InterpolationMode, Is.EqualTo(DccKeyframeInterpolationMode.Linear));
    }

    [Test]
    public void CameraWithoutKeyframesHasEmptyKeyframeListsTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var camera = MapScene(snapshot).Cameras.Single(me => me.Id == "camera:main");

        Assert.That(camera.VerticalFovKeyframes, Is.Empty);
        Assert.That(camera.NearClipKeyframes, Is.Empty);
        Assert.That(camera.FarClipKeyframes, Is.Empty);
    }

    #endregion
}
