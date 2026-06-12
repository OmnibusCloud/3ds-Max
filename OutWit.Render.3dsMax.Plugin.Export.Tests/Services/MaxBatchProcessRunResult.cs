namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal sealed class MaxBatchProcessRunResult
{
    #region Properties

    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public required string ResultPath { get; init; }

    public required string OutputFolderPath { get; init; }

    public required string ListenerLogPath { get; init; }

    public required string MaxLogPath { get; init; }

    #endregion
}
