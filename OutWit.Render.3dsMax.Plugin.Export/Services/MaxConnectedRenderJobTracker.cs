using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using Serilog;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Session-scoped owner of the connected render job lifecycle: launch, the status poll loop, cancel,
/// and the persisted handle that survives the dialog. Lives in the service graph, not in a ViewModel,
/// because the Render dialog is rebuilt on every open — a job tracked by the dialog was unreachable
/// the moment it closed (and gone for good when 3ds Max restarted), which is how finished renders
/// ended up with nobody to hand the result to.
/// </summary>
/// <remarks>
/// The poll loop is started from the caller's context (the Max main thread) and keeps running after the
/// dialog closes; it reports every change to the host prompt line itself, so progress stays visible
/// with no dialog open at all. <see cref="Changed"/> may be raised from a background thread (the upload
/// callback runs on the transfer threads) — a ViewModel subscriber must marshal to its dispatcher.
/// </remarks>
public sealed class MaxConnectedRenderJobTracker
{
    #region Constants

    /// <summary>Default server poll interval while a job is active.</summary>
    private static readonly TimeSpan DEFAULT_POLL_INTERVAL = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Age past which a persisted record is dropped instead of restored: a week-old "Render complete"
    /// greeting on the next dialog open is noise, not a result the artist is still waiting for.
    /// </summary>
    private static readonly TimeSpan RESTORE_MAX_AGE = TimeSpan.FromDays(7);

    #endregion

    #region Events

    /// <summary>
    /// Raised on every tracked status change, carrying the status and the job state behind it (null when
    /// the tracker was cleared). May arrive on a background thread.
    /// </summary>
    public event Action<MaxRenderStatus, MaxConnectedRenderJobState?>? Changed;

    #endregion

    #region Fields

    private readonly MaxConnectedRenderService m_renderService;

    private readonly MaxConnectedRenderJobStore m_store;

    private readonly IMaxStatusBarService m_statusBar;

    private readonly ILogger m_logger;

    private readonly TimeSpan m_pollInterval;

    private readonly object m_lock = new();

    private bool m_cancelRequested;

    private bool m_isPolling;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the tracker.
    /// </summary>
    /// <param name="renderService">The connected-render boundary (launch / refresh / cancel).</param>
    /// <param name="store">The persisted job record.</param>
    /// <param name="statusBar">Host prompt line; reported to on every tracked change.</param>
    /// <param name="logger">Plugin logger.</param>
    /// <param name="pollInterval">Poll interval override; defaults to two seconds (tests shorten it).</param>
    public MaxConnectedRenderJobTracker(
        MaxConnectedRenderService renderService,
        MaxConnectedRenderJobStore store,
        IMaxStatusBarService statusBar,
        ILogger logger,
        TimeSpan? pollInterval = null)
    {
        m_renderService = renderService;
        m_store = store;
        m_statusBar = statusBar;
        m_logger = logger;
        m_pollInterval = pollInterval ?? DEFAULT_POLL_INTERVAL;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Launches a connected render and starts tracking it. Must be awaited on the 3ds Max main thread:
    /// the launch captures the scene through the single-threaded Max SDK before the submission awaits
    /// the network. Returns as soon as the job is submitted — the poll loop keeps running afterwards,
    /// with or without an open dialog.
    /// </summary>
    /// <param name="request">The launch request; its upload-progress callback is set by the tracker.</param>
    /// <param name="cancellationToken">Cancels the launch (not the submitted job).</param>
    /// <returns>The submitted job state.</returns>
    public async Task<MaxConnectedRenderJobState> LaunchAsync(
        MaxSceneLaunchPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HasActiveJob)
            return JobState!;

        m_cancelRequested = false;
        request.UploadProgress = OnUploadProgress;

        // A new launch supersedes whatever was tracked before (a collected result, a failed attempt):
        // the old job must not colour the new one's presentation.
        Publish(MaxRenderStatus.Submitting(), null, persist: false);

        var jobState = await m_renderService.LaunchRenderAsync(request, cancellationToken);
        StartedUtc = jobState.SubmittedUtc == default ? DateTime.UtcNow : jobState.SubmittedUtc;

        if (!Guid.TryParse(jobState.JobId, out _))
        {
            // Never reached the farm (blocked preflight / failed submission): nothing to track or keep.
            m_store.Clear();
            Publish(MaxRenderStatus.Failed(jobState.StatusText), jobState, persist: false);
            return jobState;
        }

        Publish(MaxConnectedRenderJobStatusMapper.Map(jobState), jobState, persist: true);

        // Cancel pressed while the launch was in flight — stop the job we just submitted.
        if (m_cancelRequested)
            jobState = await m_renderService.CancelJobAsync(jobState, cancellationToken);

        StartPolling(jobState);
        return jobState;
    }

