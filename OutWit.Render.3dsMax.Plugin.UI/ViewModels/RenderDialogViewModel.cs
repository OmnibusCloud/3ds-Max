using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

/// <summary>
/// Render dialog (design 4.1): one Render action over a two-axis Output model, a target group, and a
/// server-driven phase model. No business logic lives in the View. Scene config / job state are held by
/// the shared <see cref="RenderLaunchViewModel"/>; session + target groups by
/// <see cref="CloudSessionViewModel"/>.
/// </summary>
/// <remarks>
/// The dialog is a VIEW over <see cref="MaxConnectedRenderJobTracker"/>, not the owner of the render:
/// a new instance is built on every open, so a job owned here stopped being reachable the moment the
/// window closed. Opening the dialog therefore re-attaches to whatever the tracker is following —
/// a job still rendering on the farm, or a finished one whose result has not been collected yet.
/// </remarks>
public sealed class RenderDialogViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    /// <summary>Raised when the user asks for the Details/Diagnostics dialog (host opens the window).</summary>
    public event Action? DetailsRequested;

    #endregion

    #region Fields

    private System.Windows.Threading.Dispatcher? m_uiDispatcher;

    private int m_sceneResolutionX;

    private int m_sceneResolutionY;

    private double m_lockedAspect;

    private bool m_applyingAspect;

    private bool m_applyingSliderFrame;

    #endregion

    #region Constructors

    public RenderDialogViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
        // Captured here because the dialog is constructed on the 3ds Max UI thread: tracker events can
        // arrive from the transfer threads and must be marshalled back.
        m_uiDispatcher = System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread);

        InitDefault();
        InitEvents();
        InitCommands();

        // The dialog self-initializes (no code-behind): pull scene defaults and the user's groups.
        _ = InitializeAsync();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Status = MaxRenderStatus.Ready();

        // Seed the output axes from the persisted "last render mode" preference.
        ApplyRenderModeToAxes(Settings.LastRenderMode);
        SplitFrame = Settings.SplitFrame;
        BakeVRayScannedMaterials = Settings.BakeVRayScannedMaterials;

        // Quick output settings (design 4.1.2), seeded from the persisted defaults.
        LockAspectRatio = Settings.LockAspectRatio;
        SelectedImageFormat = MaxRenderOutputCatalog.NormalizeImageFormat(Settings.ImageFormat);
        SelectedVideoPreset = MaxRenderOutputCatalog.VideoPresetDisplay(Settings.VideoContainer);
        TilesX = Settings.TilesX > 0 ? Settings.TilesX : 2;
        TilesY = Settings.TilesY > 0 ? Settings.TilesY : 2;
        TileOverlap = Settings.TileOverlap > 0 ? Settings.TileOverlap : 8;

        // A persisted EXR from an earlier build (which nudged tiled stills there) would otherwise be
        // re-offered and fail on the farm again.
        ApplyImageFormatConstraints();
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        LaunchVm.PropertyChanged += OnLaunchPropertyChanged;
        CloudVm.PropertyChanged += OnCloudPropertyChanged;
        JobTracker.Changed += OnJobTrackerChanged;
        TimeSlider.FrameChanged += OnTimeSliderFrameChanged;
    }

    private void InitCommands()
    {
        RenderCommand = new RelayCommandAsync(RenderAsync);
        CancelCommand = new RelayCommandAsync(CancelAsync);
        DetailsCommand = new RelayCommand(_ => ShowDetails());
        OpenResultCommand = new RelayCommand(_ => OpenResult(), _ => File.Exists(ResultPath));
        OpenFolderCommand = new RelayCommand(_ => OpenResultFolder(), _ => !string.IsNullOrWhiteSpace(ResultPath));
        NewRenderCommand = new RelayCommand(_ => NewRender());
        CopyLogCommand = new RelayCommand(_ => CopyLog());
        ResetResolutionCommand = new RelayCommand(_ => ResetResolution());
        UpdateStatus();
    }

    #endregion

    #region Functions

    private async Task InitializeAsync()
    {
        // Scene validation reads the 3ds Max scene through the single-threaded Max SDK and must run
        // on the Max main thread: do it synchronously before the first await, then go async for the
        // network-only work (silent session restore, execution scope).
        ValidateScene();

        await CloudVm.EnsureSessionRestoredAsync();
        await LoadExecutionScopeAsync();

        // Re-attach LAST: a job this dialog knows nothing about may still be running on the farm (or may
        // have finished while every plugin window was closed), and its result is collected from here.
        // Needs the restored session, since picking a finished job up re-downloads its result.
        await ReattachTrackedJobAsync();
    }

    /// <summary>
    /// Adopts whatever the session tracker is following: a live job resumes in the active view with its
    /// progress, a finished one lands in the result view so it can still be opened.
    /// </summary>
    private async Task ReattachTrackedJobAsync()
    {
        if (JobTracker.HasTrackedJob)
        {
            ApplyTrackedJob(JobTracker.Status, JobTracker.JobState);
            return;
        }

        if (await JobTracker.RestoreAsync())
            ApplyTrackedJob(JobTracker.Status, JobTracker.JobState);
    }

    private async Task LoadExecutionScopeAsync()
    {
        var request = new MaxConnectedExecutionScopeRequest
        {
            CloudUrl = CloudVm.CloudUrl,
            IdentityUrl = CloudVm.IdentityUrl
        };

        var result = await Task.Run(() => ExecutionScope.LoadAsync(request));
        CloudVm.ApplyExecutionScope(result);
        LaunchVm.ApplyExecutionScope(result);
        UpdateStatus();
    }

    private void ValidateScene()
    {
        // Dialog-open uses the SummaryOnly capture profile: the full geometry capture of a heavy
        // scene takes MINUTES synchronously on the Max main thread (ChairCloth froze the whole
        // application here). The full capture runs when the render actually launches.
        var summary = SceneExport.CollectSummary(MaxSceneCaptureOptions.SummaryOnly);
        SummaryVm.Apply(summary);
        LaunchVm.ApplySceneDefaults(summary);

        // Scene-authored resolution — the Reset button and the aspect lock anchor to it.
        m_sceneResolutionX = LaunchVm.ResolutionX;
        m_sceneResolutionY = LaunchVm.ResolutionY;
        CaptureAspect();

        // The scanned-material bake option only makes sense when the scene actually carries
        // V-Ray scanned materials — the collector's diagnostics already name them.
        HasVRayScannedMaterials = summary.UnmappedPluginClasses.Keys
            .Any(me => me.Contains("VRayScannedMtl", StringComparison.OrdinalIgnoreCase));

        // The still frame follows the time slider within the (possibly just changed) scene range.
        StillFrameHint = $"time slider · {SummaryVm.FrameStart} – {SummaryVm.FrameEnd}";
        ApplySliderFrame(TimeSlider.CurrentFrame);
        UpdateStatus();
    }

    private async Task RenderAsync()
    {
        ResultPath = string.Empty;
        PushAxesToRenderMode();
        PersistRenderSettings();

        // The capture reads the static scene state (geometry, materials, background) at Max's CURRENT
        // time and samples only the animated channels across the range; a still whose frame differs
        // from the slider (it sat outside the scene range) would mix two instants. Put the slider on
        // the frame being rendered so the viewport, the capture and the farm all agree.
        if (OutputAxis == RenderOutputAxis.Image && TimeSlider.CurrentFrame is { } sliderFrame && sliderFrame != StillFrame)
            TimeSlider.SetCurrentFrame(StillFrame);

        Status = MaxRenderStatus.Submitting();
        UpdateStatus();

        // No Task.Run: the launch captures the scene through the single-threaded 3ds Max SDK and must
        // stay on the Max main thread; only the submission part awaits the network. The tracker keeps
        // following the job afterwards — closing this dialog no longer abandons it.
        await JobTracker.LaunchAsync(BuildRequest());
    }

    private Task CancelAsync()
    {
        // A server-side stop; the tracker's poll loop reports the terminal cancelled status.
        return JobTracker.RequestCancelAsync();
    }

    private void OpenResult()
    {
        if (!File.Exists(ResultPath))
            return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ResultPath) { UseShellExecute = true });
    }

    private void OpenResultFolder()
    {
        if (string.IsNullOrWhiteSpace(ResultPath))
            return;

        // Select the result file in Explorer when it exists; otherwise just open its folder.
        if (File.Exists(ResultPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{ResultPath}\"") { UseShellExecute = true });
            return;
        }

        var folder = Path.GetDirectoryName(ResultPath);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }

    /// <summary>
    /// Back to the configuration view. Offered from BOTH terminal cards: after a result was collected,
    /// and after a failure — a failed render otherwise left no way back to the settings at all, so the
    /// only action was Retry, which re-submitted the very settings that had just failed.
    /// </summary>
    private void NewRender()
    {
        // Drop the tracked job (and its persisted record) so the next open greets the artist with the
        // config view instead of yesterday's render.
        JobTracker.Clear();
        ResultPath = string.Empty;
        Status = MaxRenderStatus.Ready();
        UpdateStatus();
    }

    private void CopyLog()
    {
        var text = $"{Status.StatusLine}\nJob: {LaunchVm.JobId}\n\n{DiagnosticsVm.LogText}";

        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard access can fail when another app holds it — never break the dialog.
        }
    }

    /// <summary>
    /// A tracked-job change, marshalled to the UI thread (command CanExecute updates must not fire from
    /// a worker continuation — the upload callback runs on the transfer threads). Thread identity is
    /// checked via Dispatcher.CheckAccess — NEVER by comparing SynchronizationContext instances: WPF
    /// creates a fresh DispatcherSynchronizationContext per operation, so a reference compare re-posts
    /// forever and the Normal-priority flood starves Render/Input (frozen "Uploading 0%" dialog).
    /// </summary>
    private void OnJobTrackerChanged(MaxRenderStatus status, MaxConnectedRenderJobState? jobState)
    {
        if (m_uiDispatcher != null && !m_uiDispatcher.CheckAccess())
        {
            m_uiDispatcher.BeginInvoke(() => ApplyTrackedJob(status, jobState));
            return;
        }

        ApplyTrackedJob(status, jobState);
    }

    private void ApplyTrackedJob(MaxRenderStatus status, MaxConnectedRenderJobState? jobState)
    {
        Status = status;

        if (jobState != null)
        {
            LaunchVm.ApplyJobState(jobState);
            DiagnosticsVm.Apply(jobState.Diagnostics);

            if (status.Phase == MaxRenderPhase.Completed)
                ResultPath = jobState.PrimaryArtifactPath;
        }

        UpdateStatus();
    }

    private void ShowDetails()
    {
        // Details surface (MX-12): refresh validation + preflight, then let the host open the
        // Diagnostics dialog over the freshly filled DiagnosticsVm / SummaryVm.
        RefreshValidation();
        RunPreflight();
        DetailsRequested?.Invoke();
    }

    /// <summary>Re-runs scene validation (Diagnostics dialog's Validate button).</summary>
    public void RefreshValidation()
    {
        ValidateScene();
    }

    /// <summary>Re-runs submission preflight (Diagnostics dialog's Preflight button).</summary>
    public void RunPreflight()
    {
        var preflight = Preflight.Run(BuildRequest());
        LaunchVm.ApplyPreflight(preflight);
        DiagnosticsVm.Apply(preflight.Diagnostics);
        UpdateStatus();
    }

    private MaxSceneLaunchPackageRequest BuildRequest()
    {
        var outputFolder = Path.Combine(OptionsVm.OutputFolder, "OmnibusCloudLaunches");
        Directory.CreateDirectory(outputFolder);

        // A still renders ONE frame — the Frame row (the time slider). The Range row belongs to the
        // animation axis; a still used to render its first frame, whatever the artist was looking at.
        var isStill = OutputAxis == RenderOutputAxis.Image;

        return new MaxSceneLaunchPackageRequest
        {
            CloudUrl = CloudVm.CloudUrl,
            IdentityUrl = CloudVm.IdentityUrl,
            RenderMode = LaunchVm.SelectedRenderMode,
            ResolutionX = LaunchVm.ResolutionX,
            ResolutionY = LaunchVm.ResolutionY,
            FrameStart = isStill ? StillFrame : LaunchVm.FrameStart,
            FrameEnd = isStill ? StillFrame : LaunchVm.FrameEnd,
            Samples = LaunchVm.Samples,
            UseAllClients = LaunchVm.UseAllClients,
            SelectedGroupName = LaunchVm.SelectedGroupTargetName,
            SelectedProjectName = LaunchVm.SelectedProjectTargetName,
            OutputFolder = outputFolder,
            ImageFormat = SelectedImageFormat,
            TilesX = TilesX,
            TilesY = TilesY,
            TileOverlap = TileOverlap,
            VideoPreset = MaxRenderOutputCatalog.VideoPresetKeyFromDisplay(SelectedVideoPreset),
            VideoCrf = Settings.VideoCrf,
            BakeVRayScannedMaterials = HasVRayScannedMaterials && BakeVRayScannedMaterials
            // UploadProgress is wired by the job tracker, which owns the phase reporting.
        };
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        // Launch-week req 4: no target at all (no project, no group, no all-network right) keeps
        // Render disabled — the scope summary line already says "Select a project or group…".
        var hasRenderTarget = LaunchVm.UseAllClients || LaunchVm.SelectedTarget is not null;
        CanRender = CloudVm.IsSignedIn && !Status.IsActiveJob && hasRenderTarget;
        CanCancel = Status.IsActiveJob && Status.Phase != MaxRenderPhase.Cancelling;
        IsImageOutput = OutputAxis == RenderOutputAxis.Image;
        IsAnimationOutput = OutputAxis == RenderOutputAxis.Animation;
        StatusLine = Status.StatusLine;
        RenderProgress = Status.Progress ?? 0d;
        ShowProgress = Status.IsActiveJob;

        // The second axis (design parity with the Blender addon's Computation bar): the farm's own
        // sub-task progress. Hidden until the server reports distributed work, so an empty bar never
        // pretends to be progress.
        ComputationProgress = Status.ComputationProgress ?? 0d;
        ShowComputationProgress = Status.IsActiveJob && Status.HasComputationProgress;
        ShowTiles = IsImageOutput && SplitFrame;
        ShowImageFormat = IsImageOutput || AnimationResult == RenderAnimationResult.Sequence;
        ShowVideoOptions = IsAnimationOutput && AnimationResult == RenderAnimationResult.Video;
        ShowResultActions = Status.Phase == MaxRenderPhase.Completed && !string.IsNullOrWhiteSpace(ResultPath);
        ShowFailedActions = Status.Phase == MaxRenderPhase.Failed;
        ShowConfigActions = !Status.IsActiveJob && !ShowResultActions && !ShowFailedActions;

        // Work-area swap (design 4.1.3): exactly one view at a time.
        ShowConfigView = ShowConfigActions;
        ShowActiveView = Status.IsActiveJob;
        FooterLine = Status.IsActiveJob ? "Close to keep working — render continues" : StatusLine;
        UpdatePhasePresentation();

        OpenResultCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();

        // The host prompt line (MX-5/6) is reported by the job tracker, which outlives this dialog —
        // reporting it here as well would fight the tracker over the same prompt slot.
    }

    /// <summary>
    /// Presentation of the active/terminal phases for the swapped work area (design 4.1.3):
    /// title + counter + sub-line + icon flags, and the completed/failed view content.
    /// </summary>
    private void UpdatePhasePresentation()
    {
        var uploadPercent = Status.Progress is { } fraction ? Percent(fraction) : string.Empty;

        // While rendering, the headline number is the FARM's own progress (units done when countable,
        // else the distributed percentage) — the engine's coarse axis parks mid-render and reading it
        // here is what made a running render look stuck at 50%.
        var renderCounter = Status.UnitsTotal is > 0
            ? $"{Status.UnitsCompleted}/{Status.UnitsTotal} {Status.UnitName}"
            : Status.ComputationProgress is { } computation
                ? Percent(computation)
                : string.Empty;

        (PhaseTitle, PhaseCounter, PhaseSubline) = Status.Phase switch
        {
            MaxRenderPhase.Submitting => ("Submitting scene", string.Empty, "packing scene & assets"),
            MaxRenderPhase.Uploading => ("Uploading scene", uploadPercent, "sending textures & payload to OmnibusCloud"),
            MaxRenderPhase.Running => ("Rendering", renderCounter, "the farm is rendering — progress from the server"),
            MaxRenderPhase.Finalizing => ("Finalizing", string.Empty, "the farm finished rendering; assembling the result"),
            MaxRenderPhase.Cancelling => ("Cancelling…", string.Empty, "finishing the current task on the farm"),
            _ => (string.Empty, string.Empty, string.Empty)
        };

        OverallProgressLine = Status.Progress is { } overall ? $"Overall {Percent(overall)}" : "Overall";
        ComputationProgressLine = Status.UnitsTotal is > 0
            ? $"Computation {Status.UnitsCompleted}/{Status.UnitsTotal} {Status.UnitName}"
            : Status.ComputationProgress is { } value
                ? $"Computation {Percent(value)}"
                : "Computation";

        IsPhaseIndeterminate = Status.IsActiveJob && Status.Progress is null;
        IsUploadPhase = Status.Phase is MaxRenderPhase.Submitting or MaxRenderPhase.Uploading;
        IsRenderPhase = Status.Phase == MaxRenderPhase.Running;
        IsFinishPhase = Status.Phase == MaxRenderPhase.Finalizing;
        IsCancelPhase = Status.Phase == MaxRenderPhase.Cancelling;

        if (ShowResultActions)
        {
            ResultFileName = Path.GetFileName(ResultPath);
            CompletedMeta = FormatCompletedMeta();
            ResultThumbnail = TryLoadThumbnail(ResultPath);
        }

        if (Status.Phase == MaxRenderPhase.Failed)
        {
            FailedMessage = Status.StatusLine;
            FailedDetail = string.IsNullOrWhiteSpace(LaunchVm.JobId) ? string.Empty : $"job {LaunchVm.JobId}";
        }
    }

    private static string Percent(double fraction) => $"{(int)Math.Round(fraction * 100d)}%";

    /// <summary>
    /// "finished in …" for the completed card. Measured submit → last server refresh, so a job
    /// collected after the dialog was closed reports the RENDER's duration, not the time the artist
    /// took to come back for it.
    /// </summary>
    private string FormatCompletedMeta()
    {
        var jobState = JobTracker.JobState;
        var startedUtc = JobTracker.StartedUtc;
        if (startedUtc == default)
            return string.Empty;

        var finishedUtc = jobState is { IsCompleted: true, UpdatedUtc: var updated } && updated != default
            ? updated
            : DateTime.UtcNow;

        var elapsed = finishedUtc - startedUtc;
        if (elapsed < TimeSpan.Zero)
            return string.Empty;

        return elapsed.TotalHours >= 1
            ? $"finished in {(int)elapsed.TotalHours} h {elapsed.Minutes} min"
            : elapsed.TotalMinutes >= 1
                ? $"finished in {(int)elapsed.TotalMinutes} min {elapsed.Seconds} s"
                : $"finished in {elapsed.Seconds} s";
    }

    /// <summary>Small preview of an image result (video/archives get no thumbnail).</summary>
    private static System.Windows.Media.ImageSource? TryLoadThumbnail(string resultPath)
    {
        try
        {
            var extension = Path.GetExtension(resultPath).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".tif" or ".tiff" or ".bmp"))
                return null;

            if (!File.Exists(resultPath))
                return null;

            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(resultPath, UriKind.Absolute);
            bitmap.DecodePixelWidth = 160;
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void PushAxesToRenderMode()
    {
        LaunchVm.SelectedRenderMode = ResolveRenderMode();
    }

    /// <summary>
    /// Narrows the offered image formats to what the chosen mode can actually produce. A tiled still is
    /// collected by the server's 8-bit stitcher, which refuses everything but PNG and JPEG — an earlier
    /// build instead NUDGED tiled stills to EXR, so the dialog itself steered the artist into a job the
    /// farm was guaranteed to reject after the whole scene had already rendered.
    /// </summary>
    private void ApplyImageFormatConstraints()
    {
        var tiled = OutputAxis == RenderOutputAxis.Image && SplitFrame;

        // The selection is moved BEFORE the list shrinks: a ComboBox whose ItemsSource no longer holds
        // the selected item drops the selection (and writes an empty format back through the binding).
        if (tiled)
            SelectedImageFormat = MaxRenderOutputCatalog.NormalizeTiledImageFormat(SelectedImageFormat);

        AvailableImageFormats = tiled
            ? MaxRenderOutputCatalog.TiledImageFormats
            : MaxRenderOutputCatalog.ImageFormats;
    }

    private string ResolveRenderMode()
    {
        if (OutputAxis == RenderOutputAxis.Image)
            return SplitFrame ? "RenderStillTiled" : "RenderStill";

        return AnimationResult == RenderAnimationResult.Video ? "RenderVideo" : "RenderFrames";
    }

    private void ApplyRenderModeToAxes(string renderMode)
    {
        switch (renderMode)
        {
            case "RenderStillTiled":
                OutputAxis = RenderOutputAxis.Image;
                SplitFrame = true;
                break;
            case "RenderFrames":
                OutputAxis = RenderOutputAxis.Animation;
                AnimationResult = RenderAnimationResult.Sequence;
                break;
            case "RenderVideo":
                OutputAxis = RenderOutputAxis.Animation;
                AnimationResult = RenderAnimationResult.Video;
                break;
            default:
                OutputAxis = RenderOutputAxis.Image;
                SplitFrame = false;
                break;
        }
    }

    private void PersistRenderSettings()
    {
        if (!Settings.RememberLastRenderSettings)
            return;

        Settings.LastRenderMode = ResolveRenderMode();
        Settings.SplitFrame = SplitFrame;
        Settings.LockAspectRatio = LockAspectRatio;
        Settings.BakeVRayScannedMaterials = BakeVRayScannedMaterials;
        Settings.UseAllClients = LaunchVm.UseAllClients;
        // Historic field name; carries the unified target DISPLAY name (project or group).
        Settings.LastGroupName = LaunchVm.SelectedTargetName;
        Settings.ImageFormat = SelectedImageFormat;
        Settings.VideoContainer = MaxRenderOutputCatalog.VideoPresetKeyFromDisplay(SelectedVideoPreset);
        Settings.TilesX = TilesX;
        Settings.TilesY = TilesY;
        Settings.TileOverlap = TileOverlap;
        Settings.SettingsManager.Save();
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OutputAxis) or nameof(SplitFrame) or nameof(AnimationResult))
        {
            PushAxesToRenderMode();
            UpdateStatus();
            ApplyImageFormatConstraints();
        }

        // An edit in the Frame field (not an echo of the slider) moves the Max time slider.
        if (e.PropertyName == nameof(StillFrame) && !m_applyingSliderFrame)
            PushStillFrameToSlider();

        if (e.PropertyName == nameof(LockAspectRatio))
        {
            // Engaging the lock freezes the CURRENT ratio.
            if (LockAspectRatio)
                CaptureAspect();

            // The lock is a sticky dialog habit (like ThemeMode), not a render parameter: persist
            // the toggle immediately, independent of the RememberLastRenderSettings gate.
            if (Settings.LockAspectRatio != LockAspectRatio)
            {
                Settings.LockAspectRatio = LockAspectRatio;
                Settings.SettingsManager.Save();
            }
        }
    }

    private void OnLaunchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Aspect lock: editing one resolution axis recomputes the other from the locked ratio.
        if (LockAspectRatio && !m_applyingAspect && m_lockedAspect > 0)
        {
            m_applyingAspect = true;
            try
            {
                if (e.PropertyName == nameof(RenderLaunchViewModel.ResolutionX) && LaunchVm.ResolutionX > 0)
                    LaunchVm.ResolutionY = Math.Max(1, (int)Math.Round(LaunchVm.ResolutionX / m_lockedAspect));
                else if (e.PropertyName == nameof(RenderLaunchViewModel.ResolutionY) && LaunchVm.ResolutionY > 0)
                    LaunchVm.ResolutionX = Math.Max(1, (int)Math.Round(LaunchVm.ResolutionY * m_lockedAspect));
            }
            finally
            {
                m_applyingAspect = false;
            }
        }

        UpdateStatus();
    }

    /// <summary>Locks the current width/height ratio for the aspect chain.</summary>
    private void CaptureAspect()
    {
        if (LaunchVm.ResolutionX > 0 && LaunchVm.ResolutionY > 0)
            m_lockedAspect = LaunchVm.ResolutionX / (double)LaunchVm.ResolutionY;
    }

    private void ResetResolution()
    {
        if (m_sceneResolutionX <= 0 || m_sceneResolutionY <= 0)
            return;

        m_applyingAspect = true;
        try
        {
            LaunchVm.ResolutionX = m_sceneResolutionX;
            LaunchVm.ResolutionY = m_sceneResolutionY;
        }
        finally
        {
            m_applyingAspect = false;
        }

        CaptureAspect();
        UpdateStatus();
    }

    private void OnCloudPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CloudSessionViewModel.IsSignedIn))
            UpdateStatus();
    }

    /// <summary>The artist scrubbed, stepped or played the timeline: the Frame field follows.</summary>
    private void OnTimeSliderFrameChanged(int frame)
    {
        if (m_uiDispatcher != null && !m_uiDispatcher.CheckAccess())
        {
            m_uiDispatcher.BeginInvoke(() => ApplySliderFrame(frame));
            return;
        }

        ApplySliderFrame(frame);
    }

    /// <summary>
    /// Shows the slider's frame in the Frame field, clamped into the scene range. Clamping here is
    /// display-only: opening the dialog must never move the artist's slider, even when it sits outside
    /// the range (frame 0 of a 0-based timeline) — Render moves it, explicitly, when it has to.
    /// </summary>
    private void ApplySliderFrame(int? sliderFrame)
    {
        m_applyingSliderFrame = true;
        try
        {
            StillFrame = MaxStillFrameResolver.Resolve(sliderFrame, SummaryVm.FrameStart, SummaryVm.FrameEnd);
        }
        finally
        {
            m_applyingSliderFrame = false;
        }
    }

    /// <summary>
    /// A typed frame: bounded by the scene range (the capture samples animation across exactly that
    /// range), then mirrored onto the Max time slider so the viewport shows what will render.
    /// </summary>
    private void PushStillFrameToSlider()
    {
        var clamped = MaxStillFrameResolver.Resolve(StillFrame, SummaryVm.FrameStart, SummaryVm.FrameEnd);
        if (clamped != StillFrame)
        {
            // Re-enters through PropertyChanged with the in-range value, which is then pushed.
            StillFrame = clamped;
            return;
        }

        if (TimeSlider.CurrentFrame != StillFrame)
            TimeSlider.SetCurrentFrame(StillFrame);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Detaches from the session tracker. A running or completed JOB is deliberately untouched: closing
    /// the dialog is not a cancel, and the next open re-attaches to whatever is still running or waiting
    /// to be collected. A FAILURE this dialog has already shown is dropped instead — re-presenting it on
    /// every open made the window a dead end the artist could not configure their way out of.
    /// </summary>
    public override void Dispose()
    {
        JobTracker.Changed -= OnJobTrackerChanged;

        // The last listener leaving stops the slider sampling in the host service.
        TimeSlider.FrameChanged -= OnTimeSliderFrameChanged;
        PropertyChanged -= OnPropertyChanged;
        LaunchVm.PropertyChanged -= OnLaunchPropertyChanged;
        CloudVm.PropertyChanged -= OnCloudPropertyChanged;

        // Gated on the failed card having been on screen: a job that fails while every window is closed
        // has never been seen, and still has to greet the artist on the next open.
        if (ShowFailedActions)
            JobTracker.AcknowledgeFailure();

        base.Dispose();
    }

    #endregion

    #region Properties

    public ExportSummaryViewModel SummaryVm => ApplicationVm.MainVm.SummaryVm;

    public ExportOptionsViewModel OptionsVm => ApplicationVm.MainVm.OptionsVm;

    public ExportDiagnosticsViewModel DiagnosticsVm => ApplicationVm.MainVm.DiagnosticsVm;

    public CloudSessionViewModel CloudVm => ApplicationVm.CloudSessionVm;

    public RenderLaunchViewModel LaunchVm => ApplicationVm.RenderLaunchVm;

    [Notify]
    public RenderOutputAxis OutputAxis { get; set; }

    [Notify]
    public RenderAnimationResult AnimationResult { get; set; }

    [Notify]
    public bool SplitFrame { get; set; }

    // Visible only when the scene carries V-Ray scanned materials; states explicitly that part
    // of the work (a local V-Ray render-to-texture pass) runs on the user's machine.
    [Notify]
    public bool BakeVRayScannedMaterials { get; set; }

    [Notify]
    public bool HasVRayScannedMaterials { get; set; }

    [Notify]
    public bool IsImageOutput { get; set; }

    [Notify]
    public bool IsAnimationOutput { get; set; }

    // Quick output settings (design 4.1.2) — every value here actually travels in the launch request.
    /// <summary>The formats the CURRENT mode can produce; tiled stills only ever offer PNG/JPEG.</summary>
    [Notify]
    public IReadOnlyList<string> AvailableImageFormats { get; set; } = MaxRenderOutputCatalog.ImageFormats;

    public IReadOnlyList<string> AvailableVideoPresets { get; } =
        MaxRenderOutputCatalog.VideoPresets.Select(me => me.Value).ToArray();

    [Notify]
    public string SelectedImageFormat { get; set; } = "PNG";

    [Notify]
    public string SelectedVideoPreset { get; set; } = string.Empty;

    [Notify]
    public int TilesX { get; set; } = 2;

    [Notify]
    public int TilesY { get; set; } = 2;

    [Notify]
    public int TileOverlap { get; set; } = 8;

    [Notify]
    public bool ShowTiles { get; set; }

    [Notify]
    public bool ShowImageFormat { get; set; }

    [Notify]
    public bool ShowVideoOptions { get; set; }

    /// <summary>Chains width↔height edits to the ratio captured when the lock was engaged.</summary>
    [Notify]
    public bool LockAspectRatio { get; set; }

    /// <summary>
    /// The frame a still renders (plain and tiled): two-way with the 3ds Max time slider, bounded by the
    /// scene's animation range. Derived from the scene, never persisted — like the Blender addon's
    /// Frame field over <c>scene.frame_current</c>.
    /// </summary>
    [Notify]
    public int StillFrame { get; set; } = 1;

    /// <summary>The quiet note beside the Frame field, e.g. "time slider · 1 – 100".</summary>
    [Notify]
    public string StillFrameHint { get; set; } = string.Empty;

    [Notify]
    public MaxRenderStatus Status { get; set; } = null!;

    [Notify]
    public string StatusLine { get; set; } = string.Empty;

    /// <summary>The coarse engine axis (upload fraction, then the job's stage fraction): bar one.</summary>
    [Notify]
    public double RenderProgress { get; set; }

    /// <summary>
    /// The farm's own sub-task fraction: bar two. Separate because the engine axis parks for the whole
    /// distributed render — one bar could only ever show one of the two truths.
    /// </summary>
    [Notify]
    public double ComputationProgress { get; set; }

    [Notify]
    public bool ShowProgress { get; set; }

    /// <summary>True while the server reports distributed work (the computation bar is meaningful).</summary>
    [Notify]
    public bool ShowComputationProgress { get; set; }

    /// <summary>Caption of the coarse bar, e.g. "Overall 50%".</summary>
    [Notify]
    public string OverallProgressLine { get; set; } = string.Empty;

    /// <summary>Caption of the farm bar, e.g. "Computation 142/240 frames".</summary>
    [Notify]
    public string ComputationProgressLine { get; set; } = string.Empty;

    [Notify]
    public bool ShowResultActions { get; set; }

    // Footer shows exactly one action set at a time: config (Details + Render), active (Cancel),
    // result (Open + Open folder + New render) or failed (Copy log + Retry + New render).
    [Notify]
    public bool ShowConfigActions { get; set; } = true;

    [Notify]
    public bool ShowFailedActions { get; set; }

    // Work-area swap (design 4.1.3): config / active-phase / completed / failed views.
    [Notify]
    public bool ShowConfigView { get; set; } = true;

    [Notify]
    public bool ShowActiveView { get; set; }

    [Notify]
    public string FooterLine { get; set; } = string.Empty;

    [Notify]
    public string PhaseTitle { get; set; } = string.Empty;

    [Notify]
    public string PhaseCounter { get; set; } = string.Empty;

    [Notify]
    public string PhaseSubline { get; set; } = string.Empty;

    [Notify]
    public bool IsPhaseIndeterminate { get; set; }

    [Notify]
    public bool IsUploadPhase { get; set; }

    [Notify]
    public bool IsRenderPhase { get; set; }

    [Notify]
    public bool IsFinishPhase { get; set; }

    [Notify]
    public bool IsCancelPhase { get; set; }

    [Notify]
    public string ResultFileName { get; set; } = string.Empty;

    [Notify]
    public string CompletedMeta { get; set; } = string.Empty;

    [Notify]
    public System.Windows.Media.ImageSource? ResultThumbnail { get; set; }

    [Notify]
    public string FailedMessage { get; set; } = string.Empty;

    [Notify]
    public string FailedDetail { get; set; } = string.Empty;

    [Notify]
    public string ResultPath { get; set; } = string.Empty;

    [Notify]
    public bool CanRender { get; set; }

    [Notify]
    public bool CanCancel { get; set; }

    #endregion

    #region Commands

    public ICommand RenderCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand DetailsCommand { get; private set; } = null!;

    public RelayCommand OpenResultCommand { get; private set; } = null!;

    public RelayCommand OpenFolderCommand { get; private set; } = null!;

    public ICommand NewRenderCommand { get; private set; } = null!;

    public ICommand CopyLogCommand { get; private set; } = null!;

    public ICommand ResetResolutionCommand { get; private set; } = null!;

    #endregion

    #region Services

    private MaxSceneExportService SceneExport => ApplicationVm.SceneExportService;

    private MaxConnectedRenderPreflightService Preflight => ApplicationVm.ConnectedRenderPreflightService;

    private MaxConnectedExecutionScopeService ExecutionScope => ApplicationVm.ConnectedExecutionScopeService;

    private MaxPluginSettings Settings => ApplicationVm.Settings;

    /// <summary>The session-scoped job lifecycle this dialog presents (and outlives it).</summary>
    private MaxConnectedRenderJobTracker JobTracker => ApplicationVm.ConnectedRenderJobTracker;

    /// <summary>The host time slider the still frame follows.</summary>
    private IMaxTimeSliderService TimeSlider => ApplicationVm.TimeSlider;

    #endregion
}
