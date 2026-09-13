using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

/// <summary>
/// Scriptable submission transport: each refresh returns the next queued server snapshot, so a test can
/// walk a job through the farm's progress axes to a terminal state without a cloud.
/// </summary>
internal sealed class FakeMaxConnectedRenderSubmissionTransport : IMaxConnectedRenderSubmissionTransport
{
    #region Fields

    private readonly Queue<Action<MaxConnectedRenderJobState>> m_refreshes = new();

    #endregion

    #region Functions

    /// <summary>Queues one server snapshot, applied on the matching refresh call.</summary>
    public FakeMaxConnectedRenderSubmissionTransport EnqueueRefresh(Action<MaxConnectedRenderJobState> apply)
    {
        m_refreshes.Enqueue(apply);
        return this;
    }

    #endregion

    #region IMaxConnectedRenderSubmissionTransport

    public Task<MaxConnectedRenderJobState> SubmitAsync(MaxSceneLaunchPackageRequest request, MaxSceneLaunchPackageResult package, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The tracker tests drive refresh / cancel, not submission.");
    }

    public Task<MaxConnectedRenderJobState> RefreshAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default)
    {
        RefreshCount++;
        jobState.UpdatedUtc = DateTime.UtcNow;

        if (m_refreshes.Count > 0)
            m_refreshes.Dequeue().Invoke(jobState);

        return Task.FromResult(jobState);
    }

    public Task<MaxConnectedRenderJobState> CancelAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default)
    {
        CancelCount++;
        jobState.IsCancelled = true;
        jobState.StatusText = "OmnibusCloud job status: Cancelled.";
        return Task.FromResult(jobState);
    }

    #endregion

    #region Properties

    public int RefreshCount { get; private set; }

    public int CancelCount { get; private set; }

    #endregion
}
