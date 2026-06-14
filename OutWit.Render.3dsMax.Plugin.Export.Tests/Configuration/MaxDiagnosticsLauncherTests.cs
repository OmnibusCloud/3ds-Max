using System.IO;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Configuration;

[TestFixture]
public sealed class MaxDiagnosticsLauncherTests
{
    #region Tests

    [Test]
    public void LogsDirectoryIsUnderAppDataOmnibusCloudTest()
    {
        var directory = MaxDiagnosticsLauncher.GetLogsDirectory();

        Assert.That(directory, Is.Not.Empty);
        Assert.That(directory, Does.EndWith(Path.Combine("OmnibusCloud", "Logs")));
    }

    [Test]
    public void LogsDirectoryIsStableAcrossCallsTest()
    {
        // "Open last log" relies on a stable directory that matches where the Serilog bootstrap writes.
        Assert.That(MaxDiagnosticsLauncher.GetLogsDirectory(), Is.EqualTo(MaxDiagnosticsLauncher.GetLogsDirectory()));
    }

    [Test]
    public void GetLatestLogFileDoesNotThrowTest()
    {
        Assert.That(() => MaxDiagnosticsLauncher.GetLatestLogFile(), Throws.Nothing);
    }

    #endregion
}
