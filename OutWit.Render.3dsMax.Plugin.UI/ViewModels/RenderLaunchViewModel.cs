using OutWit.Common.Aspects;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

public sealed class RenderLaunchViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private static readonly IReadOnlyList<string> RENDER_MODE_OPTIONS =
    [
        "RenderStill",
        "RenderStillTiled",
        "RenderFrames",
        "RenderVideo",
        "ExportBlend"
    ];

    #endregion

    #region Constructors

    public RenderLaunchViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
        InitDefault();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        SelectedRenderMode = RENDER_MODE_OPTIONS[0];
        AvailableTargets = [];
        ExecutionScopeSummary = "Select a project or group before connected launch";
        // Whole-network is a permission: hidden until the loaded execution scope confirms it (MX-10).
        CanRunOnAllClientsOption = false;
        JobStatusText = "No active job";
        JobProgressText = "0%";
        ResultPath = string.Empty;
        RenderResolutionText = "1920 x 1080";
    }

    private void InitEvents()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ResolutionX) || e.PropertyName == nameof(ResolutionY))
                RenderResolutionText = $"{ResolutionX} x {ResolutionY}";

            if (e.PropertyName == nameof(UseAllClients) || e.PropertyName == nameof(SelectedTarget))
                ExecutionScopeSummary = BuildScopeSummary();

            if (e.PropertyName == nameof(CanRunOnAllClientsOption) && !CanRunOnAllClientsOption)
                UseAllClients = false;
        };
    }

    #endregion

    #region Functions

    /// <summary>
    /// Applies launch defaults from the currently collected 3ds Max scene summary.
    /// </summary>
    public void ApplySceneDefaults(MaxSceneSummaryData summary)
    {
        ResolutionX = summary.RenderWidth > 0 ? summary.RenderWidth : 1920;
        ResolutionY = summary.RenderHeight > 0 ? summary.RenderHeight : 1080;
        FrameStart = summary.FrameStart > 0 ? summary.FrameStart : 1;
        FrameEnd = summary.FrameEnd >= FrameStart ? summary.FrameEnd : FrameStart;
    }

    /// <summary>
    /// Records a local placeholder for the current connected-launch state until the real cloud transport is wired.
    /// </summary>
    public void MarkLaunchNotYetConnected()
    {
        JobStatusText = "Connected render launch is not wired yet";
        JobProgressText = "Not started";
        ResultPath = string.Empty;
        LaunchPackageFolderPath = string.Empty;
        LaunchManifestPath = string.Empty;
    }

    /// <summary>
    /// Applies the prepared local launch-package state to the connected-render status area.
    /// </summary>
    public void ApplyPreparedLaunch(MaxSceneLaunchPackageResult result)
    {
        JobStatusText = result.StatusText;
        JobProgressText = result.IsSuccess ? "Prepared locally" : "Failed";
        LaunchPackageFolderPath = result.PackageFolderPath;
        LaunchManifestPath = result.ManifestPath;
        ResultPath = result.PrimaryArtifactPath;
    }

    /// <summary>
    /// Applies the current connected-render job state to the status area.
    /// </summary>
    public void ApplyJobState(MaxConnectedRenderJobState jobState)
    {
        JobId = jobState.JobId;
        JobStatusText = jobState.StatusText;
        JobProgressText = $"{jobState.ProgressPercent:0.#}%";
        LaunchPackageFolderPath = jobState.PackageFolderPath;
        LaunchManifestPath = jobState.ManifestPath;
        SubmissionReceiptPath = jobState.SubmissionReceiptPath;
        ResultPath = jobState.PrimaryArtifactPath;
    }

    /// <summary>
    /// Applies the current connected-render preflight result to the status area.
    /// </summary>
    public void ApplyPreflight(MaxConnectedRenderPreflightResult preflight)
    {
        JobStatusText = preflight.StatusText;
        JobProgressText = preflight.CanLaunch ? "Preflight ready" : "Preflight blocked";
    }

    /// <summary>
    /// Applies the current connected render download result to the status area.
    /// </summary>
    public void ApplyDownload(MaxConnectedRenderDownloadResult result)
    {
        JobStatusText = result.StatusText;
        JobProgressText = result.IsSuccess ? "Downloaded" : "Download failed";

        if (!string.IsNullOrWhiteSpace(result.DownloadedFilePath))
            DownloadedResultPath = result.DownloadedFilePath;
    }

    /// <summary>
    /// Applies the current connected render archive upload result to the status area.
    /// </summary>
    public void ApplyUpload(MaxConnectedRenderUploadResult result)
    {
        JobStatusText = result.StatusText;
        JobProgressText = result.IsSuccess ? "Uploaded" : "Upload failed";

        if (result.UploadedBlobId != Guid.Empty)
            UploadedPackageBlobId = result.UploadedBlobId;

        if (!string.IsNullOrWhiteSpace(result.UploadReceiptPath))
            UploadReceiptPath = result.UploadReceiptPath;
    }

    /// <summary>
    /// Applies the currently available execution scope options to the launch UI: the unified
    /// Target list carries the user's projects (campaigns) FIRST, then the authorized groups.
    /// The previous selection survives a refresh when a matching kind+name entry still exists.
    /// </summary>
    public void ApplyExecutionScope(MaxConnectedExecutionScopeResult result)
    {
        var previous = SelectedTarget;

        AvailableTargets = result.Projects
            .Select(me => new RenderTargetOption { IsProject = true, Name = me.Name })
            .Concat(result.Groups.Select(me => new RenderTargetOption { IsProject = false, Name = me.Name }))
            .ToArray();
        CanRunOnAllClientsOption = result.CanRunOnAllClients;
        UseAllClients = result.CanRunOnAllClients && UseAllClients;

        // Rebuilt list = new instances; reselect by kind+name, else default to the first entry.
        SelectedTarget = previous is null
            ? null
            : AvailableTargets.FirstOrDefault(me =>
                me.IsProject == previous.IsProject &&
                string.Equals(me.Name, previous.Name, StringComparison.OrdinalIgnoreCase));
        if (!UseAllClients && SelectedTarget is null && AvailableTargets.Count > 0)
            SelectedTarget = AvailableTargets[0];

        ExecutionScopeSummary = BuildScopeSummary();
    }

    #endregion

    #region Tools

    private string BuildScopeSummary()
    {
        if (UseAllClients)
            return "All clients";
        if (SelectedTarget is null)
            return "Select a project or group before connected launch";
        return SelectedTarget.IsProject ? $"Project: {SelectedTarget.Name}" : $"Group: {SelectedTarget.Name}";
    }

    #endregion

    #region Properties

    public IReadOnlyList<string> RenderModeOptions => RENDER_MODE_OPTIONS;

    [Notify]
    public string SelectedRenderMode { get; set; }

    [Notify]
    public int ResolutionX { get; set; } = 1920;

    [Notify]
    public int ResolutionY { get; set; } = 1080;

    [Notify]
    public int FrameStart { get; set; } = 1;

    [Notify]
    public int FrameEnd { get; set; } = 1;

    [Notify]
    public int Samples { get; set; } = 64;

    [Notify]
    public bool UseAllClients { get; set; }

    [Notify]
    public bool CanRunOnAllClientsOption { get; set; }

    /// <summary>The unified Target list: projects (campaigns) first, then groups.</summary>
    [Notify]
    public IReadOnlyList<RenderTargetOption> AvailableTargets { get; set; } = [];

    [Notify]
    public RenderTargetOption? SelectedTarget { get; set; }

    /// <summary>The selected target's display name ('' when none) — the persisted value.</summary>
    public string SelectedTargetName => SelectedTarget?.Name ?? string.Empty;

    /// <summary>The selected name when it is a PROJECT, else empty — feeds the launch request.</summary>
    public string SelectedProjectTargetName => SelectedTarget is { IsProject: true } target ? target.Name : string.Empty;

    /// <summary>The selected name when it is a GROUP, else empty — feeds the launch request.</summary>
    public string SelectedGroupTargetName => SelectedTarget is { IsProject: false } target ? target.Name : string.Empty;

    [Notify]
    public string ExecutionScopeSummary { get; set; }

    [Notify]
    public string JobStatusText { get; set; }

    [Notify]
    public string JobId { get; set; } = string.Empty;

    [Notify]
    public string JobProgressText { get; set; }

    [Notify]
    public string ResultPath { get; set; }

    [Notify]
    public string LaunchPackageFolderPath { get; set; } = string.Empty;

    [Notify]
    public string LaunchManifestPath { get; set; } = string.Empty;

    [Notify]
    public string SubmissionReceiptPath { get; set; } = string.Empty;

    [Notify]
    public string DownloadedResultPath { get; set; } = string.Empty;

    [Notify]
    public Guid? UploadedPackageBlobId { get; set; }

    [Notify]
    public string UploadReceiptPath { get; set; } = string.Empty;

    [Notify]
    public string RenderResolutionText { get; set; }

    #endregion
}
