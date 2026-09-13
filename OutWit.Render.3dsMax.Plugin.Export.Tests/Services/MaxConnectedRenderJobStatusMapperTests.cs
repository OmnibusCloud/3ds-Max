using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxConnectedRenderJobStatusMapperTests
{
    #region Progress Axis Tests

    [Test]
    public void MapKeepsBothServerProgressAxesApartTest()
    {
        // The regression this guards: only the coarse axis was read, and it parks at 50% for the whole
        // distributed render — the dialog and the prompt line looked frozen from the first frame to the
        // last while the farm was working.
        var status = MaxConnectedRenderJobStatusMapper.Map(CreateJob(progressPercent: 50d, distributedPercent: 37d));

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Running));
        Assert.That(status.Progress, Is.EqualTo(0.5d).Within(1e-9));
        Assert.That(status.ComputationProgress, Is.EqualTo(0.37d).Within(1e-9));
        Assert.That(status.StatusLine, Is.EqualTo("Rendering 37%"));
    }

    [Test]
    public void MapCountsFramesForAFrameSequenceTest()
    {
        var jobState = CreateJob(progressPercent: 50d, distributedPercent: 59.2d);
        jobState.RenderMode = "RenderFrames";
        jobState.FrameStart = 1;
        jobState.FrameEnd = 240;

        var status = MaxConnectedRenderJobStatusMapper.Map(jobState);

        Assert.That(status.UnitsCompleted, Is.EqualTo(142));
        Assert.That(status.UnitsTotal, Is.EqualTo(240));
        Assert.That(status.UnitName, Is.EqualTo(MaxRenderStatus.UNIT_FRAMES));
        Assert.That(status.StatusLine, Is.EqualTo("Rendering 142/240"));
    }

    [Test]
    public void MapFloorsTheUnitCounterTest()
    {
        // Rounding up would announce the last frame before the farm reported it.
        var jobState = CreateJob(progressPercent: 50d, distributedPercent: 99.6d);
        jobState.RenderMode = "RenderFrames";
        jobState.FrameStart = 1;
        jobState.FrameEnd = 240;

        var status = MaxConnectedRenderJobStatusMapper.Map(jobState);

        Assert.That(status.UnitsCompleted, Is.EqualTo(239));
    }

    [Test]
    public void MapCountsTilesForATiledStillTest()
    {
        var jobState = CreateJob(progressPercent: 50d, distributedPercent: 50d);
        jobState.RenderMode = "RenderStillTiled";
        jobState.TileCount = 4;

        var status = MaxConnectedRenderJobStatusMapper.Map(jobState);

        Assert.That(status.UnitName, Is.EqualTo(MaxRenderStatus.UNIT_TILES));
        Assert.That(status.StatusLine, Is.EqualTo("Rendering 2/4"));
    }

    [Test]
    public void MapLeavesAPlainStillUncountedTest()
    {
        var status = MaxConnectedRenderJobStatusMapper.Map(CreateJob(progressPercent: 50d, distributedPercent: 42d));

        Assert.That(status.UnitsTotal, Is.Null);
        Assert.That(status.StatusLine, Is.EqualTo("Rendering 42%"));
    }

    [Test]
    public void MapHidesTheComputationAxisBeforeAnyDistributedWorkTest()
    {
        var status = MaxConnectedRenderJobStatusMapper.Map(CreateJob(progressPercent: 50d, distributedPercent: 0d));

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Running));
        Assert.That(status.ComputationProgress, Is.Null);
        Assert.That(status.StatusLine, Is.EqualTo("Rendering…"));
    }

    #endregion

    #region Phase Tests

    [Test]
    public void MapReportsSubmittingBeforeTheEngineMovesTest()
    {
        var status = MaxConnectedRenderJobStatusMapper.Map(CreateJob(progressPercent: 5d, distributedPercent: 0d));

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Submitting));
    }

    [Test]
    public void MapReportsFinalizingWhenTheFarmWorkIsDoneTest()
    {
        // Distributed work complete while the coarse axis is still mid-script: the frames are rendered
        // and the result is being stitched / encoded / uploaded.
        var status = MaxConnectedRenderJobStatusMapper.Map(CreateJob(progressPercent: 50d, distributedPercent: 100d));

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Finalizing));
        Assert.That(status.ComputationProgress, Is.EqualTo(1d).Within(1e-9));
    }

    [Test]
    public void MapReportsFinalizingNearTheEndOfTheCoarseAxisTest()
    {
        var status = MaxConnectedRenderJobStatusMapper.Map(CreateJob(progressPercent: 99.5d, distributedPercent: 80d));

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Finalizing));
    }

    [Test]
    public void MapReportsCancellingWhileTheCancelIsInFlightTest()
    {
        var status = MaxConnectedRenderJobStatusMapper.Map(
            CreateJob(progressPercent: 50d, distributedPercent: 40d), cancelRequested: true);

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Cancelling));
    }

    [Test]
    public void MapReportsTerminalStatesFromTheServerTest()
    {
        var completed = CreateJob(progressPercent: 100d, distributedPercent: 100d);
        completed.IsCompleted = true;

        var cancelled = CreateJob(progressPercent: 40d, distributedPercent: 20d);
        cancelled.IsCancelled = true;

        var failed = CreateJob(progressPercent: 40d, distributedPercent: 20d);
        failed.IsFailed = true;
        failed.StatusText = "OmnibusCloud job status: Failed. node lost";

        Assert.That(MaxConnectedRenderJobStatusMapper.Map(completed).Phase, Is.EqualTo(MaxRenderPhase.Completed));
        Assert.That(MaxConnectedRenderJobStatusMapper.Map(cancelled).Phase, Is.EqualTo(MaxRenderPhase.Cancelled));
        Assert.That(MaxConnectedRenderJobStatusMapper.Map(failed).Phase, Is.EqualTo(MaxRenderPhase.Failed));
        Assert.That(MaxConnectedRenderJobStatusMapper.Map(failed).StatusLine, Is.EqualTo("OmnibusCloud job status: Failed. node lost"));
    }

    [Test]
    public void MapIgnoresCancelRequestOnceTheServerIsTerminalTest()
    {
        var jobState = CreateJob(progressPercent: 100d, distributedPercent: 100d);
        jobState.IsCompleted = true;

        var status = MaxConnectedRenderJobStatusMapper.Map(jobState, cancelRequested: true);

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Completed));
    }

    #endregion

    #region Failure Detection Tests

    [Test]
    public void MapTreatsATransientRefreshFailureAsStillRunningTest()
    {
        // "Job refresh failed." is a DROPPED POLL, not a dead render. Sniffing it as a failure ended the
        // tracking while the farm kept rendering — and the result was never collected.
        var jobState = CreateJob(progressPercent: 50d, distributedPercent: 40d);
        jobState.StatusText = "Job refresh failed.";

        var status = MaxConnectedRenderJobStatusMapper.Map(jobState);

        Assert.That(MaxConnectedRenderJobStatusMapper.HasFailed(jobState), Is.False);
        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Running));
    }

    [Test]
    public void MapReportsFailureForALaunchThatNeverReachedTheFarmTest()
    {
        var jobState = CreateJob(progressPercent: 0d, distributedPercent: 0d);
        jobState.JobId = $"blocked-{Guid.NewGuid():N}";
        jobState.StatusText = "Connected render submission failed. Select a project or a render group first.";

        Assert.That(MaxConnectedRenderJobStatusMapper.HasFailed(jobState), Is.True);
        Assert.That(MaxConnectedRenderJobStatusMapper.Map(jobState).Phase, Is.EqualTo(MaxRenderPhase.Failed));
    }

    #endregion

    #region Tools

    private static MaxConnectedRenderJobState CreateJob(double progressPercent, double distributedPercent)
    {
        return new MaxConnectedRenderJobState
        {
            JobId = Guid.NewGuid().ToString("D"),
            CloudUrl = "https://test.omnibuscloud.com",
            RenderMode = "RenderStill",
            ServerStatus = "Processing",
            StatusText = "OmnibusCloud job status: Processing.",
            ProgressPercent = progressPercent,
            DistributedProgressPercent = distributedPercent,
            FrameStart = 1,
            FrameEnd = 1,
            SubmittedUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedUtc = DateTime.UtcNow
        };
    }

    #endregion
}
