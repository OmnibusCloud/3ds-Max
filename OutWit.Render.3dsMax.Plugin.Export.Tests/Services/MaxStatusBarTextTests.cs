using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxStatusBarTextTests
{
    #region Format Tests

    [Test]
    public void FormatPrefixesRunningStatusLineTest()
    {
        var text = MaxStatusBarText.Format(MaxRenderStatus.Running(0.5d, 0.592d, 142, 240, MaxRenderStatus.UNIT_FRAMES));

        Assert.That(text, Is.EqualTo("OmnibusCloud · Rendering 142/240"));
    }

    [Test]
    public void FormatPrefixesUploadingPercentTest()
    {
        var text = MaxStatusBarText.Format(MaxRenderStatus.Uploading(0.38d));

        Assert.That(text, Is.EqualTo("OmnibusCloud · Uploading 38%"));
    }

    [Test]
    public void FormatPrefixesTerminalStatusTest()
    {
        var text = MaxStatusBarText.Format(MaxRenderStatus.Completed());

        Assert.That(text, Is.EqualTo("OmnibusCloud · Completed"));
    }

    [Test]
    public void FormatFallsBackToPhaseWhenStatusLineEmptyTest()
    {
        var status = new MaxRenderStatus { Phase = MaxRenderPhase.Running, StatusLine = string.Empty };

        var text = MaxStatusBarText.Format(status);

        Assert.That(text, Is.EqualTo("OmnibusCloud · Running"));
    }

    #endregion

    #region Null Service Tests

    [Test]
    public void NullServiceReportAndClearDoNotThrowTest()
    {
        var service = MaxStatusBarServiceNull.Instance;

        Assert.DoesNotThrow(() =>
        {
            service.Report(MaxRenderStatus.Running(0.5d, 0.25d, 1, 4, MaxRenderStatus.UNIT_TILES));
            service.Clear();
        });
    }

    #endregion
}
