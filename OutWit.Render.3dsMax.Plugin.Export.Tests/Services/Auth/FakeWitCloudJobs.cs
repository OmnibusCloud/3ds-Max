using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK.Jobs;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Auth;

/// <summary>
/// Fake jobs facet: fixed status, one configurable typed result payload, and cancel-call recording.
/// </summary>
internal sealed class FakeWitCloudJobs : IWitCloudJobs
{
    #region IWitCloudJobs

    public Task<ProcessingJobInfo> GetStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        return Task.FromResult(new ProcessingJobInfo
        {
            Id = jobId,
            Status = Status,
            OverallProgress = OverallProgress
        });
    }

    public Task<TResult?> GetResultAsync<TResult>(Guid jobId, string resultVariable = "result", CancellationToken ct = default)
    {
        if (Result is TResult typedResult)
            return Task.FromResult<TResult?>(typedResult);

        throw new InvalidOperationException($"No result of type '{typeof(TResult).Name}' is configured.");
    }

    public Task CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        CancelledJobIds.Add(jobId);
        return Task.CompletedTask;
    }

    #endregion

    #region Properties

    public ProcessingJobStatus Status { get; set; } = ProcessingJobStatus.Processing;

    public double OverallProgress { get; set; }

    public object? Result { get; set; }

    public List<Guid> CancelledJobIds { get; } = [];

    #endregion
}
