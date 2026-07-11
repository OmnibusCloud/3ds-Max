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
/// server-driven phase model. No business logic lives in the View — this owns the render lifecycle and
/// drives <see cref="MaxRenderStatus"/>. Scene config / job state are held by the shared
/// <see cref="RenderLaunchViewModel"/>; session + target groups by <see cref="CloudSessionViewModel"/>.
/// </summary>
public sealed class RenderDialogViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    /// <summary>Raised when the user asks for the Details/Diagnostics dialog (host opens the window).</summary>
    public event Action? DetailsRequested;

    #endregion

    #region Fields

    private bool m_cancelRequested;

    private MaxConnectedRenderJobState? m_activeJobState;

    #endregion

    #region Constructors

    public RenderDialogViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
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
        SelectedImageFormat = MaxRenderOutputCatalog.NormalizeImageFormat(Settings.ImageFormat);
        SelectedVideoPreset = MaxRenderOutputCatalog.VideoPresetDisplay(Settings.VideoContainer);
        TilesX = Settings.TilesX > 0 ? Settings.TilesX : 2;
        TilesY = Settings.TilesY > 0 ? Settings.TilesY : 2;
        TileOverlap = Settings.TileOverlap > 0 ? Settings.TileOverlap : 8;
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        LaunchVm.PropertyChanged += OnLaunchPropertyChanged;
        CloudVm.PropertyChanged += OnCloudPropertyChanged;
    }

    private void InitCommands()
    {
        RenderCommand = new RelayCommandAsync(RenderAsync);
        CancelCommand = new RelayCommandAsync(CancelAsync);
        DetailsCommand = new RelayCommand(_ => ShowDetails());
        OpenResultCommand = new RelayCommand(_ => OpenResult(), _ => File.Exists(ResultPath));
        OpenFolderCommand = new RelayCommand(_ => OpenResultFolder(), _ => !string.IsNullOrWhiteSpace(ResultPath));
        NewRenderCommand = new RelayCommand(_ => NewRender());
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

        // The scanned-material bake option only makes sense when the scene actually carries
        // V-Ray scanned materials — the collector's diagnostics already name them.
        HasVRayScannedMaterials = summary.UnmappedPluginClasses.Keys
            .Any(me => me.Contains("VRayScannedMtl", StringComparison.OrdinalIgnoreCase));
        UpdateStatus();
    }

    private async Task RenderAsync()
    {
        m_cancelRequested = false;
        ResultPath = string.Empty;
        PushAxesToRenderMode();
        PersistRenderSettings();

        Status = MaxRenderStatus.Submitting();
        UpdateStatus();

        // No Task.Run: the launch captures the scene through the single-threaded 3ds Max SDK and must
        // stay on the Max main thread; only the submission part awaits the network.
        var request = BuildRequest();
        var jobState = await ConnectedRender.LaunchRenderAsync(request);

        LaunchVm.ApplyJobState(jobState);
        DiagnosticsVm.Apply(jobState.Diagnostics);

        if (!Guid.TryParse(jobState.JobId, out _))
        {
            Status = MaxRenderStatus.Failed(jobState.StatusText);
            UpdateStatus();
            return;
        }

        // Cancel pressed while the launch was in flight — stop the job we just submitted.
        if (m_cancelRequested)
            jobState = await ConnectedRender.CancelJobAsync(jobState);

        await PollUntilTerminalAsync(jobState);
    }

    private async Task PollUntilTerminalAsync(MaxConnectedRenderJobState jobState)
    {
        // Source of truth is the server job status (MX-13), polled until terminal. Close != cancel
        // (MX-5): if the dialog is closed the job keeps running. Cancel requests a server-side stop
        // and the loop keeps polling until the farm reports the terminal cancelled status.
        m_activeJobState = jobState;

        try
        {
            while (true)
            {
                if (jobState.IsCancelled)
                {
                    Status = MaxRenderStatus.Cancelled();
                    UpdateStatus();
                    return;
                }

                if (jobState.IsCompleted)
                {
                    ResultPath = jobState.PrimaryArtifactPath;
                    Status = MaxRenderStatus.Completed();
                    UpdateStatus();
                    return;
                }

                if (IsFailed(jobState))
                {
                    Status = MaxRenderStatus.Failed(jobState.StatusText);
                    UpdateStatus();
                    return;
                }

                Status = m_cancelRequested ? MaxRenderStatus.Cancelling() : MapJobToStatus(jobState);
                UpdateStatus();

                await Task.Delay(TimeSpan.FromSeconds(2));

                jobState = await ConnectedRender.RefreshJobAsync(jobState);
                m_activeJobState = jobState;
                LaunchVm.ApplyJobState(jobState);
                DiagnosticsVm.Apply(jobState.Diagnostics);
            }
        }
        finally
        {
            m_activeJobState = null;
        }
    }

    private async Task CancelAsync()
    {
        if (m_cancelRequested)
            return;

        m_cancelRequested = true;
        Status = MaxRenderStatus.Cancelling();
        UpdateStatus();

        // Actually stop the job on the farm; the poll loop observes the terminal cancelled status.
        var jobState = m_activeJobState;
        if (jobState != null)
        {
            jobState = await ConnectedRender.CancelJobAsync(jobState);
            DiagnosticsVm.Apply(jobState.Diagnostics);
        }
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

    private void NewRender()
    {
        ResultPath = string.Empty;
        Status = MaxRenderStatus.Ready();
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

        return new MaxSceneLaunchPackageRequest
        {
            CloudUrl = CloudVm.CloudUrl,
            IdentityUrl = CloudVm.IdentityUrl,
            RenderMode = LaunchVm.SelectedRenderMode,
            ResolutionX = LaunchVm.ResolutionX,
            ResolutionY = LaunchVm.ResolutionY,
            FrameStart = LaunchVm.FrameStart,
            FrameEnd = LaunchVm.FrameEnd,
            Samples = LaunchVm.Samples,
            UseAllClients = LaunchVm.UseAllClients,
            SelectedGroupName = LaunchVm.SelectedGroupName,
            OutputFolder = outputFolder,
            ImageFormat = SelectedImageFormat,
            TilesX = TilesX,
            TilesY = TilesY,
            TileOverlap = TileOverlap,
            VideoPreset = MaxRenderOutputCatalog.VideoPresetKeyFromDisplay(SelectedVideoPreset),
            VideoCrf = Settings.VideoCrf,
            BakeVRayScannedMaterials = HasVRayScannedMaterials && BakeVRayScannedMaterials
        };
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        CanRender = CloudVm.IsSignedIn && !Status.IsActiveJob;
        CanCancel = Status.IsActiveJob && !m_cancelRequested;
        IsImageOutput = OutputAxis == RenderOutputAxis.Image;
        IsAnimationOutput = OutputAxis == RenderOutputAxis.Animation;
        StatusLine = Status.StatusLine;
        RenderProgress = Status.Progress ?? 0d;
        ShowProgress = Status.IsActiveJob;
        ShowTiles = IsImageOutput && SplitFrame;
        ShowImageFormat = IsImageOutput || AnimationResult == RenderAnimationResult.Sequence;
        ShowVideoOptions = IsAnimationOutput && AnimationResult == RenderAnimationResult.Video;
        ShowResultActions = Status.Phase == MaxRenderPhase.Completed && !string.IsNullOrWhiteSpace(ResultPath);
        ShowConfigActions = !Status.IsActiveJob && !ShowResultActions;

        OpenResultCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();

        // Mirror active/terminal render status to the host prompt line (MX-5/6) so progress stays
        // visible while the dialog is minimized. Idle (Ready) states are not pushed, to avoid noise;
        // a terminal message persists in the prompt (the status bar service is shared/long-lived).
        if (Status.IsActiveJob || Status.IsTerminal)
            StatusBar.Report(Status);
    }

    private void PushAxesToRenderMode()
    {
        LaunchVm.SelectedRenderMode = ResolveRenderMode();
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
        Settings.BakeVRayScannedMaterials = BakeVRayScannedMaterials;
        Settings.UseAllClients = LaunchVm.UseAllClients;
        Settings.LastGroupName = LaunchVm.SelectedGroupName ?? string.Empty;
        Settings.ImageFormat = SelectedImageFormat;
        Settings.VideoContainer = MaxRenderOutputCatalog.VideoPresetKeyFromDisplay(SelectedVideoPreset);
        Settings.TilesX = TilesX;
        Settings.TilesY = TilesY;
        Settings.TileOverlap = TileOverlap;
        Settings.SettingsManager.Save();
    }

    private static MaxRenderStatus MapJobToStatus(MaxConnectedRenderJobState jobState)
    {
        if (jobState.IsCompleted)
            return MaxRenderStatus.Completed();

        var fraction = Math.Clamp(jobState.ProgressPercent / 100d, 0d, 1d);
        return fraction < 0.1d ? MaxRenderStatus.Submitting() : MaxRenderStatus.Running((int)(fraction * 100), 100);
    }

    private static bool IsFailed(MaxConnectedRenderJobState jobState)
    {
        return !jobState.IsCompleted
               && !string.IsNullOrWhiteSpace(jobState.StatusText)
               && (jobState.StatusText.Contains("Failed", StringComparison.OrdinalIgnoreCase)
                   || jobState.StatusText.Contains("Cancelled", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OutputAxis) or nameof(SplitFrame) or nameof(AnimationResult))
        {
            PushAxesToRenderMode();
            UpdateStatus();

            // Tiled stills stitch in EXR precision (mockup 4.1.2) — nudge the format when tiling on.
            if (ShowTiles && SelectedImageFormat == "PNG")
                SelectedImageFormat = "EXR";
        }
    }

    private void OnLaunchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateStatus();
    }

    private void OnCloudPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CloudSessionViewModel.IsSignedIn))
            UpdateStatus();
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
    public IReadOnlyList<string> AvailableImageFormats => MaxRenderOutputCatalog.ImageFormats;

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

    [Notify]
    public MaxRenderStatus Status { get; set; } = null!;

    [Notify]
    public string StatusLine { get; set; } = string.Empty;

    [Notify]
    public double RenderProgress { get; set; }

    [Notify]
    public bool ShowProgress { get; set; }

    [Notify]
    public bool ShowResultActions { get; set; }

    // Footer shows exactly one action set at a time: config (Details + Render), active (Cancel),
    // or result (Open folder + New render) — never a pile-up of all buttons at once.
    [Notify]
    public bool ShowConfigActions { get; set; } = true;

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

    #endregion

    #region Services

    private MaxSceneExportService SceneExport => ApplicationVm.SceneExportService;

    private MaxConnectedRenderService ConnectedRender => ApplicationVm.ConnectedRenderService;

    private MaxConnectedRenderPreflightService Preflight => ApplicationVm.ConnectedRenderPreflightService;

    private MaxConnectedExecutionScopeService ExecutionScope => ApplicationVm.ConnectedExecutionScopeService;

    private MaxPluginSettings Settings => ApplicationVm.Settings;

    private IMaxStatusBarService StatusBar => ApplicationVm.StatusBar;

    #endregion
}
