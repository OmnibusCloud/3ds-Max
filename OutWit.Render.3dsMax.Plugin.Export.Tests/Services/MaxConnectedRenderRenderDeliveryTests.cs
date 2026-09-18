using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

/// <summary>
/// Render results land in the "Save to" folder under the scene's name. They used to stay in
/// %TEMP%\OmnibusCloudResults\&lt;job&gt; for good — the Render dialog had no "Save to", and the one in
/// Settings ▸ Output ("Defaults for Render and Export") never reached it.
/// </summary>
[TestFixture]
public sealed class MaxConnectedRenderRenderDeliveryTests
{
    #region Fields

    private string m_downloadDir = null!;

    private string m_saveToDir = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        // The delivery only moves results out of the REAL download area, so the tests download there.
        m_downloadDir = MaxRenderResultFileNaming.DownloadFolder($"test_{Guid.NewGuid():N}");
        m_saveToDir = Path.Combine(Path.GetTempPath(), $"max-render-save-to-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_downloadDir);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var folder in new[] { m_downloadDir, m_saveToDir })
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            catch
            {
                // A locked temp file must not fail the suite.
            }
        }
    }

    #region Naming Tests

    [TestCase("RenderStill", 26, 26, "robby_vs_fly_0026")]
    [TestCase("RenderStillTiled", 26, 26, "robby_vs_fly_0026")]
    [TestCase("RenderVideo", 1, 340, "robby_vs_fly_0001-0340")]
    public void AResultIsNamedAfterTheSceneAndItsFramesTest(string renderMode, int frameStart, int frameEnd, string expected)
    {
        var jobState = new MaxConnectedRenderJobState { RenderMode = renderMode, ResultName = "robby_vs_fly", FrameStart = frameStart, FrameEnd = frameEnd };

        Assert.That(MaxRenderResultFileNaming.DeliveredFileStem(jobState), Is.EqualTo(expected));
    }

    [Test]
    public void ASequenceGetsItsOwnFolderTest()
    {
        var jobState = new MaxConnectedRenderJobState { RenderMode = "RenderFrames", ResultName = "robby_vs_fly", FrameStart = 1, FrameEnd = 340 };

        Assert.That(MaxRenderResultFileNaming.DeliveredSequenceFolderName(jobState), Is.EqualTo("robby_vs_fly_0001-0340"));
    }

    [TestCase("frame_0005.png", "robby_vs_fly_0005.png")]
    [TestCase("frame_0120.exr", "robby_vs_fly_0120.exr")]
    [TestCase("result.png", null)]
    [TestCase("frame_.png", null)]
    [TestCase("frame_12a.png", null)]
    public void ADownloadedFrameTakesTheScenesNameTest(string downloaded, string? expected)
    {
        Assert.That(MaxRenderResultFileNaming.DeliveredFrameFileName(downloaded, "robby_vs_fly"), Is.EqualTo(expected));
    }

    [Test]
    public void TheDownloadAreaIsRecognisedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MaxRenderResultFileNaming.IsInDownloadArea(Path.Combine(m_downloadDir, "result.jpg")), Is.True);
            Assert.That(MaxRenderResultFileNaming.IsInDownloadArea(Path.Combine(m_saveToDir, "robby_vs_fly_0001.jpg")), Is.False);
            Assert.That(MaxRenderResultFileNaming.IsInDownloadArea(MaxRenderResultFileNaming.DownloadRoot + "Evil\\x.jpg"), Is.False);
            Assert.That(MaxRenderResultFileNaming.IsInDownloadArea(null), Is.False);
        });
    }

    #endregion

    #region Delivery Tests

    [Test]
    public void AStillLandsInSaveToUnderTheSceneAndFrameTest()
    {
        var jobState = CreateCompletedJob("RenderStill", 26, 26, ("result.jpg", "the jpeg"));

        var result = new MaxConnectedRenderDownloadService().DeliverRender(jobState);

        var expected = Path.Combine(m_saveToDir, "robby_vs_fly_0026.jpg");
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.StatusText);
            Assert.That(jobState.PrimaryArtifactPath, Is.EqualTo(expected));
            Assert.That(File.ReadAllText(expected), Is.EqualTo("the jpeg"));
            Assert.That(Directory.Exists(m_downloadDir), Is.False);
        });
    }

    [Test]
    public void AVideoLandsUnderItsFrameRangeTest()
    {
        var jobState = CreateCompletedJob("RenderVideo", 1, 340, ("result.webm", "the video"));

        new MaxConnectedRenderDownloadService().DeliverRender(jobState);

        Assert.That(jobState.PrimaryArtifactPath, Is.EqualTo(Path.Combine(m_saveToDir, "robby_vs_fly_0001-0340.webm")));
    }

    [Test]
    public void ASequenceLandsInItsOwnSubfolderTest()
    {
        var jobState = CreateCompletedJob("RenderFrames", 1, 3,
            ("frame_0001.exr", "f1"), ("frame_0002.exr", "f2"), ("frame_0003.exr", "f3"));

        var result = new MaxConnectedRenderDownloadService().DeliverRender(jobState);

        var folder = Path.Combine(m_saveToDir, "robby_vs_fly_0001-0003");
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.StatusText);
            Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName),
                Is.EquivalentTo(new[] { "robby_vs_fly_0001.exr", "robby_vs_fly_0002.exr", "robby_vs_fly_0003.exr" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "robby_vs_fly_0002.exr")), Is.EqualTo("f2"));
            Assert.That(jobState.PrimaryArtifactPath, Is.EqualTo(Path.Combine(folder, "robby_vs_fly_0001.exr")));
            Assert.That(Directory.Exists(m_downloadDir), Is.False);
        });
    }

    [Test]
    public void EarlierResultsAreNeverOverwrittenTest()
    {
        Directory.CreateDirectory(Path.Combine(m_saveToDir, "robby_vs_fly_0001-0002"));
        File.WriteAllText(Path.Combine(m_saveToDir, "robby_vs_fly_0026.jpg"), "yesterday's still");

        var still = CreateCompletedJob("RenderStill", 26, 26, ("result.jpg", "today's still"));
        new MaxConnectedRenderDownloadService().DeliverRender(still);

        var sequenceDownload = MaxRenderResultFileNaming.DownloadFolder($"test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sequenceDownload);
            File.WriteAllText(Path.Combine(sequenceDownload, "frame_0001.png"), "a");
            var sequence = new MaxConnectedRenderJobState
            {
                RenderMode = "RenderFrames", ResultName = "robby_vs_fly", FrameStart = 1, FrameEnd = 2,
                IsCompleted = true, ResultFolder = m_saveToDir,
                PrimaryArtifactPath = Path.Combine(sequenceDownload, "frame_0001.png")
            };
            new MaxConnectedRenderDownloadService().DeliverRender(sequence);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(m_saveToDir, "robby_vs_fly_0026.jpg")), Is.EqualTo("yesterday's still"));
                Assert.That(still.PrimaryArtifactPath, Is.EqualTo(Path.Combine(m_saveToDir, "robby_vs_fly_0026 (2).jpg")));
                Assert.That(sequence.PrimaryArtifactPath, Is.EqualTo(Path.Combine(m_saveToDir, "robby_vs_fly_0001-0002 (2)", "robby_vs_fly_0001.png")));
            });
        }
        finally
        {
            if (Directory.Exists(sequenceDownload))
                Directory.Delete(sequenceDownload, true);
        }
    }

    [Test]
    public void AJobWithNoChosenFolderStaysWhereItWasDownloadedTest()
    {
        // Records written before "Save to" reached renders, and the batch / smoke flows.
        var jobState = CreateCompletedJob("RenderStill", 1, 1, ("result.png", "x"));
        jobState.ResultFolder = string.Empty;
        var downloaded = jobState.PrimaryArtifactPath;

        var result = new MaxConnectedRenderDownloadService().DeliverRender(jobState);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(jobState.PrimaryArtifactPath, Is.EqualTo(downloaded));
            Assert.That(File.Exists(downloaded), Is.True);
        });
    }

    [Test]
    public void AnAlreadyDeliveredResultIsLeftAloneTest()
    {
        var jobState = CreateCompletedJob("RenderStill", 26, 26, ("result.jpg", "x"));
        new MaxConnectedRenderDownloadService().DeliverRender(jobState);
        var delivered = jobState.PrimaryArtifactPath;

        var again = new MaxConnectedRenderDownloadService().DeliverRender(jobState);

        Assert.Multiple(() =>
        {
            Assert.That(again.IsSuccess, Is.False);
            Assert.That(jobState.PrimaryArtifactPath, Is.EqualTo(delivered));
            Assert.That(File.Exists(delivered), Is.True);
        });
    }

    #endregion

    #region Tools

    private MaxConnectedRenderJobState CreateCompletedJob(string renderMode, int frameStart, int frameEnd, params (string Name, string Contents)[] files)
    {
        foreach (var (name, contents) in files)
            File.WriteAllText(Path.Combine(m_downloadDir, name), contents);

        return new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            RenderMode = renderMode,
            ServerStatus = "Completed",
            IsCompleted = true,
            FrameStart = frameStart,
            FrameEnd = frameEnd,
            ResultFolder = m_saveToDir,
            ResultName = "robby_vs_fly",
            PrimaryArtifactPath = Path.Combine(m_downloadDir, files[0].Name)
        };
    }

    #endregion
}
