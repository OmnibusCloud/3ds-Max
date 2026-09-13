using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Models;

[TestFixture]
public sealed class MaxRenderStatusTests
{
    #region Active Job Tests

    [Test]
    public void RunningCarriesBothProgressAxesTest()
    {
        var status = MaxRenderStatus.Running(0.5d, 0.25d, 3, 12, MaxRenderStatus.UNIT_FRAMES);

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Running));
        Assert.That(status.Progress, Is.EqualTo(0.5d).Within(1e-9));
        Assert.That(status.ComputationProgress, Is.EqualTo(0.25d).Within(1e-9));
        Assert.That(status.FramesCompleted, Is.EqualTo(3));
        Assert.That(status.FramesTotal, Is.EqualTo(12));
        Assert.That(status.StatusLine, Is.EqualTo("Rendering 3/12"));
        Assert.That(status.IsActiveJob, Is.True);
        Assert.That(status.IsTerminal, Is.False);
        Assert.That(status.HasDeterminateProgress, Is.True);
        Assert.That(status.HasComputationProgress, Is.True);
    }

    [Test]
    public void RunningWithoutCountableUnitsReportsFarmPercentTest()
    {
        var status = MaxRenderStatus.Running(0.5d, 0.37d);

        Assert.That(status.StatusLine, Is.EqualTo("Rendering 37%"));
        Assert.That(status.UnitsTotal, Is.Null);
        Assert.That(status.FramesCompleted, Is.Null);
        Assert.That(status.HasComputationProgress, Is.True);
    }

    [Test]
    public void RunningWithoutDistributedDataHidesComputationBarTest()
    {
        // The coarse axis alone is no reason to draw a second bar at 0 — that is what made a running
        // render look parked. No distributed data yet means "no fine-grained progress", not "0%".
        var status = MaxRenderStatus.Running(0.5d);

        Assert.That(status.Progress, Is.EqualTo(0.5d).Within(1e-9));
        Assert.That(status.ComputationProgress, Is.Null);
        Assert.That(status.HasComputationProgress, Is.False);
        Assert.That(status.StatusLine, Is.EqualTo("Rendering…"));
    }

    [Test]
    public void RunningClampsBothAxesTest()
    {
        var status = MaxRenderStatus.Running(1.4d, -0.2d);

        Assert.That(status.Progress, Is.EqualTo(1d));
        Assert.That(status.ComputationProgress, Is.EqualTo(0d));
    }

    [Test]
    public void RunningIgnoresPartialUnitInformationTest()
    {
        var status = MaxRenderStatus.Running(0.5d, 0.5d, 2, 0, MaxRenderStatus.UNIT_FRAMES);

        Assert.That(status.UnitsCompleted, Is.Null);
        Assert.That(status.UnitsTotal, Is.Null);
        Assert.That(status.UnitName, Is.Empty);
        Assert.That(status.StatusLine, Is.EqualTo("Rendering 50%"));
    }

    [Test]
    public void FinalizingKeepsBothBarsFilledTest()
    {
        var status = MaxRenderStatus.Finalizing(0.5d, 1d);

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Finalizing));
        Assert.That(status.Progress, Is.EqualTo(0.5d).Within(1e-9));
        Assert.That(status.ComputationProgress, Is.EqualTo(1d).Within(1e-9));
        Assert.That(status.StatusLine, Is.EqualTo("Finalizing…"));
        Assert.That(status.IsActiveJob, Is.True);
    }

    [Test]
    public void UploadingClampsAndFormatsPercentTest()
    {
        var status = MaxRenderStatus.Uploading(0.5d);

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Uploading));
        Assert.That(status.StatusLine, Is.EqualTo("Uploading 50%"));
        Assert.That(status.HasDeterminateProgress, Is.True);
        Assert.That(status.IsActiveJob, Is.True);
        Assert.That(status.HasComputationProgress, Is.False);

        Assert.That(MaxRenderStatus.Uploading(1.5d).Progress, Is.EqualTo(1d));
        Assert.That(MaxRenderStatus.Uploading(-0.5d).Progress, Is.EqualTo(0d));
    }

    #endregion

    #region Terminal / Ready Tests

    [Test]
    public void TerminalStatesAreTerminalTest()
    {
        Assert.That(MaxRenderStatus.Completed().IsTerminal, Is.True);
        Assert.That(MaxRenderStatus.Failed("boom").IsTerminal, Is.True);
        Assert.That(MaxRenderStatus.Cancelled().IsTerminal, Is.True);
        Assert.That(MaxRenderStatus.Failed("boom").StatusLine, Is.EqualTo("boom"));
        Assert.That(MaxRenderStatus.Failed("").StatusLine, Is.EqualTo("Failed"));
    }

    [Test]
    public void ReadyStatusIsReadyAndIndeterminateTest()
    {
        var status = MaxRenderStatus.Ready();

        Assert.That(status.IsReady, Is.True);
        Assert.That(status.IsActiveJob, Is.False);
        Assert.That(status.IsTerminal, Is.False);
        Assert.That(status.Progress, Is.Null);
        Assert.That(status.HasDeterminateProgress, Is.False);
    }

    [Test]
    public void SignedOutAndBlockedAreNotReadyTest()
    {
        Assert.That(MaxRenderStatus.SignedOut().IsReady, Is.False);
        Assert.That(MaxRenderStatus.Blocked("save the scene").StatusLine, Is.EqualTo("save the scene"));
        Assert.That(MaxRenderStatus.Blocked("").StatusLine, Is.EqualTo("Blocked"));
    }

    #endregion
}
