using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxConnectedRenderJobStoreTests
{
    #region Fields

    private string m_testDir = null!;

    private string m_recordPath = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"max-job-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
        m_recordPath = Path.Combine(m_testDir, "nested", "active-job.json");
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

    #region Round Trip Tests

    [Test]
    public void SaveThenLoadCarriesTheJobHandleTest()
    {
        var store = new MaxConnectedRenderJobStore(m_recordPath);
        var jobState = CreateJob();

        store.Save(jobState);
        var restored = store.Load();

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.JobId, Is.EqualTo(jobState.JobId));
        Assert.That(restored.CloudUrl, Is.EqualTo(jobState.CloudUrl));
        Assert.That(restored.RenderMode, Is.EqualTo("RenderFrames"));
        Assert.That(restored.FrameStart, Is.EqualTo(1));
        Assert.That(restored.FrameEnd, Is.EqualTo(240));
        Assert.That(restored.DistributedProgressPercent, Is.EqualTo(37d).Within(1e-9));
        Assert.That(restored.ServerStatus, Is.EqualTo("Processing"));
        Assert.That(restored.PrimaryArtifactPath, Is.EqualTo(jobState.PrimaryArtifactPath));
        Assert.That(restored.ResultBlobId, Is.EqualTo(jobState.ResultBlobId));
        Assert.That(restored.ResultFrameBlobIds, Is.EqualTo(jobState.ResultFrameBlobIds));
        Assert.That(restored.Diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void SaveCreatesTheRecordFolderTest()
    {
        var store = new MaxConnectedRenderJobStore(m_recordPath);

        store.Save(CreateJob());

        Assert.That(File.Exists(m_recordPath), Is.True);
    }

    [Test]
    public void ClearRemovesTheRecordTest()
    {
        var store = new MaxConnectedRenderJobStore(m_recordPath);
        store.Save(CreateJob());

        store.Clear();

        Assert.That(store.Load(), Is.Null);
        Assert.That(File.Exists(m_recordPath), Is.False);
    }

    [Test]
    public void ClearOnMissingRecordDoesNotThrowTest()
    {
        var store = new MaxConnectedRenderJobStore(m_recordPath);

        Assert.DoesNotThrow(() => store.Clear());
        Assert.That(store.Load(), Is.Null);
    }

    #endregion

    #region Rejection Tests

    [Test]
    public void JobThatNeverReachedTheFarmIsNotPersistedTest()
    {
        // A blocked preflight / failed submission is not a handle to anything: restoring it would just
        // re-present an error nobody can act on.
        var store = new MaxConnectedRenderJobStore(m_recordPath);
        var jobState = CreateJob();
        jobState.JobId = $"blocked-{Guid.NewGuid():N}";

        store.Save(jobState);

        Assert.That(File.Exists(m_recordPath), Is.False);
        Assert.That(store.Load(), Is.Null);
    }

    [Test]
    public void CorruptRecordLoadsAsNothingTrackedTest()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(m_recordPath)!);
        File.WriteAllText(m_recordPath, "{ this is not json");

        Assert.That(new MaxConnectedRenderJobStore(m_recordPath).Load(), Is.Null);
    }

    #endregion

    #region Trim Tests

    [Test]
    public void DiagnosticsAreCappedKeepingTheSubmissionHeadTest()
    {
        var jobState = CreateJob();
        jobState.Diagnostics.Clear();
        for (var index = 0; index < 120; index++)
        {
            jobState.Diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = $"line {index}"
            });
        }

        MaxConnectedRenderJobStore.TrimDiagnostics(jobState);

        Assert.That(jobState.Diagnostics, Has.Count.EqualTo(40));
        Assert.That(jobState.Diagnostics[0].Message, Is.EqualTo("line 0"));
        Assert.That(jobState.Diagnostics[^1].Message, Is.EqualTo("line 119"));
    }

    [Test]
    public void ShortDiagnosticsAreLeftAloneTest()
    {
        var jobState = CreateJob();

        MaxConnectedRenderJobStore.TrimDiagnostics(jobState);

        Assert.That(jobState.Diagnostics, Has.Count.EqualTo(1));
    }

    #endregion

    #region Default Path Tests

    [Test]
    public void DefaultRecordLivesNextToTheSessionFileTest()
    {
        var path = MaxConnectedRenderJobStore.ResolveDefaultFilePath();

        Assert.That(Path.GetFileName(path), Is.EqualTo("active-job.json"));
        Assert.That(path, Does.Contain(Path.Combine("OmnibusCloud", "3dsMax")));
    }

    #endregion

    #region Tools

    private static MaxConnectedRenderJobState CreateJob()
    {
        return new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            CloudUrl = "https://test.omnibuscloud.com",
            RenderMode = "RenderFrames",
            ServerStatus = "Processing",
            StatusText = "OmnibusCloud job status: Processing.",
            ProgressPercent = 50d,
            DistributedProgressPercent = 37d,
            FrameStart = 1,
            FrameEnd = 240,
            SubmittedUtc = DateTime.UtcNow.AddMinutes(-7),
            UpdatedUtc = DateTime.UtcNow,
            PackageFolderPath = Path.Combine(Path.GetTempPath(), "package"),
            PrimaryArtifactPath = Path.Combine(Path.GetTempPath(), "result.png"),
            ResultBlobId = Guid.NewGuid(),
            ResultFrameBlobIds = [Guid.NewGuid(), Guid.NewGuid()],
            Diagnostics =
            [
                new MaxSceneDiagnosticItem { Severity = MaxSceneDiagnosticSeverity.Info, Message = "Submitted job" }
            ]
        };
    }

    #endregion
}
