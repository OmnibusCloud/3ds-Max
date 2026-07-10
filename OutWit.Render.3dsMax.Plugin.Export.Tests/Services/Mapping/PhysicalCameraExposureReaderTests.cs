using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Mapping;

/// <summary>
/// Verifies the physical-camera exposure reader (stock Physical and VRayPhysicalCamera EV100
/// resolution) and the mapper's deviation-from-reference exposure.
/// </summary>
[TestFixture]
public sealed class PhysicalCameraExposureReaderTests
{
    #region Tools

    // Token order: exposure_value, exposure_gain_type, ISO, f_number, shutter_length_seconds,
    // shutter_speed, exposure (V-Ray mode), shutter_unit_type.
    private static string BuildPayload(
        string exposureValue = "?",
        string gainType = "?",
        string iso = "?",
        string fNumber = "?",
        string shutterLengthSeconds = "?",
        string shutterSpeed = "?",
        string vrayExposureMode = "?",
        string shutterUnitType = "?")
    {
        return string.Join('|', exposureValue, gainType, iso, fNumber, shutterLengthSeconds, shutterSpeed, vrayExposureMode, shutterUnitType) + "|";
    }

    #endregion

    #region Script Tests

    [Test]
    public void BuildMaxScriptEmbedsHandleAndPropertiesTest()
    {
        var script = PhysicalCameraExposureReader.BuildMaxScript(31337);

        Assert.That(script, Does.Contain("getAnimByHandle 31337"));
        Assert.That(script, Does.Contain("m.exposure_value"));
        Assert.That(script, Does.Contain("m.exposure_gain_type"));
        Assert.That(script, Does.Contain("m.shutter_length_seconds"));
        Assert.That(script, Does.Contain("m.shutter_speed"));
        Assert.That(script, Does.Not.Contain("\n"));
    }

    #endregion

    #region Resolution Tests

    [Test]
    public void StockPhysicalTargetEvModeUsesAuthoredValueTest()
    {
        // Gain type 1 = Target EV: the authored EV wins even when the inactive manual spinners
        // hold values that would compute differently.
        var ev = PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload(
            exposureValue: "6.0",
            gainType: "1.0",
            iso: "100.0",
            fNumber: "8.0",
            shutterLengthSeconds: "0.0166667"));

