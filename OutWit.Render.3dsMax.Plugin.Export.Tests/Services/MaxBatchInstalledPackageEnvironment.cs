namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal sealed class MaxBatchInstalledPackageEnvironment
{
    #region Properties

    public required string BatchExecutablePath { get; init; }

    public required string WorkingDirectoryPath { get; init; }

    public required string ScriptPath { get; init; }

    public required string ScenePath { get; init; }

    public required string InstallScriptPath { get; init; }

    public required string UninstallScriptPath { get; init; }

    public required string ApplicationPluginsRootPath { get; init; }

    public required string Configuration { get; init; }

    #endregion
}
