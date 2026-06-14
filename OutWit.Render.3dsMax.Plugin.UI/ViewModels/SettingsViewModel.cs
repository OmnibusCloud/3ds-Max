using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
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
/// Settings dialog (design 4.3): a sidebar of sections (General / Connection / Output / Diagnostics /
/// About) over a swappable content panel, with Cancel / Save. No business logic in the View — this
/// loads from and persists to <see cref="MaxPluginSettings"/> and raises <see cref="DialogClosed"/>.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event Action<bool>? DialogClosed;

    #endregion

    #region Constructors

    public SettingsViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
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
        Section = SettingsSection.General;
        AvailableThemes = ["FollowMax", "Dark", "Light"];
        AvailableExportTargets = ["Blend", "DccJson"];
        AvailableVideoContainers = ["mp4", "mov", "webm"];
        AvailableLogLevels = ["Information", "Debug", "Warning", "Error"];
        PluginVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        HostVersion = "3ds Max 2027";
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
    }

    private void InitCommands()
    {
        SaveCommand = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => Cancel());
        TestConnectionCommand = new RelayCommandAsync(TestConnectionAsync);
        BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder());
        OpenLogsFolderCommand = new RelayCommand(_ => MaxDiagnosticsLauncher.OpenLogsFolder());
        OpenLastLogCommand = new RelayCommand(_ => MaxDiagnosticsLauncher.OpenLatestLog());
        OpenPortalCommand = new RelayCommand(_ => OpenPortal());
        UpdateStatus();
    }

    #endregion

    #region Functions

    public Task InitializeAsync()
    {
        SelectedTheme = Coalesce(Settings.ThemeMode, "FollowMax");
        RememberLastRenderSettings = Settings.RememberLastRenderSettings;
        CloudUrl = CloudVm.CloudUrl;
        IdentityUrl = CloudVm.IdentityUrl;
        SelectedExportTarget = Coalesce(Settings.ExportTarget, "Blend");
        OutputFolder = Settings.OutputFolder;
        SelectedVideoContainer = Coalesce(Settings.VideoContainer, "mp4");
        SelectedLogLevel = Coalesce(Settings.LogLevel, "Information");
        UpdateStatus();
        return Task.CompletedTask;
    }

    private void Save()
    {
        Settings.ThemeMode = SelectedTheme;
        Settings.RememberLastRenderSettings = RememberLastRenderSettings;
        Settings.ExportTarget = SelectedExportTarget;
        Settings.OutputFolder = OutputFolder;
        Settings.VideoContainer = SelectedVideoContainer;
        Settings.LogLevel = SelectedLogLevel;
        Settings.SettingsManager.Save();

        // Connection URLs live on the session VM (used by the next sign-in / connect).
        CloudVm.CloudUrl = CloudUrl;
        CloudVm.IdentityUrl = IdentityUrl;

        DialogClosed?.Invoke(true);
    }

    private void Cancel()
    {
        DialogClosed?.Invoke(false);
    }

    private async Task TestConnectionAsync()
    {
        ConnectionStatus = "Testing…";
        UpdateStatus();

        var request = new MaxConnectedExecutionScopeRequest
        {
            CloudUrl = CloudUrl,
            IdentityUrl = IdentityUrl
        };

        var result = await Task.Run(() => ExecutionScope.LoadAsync(request));
        ConnectionStatus = result.IsSuccess ? "Connected" : $"Failed: {result.StatusText}";
        IsConnectionOk = result.IsSuccess;
        UpdateStatus();
    }

    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose default output folder",
            InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder : string.Empty
        };

        if (dialog.ShowDialog() == true)
            OutputFolder = dialog.FolderName;
    }

    private void OpenPortal()
    {
        var portalUrl = string.IsNullOrWhiteSpace(CloudUrl) ? "https://omnibuscloud.com" : CloudUrl;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(portalUrl) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort — opening the portal must never break Settings.
        }
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        IsGeneral = Section == SettingsSection.General;
        IsConnection = Section == SettingsSection.Connection;
        IsOutput = Section == SettingsSection.Output;
        IsDiagnostics = Section == SettingsSection.Diagnostics;
        IsAbout = Section == SettingsSection.About;
    }

    private static string Coalesce(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Section))
            UpdateStatus();
    }

    #endregion

    #region Properties

    public CloudSessionViewModel CloudVm => ApplicationVm.CloudSessionVm;

    [Notify]
    public SettingsSection Section { get; set; }

    [Notify]
    public bool IsGeneral { get; set; }

    [Notify]
    public bool IsConnection { get; set; }

    [Notify]
    public bool IsOutput { get; set; }

    [Notify]
    public bool IsDiagnostics { get; set; }

    [Notify]
    public bool IsAbout { get; set; }

    // General
    public ObservableCollection<string> AvailableThemes { get; private set; } = null!;

    [Notify]
    public string SelectedTheme { get; set; } = "FollowMax";

    [Notify]
    public bool RememberLastRenderSettings { get; set; }

    // Connection
    [Notify]
    public string CloudUrl { get; set; } = string.Empty;

    [Notify]
    public string IdentityUrl { get; set; } = string.Empty;

    [Notify]
    public string ConnectionStatus { get; set; } = string.Empty;

    [Notify]
    public bool IsConnectionOk { get; set; }

    // Output
    public ObservableCollection<string> AvailableExportTargets { get; private set; } = null!;

    public ObservableCollection<string> AvailableVideoContainers { get; private set; } = null!;

    [Notify]
    public string SelectedExportTarget { get; set; } = "Blend";

    [Notify]
    public string OutputFolder { get; set; } = string.Empty;

    [Notify]
    public string SelectedVideoContainer { get; set; } = "mp4";

    // Diagnostics
    public ObservableCollection<string> AvailableLogLevels { get; private set; } = null!;

    [Notify]
    public string SelectedLogLevel { get; set; } = "Information";

    // About
    [Notify]
    public string PluginVersion { get; set; } = string.Empty;

    [Notify]
    public string HostVersion { get; set; } = string.Empty;

    #endregion

    #region Commands

    public ICommand SaveCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand TestConnectionCommand { get; private set; } = null!;

    public ICommand BrowseOutputFolderCommand { get; private set; } = null!;

    public ICommand OpenLogsFolderCommand { get; private set; } = null!;

    public ICommand OpenLastLogCommand { get; private set; } = null!;

    public ICommand OpenPortalCommand { get; private set; } = null!;

    #endregion

    #region Services

    private MaxPluginSettings Settings => ApplicationVm.Settings;

    private MaxConnectedExecutionScopeService ExecutionScope => ApplicationVm.ConnectedExecutionScopeService;

    #endregion
}
