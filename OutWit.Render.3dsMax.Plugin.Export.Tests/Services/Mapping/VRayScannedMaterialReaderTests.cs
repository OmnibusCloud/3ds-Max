using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Mapping;

/// <summary>
/// Verifies the VRayScannedMtl approximation: artist overrides win, then the Chaos scan file
/// name drives the colour and the scan type drives the response.
/// </summary>
[TestFixture]
public sealed class VRayScannedMaterialReaderTests
{
    #region Tools

    private static string BuildPayload(
        string usePaint = "0.0",
        string paint = "16.0|0.0|0.0",
        string useFilter = "0.0",
        string filter = "255.0|255.0|255.0",
        string filename = @"\Assets\VRScans\Carpaint_Red_1_S.vrscan")
    {
        return string.Join('|', usePaint, paint, useFilter, filter, filename) + "|";
    }

    private static MaxSceneMaterialSnapshotData Apply(string payload, string materialName = "")
    {
        var snapshot = new MaxSceneMaterialSnapshotData { Name = materialName };
        Assert.That(VRayScannedMaterialReader.TryApply(payload, snapshot), Is.True);
        return snapshot;
    }

    #endregion

    #region Script Tests

    [Test]
    public void BuildMaxScriptEmbedsHandleAndPropertiesTest()
    {
        var script = VRayScannedMaterialReader.BuildMaxScript(77);

        Assert.That(script, Does.Contain("getAnimByHandle 77"));
        Assert.That(script, Does.Contain("m.usepaint"));
        Assert.That(script, Does.Contain("m.paint_color.b"));
        Assert.That(script, Does.Contain("m.useflt"));
        Assert.That(script, Does.Contain("m.filter_Color.g"));
        Assert.That(script, Does.Contain("m.filename"));
        Assert.That(script, Does.Not.Contain("\n"));
    }

    #endregion

    #region Mapping Tests

    [Test]
    public void CarpaintScanNameDrivesColourAndFinishTest()
    {
        var snapshot = Apply(BuildPayload());

        Assert.That(snapshot.BaseColor.R, Is.EqualTo(0.62d).Within(1e-9), "colour word 'red' from the scan file name");
        Assert.That(snapshot.BaseColor.G, Is.EqualTo(0.04d).Within(1e-9));
        Assert.That(snapshot.Metallic, Is.EqualTo(0.85d).Within(1e-9), "carpaint = pigment under clearcoat");
        Assert.That(snapshot.Roughness, Is.EqualTo(0.3d).Within(1e-9));
    }

    [Test]
    public void EnabledPaintOverrideWinsTest()
    {
        var snapshot = Apply(BuildPayload(usePaint: "1.0", paint: "128.0|64.0|32.0"));

        Assert.That(snapshot.BaseColor.R, Is.EqualTo(128d / 255d).Within(1e-9));
        Assert.That(snapshot.BaseColor.B, Is.EqualTo(32d / 255d).Within(1e-9));
    }

    [Test]
    public void DisabledPaintOverrideIsIgnoredTest()
    {
        // The auto scene authors paint_color=(16,0,0) but leaves usepaint OFF — the scan's own
        // (file-name) red must win over the nearly-black disabled override.
        var snapshot = Apply(BuildPayload(usePaint: "0.0", paint: "16.0|0.0|0.0"));

        Assert.That(snapshot.BaseColor.R, Is.EqualTo(0.62d).Within(1e-9));
    }

    [Test]
    public void EnabledFilterTintWinsOverScanNameTest()
    {
        var snapshot = Apply(BuildPayload(useFilter: "1.0", filter: "255.0|128.0|128.0"));

        Assert.That(snapshot.BaseColor.G, Is.EqualTo(128d / 255d).Within(1e-9));
    }

    [Test]
    public void WhiteFilterIsNotATintTest()
    {
        var snapshot = Apply(BuildPayload(useFilter: "1.0", filter: "255.0|255.0|255.0"));

        Assert.That(snapshot.BaseColor.R, Is.EqualTo(0.62d).Within(1e-9), "white filter is a no-op; scan colour wins");
    }

    [Test]
    public void MatteMetalScanTest()
    {
        var snapshot = Apply(BuildPayload(filename: @"C:\scans\Metal_Matte_3_S.vrscan"));

        Assert.That(snapshot.Metallic, Is.EqualTo(1d).Within(1e-9));
        Assert.That(snapshot.Roughness, Is.EqualTo(0.5d).Within(1e-9));
        Assert.That(snapshot.BaseColor.R, Is.EqualTo(0.78d).Within(1e-9), "metals default to a silvery base");
    }

    [Test]
    public void CarbonScanIsGlossyNotMatteTest()
    {
        // Carbon fibre is a woven composite under clearcoat — the fabric branch rendered it
        // as matte cloth.
        var snapshot = Apply(BuildPayload(filename: "Carbon_Fiber_2x2.vrscan"));

        Assert.That(snapshot.Metallic, Is.EqualTo(0.85d).Within(1e-9));
        Assert.That(snapshot.Roughness, Is.EqualTo(0.3d).Within(1e-9));
    }

    [Test]
    public void FabricScanIsMatteTest()
    {
        var snapshot = Apply(BuildPayload(filename: "Fabric_Weave_2.vrscan"));

        Assert.That(snapshot.Metallic, Is.Zero);
        Assert.That(snapshot.Roughness, Is.EqualTo(0.85d).Within(1e-9));
        Assert.That(snapshot.BaseColor.R, Is.EqualTo(0.72d).Within(1e-9), "neutral base without a colour word");
    }

    [Test]
    public void MaterialNameSuppliesTypeKeywordTest()
    {
        // ChairCloth's 'VRScan_MetalSheet' points at a scan file with no type word in it — the
        // artist's material name must still classify the scan as a metal.
        var snapshot = Apply(BuildPayload(filename: "Sheet_3_S.vrscan"), materialName: "VRScan_MetalSheet");

        Assert.That(snapshot.Metallic, Is.EqualTo(1d).Within(1e-9));
        Assert.That(snapshot.BaseColor.R, Is.EqualTo(0.78d).Within(1e-9));
    }

    [Test]
    public void MalformedPayloadFailsTest()
    {
        var snapshot = new MaxSceneMaterialSnapshotData();
        Assert.That(VRayScannedMaterialReader.TryApply(null, snapshot), Is.False);
        Assert.That(VRayScannedMaterialReader.TryApply("", snapshot), Is.False);
        Assert.That(VRayScannedMaterialReader.TryApply("1.0|2.0", snapshot), Is.False);
        Assert.That(VRayScannedMaterialReader.TryApply(BuildPayload(paint: "abc|0.0|0.0"), snapshot), Is.False);
    }

    [Test]
    public void MissingOverridesStillMapScanNameTest()
    {
        var snapshot = Apply(BuildPayload(usePaint: "?", paint: "?|?|?", useFilter: "?", filter: "?|?|?"));

        Assert.That(snapshot.BaseColor.R, Is.EqualTo(0.62d).Within(1e-9));
        Assert.That(snapshot.Metallic, Is.EqualTo(0.85d).Within(1e-9));
    }

    #endregion
}
