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

    /// <summary>The scope the server-side build is submitted under — picked, never asked.</summary>
    private MaxServerJobScope m_scope = new();

    private bool m_scopeLoaded;

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
        // Shared with the Render dialog — the user's bake preference applies to both round-trips.
        BakeVRayScannedMaterials = Settings.BakeVRayScannedMaterials;
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
        RetryCommand = new RelayCommandAsync(RetryAsync);
        CopyLogCommand = new RelayCommand(_ => CopyLog());
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

        // The scanned-material bake option only makes sense when the scene actually carries
        // V-Ray scanned materials — the collector's diagnostics already name them.
        HasVRayScannedMaterials = summary.UnmappedPluginClasses.Keys
            .Any(me => me.Contains("VRayScannedMtl", StringComparison.OrdinalIgnoreCase));
        UpdateStatus();

        // Silent session restore so the default Blender target is available without a browser trip.
        await CloudVm.EnsureSessionRestoredAsync();
        await LoadExecutionScopeAsync();
        UpdateStatus();
    }

    /// <summary>
    /// Resolves the scope the server-side .blend build is submitted under. The build never reaches a
    /// render node (Render.Dcc is a host-only controller), so the scope is only the server's
    /// submit-time permission check — an unscoped submit needs the whole-network right, which is why
    /// non-admin exports once died with "not authorized to launch on all clients". It used to be a
    /// "Run on" picker, a choice that changed nothing about where the build ran; it is now picked.
    /// </summary>
    private async Task LoadExecutionScopeAsync()
    {
        if (!CloudVm.IsSignedIn)
            return;

        var request = new MaxConnectedExecutionScopeRequest
        {
            CloudUrl = CloudVm.CloudUrl,
            IdentityUrl = CloudVm.IdentityUrl
        };

        var result = await Task.Run(() => ApplicationVm.ConnectedExecutionScopeService.LoadAsync(request));
        m_scope = MaxServerJobScopeResolver.Resolve(result);
        m_scopeLoaded = result.IsSuccess;

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
        var result = SceneExport.ExportCurrentScene(OptionsVm.EffectiveOutputFolder, MaxSceneExportOutputFormat.Json);
        DiagnosticsVm.Apply(result.Diagnostics);

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.OutputPath))
            Complete(result.OutputPath!);
        else
            Fail(result.StatusText);

        return Task.CompletedTask;
    }

    private async Task ExportBlendAsync()
    {
        StatusLine = "Preparing the scene…";

        // The launch package is the export's INPUT (the scene payload, often tens of MB with textures),
        // not something the artist asked for: it goes to a working folder and is discarded once the
        // .blend has been saved. It used to be written into the "Save to" folder while the .blend itself
        // stayed in %TEMP% — exactly backwards.
        var packageFolder = Path.Combine(Path.GetTempPath(), "OmnibusCloudExports");
        Directory.CreateDirectory(packageFolder);

        var request = new MaxSceneLaunchPackageRequest
        {
            CloudUrl = CloudVm.CloudUrl,
            IdentityUrl = CloudVm.IdentityUrl,
            RenderMode = "ExportBlend",
            OutputFolder = packageFolder,
            UseAllClients = m_scope.UseAllClients,
            SelectedGroupName = m_scope.GroupName,
            SelectedProjectName = m_scope.ProjectName,
            // The server packs every attachment into the .blend (pack_all), so a baked scanned
            // material travels inside the returned file like any authored texture.
            BakeVRayScannedMaterials = HasVRayScannedMaterials && BakeVRayScannedMaterials,
            // Scalar text-only binding — safe to set from a worker continuation.
            UploadProgress = fraction => StatusLine = $"Uploading scene… {(int)Math.Round(fraction * 100d)}%"
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
        var buildStartedUtc = DateTime.UtcNow;

        try
        {
            while (!jobState.IsCompleted)
            {
                if (jobState.IsCancelled)
                {
                    StatusLine = "Export cancelled";
                    return;
                }

                // Failure comes from the SERVER status, not from sniffing the status text: a transient
                // "Job refresh failed." (one dropped poll) used to abort an export that was still
                // converting on the farm.
                if (!m_cancelRequested && MaxConnectedRenderJobStatusMapper.HasFailed(jobState))
                {
                    Fail(jobState.StatusText);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2));

                jobState = await ConnectedRender.RefreshJobAsync(jobState);
                m_activeJobState = jobState;
                DiagnosticsVm.Apply(jobState.Diagnostics);

                // No percentage: the build is ONE long server-side step of a three-activity script
                // (unzip, build, clear), and the engine's axis counts finished activities — it read
                // 33% for the whole build and then jumped to done. Nothing distributed, so there is
                // no finer axis either. Elapsed time is the honest signal that it is still working.
                StatusLine = m_cancelRequested
                    ? "Cancelling…"
                    : $"Building the .blend on the server · {FormatElapsed(DateTime.UtcNow - buildStartedUtc)}";
            }
        }
        finally
        {
            m_activeJobState = null;
        }

        if (jobState.IsCompleted && !string.IsNullOrWhiteSpace(jobState.PrimaryArtifactPath))
            Complete(DeliverResult(jobState));
        else if (!m_cancelRequested)
            Fail(jobState.StatusText);
    }

    /// <summary>
    /// Moves the downloaded .blend into the "Save to" folder under the scene's name, then drops the
    /// launch package. When the folder cannot be written the file stays where it was downloaded and the
    /// completed card points there (Open folder), with the reason in the diagnostics.
    /// </summary>
    private string DeliverResult(MaxConnectedRenderJobState jobState)
    {
        StatusLine = "Saving the .blend…";

        var delivery = ApplicationVm.ConnectedRenderDownloadService.Deliver(jobState, OptionsVm.EffectiveOutputFolder, SummaryVm.SceneName);
        DiagnosticsVm.Apply(delivery.Diagnostics);

        if (delivery.IsSuccess)
            MaxSceneLaunchPreparationService.Discard(jobState.PackageFolderPath, jobState.PackageArchivePath);

        return string.IsNullOrWhiteSpace(delivery.DownloadedFilePath) ? jobState.PrimaryArtifactPath : delivery.DownloadedFilePath;
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";

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
            InitialDirectory = Directory.Exists(OptionsVm.EffectiveOutputFolder) ? OptionsVm.EffectiveOutputFolder : string.Empty
        };

        // The shared "Save to": persisted at once and seen by Render and Settings too.
        if (dialog.ShowDialog() == true)
            OptionsVm.OutputFolder = dialog.FolderName;
    }

    private void OpenFolder()
    {
        var folder = string.IsNullOrWhiteSpace(ResultPath) ? OptionsVm.EffectiveOutputFolder : Path.GetDirectoryName(ResultPath);
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

    private async Task RetryAsync()
    {
        NewExport();
        await ExportAsync();
    }

    private void CopyLog()
    {
        var text = $"{ErrorMessage}\n\n{DiagnosticsVm.LogText}";

        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard access can fail when another app holds it — never break the dialog.
        }
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        // The Blender target is a server round-trip and needs a session PLUS an authorizing scope for
        // the submit (see LoadExecutionScopeAsync); DCC JSON is local.
        var targetReady = Target == ExportTarget.DccJson || (CloudVm.IsSignedIn && m_scope.IsResolved);
        CanExport = targetReady && !IsExporting && !IsCompleted;
        CanCancel = IsExporting;
        IsBlend = Target == ExportTarget.Blend;
        IsReady = !IsExporting && !IsCompleted && !IsFailed;

        // Only once the scope is known to be empty — not while it is still loading.
        ShowNoTargetsHint = IsBlend && CloudVm.IsSignedIn && m_scopeLoaded && !m_scope.IsResolved;
    }

    private void PersistSettings()
    {
        // "Save to" is not written here: the shared OptionsVm persists it the moment it changes, and
        // writing this dialog's view of it back used to undo a change made in Settings meanwhile.
        Settings.ExportTarget = Target == ExportTarget.DccJson ? "DccJson" : "Blend";
        Settings.BakeVRayScannedMaterials = BakeVRayScannedMaterials;
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
        {
            UpdateStatus();
            if (CloudVm.IsSignedIn)
                _ = LoadExecutionScopeAsync();
        }
    }

    #endregion

    #region Properties

    public ExportSummaryViewModel SummaryVm => ApplicationVm.MainVm.SummaryVm;

    public ExportDiagnosticsViewModel DiagnosticsVm => ApplicationVm.MainVm.DiagnosticsVm;

    /// <summary>Session-wide output options — its OutputFolder is the one shared "Save to".</summary>
    public ExportOptionsViewModel OptionsVm => ApplicationVm.MainVm.OptionsVm;

    public CloudSessionViewModel CloudVm => ApplicationVm.CloudSessionVm;

    [Notify]
    public ExportTarget Target { get; set; }

    [Notify]
    public bool IsBlend { get; set; }

    [Notify]
    public bool BakeVRayScannedMaterials { get; set; }

    [Notify]
    public bool HasVRayScannedMaterials { get; set; }

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

    /// <summary>The signed-in account has no scope the server would accept for the build.</summary>
    [Notify]
    public bool ShowNoTargetsHint { get; set; }

    #endregion

    #region Commands

    public ICommand ExportCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand BrowseCommand { get; private set; } = null!;

    public ICommand OpenFolderCommand { get; private set; } = null!;

    public ICommand NewExportCommand { get; private set; } = null!;

    public ICommand RetryCommand { get; private set; } = null!;

    public ICommand CopyLogCommand { get; private set; } = null!;

    #endregion

    #region Services

    private MaxSceneExportService SceneExport => ApplicationVm.SceneExportService;

    private MaxConnectedRenderService ConnectedRender => ApplicationVm.ConnectedRenderService;

    private MaxPluginSettings Settings => ApplicationVm.Settings;

    #endregion
}
