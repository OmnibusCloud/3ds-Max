namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal sealed class MaxBatchSmokeEnvironment
{
    #region Properties

    public required string BatchExecutablePath { get; init; }

    public required string WorkingDirectoryPath { get; init; }

    public required string ScriptPath { get; init; }

    public required string ScenePath { get; init; }

    public required string PluginAssemblyPath { get; init; }

    #endregion
}
