using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Mapping;

/// <summary>
/// Verifies the V-Ray light readers: plane/disc/sphere map to node lights, a dome routes to
/// the environment, VRaySun maps to the sun, and malformed payloads fall back.
/// </summary>
[TestFixture]
public sealed class VRayLightReaderTests
{
    #region Tools

    // Token order mirrors VRayLightReader.LIGHT_PROPERTY_EXPRESSIONS.
    private static string BuildLightPayload(
        string on = "1.0",
        string type = "0.0",
        string color = "255.0|255.0|255.0",
        string multiplier = "1.0",
        string normalizeColor = "0.0",
        string sizeLength = "100.0",
        string sizeWidth = "20.0",
        string size0 = "50.0",
        string noDecay = "0.0",
        string castShadows = "1.0")
    {
        return string.Join('|', on, type, color, multiplier, normalizeColor, sizeLength, sizeWidth, size0, noDecay, castShadows) + "|";
    }

    private static string BuildSunPayload(
        string enabled = "1.0",
        string intensityMultiplier = "1.0",
        string filterColor = "255.0|248.0|246.0")
    {
        return string.Join('|', enabled, intensityMultiplier, filterColor) + "|";
    }

    #endregion

    #region Script Tests

    [Test]
    public void BuildScriptsEmbedHandleAndPropertiesTest()
    {
        var lightScript = VRayLightReader.BuildLightMaxScript(101);
        Assert.That(lightScript, Does.Contain("getAnimByHandle 101"));
        Assert.That(lightScript, Does.Contain("m.type"));
        Assert.That(lightScript, Does.Contain("m.multiplier"));
        Assert.That(lightScript, Does.Contain("m.normalizeColor"));
        Assert.That(lightScript, Does.Contain("m.sizeLength"));
        Assert.That(lightScript, Does.Contain("m.noDecay"));

        var sunScript = VRayLightReader.BuildSunMaxScript(202);
        Assert.That(sunScript, Does.Contain("getAnimByHandle 202"));
        Assert.That(sunScript, Does.Contain("m.intensity_multiplier"));
        Assert.That(sunScript, Does.Contain("m.filter_Color.b"));
    }

    #endregion

    #region VRayLight Tests

    [Test]
    public void PlaneLightMapsToAreaTest()
    {
        // The auto scene's TireFill_L01: plane, 3900K colour, 5.5 in radiant-power units.
        var snapshot = new MaxSceneLightSnapshotData();
        var role = VRayLightReader.Apply(BuildLightPayload(
            color: "255.0|166.969|88.9383",
            multiplier: "5.5",
            normalizeColor: "3.0",
            sizeLength: "100.0",
            sizeWidth: "19.661",
            noDecay: "1.0"), snapshot);

        Assert.That(role, Is.EqualTo(VRayLightRole.Light));
        Assert.That(snapshot.Kind, Is.EqualTo(DccLightKind.Area));
        Assert.That(snapshot.AreaWidth, Is.EqualTo(100d).Within(1e-9));
        Assert.That(snapshot.AreaHeight, Is.EqualTo(19.661d).Within(1e-6));
        Assert.That(snapshot.Intensity, Is.EqualTo(5.5d).Within(1e-9));
        Assert.That(snapshot.IsPhotometric, Is.True, "radiant-power units are physical");
        Assert.That(snapshot.NoDecay, Is.True);
        Assert.That(snapshot.Color.G, Is.EqualTo(166.969d / 255d).Within(1e-6));
    }

    [Test]
    public void DefaultUnitsAreNotPhotometricTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        VRayLightReader.Apply(BuildLightPayload(normalizeColor: "0.0"), snapshot);

