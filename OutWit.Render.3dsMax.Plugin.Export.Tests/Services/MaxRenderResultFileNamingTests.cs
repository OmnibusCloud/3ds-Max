using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

/// <summary>
/// Results are named after the format the artist chose. Every still, tiled still and frame used to be
/// saved as .png and every video as .mp4, whatever was selected.
/// </summary>
/// <remarks>
/// The golden headers below are the first 16 bytes of files produced by the farm's OWN writers — the
/// Blender 5.1.0 and ffmpeg shipped in the Render controller module synced to a node — driven with the
/// controller's arguments (<c>-F JPEG/OPEN_EXR/TIFF/WEBP/PNG</c>; libx264, libx265 + hvc1, libvpx-vp9,
/// prores_ks profile 3).
/// </remarks>
[TestFixture]
public sealed class MaxRenderResultFileNamingTests
{
    #region Constants

    private const string BLENDER_PNG = "89 50 4e 47 0d 0a 1a 0a 00 00 00 0d 49 48 44 52";

    private const string BLENDER_JPEG = "ff d8 ff e0 00 10 4a 46 49 46 00 01 01 00 00 01";

    private const string BLENDER_EXR = "76 2f 31 01 02 04 00 00 43 61 6d 65 72 61 00 73";

    private const string BLENDER_TIFF = "49 49 2a 00 08 00 00 00 12 00 00 01 03 00 01 00";

    private const string BLENDER_WEBP = "52 49 46 46 a6 00 00 00 57 45 42 50 56 50 38 20";

    private const string FFMPEG_H264_MP4 = "00 00 00 20 66 74 79 70 69 73 6f 6d 00 00 02 00";

    private const string FFMPEG_H265_MP4 = "00 00 00 1c 66 74 79 70 69 73 6f 6d 00 00 02 00";

    private const string FFMPEG_VP9_WEBM = "1a 45 df a3 9f 42 86 81 01 42 f7 81 01 42 f2 81";

    private const string FFMPEG_PRORES_MOV = "00 00 00 14 66 74 79 70 71 74 20 20 00 00 02 00";

    #endregion

    #region Fields

