using System.IO;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Configuration;

[TestFixture]
public sealed class MaxPluginSettingsTests
{
    #region Fields

    private string m_tempDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_tempDir = Path.Combine(Path.GetTempPath(), "OmnibusCloud3dsMaxSettingsTests", Path.GetRandomFileName());
        Directory.CreateDirectory(m_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_tempDir))
                Directory.Delete(m_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    #endregion

    #region Tests

    [Test]
    public void DefaultsAreLoadedFromEmbeddedResourceTest()
    {
        var settings = MaxPluginSettingsFactory.CreateForUserStore(Path.Combine(m_tempDir, "settings.json"));

        Assert.That(settings.ThemeMode, Is.EqualTo("FollowMax"));
        Assert.That(settings.ExportTarget, Is.EqualTo("Blend"));
        Assert.That(settings.OpenFolderAfterExport, Is.True);
        Assert.That(settings.RememberLastRenderSettings, Is.True);
        Assert.That(settings.LastRenderMode, Is.EqualTo("RenderStill"));
        Assert.That(settings.TilesX, Is.EqualTo(2));
        Assert.That(settings.VideoContainer, Is.EqualTo("mp4"));
        Assert.That(settings.VideoCrf, Is.EqualTo(18));
        Assert.That(settings.LogLevel, Is.EqualTo("Information"));
    }

    [Test]
    public void UserValuesPersistAcrossReloadTest()
    {
        var path = Path.Combine(m_tempDir, "settings.json");

        var settings = MaxPluginSettingsFactory.CreateForUserStore(path);
        settings.ExportTarget = "DccJson";
        settings.TilesX = 4;
        settings.RememberLastRenderSettings = false;
        settings.LastGroupName = "Studio Farm";
        settings.SettingsManager.Save();

        var reloaded = MaxPluginSettingsFactory.CreateForUserStore(path);

        Assert.That(reloaded.ExportTarget, Is.EqualTo("DccJson"));
        Assert.That(reloaded.TilesX, Is.EqualTo(4));
        Assert.That(reloaded.RememberLastRenderSettings, Is.False);
        Assert.That(reloaded.LastGroupName, Is.EqualTo("Studio Farm"));
        // Untouched values still fall back to the shipped defaults.
        Assert.That(reloaded.ThemeMode, Is.EqualTo("FollowMax"));
    }

    #endregion
}