        Assert.That(snapshot.IsPhotometric, Is.False);
    }

    [Test]
    public void DiscLightMapsToSquareAreaTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        var role = VRayLightReader.Apply(BuildLightPayload(type: "4.0", size0: "25.0"), snapshot);

        Assert.That(role, Is.EqualTo(VRayLightRole.Light));
        Assert.That(snapshot.Kind, Is.EqualTo(DccLightKind.Area));
        Assert.That(snapshot.AreaWidth, Is.EqualTo(50d).Within(1e-9), "disc diameter from its radius");
        Assert.That(snapshot.AreaHeight, Is.EqualTo(50d).Within(1e-9));
    }

    [Test]
    public void SphereLightMapsToPointTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        var role = VRayLightReader.Apply(BuildLightPayload(type: "2.0", multiplier: "30.0"), snapshot);

        Assert.That(role, Is.EqualTo(VRayLightRole.Light));
        Assert.That(snapshot.Kind, Is.EqualTo(DccLightKind.Point));
        Assert.That(snapshot.Intensity, Is.EqualTo(30d).Within(1e-9));
    }

    [Test]
    public void DomeLightRoutesToEnvironmentTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        var role = VRayLightReader.Apply(BuildLightPayload(type: "1.0", multiplier: "1.0"), snapshot);

        Assert.That(role, Is.EqualTo(VRayLightRole.DomeEnvironment));
        Assert.That(snapshot.Intensity, Is.EqualTo(1d).Within(1e-9), "multiplier survives for the environment capture");
    }

    [Test]
    public void DisabledDomeIsJustADroppedLightTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        var role = VRayLightReader.Apply(BuildLightPayload(on: "0.0", type: "1.0"), snapshot);

        Assert.That(role, Is.EqualTo(VRayLightRole.Light));
        Assert.That(snapshot.Intensity, Is.Zero);
    }

    [Test]
    public void MeshLightIsUnsupportedTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        Assert.That(VRayLightReader.Apply(BuildLightPayload(type: "3.0"), snapshot), Is.EqualTo(VRayLightRole.Unsupported));
    }

    [Test]
    public void DisabledLightExportsZeroIntensityTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        var role = VRayLightReader.Apply(BuildLightPayload(on: "0.0", multiplier: "5.0"), snapshot);

        Assert.That(role, Is.EqualTo(VRayLightRole.Light));
        Assert.That(snapshot.Intensity, Is.Zero);
    }

    [Test]
    public void MalformedLightPayloadIsUnsupportedTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        Assert.That(VRayLightReader.Apply(null, snapshot), Is.EqualTo(VRayLightRole.Unsupported));
        Assert.That(VRayLightReader.Apply("", snapshot), Is.EqualTo(VRayLightRole.Unsupported));
        Assert.That(VRayLightReader.Apply("1.0|2.0", snapshot), Is.EqualTo(VRayLightRole.Unsupported));
        Assert.That(VRayLightReader.Apply(BuildLightPayload(type: "?"), snapshot), Is.EqualTo(VRayLightRole.Unsupported), "type is required");
        Assert.That(VRayLightReader.Apply(BuildLightPayload(multiplier: "?"), snapshot), Is.EqualTo(VRayLightRole.Unsupported), "multiplier is required");
    }

    #endregion

    #region VRaySun Tests

    [Test]
    public void SunMapsToSunKindTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        var applied = VRayLightReader.TryApplySun(BuildSunPayload(intensityMultiplier: "1.0"), snapshot);

        Assert.That(applied, Is.True);
        Assert.That(snapshot.Kind, Is.EqualTo(DccLightKind.Sun));
        Assert.That(snapshot.Intensity, Is.EqualTo(1d).Within(1e-9));
        Assert.That(snapshot.Color.G, Is.EqualTo(248d / 255d).Within(1e-6));
        Assert.That(snapshot.CastShadows, Is.True);
    }

    [Test]
    public void DisabledSunExportsZeroIntensityTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        var applied = VRayLightReader.TryApplySun(BuildSunPayload(enabled: "0.0", intensityMultiplier: "2.0"), snapshot);

        Assert.That(applied, Is.True);
        Assert.That(snapshot.Intensity, Is.Zero);
    }

    [Test]
    public void MalformedSunPayloadFailsTest()
    {
        var snapshot = new MaxSceneLightSnapshotData();
        Assert.That(VRayLightReader.TryApplySun(null, snapshot), Is.False);
        Assert.That(VRayLightReader.TryApplySun(BuildSunPayload(intensityMultiplier: "?"), snapshot), Is.False, "intensity is required");
    }

    #endregion
}
