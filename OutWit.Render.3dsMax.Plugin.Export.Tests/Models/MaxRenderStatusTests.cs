using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Models;

[TestFixture]
public sealed class MaxRenderStatusTests
{
    #region Active Job Tests

    [Test]
    public void RunningCarriesFramesAndFractionTest()
    {
        var status = MaxRenderStatus.Running(3, 12);

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Running));
        Assert.That(status.FramesCompleted, Is.EqualTo(3));
        Assert.That(status.FramesTotal, Is.EqualTo(12));
        Assert.That(status.Progress, Is.EqualTo(0.25d).Within(1e-9));
        Assert.That(status.StatusLine, Is.EqualTo("Rendering 3/12"));
        Assert.That(status.IsActiveJob, Is.True);
        Assert.That(status.IsTerminal, Is.False);
        Assert.That(status.HasDeterminateProgress, Is.True);
    }

    [Test]
    public void RunningWithoutTotalIsIndeterminateTest()
    {
        var status = MaxRenderStatus.Running(0, 0);

        Assert.That(status.Progress, Is.Null);
        Assert.That(status.StatusLine, Is.EqualTo("Rendering…"));
        Assert.That(status.HasDeterminateProgress, Is.False);
    }

    [Test]
    public void UploadingClampsAndFormatsPercentTest()
    {
        var status = MaxRenderStatus.Uploading(0.5d);

        Assert.That(status.Phase, Is.EqualTo(MaxRenderPhase.Uploading));
        Assert.That(status.StatusLine, Is.EqualTo("Uploading 50%"));
        Assert.That(status.HasDeterminateProgress, Is.True);
        Assert.That(status.IsActiveJob, Is.True);

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