    /// <summary>
    /// Requests server-side cancellation of the tracked job. The poll loop keeps running until the farm
    /// reports the terminal cancelled status.
    /// </summary>
    public async Task RequestCancelAsync()
    {
        var jobState = JobState;
        if (jobState is null || m_cancelRequested)
            return;

        m_cancelRequested = true;
        Publish(MaxRenderStatus.Cancelling(), jobState, persist: false);

        if (!Guid.TryParse(jobState.JobId, out _))
            return;

        var cancelled = await m_renderService.CancelJobAsync(jobState);
        Publish(MaxConnectedRenderJobStatusMapper.Map(cancelled, cancelRequested: true), cancelled, persist: true);
    }

    /// <summary>
    /// Picks up the persisted job record, if any: resumes polling an unfinished job (including one that
    /// outlived a 3ds Max restart) and re-presents a finished one so its result can still be collected.
    /// A completed job whose downloaded result is missing is refreshed once, which re-downloads it.
    /// </summary>
    /// <returns>True when a job was picked up and is now tracked.</returns>
    public async Task<bool> RestoreAsync()
    {
        if (JobState is not null || m_isPolling)
            return true;

        var jobState = m_store.Load();
        if (jobState is null)
            return false;

        var stamp = jobState.UpdatedUtc == default ? jobState.SubmittedUtc : jobState.UpdatedUtc;
        if (stamp != default && DateTime.UtcNow - stamp > RESTORE_MAX_AGE)
        {
            m_store.Clear();
            return false;
        }

        StartedUtc = jobState.SubmittedUtc == default ? stamp : jobState.SubmittedUtc;
        m_cancelRequested = false;
        m_logger.Information("Restored tracked OmnibusCloud render job {JobId} (server status {Status})", jobState.JobId, jobState.ServerStatus);

        var status = MaxConnectedRenderJobStatusMapper.Map(jobState);
        Publish(status, jobState, persist: false);

        if (!status.IsTerminal)
        {
            // Unfinished: the farm kept rendering while the dialog (or Max) was gone.
            StartPolling(jobState);
            return true;
        }

        // Completed, but the result file is not on disk (a crash between completion and download, or a
        // cleaned temp folder): one refresh re-fetches the blob so the result is collectable again.
        if (status.Phase == MaxRenderPhase.Completed && !File.Exists(jobState.PrimaryArtifactPath))
        {
            var refreshed = await m_renderService.RefreshJobAsync(jobState);
            Publish(MaxConnectedRenderJobStatusMapper.Map(refreshed), refreshed, persist: true);
        }

        return true;
    }

    /// <summary>
    /// Drops a tracked FAILURE the artist has already been shown, so the next dialog open greets them
    /// with the configuration view instead of the same failure card. A failure that happened while every
    /// plugin window was closed is left alone — it has never been presented and still has to be.
    /// </summary>
    /// <returns>True when a presented failure was dropped.</returns>
    public bool AcknowledgeFailure()
    {
        if (Status.Phase != MaxRenderPhase.Failed)
            return false;

        Clear();
        return true;
    }

