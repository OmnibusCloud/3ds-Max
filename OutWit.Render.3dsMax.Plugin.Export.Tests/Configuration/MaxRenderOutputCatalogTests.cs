using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Configuration;

[TestFixture]
public sealed class MaxRenderOutputCatalogTests
{
    #region Tiled Format Tests

    [Test]
    public void TiledStillsOfferOnlyWhatTheServerStitcherAcceptsTest()
    {
        // The server's tile collection runs an 8-bit ffmpeg crop/pad pipeline and throws on anything
        // else; offering the rest for a tiled launch only ever produced a job that failed on the farm.
        Assert.That(MaxRenderOutputCatalog.TiledImageFormats, Is.EquivalentTo(new[] { "PNG", "JPEG" }));

        // Still a subset of the full list — one catalog, not two diverging ones.
        Assert.That(MaxRenderOutputCatalog.TiledImageFormats, Is.SubsetOf(MaxRenderOutputCatalog.ImageFormats));
    }

    [TestCase("PNG", true)]
    [TestCase("JPEG", true)]
    [TestCase("jpeg", true)]
    [TestCase("EXR", false)]
    [TestCase("TIFF", false)]
    [TestCase("WEBP", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void TiledFormatSupportIsRecognisedCaseInsensitivelyTest(string? value, bool expected)
    {
        Assert.That(MaxRenderOutputCatalog.IsTiledImageFormat(value), Is.EqualTo(expected));
    }

    [TestCase("PNG", "PNG")]
    [TestCase("jpeg", "JPEG")]
    [TestCase("EXR", "PNG")]
    [TestCase("TIFF", "PNG")]
    [TestCase("", "PNG")]
    [TestCase(null, "PNG")]
    public void AnUnsupportedTiledFormatFallsBackToPngTest(string? value, string expected)
    {
        // A fallback, not a failure: a tiled launch the farm cannot collect is never the intent, and
        // an earlier build actively steered stills here by nudging tiled PNG to EXR.
        Assert.That(MaxRenderOutputCatalog.NormalizeTiledImageFormat(value), Is.EqualTo(expected));
    }

    #endregion
}