        Assert.That(ev, Is.EqualTo(6d).Within(1e-6));
    }

    [Test]
    public void StockPhysicalManualIsoModeComputesEvTest()
    {
        // Gain type 0 = Manual ISO (the auto sample's RenderCam): EV100 derives from the
        // physicals — log2(N²/t · 100/ISO) = log2(12.25 · 1200) ≈ 13.84 — and must NOT trust
        // the Target spinner, which is inactive in this mode and may be stale.
        var ev = PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload(
            exposureValue: "6.0",
            gainType: "0.0",
            iso: "100.0",
            fNumber: "3.5",
            shutterLengthSeconds: "0.000833333"));

        Assert.That(ev, Is.EqualTo(Math.Log2(3.5d * 3.5d / 0.000833333d)).Within(1e-6));
    }

    [Test]
    public void StockPhysicalManualModeIgnoresStaleShutterSecondsForNonSecondsUnitsTest()
    {
        // shutter_unit_type 2 (frames): shutter_length_seconds is stale — fall back to the
        // authored EV instead of computing a wrong one from it.
        var ev = PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload(
            exposureValue: "9.5",
            gainType: "0.0",
            iso: "100.0",
            fNumber: "3.5",
            shutterLengthSeconds: "0.000833333",
            shutterUnitType: "2.0"));

        Assert.That(ev, Is.EqualTo(9.5d).Within(1e-9));
    }

    [Test]
    public void StockPhysicalManualIsoModeFallsBackToAuthoredEvOnDegeneratePhysicalsTest()
    {
        var ev = PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload(
            exposureValue: "11.0",
            gainType: "0.0",
            iso: "?",
            fNumber: "3.5",
            shutterLengthSeconds: "?"));

        Assert.That(ev, Is.EqualTo(11d).Within(1e-9));
    }

    [Test]
    public void VRayPhysicalCameraComputesEvFromPhysicalsTest()
    {
        // ChairCloth's CM_Animated: ISO 200, f/1.4, shutter 1/250 → EV100 ≈ 7.94. V-Ray
        // shutter_speed is 1/seconds.
        var ev = PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload(
            exposureValue: "13.0",
            iso: "200.0",
            fNumber: "1.4",
            shutterSpeed: "250.0",
            vrayExposureMode: "1.0"));

        Assert.That(ev, Is.EqualTo(Math.Log2(1.4d * 1.4d * 250d * 100d / 200d)).Within(1e-6));
    }

    [Test]
    public void VRayPhysicalCameraExposureOffYieldsNullTest()
    {
        var ev = PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload(
            exposureValue: "13.0",
            iso: "100.0",
            fNumber: "16.0",
            shutterSpeed: "1.0",
            vrayExposureMode: "0.0"));

        Assert.That(ev, Is.Null);
    }

    [Test]
    public void VRayPhysicalCameraEvModeUsesAuthoredValueTest()
    {
        var ev = PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload(
            exposureValue: "11.5",
            iso: "100.0",
            fNumber: "16.0",
            shutterSpeed: "1.0",
            vrayExposureMode: "2.0"));

        Assert.That(ev, Is.EqualTo(11.5d).Within(1e-9));
    }

    [Test]
    public void OrdinaryCameraResolvesNullTest()
    {
        Assert.That(PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload()), Is.Null);
        Assert.That(PhysicalCameraExposureReader.TryResolveExposureValue(null), Is.Null);
        Assert.That(PhysicalCameraExposureReader.TryResolveExposureValue(""), Is.Null);
        Assert.That(PhysicalCameraExposureReader.TryResolveExposureValue("1.0|2.0"), Is.Null, "wrong token count");
    }

    [Test]
    public void DegeneratePhysicalsResolveNullTest()
    {
        Assert.That(PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload(
            iso: "0.0", fNumber: "1.4", shutterSpeed: "250.0", vrayExposureMode: "1.0")), Is.Null);
        Assert.That(PhysicalCameraExposureReader.TryResolveExposureValue(BuildPayload(
            iso: "100.0", fNumber: "0.0", shutterSpeed: "250.0", vrayExposureMode: "1.0")), Is.Null);
    }

    #endregion

    #region Mapper Contract Tests

    [Test]
    public void CameraEvMapsToExposureDeviationFromReferenceTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Cameras[0].ExposureEv = 13.8435d;

        var scene = MaxSceneDccSceneMapper.Create(MaxSceneExportTestData.CreateService(snapshot).CollectSummary());

        Assert.That(scene.RenderSettings.Exposure, Is.EqualTo(12d - 13.8435d).Within(1e-6),
            "camera EV maps as a deviation from the EV-12 photographic reference");
    }

    [Test]
    public void ExposureControlWinsOverCameraEvTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Cameras[0].ExposureEv = 13.8435d;
        snapshot.ExposureControlEv = 7d;

        var scene = MaxSceneDccSceneMapper.Create(MaxSceneExportTestData.CreateService(snapshot).CollectSummary());

        Assert.That(scene.RenderSettings.Exposure, Is.EqualTo(-1d).Within(1e-6),
            "an explicit exposure control keeps its EV-6 neutral mapping");
    }

    [Test]
    public void BrightCameraEvNeverBrightensTest()
    {
        // ChairCloth's f/1.4 studio camera reads EV ~7.9 — a fast lens compensating dim
        // physical light the calibration already normalized. Carrying +4 stops would blow out.
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();
        snapshot.Cameras[0].ExposureEv = 7.94d;

        var scene = MaxSceneDccSceneMapper.Create(MaxSceneExportTestData.CreateService(snapshot).CollectSummary());

        Assert.That(scene.RenderSettings.Exposure, Is.Zero);
    }

    [Test]
    public void NoExposureSignalsKeepNeutralExposureTest()
    {
        var snapshot = MaxSceneExportTestData.CreateMinimalValidSceneSnapshot();

        var scene = MaxSceneDccSceneMapper.Create(MaxSceneExportTestData.CreateService(snapshot).CollectSummary());

        Assert.That(scene.RenderSettings.Exposure, Is.Zero);
    }

    #endregion
}
