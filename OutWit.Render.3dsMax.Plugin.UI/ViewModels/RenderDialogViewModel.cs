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
    #region Fields

    private bool m_cancelRequested;

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
        CancelCommand = new RelayCommand(_ => Cancel());
        DetailsCommand = new RelayCommand(_ => ShowDetails());
        UpdateStatus();
    }

    #endregion

    #region Functions

    private async Task InitializeAsync()
    {
        await LoadExecutionScopeAsync();
        ValidateScene();
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
        var result = SceneExport.ValidateCurrentScene();
        SummaryVm.Apply(result.Summary);
        LaunchVm.ApplySceneDefaults(result.Summary);
        DiagnosticsVm.Apply(result.Diagnostics);
        UpdateStatus();
    }

    private async Task RenderAsync()
    {
        m_cancelRequested = false;
        PushAxesToRenderMode();
        PersistRenderSettings();

        Status = MaxRenderStatus.Submitting();
        UpdateStatus();

        var request = BuildRequest();
        var jobState = await Task.Run(() => ConnectedRender.LaunchRenderAsync(request));

        LaunchVm.ApplyJobState(jobState);
        DiagnosticsVm.Apply(jobState.Diagnostics);

        if (!Guid.TryParse(jobState.JobId, out _))
        {
            Status = MaxRenderStatus.Failed(jobState.StatusText);
            UpdateStatus();
            return;
        }

        await PollUntilTerminalAsync(jobState);
    }

    private async Task PollUntilTerminalAsync(MaxConnectedRenderJobState jobState)
    {
        // Source of truth is the server job status (MX-13), polled until terminal. Close != cancel
        // (MX-5): if the dialog is closed the job keeps running; only Cancel stops the loop.
        while (!m_cancelRequested)
        {
            Status = MapJobToStatus(jobState);
            UpdateStatus();

            if (jobState.IsCompleted)
            {
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

            await Task.Delay(TimeSpan.FromSeconds(2));
            if (m_cancelRequested)
                break;

            jobState = await Task.Run(() => ConnectedRender.RefreshJobAsync(jobState));
            LaunchVm.ApplyJobState(jobState);
            DiagnosticsVm.Apply(jobState.Diagnostics);
        }

        Status = MaxRenderStatus.Cancelling();
        UpdateStatus();
    }

    private void Cancel()
    {
        m_cancelRequested = true;
        Status = MaxRenderStatus.Cancelling();
        UpdateStatus();
    }

    private void ShowDetails()
    {
        // Details surface (MX-12): refresh validation + preflight diagnostics. The dedicated Details
        // window is a follow-up; the data it shows is gathered here into DiagnosticsVm / SummaryVm.
        ValidateScene();

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
            OutputFolder = outputFolder
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
        Settings.UseAllClients = LaunchVm.UseAllClients;
        Settings.LastGroupName = LaunchVm.SelectedGroupName ?? string.Empty;
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

    [Notify]
    public bool IsImageOutput { get; set; }

    [Notify]
    public bool IsAnimationOutput { get; set; }

    [Notify]
    public MaxRenderStatus Status { get; set; } = null!;

    [Notify]
    public string StatusLine { get; set; } = string.Empty;

    [Notify]
    public double RenderProgress { get; set; }

    [Notify]
    public bool ShowProgress { get; set; }

    [Notify]
    public bool CanRender { get; set; }

    [Notify]
    public bool CanCancel { get; set; }

    #endregion

    #region Commands

    public ICommand RenderCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand DetailsCommand { get; private set; } = null!;

    #endregion

    #region Services

    private MaxSceneExportService SceneExport => ApplicationVm.SceneExportService;

    private MaxConnectedRenderService ConnectedRender => ApplicationVm.ConnectedRenderService;

    private MaxConnectedRenderPreflightService Preflight => ApplicationVm.ConnectedRenderPreflightService;

    private MaxConnectedExecutionScopeService ExecutionScope => ApplicationVm.ConnectedExecutionScopeService;

    private MaxPluginSettings Settings => ApplicationVm.Settings;

    #endregion
}