    private string m_testDir = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"max-result-naming-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, true);
        }
        catch
        {
            // A locked temp file must not fail the suite.
        }
    }

    #region Expected Extension Tests

    [TestCase("RenderStill", "PNG", ".png")]
    [TestCase("RenderStill", "JPEG", ".jpg")]
    [TestCase("RenderStill", "EXR", ".exr")]
    [TestCase("RenderStill", "TIFF", ".tif")]
    [TestCase("RenderStill", "WEBP", ".webp")]
    [TestCase("RenderStillTiled", "JPEG", ".jpg")]
    [TestCase("RenderFrames", "EXR", ".exr")]
    [TestCase("RenderFrames", "TIFF", ".tif")]
    [TestCase("RenderStill", "jpeg", ".jpg")]
    [TestCase("RenderStill", "", ".png")]
    public void AnImageResultTakesTheChosenFormatsExtensionTest(string renderMode, string imageFormat, string expected)
    {
        // The same names the Render controller gives its own outputs (BlenderRenderArgsBuilder), and the
        // ones Blender itself wrote for the golden samples above. An empty format travels as PNG.
        Assert.That(MaxRenderOutputCatalog.ResultExtension(renderMode, imageFormat, string.Empty), Is.EqualTo(expected));
    }

    [TestCase("mp4-h264", ".mp4")]
    [TestCase("mp4-h265", ".mp4")]
    [TestCase("webm-vp9", ".webm")]
    [TestCase("mov-prores", ".mov")]
    [TestCase("", ".mp4")]
    public void AVideoResultTakesTheChosenContainersExtensionTest(string videoPreset, string expected)
    {
        // FfmpegRunner.GetVideoFileName on the farm: render.webm / render.mov / render.mp4.
        Assert.That(MaxRenderOutputCatalog.ResultExtension("RenderVideo", "PNG", videoPreset), Is.EqualTo(expected));
    }

    [Test]
    public void AnExportIsAlwaysABlendTest()
    {
        Assert.That(MaxRenderOutputCatalog.ResultExtension("ExportBlend", "JPEG", "webm-vp9"), Is.EqualTo(".blend"));
    }

    [Test]
    public void EveryExpectedExtensionIsARecognisedResultExtensionTest()
    {
        // FindLanded looks results up by this list; an extension missing from it would re-download
        // that result on every refresh.
        var produced = MaxRenderOutputCatalog.ImageFormats.Select(me => MaxRenderOutputCatalog.ResultExtension("RenderStill", me, string.Empty))
            .Concat(MaxRenderOutputCatalog.VideoPresets.Select(me => MaxRenderOutputCatalog.ResultExtension("RenderVideo", string.Empty, me.Key)))
            .Append(MaxRenderOutputCatalog.ResultExtension("ExportBlend", string.Empty, string.Empty))
            .Distinct(); // H.264 and H.265 share .mp4, and SubsetOf compares as a multiset

        Assert.That(produced, Is.SubsetOf(MaxRenderOutputCatalog.ResultExtensions));
    }

    #endregion

    #region Sniffer Tests

    [TestCase(BLENDER_PNG, ".png")]
    [TestCase(BLENDER_JPEG, ".jpg")]
    [TestCase(BLENDER_EXR, ".exr")]
    [TestCase(BLENDER_TIFF, ".tif")]
    [TestCase(BLENDER_WEBP, ".webp")]
    [TestCase(FFMPEG_H264_MP4, ".mp4")]
    [TestCase(FFMPEG_H265_MP4, ".mp4")]
    [TestCase(FFMPEG_VP9_WEBM, ".webm")]
    [TestCase(FFMPEG_PRORES_MOV, ".mov")]
    public void TheFarmsOwnOutputsAreRecognisedTest(string header, string expected)
    {
        Assert.That(MaxRenderResultSniffer.DetectExtension(Hex(header)), Is.EqualTo(expected));
    }

    [Test]
    public void AnUncompressedBlendIsRecognisedTest()
    {
        Assert.That(MaxRenderResultSniffer.DetectExtension("BLENDER-v501"u8), Is.EqualTo(".blend"));
    }

    [TestCase("28 b5 2f fd 00 00 00 00 00 00 00 00")] // zstd: a compressed .blend, but also anything else
    [TestCase("00 00 00 14 66 74 79 70 58 58 58 58")] // ISO media with an unknown brand
    [TestCase("de ad be ef")]
    [TestCase("")]
    public void AnythingAmbiguousStaysUnrecognisedTest(string header)
    {
        // Unrecognised defers to the requested format instead of guessing.
        Assert.That(MaxRenderResultSniffer.DetectExtension(Hex(header)), Is.Null);
    }

    [Test]
    public void AnUnreadableFileIsUnrecognisedTest()
    {
        Assert.That(MaxRenderResultSniffer.DetectExtension(Path.Combine(m_testDir, "missing.png")), Is.Null);
    }

    #endregion

    #region Naming Tests

    [Test]
    public void AFileWhoseBytesMatchItsNameIsLeftAloneTest()
    {
        var path = WriteFile("result.jpg", BLENDER_JPEG);

        Assert.That(MaxRenderResultFileNaming.NameByContent(path), Is.EqualTo(path));
        Assert.That(File.Exists(path), Is.True);
    }

    [TestCase(BLENDER_JPEG, ".jpg")]
    [TestCase(BLENDER_EXR, ".exr")]
    [TestCase(FFMPEG_PRORES_MOV, ".mov")]
    public void TheBytesWinOverTheRequestedNameTest(string header, string expected)
    {
        // A record written before the format was kept requests .png / .mp4 — exactly the old bug. The
        // downloaded bytes rename it to what it really is.
        var requested = expected == ".mov" ? "result.mp4" : "result.png";
        var path = WriteFile(requested, header);

        var named = MaxRenderResultFileNaming.NameByContent(path);

        Assert.Multiple(() =>
        {
            Assert.That(named, Is.EqualTo(Path.Combine(m_testDir, "result" + expected)));
            Assert.That(File.Exists(named), Is.True);
            Assert.That(File.Exists(path), Is.False);
        });
    }

    [Test]
    public void AnUnrecognisedFileKeepsTheRequestedNameTest()
    {
        // A compressed .blend (zstd) sniffs as nothing — its requested .blend stands.
        var path = WriteFile("result.blend", "28 b5 2f fd 00 00 00 00");

        Assert.That(MaxRenderResultFileNaming.NameByContent(path), Is.EqualTo(path));
    }

    [Test]
    public void ALandedResultIsFoundUnderItsRealExtensionTest()
    {
        WriteFile("result.webm", FFMPEG_VP9_WEBM);

        Assert.That(MaxRenderResultFileNaming.FindLanded(m_testDir, "result"), Is.EqualTo(Path.Combine(m_testDir, "result.webm")));
    }

    [Test]
    public void AnUnfinishedTransferDoesNotCountAsLandedTest()
    {
        // A temporary transfer file must not pass for a finished result, or it would never be retried.
        WriteFile("frame_0001.jpg.partial", BLENDER_JPEG);
        WriteFile("frame_0001.download", BLENDER_JPEG);

        Assert.That(MaxRenderResultFileNaming.FindLanded(m_testDir, "frame_0001"), Is.Null);
    }

    [Test]
    public void TheJobsChosenFormatDrivesTheExpectedExtensionTest()
    {
        var jobState = new MaxConnectedRenderJobState { RenderMode = "RenderFrames", ImageFormat = "TIFF" };

        Assert.That(MaxRenderResultFileNaming.ExpectedExtension(jobState), Is.EqualTo(".tif"));
    }

    [Test]
    public void TheChosenFormatSurvivesTheJobRecordTest()
    {
        // The result may be collected after a 3ds Max restart; the format has to come back with the job.
        var store = new MaxConnectedRenderJobStore(Path.Combine(m_testDir, "active-job.json"));
        store.Save(new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            RenderMode = "RenderVideo",
            ImageFormat = "PNG",
            VideoPreset = "webm-vp9",
            SubmittedUtc = DateTime.UtcNow
        });

        var restored = store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(restored!.VideoPreset, Is.EqualTo("webm-vp9"));
            Assert.That(MaxRenderResultFileNaming.ExpectedExtension(restored), Is.EqualTo(".webm"));
        });
    }

    #endregion

    #region Tools

    private string WriteFile(string name, string hexHeader)
    {
        var path = Path.Combine(m_testDir, name);
        File.WriteAllBytes(path, Hex(hexHeader).Concat(new byte[32]).ToArray());
        return path;
    }

    private static byte[] Hex(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(me => Convert.ToByte(me, 16)).ToArray();

    #endregion
}
