using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

/// <summary>
/// Export dialog (design 4.2): export the scene as a server-built Blender <c>.blend</c> (default) or a
/// local DCC JSON. No business logic in the View — this owns the export lifecycle (Ready → Exporting →
/// Completed/Failed). Blender export is the connected <c>ExportBlend</c> round-trip; DCC JSON is the
/// local <see cref="MaxSceneExportService"/> path.
/// </summary>
public sealed class ExportDialogViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event Action<bool>? DialogClosed;

    #endregion

    #region Fields

    private bool m_cancelRequested;

    private MaxConnectedRenderJobState? m_activeJobState;

    #endregion

    #region Constructors

    public ExportDialogViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
        InitDefault();
        InitEvents();
        InitCommands();

        _ = InitializeAsync();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Target = string.Equals(Settings.ExportTarget, "DccJson", StringComparison.OrdinalIgnoreCase)
            ? ExportTarget.DccJson
            : ExportTarget.Blend;
        PackAssets = true;
        OutputFolder = string.IsNullOrWhiteSpace(Settings.OutputFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : Settings.OutputFolder;
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        CloudVm.PropertyChanged += OnCloudPropertyChanged;
    }

    private void InitCommands()
    {
        ExportCommand = new RelayCommandAsync(ExportAsync);
        CancelCommand = new RelayCommand(_ => Cancel());
        BrowseCommand = new RelayCommand(_ => Browse());
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
        NewExportCommand = new RelayCommand(_ => NewExport());
        UpdateStatus();
    }

    #endregion

    #region Functions

    private async Task InitializeAsync()
    {
        // Scene reads go through the single-threaded Max SDK and must run on the Max main
        // thread. Dialog-open uses the SummaryOnly capture profile — the full geometry capture
        // of a heavy scene freezes the application for minutes; the real capture happens on
        // the export action itself.
        var summary = SceneExport.CollectSummary(MaxSceneCaptureOptions.SummaryOnly);
        SummaryVm.Apply(summary);
        UpdateStatus();

        // Silent session restore so the default Blender target is available without a browser trip.
        await CloudVm.EnsureSessionRestoredAsync();
        UpdateStatus();
    }

    private async Task ExportAsync()
    {
        m_cancelRequested = false;
        IsExporting = true;
        IsCompleted = false;
        IsFailed = false;
        ErrorMessage = string.Empty;
        PersistSettings();
        UpdateStatus();

        try
        {
            if (Target == ExportTarget.DccJson)
                await ExportDccJsonAsync();
            else
                await ExportBlendAsync();
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
        finally
        {
            IsExporting = false;
            UpdateStatus();
        }
    }

    private Task ExportDccJsonAsync()
    {
        StatusLine = "Exporting DCC JSON…";

        // Scene capture + write go through the single-threaded 3ds Max SDK: run synchronously on the
        // Max main thread (no Task.Run).
        var result = SceneExport.ExportCurrentScene(OutputFolder, MaxSceneExportOutputFormat.Json);
        DiagnosticsVm.Apply(result.Diagnostics);

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.OutputPath))
            Complete(result.OutputPath!);
        else
            Fail(result.StatusText);

        return Task.CompletedTask;
    }

    private async Task ExportBlendAsync()
    {
        StatusLine = "Converting to Blender on the server…";

        var outputFolder = Path.Combine(OutputFolder, "OmnibusCloudExports");
        Directory.CreateDirectory(outputFolder);

        var request = new MaxSceneLaunchPackageRequest
        {
            CloudUrl = CloudVm.CloudUrl,
            IdentityUrl = CloudVm.IdentityUrl,
            RenderMode = "ExportBlend",
            OutputFolder = outputFolder
        };

        // No Task.Run: the launch captures the scene through the single-threaded 3ds Max SDK and must
        // stay on the Max main thread; only the submission part awaits the network.
        var jobState = await ConnectedRender.LaunchRenderAsync(request);
        DiagnosticsVm.Apply(jobState.Diagnostics);

        if (!Guid.TryParse(jobState.JobId, out _))
        {
            Fail(jobState.StatusText);
            return;
        }

        m_activeJobState = jobState;

        try
        {
            while (!jobState.IsCompleted)
            {
                if (jobState.IsCancelled)
                {
                    StatusLine = "Export cancelled";
                    return;
                }

                if (!m_cancelRequested
                    && !string.IsNullOrWhiteSpace(jobState.StatusText)
                    && jobState.StatusText.Contains("Failed", StringComparison.OrdinalIgnoreCase))
                {
                    Fail(jobState.StatusText);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2));

                jobState = await ConnectedRender.RefreshJobAsync(jobState);
                m_activeJobState = jobState;
                DiagnosticsVm.Apply(jobState.Diagnostics);
            }
        }
        finally
        {
            m_activeJobState = null;
        }

        if (jobState.IsCompleted && !string.IsNullOrWhiteSpace(jobState.PrimaryArtifactPath))
            Complete(jobState.PrimaryArtifactPath);
        else if (!m_cancelRequested)
            Fail(jobState.StatusText);
    }

    private void Complete(string resultPath)
    {
        ResultPath = resultPath;
        ResultFileName = Path.GetFileName(resultPath);
        IsCompleted = true;
        StatusLine = "Export complete";
    }

    private void Fail(string message)
    {
        IsFailed = true;
        ErrorMessage = string.IsNullOrWhiteSpace(message) ? "Export failed" : message;
        StatusLine = "Export failed";
    }

    private void Cancel()
    {
        if (IsExporting)
        {
            m_cancelRequested = true;
            StatusLine = "Cancelling…";

            // Actually stop the server-side ExportBlend job; the poll loop observes the terminal
            // cancelled status. Fire-and-forget by design: the loop keeps running either way.
            var jobState = m_activeJobState;
            if (jobState != null)
                _ = ConnectedRender.CancelJobAsync(jobState);

            return;
        }

        DialogClosed?.Invoke(false);
    }

    private void Browse()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose export folder",
            InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder : string.Empty
        };

        if (dialog.ShowDialog() == true)
            OutputFolder = dialog.FolderName;
    }

    private void OpenFolder()
    {
        var folder = string.IsNullOrWhiteSpace(ResultPath) ? OutputFolder : Path.GetDirectoryName(ResultPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void NewExport()
    {
        IsCompleted = false;
        IsFailed = false;
        ResultPath = string.Empty;
        ResultFileName = string.Empty;
        ErrorMessage = string.Empty;
        StatusLine = string.Empty;
        UpdateStatus();
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        // The Blender target is a server round-trip and needs a session; DCC JSON is local.
        var targetReady = Target == ExportTarget.DccJson || CloudVm.IsSignedIn;
        CanExport = targetReady && !IsExporting && !IsCompleted;
        CanCancel = IsExporting;
        IsBlend = Target == ExportTarget.Blend;
        IsReady = !IsExporting && !IsCompleted && !IsFailed;
    }

    private void PersistSettings()
    {
        Settings.ExportTarget = Target == ExportTarget.DccJson ? "DccJson" : "Blend";
        Settings.OutputFolder = OutputFolder;
        Settings.SettingsManager.Save();
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Target) or nameof(IsExporting) or nameof(IsCompleted) or nameof(IsFailed))
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

    public ExportDiagnosticsViewModel DiagnosticsVm => ApplicationVm.MainVm.DiagnosticsVm;

    public CloudSessionViewModel CloudVm => ApplicationVm.CloudSessionVm;

    [Notify]
    public ExportTarget Target { get; set; }

    [Notify]
    public bool IsBlend { get; set; }

    [Notify]
    public bool PackAssets { get; set; }

    [Notify]
    public string OutputFolder { get; set; } = string.Empty;

    [Notify]
    public bool IsReady { get; set; }

    [Notify]
    public bool IsExporting { get; set; }

    [Notify]
    public bool IsCompleted { get; set; }

    [Notify]
    public bool IsFailed { get; set; }

    [Notify]
    public string StatusLine { get; set; } = string.Empty;

    [Notify]
    public string ResultPath { get; set; } = string.Empty;

    [Notify]
    public string ResultFileName { get; set; } = string.Empty;

    [Notify]
    public string ErrorMessage { get; set; } = string.Empty;

    [Notify]
    public bool CanExport { get; set; }

    [Notify]
    public bool CanCancel { get; set; }

    #endregion

    #region Commands

    public ICommand ExportCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand BrowseCommand { get; private set; } = null!;

    public ICommand OpenFolderCommand { get; private set; } = null!;

    public ICommand NewExportCommand { get; private set; } = null!;

    #endregion

    #region Services

    private MaxSceneExportService SceneExport => ApplicationVm.SceneExportService;

    private MaxConnectedRenderService ConnectedRender => ApplicationVm.ConnectedRenderService;

    private MaxPluginSettings Settings => ApplicationVm.Settings;

    #endregion
}
