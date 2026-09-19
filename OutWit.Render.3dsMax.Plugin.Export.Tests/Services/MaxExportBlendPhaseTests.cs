using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

/// <summary>
/// The Export dialog names the step the server is on. The wire contract has no activity name, only a
/// stage-based fraction — which for our own three-activity export script says exactly which step runs.
/// </summary>
[TestFixture]
public sealed class MaxExportBlendPhaseTests
{
    #region Describe Tests

    [TestCase(0d, "Unpacking")]
    [TestCase(33.333333d, "Building")]
    [TestCase(66.666667d, "Finishing")]
    public void EachOfTheScriptsThreeActivitiesIsNamedTest(double percent, string expected)
    {
        // 0 / 1 / 2 activities finished means the 1st / 2nd / 3rd is running: unzip, build, clear.
        Assert.That(MaxExportBlendPhase.Describe("Processing", percent), Does.StartWith(expected));
    }

    [Test]
    public void TheLongBuildIsNamedInsteadOfShowingAFrozenPercentageTest()
    {
        // The whole .blend build sat at 33% — the number never moved, the name says what is happening.
        var phase = MaxExportBlendPhase.Describe("Processing", 33.333333d);

        Assert.Multiple(() =>
        {
            Assert.That(phase, Is.EqualTo("Building the Blender scene…"));
            Assert.That(phase, Does.Not.Contain("%"));
        });
    }

    [TestCase("Pending", "Queued on the server…")]
    [TestCase("Scheduled", "Queued on the server…")]
    [TestCase("pending", "Queued on the server…")]
    [TestCase("Distributing", "Starting on the server…")]
    public void AJobThatHasNotStartedSaysSoTest(string serverStatus, string expected)
    {
        // Before it runs, every fraction is 0 — naming the first activity would be a lie.
        Assert.That(MaxExportBlendPhase.Describe(serverStatus, 0d), Is.EqualTo(expected));
    }

    [TestCase(100d)]
    [TestCase(120d)]
    [TestCase(-5d)]
    public void AFractionOutsideTheScriptFallsBackToAPlainLineTest(double percent)
    {
        // A finished job is not shown here, and a fraction we cannot place must not name a wrong step.
        Assert.That(MaxExportBlendPhase.Describe("Processing", percent), Is.EqualTo("Working on the server…"));
    }

    [Test]
    public void AnUnknownStatusStillNamesTheRunningActivityTest()
    {
        // Empty status right after submit, before the first poll answers.
        Assert.That(MaxExportBlendPhase.Describe(null, 33.333333d), Is.EqualTo("Building the Blender scene…"));
    }

    #endregion
}
