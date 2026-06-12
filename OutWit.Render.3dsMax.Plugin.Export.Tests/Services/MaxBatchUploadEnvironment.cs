namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal sealed class MaxBatchUploadEnvironment
{
    #region Properties

    public required string BatchExecutablePath { get; init; }

    public required string WorkingDirectoryPath { get; init; }

    public required string ScriptPath { get; init; }

    public required string ScenePath { get; init; }

    public required string PluginAssemblyPath { get; init; }

    public required string RepositoryRootPath { get; init; }

    public required string CloudUrl { get; init; }

    public required string IdentityUrl { get; init; }

    public required string ApiKey { get; init; }

    #endregion
}
