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
        Assert.That(settings.BakeVRayScannedMaterials, Is.False);
        Assert.That(settings.ImageFormat, Is.EqualTo("PNG"));
        Assert.That(settings.LockAspectRatio, Is.True, "aspect lock defaults to ON");
    }

    [Test]
    public void EverySettingPropertyHasAnEmbeddedDefaultTest()
    {
        // A [Setting] property WITHOUT a matching key in plugin-settings.json throws on first
        // read — 0.7.37 shipped BakeVRayScannedMaterials that way and the Render dialog died
        // with a NullReferenceException on open. Reading every property through the container
        // catches the desync at test time.
        var settings = MaxPluginSettingsFactory.CreateForUserStore(Path.Combine(m_tempDir, "settings.json"));

        foreach (var property in typeof(MaxPluginSettings).GetProperties()
                     .Where(me => me.GetCustomAttributes(true).Any(attr => attr.GetType().Name.StartsWith("Setting"))))
        {
            Assert.That(() => property.GetValue(settings), Throws.Nothing,
                $"[Setting] property '{property.Name}' must have a default in plugin-settings.json");
        }
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
        settings.LockAspectRatio = false;
        settings.SettingsManager.Save();

        var reloaded = MaxPluginSettingsFactory.CreateForUserStore(path);

        Assert.That(reloaded.ExportTarget, Is.EqualTo("DccJson"));
        Assert.That(reloaded.TilesX, Is.EqualTo(4));
        Assert.That(reloaded.RememberLastRenderSettings, Is.False);
        Assert.That(reloaded.LastGroupName, Is.EqualTo("Studio Farm"));
        Assert.That(reloaded.LockAspectRatio, Is.False);
        // Untouched values still fall back to the shipped defaults.
        Assert.That(reloaded.ThemeMode, Is.EqualTo("FollowMax"));
    }

    [Test]
    public void LegacyUserStoreIsCarriedOverOnceTest()
    {
        var legacy = Path.Combine(m_tempDir, "OutWit", "OmnibusCloud.3dsMax.json");
        var target = Path.Combine(m_tempDir, "OmnibusCloud", "OmnibusCloud.3dsMax.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);

        // Legacy (<= 0.7.x) store with a user-changed value survives the folder rename…
        var seeded = MaxPluginSettingsFactory.CreateForUserStore(legacy);
        seeded.ExportTarget = "DccJson";
        seeded.SettingsManager.Save();

        MaxPluginSettingsFactory.MigrateLegacyUserStore(legacy, target);
        Assert.That(MaxPluginSettingsFactory.CreateForUserStore(target).ExportTarget, Is.EqualTo("DccJson"));

        // …but an existing new store is never clobbered by a second migration.
        var current = MaxPluginSettingsFactory.CreateForUserStore(target);
        current.ExportTarget = "Blend";
        current.SettingsManager.Save();

        MaxPluginSettingsFactory.MigrateLegacyUserStore(legacy, target);
        Assert.That(MaxPluginSettingsFactory.CreateForUserStore(target).ExportTarget, Is.EqualTo("Blend"));
    }

    [Test]
    public void LegacyMigrationWithoutLegacyStoreIsANoOpTest()
    {
        var target = Path.Combine(m_tempDir, "OmnibusCloud", "OmnibusCloud.3dsMax.json");

        MaxPluginSettingsFactory.MigrateLegacyUserStore(
            Path.Combine(m_tempDir, "OutWit", "missing.json"), target);

        Assert.That(File.Exists(target), Is.False);
    }

    #endregion
}