    /// <summary>
    /// Forgets the tracked job and its persisted record — the user acknowledged it (New render / Retry).
    /// A job still running on the farm is NOT cancelled by this; it is simply no longer followed.
    /// </summary>
    public void Clear()
    {
        m_cancelRequested = false;
        m_store.Clear();
        JobState = null;
        StartedUtc = default;
        Status = MaxRenderStatus.Ready();
        Changed?.Invoke(Status, null);
    }

    #endregion

    #region Tools

    private void StartPolling(MaxConnectedRenderJobState jobState)
    {
        lock (m_lock)
        {
            if (m_isPolling)
                return;

            m_isPolling = true;
        }

        // Deliberately not awaited: the loop outlives the dialog that started it. It continues on the
        // caller's context (the Max main thread), so no marshalling is needed for the status updates.
        _ = PollUntilTerminalAsync(jobState);
    }

    private async Task PollUntilTerminalAsync(MaxConnectedRenderJobState jobState)
    {
        // The source of truth is the server job status (MX-13), polled until terminal. Close != cancel
        // (MX-5): a closed dialog leaves the job running, and this loop keeps following it.
        try
        {
            var persistedPhase = Status.Phase;

            while (true)
            {
                var status = MaxConnectedRenderJobStatusMapper.Map(jobState, m_cancelRequested);

                // The record is a HANDLE, not a progress log — progress is re-fetched from the server on
                // restore. Rewriting it on every two-second poll would be a file write per tick for the
                // whole render, so it is written when the phase actually moves (and on the terminal one,
                // which carries the result path).
                var persist = status.Phase != persistedPhase || status.IsTerminal;
                persistedPhase = status.Phase;

                Publish(status, jobState, persist);

                if (status.IsTerminal)
                    return;

                await Task.Delay(m_pollInterval);

                jobState = await m_renderService.RefreshJobAsync(jobState);
            }
        }
        catch (Exception ex)
        {
            m_logger.Error(ex, "Tracking of OmnibusCloud render job {JobId} stopped", jobState.JobId);
            Publish(MaxRenderStatus.Failed($"Lost track of the job: {ex.Message}"), jobState, persist: true);
        }
        finally
        {
            lock (m_lock)
                m_isPolling = false;
        }
    }

    private void OnUploadProgress(double fraction)
    {
        // The scene upload runs before the farm has any job status; keep the phase honest and do not
        // let a late upload callback overwrite a job that has already started rendering.
        if (Status.Phase is MaxRenderPhase.Running or MaxRenderPhase.Finalizing or MaxRenderPhase.Cancelling)
            return;

        Publish(MaxRenderStatus.Uploading(fraction), JobState, persist: false);
    }

    private void Publish(MaxRenderStatus status, MaxConnectedRenderJobState? jobState, bool persist)
    {
        Status = status;
        JobState = jobState;

        if (persist && jobState is not null)
            m_store.Save(jobState);

        // The prompt line is owned here, not by the dialog: it must keep moving while the artist works
        // in 3ds Max with every plugin window closed.
        if (status.IsActiveJob || status.IsTerminal)
            m_statusBar.Report(status);

        Changed?.Invoke(status, jobState);
    }

    #endregion

    #region Properties

    /// <summary>The current tracked status; <see cref="MaxRenderStatus.Ready"/> when nothing is tracked.</summary>
    public MaxRenderStatus Status { get; private set; } = MaxRenderStatus.Ready();

    /// <summary>The tracked job state, or null when nothing is tracked.</summary>
    public MaxConnectedRenderJobState? JobState { get; private set; }

    /// <summary>When the tracked job was submitted (UTC); default when nothing is tracked.</summary>
    public DateTime StartedUtc { get; private set; }

    /// <summary>True while a farm job is active (submit → finalize / cancelling).</summary>
    public bool HasActiveJob => Status.IsActiveJob;

    /// <summary>True while a job is tracked at all, including a finished one awaiting collection.</summary>
    public bool HasTrackedJob => JobState is not null;

    #endregion
}
