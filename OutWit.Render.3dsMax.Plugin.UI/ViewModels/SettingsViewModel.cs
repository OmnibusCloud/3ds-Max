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
    #region Constants

    private const string WEBSITE_URL = "https://omnibuscloud.io";

    private const string COMMUNITY_URL = "https://www.reddit.com/r/OmnibusCloud/";

    /// <summary>Persisted keys ↔ the friendly labels the combos show (design 4.3 Output).</summary>
    private static readonly IReadOnlyList<KeyValuePair<string, string>> EXPORT_TARGETS =
    [
        new("Blend", "Blender scene (.blend)"),
        new("DccJson", "DCC JSON (.json)")
    ];

    #endregion

    #region Events

    public event Action<bool>? DialogClosed;

    #endregion

    #region Fields

    private bool m_updateCheckStarted;

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
        AvailableExportTargets = new ObservableCollection<string>(EXPORT_TARGETS.Select(me => me.Value));
        AvailableVideoPresets = new ObservableCollection<string>(MaxRenderOutputCatalog.VideoPresets.Select(me => me.Value));
        AvailableImageFormats = new ObservableCollection<string>(MaxRenderOutputCatalog.ImageFormats);
        AvailableLogLevels = ["Information", "Debug", "Warning", "Error"];
        PluginVersion = MaxPluginVersionInfo.Resolve();
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
        OpenWebsiteCommand = new RelayCommand(_ => OpenUrl(WEBSITE_URL));
        OpenCommunityCommand = new RelayCommand(_ => OpenUrl(COMMUNITY_URL));
        CheckUpdatesCommand = new RelayCommandAsync(CheckUpdatesAsync);
        OpenDownloadPageCommand = new RelayCommand(_ => OpenUrl(MaxUpdateCheckService.DOWNLOAD_PAGE_URL));
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
        SelectedExportTarget = ExportTargetDisplay(Settings.ExportTarget);
        OutputFolder = Settings.OutputFolder;
        SelectedVideoPreset = MaxRenderOutputCatalog.VideoPresetDisplay(Settings.VideoContainer);
        SelectedImageFormat = MaxRenderOutputCatalog.NormalizeImageFormat(Settings.ImageFormat);
        SelectedLogLevel = Coalesce(Settings.LogLevel, "Information");
        UpdateStatus();
        return Task.CompletedTask;
    }

    private void Save()
    {
        Settings.ThemeMode = SelectedTheme;
        Settings.RememberLastRenderSettings = RememberLastRenderSettings;
        Settings.ExportTarget = ExportTargetKey(SelectedExportTarget);
        Settings.OutputFolder = OutputFolder;
        Settings.VideoContainer = MaxRenderOutputCatalog.VideoPresetKeyFromDisplay(SelectedVideoPreset);
        Settings.ImageFormat = MaxRenderOutputCatalog.NormalizeImageFormat(SelectedImageFormat);
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

    private static string ExportTargetDisplay(string? key) =>
        EXPORT_TARGETS.FirstOrDefault(me => me.Key == key, EXPORT_TARGETS[0]).Value;

    private static string ExportTargetKey(string? display) =>
        EXPORT_TARGETS.FirstOrDefault(me => me.Value == display, EXPORT_TARGETS[0]).Key;

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort — opening a link must never break Settings.
        }
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Section))
            return;

        UpdateStatus();

        // First visit to About kicks a silent update check (mockup 4.3 "Check for updates" also
        // stays available as an explicit button).
        if (Section == SettingsSection.About && !m_updateCheckStarted)
        {
            m_updateCheckStarted = true;
            _ = CheckUpdatesAsync();
        }
    }

    private async Task CheckUpdatesAsync()
    {
        m_updateCheckStarted = true;
        UpdateStatusText = "Checking for updates…";
        IsUpdateAvailable = false;

        var result = await Task.Run(() => new MaxUpdateCheckService().CheckAsync());

        IsUpdateAvailable = result.UpdateAvailable;
        UpdateStatusText = result.StatusText;
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

    // Output (combos show friendly display labels; the persisted keys are mapped on load/save)
    public ObservableCollection<string> AvailableExportTargets { get; private set; } = null!;

    public ObservableCollection<string> AvailableVideoPresets { get; private set; } = null!;

    public ObservableCollection<string> AvailableImageFormats { get; private set; } = null!;

    [Notify]
    public string SelectedExportTarget { get; set; } = string.Empty;

    [Notify]
    public string OutputFolder { get; set; } = string.Empty;

    [Notify]
    public string SelectedVideoPreset { get; set; } = string.Empty;

    [Notify]
    public string SelectedImageFormat { get; set; } = "PNG";

    // Diagnostics
    public ObservableCollection<string> AvailableLogLevels { get; private set; } = null!;

    [Notify]
    public string SelectedLogLevel { get; set; } = "Information";

    // About
    [Notify]
    public string PluginVersion { get; set; } = string.Empty;

    [Notify]
    public string HostVersion { get; set; } = string.Empty;

    [Notify]
    public string UpdateStatusText { get; set; } = string.Empty;

    [Notify]
    public bool IsUpdateAvailable { get; set; }

    #endregion

    #region Commands

    public ICommand SaveCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand TestConnectionCommand { get; private set; } = null!;

    public ICommand BrowseOutputFolderCommand { get; private set; } = null!;

    public ICommand OpenLogsFolderCommand { get; private set; } = null!;

    public ICommand OpenLastLogCommand { get; private set; } = null!;

    public ICommand OpenPortalCommand { get; private set; } = null!;

    public ICommand OpenWebsiteCommand { get; private set; } = null!;

    public ICommand OpenCommunityCommand { get; private set; } = null!;

    public ICommand CheckUpdatesCommand { get; private set; } = null!;

    public ICommand OpenDownloadPageCommand { get; private set; } = null!;

    #endregion

    #region Services

    private MaxPluginSettings Settings => ApplicationVm.Settings;

    private MaxConnectedExecutionScopeService ExecutionScope => ApplicationVm.ConnectedExecutionScopeService;

    #endregion
}
