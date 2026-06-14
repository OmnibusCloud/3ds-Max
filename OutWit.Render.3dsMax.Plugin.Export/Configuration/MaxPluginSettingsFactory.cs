using OutWit.Common.Settings.Configuration;
using OutWit.Common.Settings.Json;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

/// <summary>
/// Builds the <see cref="MaxPluginSettings"/> container outside any DI host (the plugin runs in-process
/// in 3ds Max). Immutable defaults come from the embedded <c>plugin-settings.json</c> resource; the
/// writable User-scope store is a per-user JSON file in <c>%APPDATA%</c>, auto-created by the builder.
/// Mirrors the desktop client's settings bootstrap (resource defaults + Merge + Load).
/// </summary>
public static class MaxPluginSettingsFactory
{
    #region Constants

    private const string DEFAULTS_RESOURCE_NAME = "plugin-settings.json";

    private const string SETTINGS_FILE_NAME = "OmnibusCloud.3dsMax";

    #endregion

    #region Functions

    /// <summary>
    /// Creates the production settings container. The User store lives in the per-user settings folder
    /// derived from the assembly name; <see cref="ISettingsManager.Merge"/> upgrades it to the current
    /// schema and <see cref="ISettingsManager.Load"/> reads the effective values.
    /// </summary>
    public static MaxPluginSettings Create()
    {
        var manager = new SettingsBuilder()
            .UseJsonResource(typeof(MaxPluginSettings).Assembly, DEFAULTS_RESOURCE_NAME)
            .WithFileName(SETTINGS_FILE_NAME)
            .WithDepth(1)
            .RegisterContainer<MaxPluginSettings>()
            .Build();

        manager.Merge();
        manager.Load();
        return new MaxPluginSettings(manager);
    }

    /// <summary>
    /// Test/dev variant that persists the User store to an explicit file path (e.g. a temp directory),
    /// so unit tests don't touch the real per-user settings folder. Defaults still come from the
    /// embedded resource.
    /// </summary>
    internal static MaxPluginSettings CreateForUserStore(string userStoreFilePath)
    {
        var manager = new SettingsBuilder()
            .UseJsonResource(typeof(MaxPluginSettings).Assembly, DEFAULTS_RESOURCE_NAME)
            .UseJsonFile(userStoreFilePath, SettingsScope.User)
            .RegisterContainer<MaxPluginSettings>()
            .Build();

        manager.Merge();
        manager.Load();
        return new MaxPluginSettings(manager);
    }

    #endregion
}
