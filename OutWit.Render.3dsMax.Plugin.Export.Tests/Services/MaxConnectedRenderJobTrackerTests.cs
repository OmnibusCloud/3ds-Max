using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using Serilog;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxConnectedRenderJobTrackerTests
{
    #region Constants

    private static readonly TimeSpan TEST_POLL_INTERVAL = TimeSpan.FromMilliseconds(10);

    private static readonly TimeSpan TEST_TIMEOUT = TimeSpan.FromSeconds(10);

    #endregion

    #region Fields

    private static readonly ILogger LOGGER = Serilog.Core.Logger.None;

    private string m_testDir = null!;

    private string m_recordPath = null!;

    private FakeMaxStatusBarService m_statusBar = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"max-job-tracker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
        m_recordPath = Path.Combine(m_testDir, "active-job.json");
        m_statusBar = new FakeMaxStatusBarService();
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

    #region Restore Tests

    [Test]
    public async Task RestoreResumesAJobTheDialogNeverSawTest()
    {
        // The whole point: the dialog that launched this job is gone (closed, or 3ds Max was restarted),
        // and the farm kept rendering. Tracking has to pick the job back up and land the result.
        var resultPath = CreateResultFile();
        var transport = new FakeMaxConnectedRenderSubmissionTransport()
            .EnqueueRefresh(job =>
            {
                job.ProgressPercent = 50d;
                job.DistributedProgressPercent = 40d;
                job.ServerStatus = "Processing";
            })
            .EnqueueRefresh(job =>
            {
                job.ProgressPercent = 100d;
                job.DistributedProgressPercent = 100d;
                job.ServerStatus = "Completed";
                job.IsCompleted = true;
                job.PrimaryArtifactPath = resultPath;
            });

        var store = new MaxConnectedRenderJobStore(m_recordPath);
        store.Save(CreateRunningJob());
        var tracker = CreateTracker(transport, store);

        Assert.That(await tracker.RestoreAsync(), Is.True);
        await WaitUntilAsync(() => tracker.Status.IsTerminal);

        Assert.That(tracker.Status.Phase, Is.EqualTo(MaxRenderPhase.Completed));
        Assert.That(tracker.JobState!.PrimaryArtifactPath, Is.EqualTo(resultPath));

        // The record survives a completed job: the dialog is opened AFTER the fact to collect it.
        Assert.That(store.Load(), Is.Not.Null);

        // And the host prompt line kept moving the whole time, with no dialog open.
        Assert.That(m_statusBar.Reports.Any(me => me.Phase == MaxRenderPhase.Running), Is.True);
        Assert.That(m_statusBar.Reports[^1].Phase, Is.EqualTo(MaxRenderPhase.Completed));
    }

    [Test]
    public async Task RestoreReportsTheFarmProgressAxisWhileRunningTest()
    {
        var transport = new FakeMaxConnectedRenderSubmissionTransport()
            .EnqueueRefresh(job =>
            {
                job.ProgressPercent = 50d;
                job.DistributedProgressPercent = 59.2d;
            })
            .EnqueueRefresh(job => job.IsCancelled = true);

        var store = new MaxConnectedRenderJobStore(m_recordPath);
        var jobState = CreateRunningJob();
        jobState.RenderMode = "RenderFrames";
        jobState.FrameStart = 1;
        jobState.FrameEnd = 240;
        store.Save(jobState);
        var tracker = CreateTracker(transport, store);

        await tracker.RestoreAsync();
        await WaitUntilAsync(() => tracker.Status.IsTerminal);

        var running = m_statusBar.Reports.LastOrDefault(me => me.Phase == MaxRenderPhase.Running);
        Assert.That(running, Is.Not.Null);
        Assert.That(running!.StatusLine, Is.EqualTo("Rendering 142/240"));
        Assert.That(running.Progress, Is.EqualTo(0.5d).Within(1e-9));
        Assert.That(running.ComputationProgress, Is.EqualTo(0.592d).Within(1e-9));
    }

    [Test]
    public async Task RestoreOfACompletedJobWithItsResultOnDiskAsksTheServerNothingTest()
    {
        var transport = new FakeMaxConnectedRenderSubmissionTransport();
        var store = new MaxConnectedRenderJobStore(m_recordPath);
        store.Save(CreateCompletedJob(CreateResultFile()));
        var tracker = CreateTracker(transport, store);

        Assert.That(await tracker.RestoreAsync(), Is.True);

        Assert.That(tracker.Status.Phase, Is.EqualTo(MaxRenderPhase.Completed));
        Assert.That(transport.RefreshCount, Is.Zero);
    }

    [Test]
    public async Task RestoreOfACompletedJobWithAMissingResultRefetchesItTest()
    {
        // Max died between "Completed" and the download, or the temp folder was cleaned: one refresh
        // re-downloads the blob, so the result is collectable again instead of lost.
        var resultPath = CreateResultFile();
        var transport = new FakeMaxConnectedRenderSubmissionTransport()
            .EnqueueRefresh(job => job.PrimaryArtifactPath = resultPath);

        var store = new MaxConnectedRenderJobStore(m_recordPath);
        store.Save(CreateCompletedJob(Path.Combine(m_testDir, "vanished.png")));
        var tracker = CreateTracker(transport, store);

        Assert.That(await tracker.RestoreAsync(), Is.True);

        Assert.That(transport.RefreshCount, Is.EqualTo(1));
        Assert.That(tracker.Status.Phase, Is.EqualTo(MaxRenderPhase.Completed));
        Assert.That(tracker.JobState!.PrimaryArtifactPath, Is.EqualTo(resultPath));
    }

    [Test]
    public async Task RestoreOfAFailedJobPresentsTheFailureWithoutPollingTest()
    {
        var transport = new FakeMaxConnectedRenderSubmissionTransport();
        var store = new MaxConnectedRenderJobStore(m_recordPath);
        var jobState = CreateRunningJob();
        jobState.IsFailed = true;
        jobState.ServerStatus = "Failed";
        jobState.StatusText = "OmnibusCloud job status: Failed. node lost";
        store.Save(jobState);
        var tracker = CreateTracker(transport, store);

        Assert.That(await tracker.RestoreAsync(), Is.True);

        Assert.That(tracker.Status.Phase, Is.EqualTo(MaxRenderPhase.Failed));
        Assert.That(transport.RefreshCount, Is.Zero);
    }

    [Test]
    public async Task RestoreDropsAStaleRecordTest()
    {
        var store = new MaxConnectedRenderJobStore(m_recordPath);
        var jobState = CreateCompletedJob(CreateResultFile());
        jobState.SubmittedUtc = DateTime.UtcNow.AddDays(-9);
        jobState.UpdatedUtc = DateTime.UtcNow.AddDays(-9);
        store.Save(jobState);
        var tracker = CreateTracker(new FakeMaxConnectedRenderSubmissionTransport(), store);

        Assert.That(await tracker.RestoreAsync(), Is.False);

        Assert.That(tracker.HasTrackedJob, Is.False);
        Assert.That(tracker.Status.IsReady, Is.True);
        Assert.That(store.Load(), Is.Null);
    }

    [Test]
    public async Task RestoreWithoutARecordTracksNothingTest()
    {
        var tracker = CreateTracker(new FakeMaxConnectedRenderSubmissionTransport(), new MaxConnectedRenderJobStore(m_recordPath));

        Assert.That(await tracker.RestoreAsync(), Is.False);
        Assert.That(tracker.HasTrackedJob, Is.False);
        Assert.That(tracker.Status.IsReady, Is.True);
    }

    #endregion

    #region Cancel / Clear Tests

    [Test]
    public async Task CancelStopsTheJobOnTheFarmTest()
    {
        var transport = new FakeMaxConnectedRenderSubmissionTransport()
            .EnqueueRefresh(job => job.DistributedProgressPercent = 10d);

        var store = new MaxConnectedRenderJobStore(m_recordPath);
        store.Save(CreateRunningJob());
        var tracker = CreateTracker(transport, store);
        await tracker.RestoreAsync();

        await tracker.RequestCancelAsync();
        await WaitUntilAsync(() => tracker.Status.IsTerminal);

        Assert.That(transport.CancelCount, Is.EqualTo(1));
        Assert.That(tracker.Status.Phase, Is.EqualTo(MaxRenderPhase.Cancelled));
    }

    [Test]
    public async Task ClearForgetsTheJobAndItsRecordTest()
    {
        var store = new MaxConnectedRenderJobStore(m_recordPath);
        store.Save(CreateCompletedJob(CreateResultFile()));
        var tracker = CreateTracker(new FakeMaxConnectedRenderSubmissionTransport(), store);
        await tracker.RestoreAsync();

        tracker.Clear();

        Assert.That(tracker.HasTrackedJob, Is.False);
        Assert.That(tracker.Status.IsReady, Is.True);
        Assert.That(store.Load(), Is.Null);
    }

    #endregion

    #region Tools

    private MaxConnectedRenderJobTracker CreateTracker(
        FakeMaxConnectedRenderSubmissionTransport transport,
        MaxConnectedRenderJobStore store)
    {
        var renderService = MaxSceneExportTestData.CreateConnectedRenderService(
            MaxSceneExportTestData.CreateMinimalValidSceneSnapshot(), transport);

        return new MaxConnectedRenderJobTracker(renderService, store, m_statusBar, LOGGER, TEST_POLL_INTERVAL);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TEST_TIMEOUT;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        Assert.Fail("The tracked job never reached a terminal state.");
    }

    private string CreateResultFile()
    {
        var path = Path.Combine(m_testDir, $"result-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "not really a png");
        return path;
    }

    private static MaxConnectedRenderJobState CreateRunningJob()
    {
        return new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            CloudUrl = "https://test.omnibuscloud.com",
            RenderMode = "RenderStill",
            ServerStatus = "Processing",
            StatusText = "OmnibusCloud job status: Processing.",
            ProgressPercent = 50d,
            DistributedProgressPercent = 20d,
            FrameStart = 1,
            FrameEnd = 1,
            SubmittedUtc = DateTime.UtcNow.AddMinutes(-3),
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-1)
        };
    }

    private static MaxConnectedRenderJobState CreateCompletedJob(string resultPath)
    {
        var jobState = CreateRunningJob();
        jobState.ProgressPercent = 100d;
        jobState.DistributedProgressPercent = 100d;
        jobState.ServerStatus = "Completed";
        jobState.IsCompleted = true;
        jobState.PrimaryArtifactPath = resultPath;
        return jobState;
    }

    #endregion
}
