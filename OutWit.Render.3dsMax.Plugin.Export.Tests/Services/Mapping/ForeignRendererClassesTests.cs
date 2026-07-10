using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Mapping;

/// <summary>
/// Verifies the foreign-renderer class detection — especially that the finalRender "fR" prefix
/// no longer swallows the stock "Free …" light classes.
/// </summary>
[TestFixture]
public sealed class ForeignRendererClassesTests
{
    #region Tests

    [TestCase("VRayMtl")]
    [TestCase("VRayLight")]
    [TestCase("VRayScannedMtl")]
    [TestCase("CoronaLight")]
    [TestCase("OctaneStandardSurface")]
    [TestCase("RedshiftMaterial")]
    [TestCase("fR-Advanced")]
    [TestCase("fRMetal")]
    [TestCase("finalRender Sky")]
    public void KnownForeignClassesAreForeignTest(string className)
    {
        Assert.That(ForeignRendererClasses.IsForeign(className), Is.True);
    }

    [TestCase("Free Light")]
    [TestCase("Free Spot")]
    [TestCase("Free Direct")]
    [TestCase("Free Area")]
    [TestCase("Fresnel")]
    [TestCase("Standard")]
    [TestCase("Physical Material")]
    [TestCase("Target Spot")]
    [TestCase("Omni")]
    [TestCase("")]
    [TestCase(null)]
    public void StockClassesAreNotForeignTest(string? className)
    {
        // The photometric "Free Light" regression: "fR" matched case-insensitively swallowed
        // every class starting with "Fr" and degraded stock free lights to the neutral fallback.
        Assert.That(ForeignRendererClasses.IsForeign(className), Is.False);
    }

    #endregion
}
