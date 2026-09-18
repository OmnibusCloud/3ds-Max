using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

[TestFixture]
public sealed class MaxStillFrameResolverTests
{
    #region Resolve Tests

    [TestCase(26, 1, 100, 26)]
    [TestCase(1, 1, 100, 1)]
    [TestCase(100, 1, 100, 100)]
    public void AFrameInsideTheRangeIsKeptTest(int frame, int rangeStart, int rangeEnd, int expected)
    {
        // The live request: scrub to frame 26 of a 1-100 animation and render THAT frame. A still used
        // to render the range start whatever the slider showed.
        Assert.That(MaxStillFrameResolver.Resolve(frame, rangeStart, rangeEnd), Is.EqualTo(expected));
    }

    [TestCase(0, 1, 100, 1)]
    [TestCase(-5, 1, 100, 1)]
    [TestCase(250, 1, 100, 100)]
    public void AFrameOutsideTheRangeIsClampedToItsNearestEndTest(int frame, int rangeStart, int rangeEnd, int expected)
    {
        // Frame 0 is the everyday case: Max timelines start at 0, while the scene range the capture
        // samples starts at 1.
        Assert.That(MaxStillFrameResolver.Resolve(frame, rangeStart, rangeEnd), Is.EqualTo(expected));
    }

    [Test]
    public void NoSliderFallsBackToTheRangeStartTest()
    {
        // No Max host (tests, batch): the historic behaviour, the first frame of the range.
        Assert.That(MaxStillFrameResolver.Resolve(null, 10, 40), Is.EqualTo(10));
    }

    [Test]
    public void ADegenerateRangeCollapsesOntoItsStartTest()
    {
        // A single-frame scene (end == start) and a corrupt one (end < start) both resolve, never throw.
        Assert.Multiple(() =>
        {
            Assert.That(MaxStillFrameResolver.Resolve(7, 5, 5), Is.EqualTo(5));
            Assert.That(MaxStillFrameResolver.Resolve(7, 5, 3), Is.EqualTo(5));
        });
    }

    #endregion

    #region Range Tests

    [TestCase(1, true)]
    [TestCase(10, true)]
    [TestCase(5, true)]
    [TestCase(0, false)]
    [TestCase(11, false)]
    public void RangeMembershipIsInclusiveTest(int frame, bool expected)
    {
        Assert.That(MaxStillFrameResolver.IsWithinRange(frame, 1, 10), Is.EqualTo(expected));
    }

    #endregion
}
