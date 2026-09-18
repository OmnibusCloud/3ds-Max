using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

/// <summary>
/// The Export dialog's "Save to" folder and the launch package's lifetime: the .blend used to stay in
/// %TEMP%\OmnibusCloudResults\&lt;job&gt;\result.blend while only the launch package (tens of MB of scene
/// payload) landed in the folder the artist chose.
/// </summary>
[TestFixture]
public sealed class MaxConnectedRenderResultDeliveryTests
{
    #region Fields

    private string m_testDir = null!;

    private string m_downloadDir = null!;

    private string m_saveToDir = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"max-result-delivery-{Guid.NewGuid():N}");
        m_downloadDir = Path.Combine(m_testDir, "OmnibusCloudResults", "job");
        m_saveToDir = Path.Combine(m_testDir, "Desktop");
        Directory.CreateDirectory(m_downloadDir);
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

    #region Deliver Tests

    [Test]
    public void TheBlendLandsInTheChosenFolderUnderTheSceneNameTest()
    {
        var jobState = CreateCompletedExport("the blend bytes");
        var downloadedPath = jobState.PrimaryArtifactPath;

        var result = new MaxConnectedRenderDownloadService().Deliver(jobState, m_saveToDir, "robby_vs_fly");

        var expected = Path.Combine(m_saveToDir, "robby_vs_fly.blend");
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.DownloadedFilePath, Is.EqualTo(expected));
            Assert.That(File.ReadAllText(expected), Is.EqualTo("the blend bytes"));

            // Moved, not copied: nothing is left behind in %TEMP% (not even the emptied per-job
            // folder), and the job points at the new home.
            Assert.That(File.Exists(downloadedPath), Is.False);
            Assert.That(Directory.Exists(m_downloadDir), Is.False);
            Assert.That(jobState.PrimaryArtifactPath, Is.EqualTo(expected));
        });
    }

    [Test]
    public void ADownloadFolderThatStillHoldsFilesIsKeptTest()
    {
        var jobState = CreateCompletedExport("x");
        File.WriteAllText(Path.Combine(m_downloadDir, "result.blend1"), "Blender's own backup");

        new MaxConnectedRenderDownloadService().Deliver(jobState, m_saveToDir, "scene");

        Assert.That(File.Exists(Path.Combine(m_downloadDir, "result.blend1")), Is.True);
    }

    [Test]
    public void AnExistingFileIsNeverOverwrittenTest()
    {
        // Exporting the same scene twice must not silently replace the file the artist may have
        // already opened and edited in Blender.
        Directory.CreateDirectory(m_saveToDir);
        File.WriteAllText(Path.Combine(m_saveToDir, "robby_vs_fly.blend"), "yesterday's edits");
        File.WriteAllText(Path.Combine(m_saveToDir, "robby_vs_fly (2).blend"), "an older copy");

        var result = new MaxConnectedRenderDownloadService().Deliver(CreateCompletedExport("fresh"), m_saveToDir, "robby_vs_fly");

        Assert.Multiple(() =>
        {
            Assert.That(result.DownloadedFilePath, Is.EqualTo(Path.Combine(m_saveToDir, "robby_vs_fly (3).blend")));
            Assert.That(File.ReadAllText(Path.Combine(m_saveToDir, "robby_vs_fly.blend")), Is.EqualTo("yesterday's edits"));
            Assert.That(File.ReadAllText(Path.Combine(m_saveToDir, "robby_vs_fly (2).blend")), Is.EqualTo("an older copy"));
        });
    }

    [Test]
    public void AMissingFolderIsCreatedTest()
    {
        var nested = Path.Combine(m_saveToDir, "exports", "blend");

        var result = new MaxConnectedRenderDownloadService().Deliver(CreateCompletedExport("x"), nested, "scene");

        Assert.That(File.Exists(Path.Combine(nested, "scene.blend")), Is.True, result.StatusText);
    }

    [Test]
    public void AnUnfinishedJobDeliversNothingTest()
    {
        // Before completion PrimaryArtifactPath is the INPUT launch archive — it must never be moved
        // into the artist's folder as if it were the result.
        var archive = Path.Combine(m_testDir, "max-launch-20260918-000000-abc.zip");
        File.WriteAllText(archive, "the scene payload");
        var jobState = new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            RenderMode = "ExportBlend",
            PackageArchivePath = archive,
            PrimaryArtifactPath = archive,
            IsCompleted = false
        };

        var result = new MaxConnectedRenderDownloadService().Deliver(jobState, m_saveToDir, "scene");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(File.Exists(archive), Is.True);
            Assert.That(Directory.Exists(m_saveToDir), Is.False);
        });
    }

    [Test]
    public void AnUnwritableFolderLeavesTheFileWhereItWasTest()
    {
        // A FILE where the folder should be: Directory.CreateDirectory fails. The export still
        // succeeded, so the result must stay reachable at its download path.
        Directory.CreateDirectory(m_testDir);
        var blocker = Path.Combine(m_testDir, "not-a-folder");
        File.WriteAllText(blocker, "occupied");
        var jobState = CreateCompletedExport("keep me");
        var downloadedPath = jobState.PrimaryArtifactPath;

        var result = new MaxConnectedRenderDownloadService().Deliver(jobState, blocker, "scene");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.DownloadedFilePath, Is.EqualTo(downloadedPath));
            Assert.That(File.ReadAllText(downloadedPath), Is.EqualTo("keep me"));
            Assert.That(jobState.PrimaryArtifactPath, Is.EqualTo(downloadedPath));
            Assert.That(result.Diagnostics.Any(me => me.Severity == MaxSceneDiagnosticSeverity.Warning), Is.True);
        });
    }

    [TestCase("robby_vs_fly", "robby_vs_fly")]
    [TestCase("  shot 010  ", "shot 010")]
    [TestCase("a:b*c?", "a_b_c_")]
    [TestCase("", "scene")]
    [TestCase(null, "scene")]
    [TestCase("...", "scene")]
    public void TheFileStemIsSafeForTheFileSystemTest(string? raw, string expected)
    {
        Assert.That(MaxConnectedRenderDownloadService.SanitizeFileStem(raw), Is.EqualTo(expected));
    }

    #endregion

    #region Discard Tests

    [Test]
    public void TheLaunchPackageIsDiscardedAfterTheExportTest()
    {
        var packageFolder = Path.Combine(m_testDir, "max-launch-20260918-151950-fb96ed390134439dbf7dd7ce551f1bdb");
        Directory.CreateDirectory(packageFolder);
        File.WriteAllText(Path.Combine(packageFolder, "launch-request.json"), "{}");
        var packageArchive = packageFolder + ".zip";
        File.WriteAllText(packageArchive, "payload");

        var discarded = MaxSceneLaunchPreparationService.Discard(packageFolder, packageArchive);

        Assert.Multiple(() =>
        {
            Assert.That(discarded, Is.True);
            Assert.That(Directory.Exists(packageFolder), Is.False);
            Assert.That(File.Exists(packageArchive), Is.False);
        });
    }

    [Test]
    public void DiscardNeverTouchesAnythingItDidNotCreateTest()
    {
        // A stale or corrupt record must never turn into deleting the artist's own folder.
        var foreignFolder = Path.Combine(m_testDir, "Desktop");
        Directory.CreateDirectory(foreignFolder);
        File.WriteAllText(Path.Combine(foreignFolder, "important.max"), "work");
        var foreignArchive = Path.Combine(m_testDir, "backup.zip");
        File.WriteAllText(foreignArchive, "work");
        var packageNamedButNotZip = Path.Combine(m_testDir, "max-launch-20260918-000000-abc.blend");
        File.WriteAllText(packageNamedButNotZip, "work");

        MaxSceneLaunchPreparationService.Discard(foreignFolder, foreignArchive);
        MaxSceneLaunchPreparationService.Discard(null, packageNamedButNotZip);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(foreignFolder, "important.max")), Is.True);
            Assert.That(File.Exists(foreignArchive), Is.True);
            Assert.That(File.Exists(packageNamedButNotZip), Is.True);
        });
    }

    #endregion

    #region Tools

    private MaxConnectedRenderJobState CreateCompletedExport(string contents)
    {
        var downloadedPath = Path.Combine(m_downloadDir, "result.blend");
        File.WriteAllText(downloadedPath, contents);

        return new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            RenderMode = "ExportBlend",
            ServerStatus = "Completed",
            IsCompleted = true,
            PackageArchivePath = Path.Combine(m_testDir, "max-launch-20260918-000000-abc.zip"),
            PrimaryArtifactPath = downloadedPath
        };
    }

    #endregion
}
