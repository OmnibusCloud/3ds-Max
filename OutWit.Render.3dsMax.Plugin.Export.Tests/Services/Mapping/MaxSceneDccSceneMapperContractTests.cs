using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Mapping;

/// <summary>
/// Verifies the Dcc 1.3 contract fields (area lights, camera depth of field, second UV set, and
/// scene world/environment colour) survive the snapshot -> summary -> neutral DccScene mapping.
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
        Assert.That(scene.World!.BackgroundColor.R, Is.EqualTo(0.2d));
        Assert.That(scene.World.BackgroundColor.G, Is.EqualTo(0.3d));
        Assert.That(scene.World.BackgroundColor.B, Is.EqualTo(0.4d));
    }

    [Test]
    public void NoEnvironmentColourLeavesWorldNullTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var scene = MapScene(snapshot);

        Assert.That(scene.World, Is.Null);
    }

    #endregion
}
