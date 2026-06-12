using System.Diagnostics;
using System.IO;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

public sealed class ExportMainViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public ExportMainViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
        SummaryVm = new ExportSummaryViewModel(applicationVm);
        OptionsVm = new ExportOptionsViewModel(applicationVm);
        DiagnosticsVm = new ExportDiagnosticsViewModel(applicationVm);
        InitDefault();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        StatusText = "Idle";
        LastOutputPath = string.Empty;
        DiagnosticsVm.SetLogText("Scaffold window created. Use Validate to exercise the initial service boundary.");
        LaunchVm.MarkLaunchNotYetConnected();
    }

    private void InitCommands()
    {
        ValidateCommand = new RelayCommand(_ => Validate());
        ExportCommand = new RelayCommand(_ => Export());
        ExportAndOpenFolderCommand = new RelayCommand(_ => ExportAndOpenFolder());
        LoadExecutionScopeCommand = new RelayCommand(_ => LoadExecutionScope());
        PreflightCommand = new RelayCommand(_ => Preflight());
        LaunchRenderCommand = new RelayCommand(_ => LaunchRender());
        RefreshJobCommand = new RelayCommand(_ => RefreshJob());
        UploadPackageCommand = new RelayCommand(_ => UploadPackage());
        DownloadResultCommand = new RelayCommand(_ => DownloadResult());
        OpenDownloadedResultCommand = new RelayCommand(_ => OpenDownloadedResult());
        OpenDownloadedResultFolderCommand = new RelayCommand(_ => OpenDownloadedResultFolder());
        OpenPrimaryArtifactCommand = new RelayCommand(_ => OpenPrimaryArtifact());
        OpenLaunchPackageFolderCommand = new RelayCommand(_ => OpenLaunchPackageFolder());
    }

    #endregion

    #region Functions

    private void Validate()
    {
        UpdateStatus("Validating");
        var result = ApplicationVm.SceneExportService.ValidateCurrentScene();
        ApplyResult(result);
    }

    private void Export()
    {
        UpdateStatus("Exporting");
        var result = ApplicationVm.SceneExportService.ExportCurrentScene(OptionsVm.OutputFolder, OptionsVm.OutputFormat);
        ApplyResult(result);
    }

    private void ExportAndOpenFolder()
    {
        Export();

        if (!string.IsNullOrWhiteSpace(LastOutputPath) && OptionsVm.OpenFolderAfterExport)
        {
            var outputDirectory = Path.GetDirectoryName(LastOutputPath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Process.Start(new ProcessStartInfo(outputDirectory) { UseShellExecute = true });
        }
    }

    private async void LaunchRender()
    {
        UpdateStatus("Launching Render");

        var outputFolder = Path.Combine(OptionsVm.OutputFolder, "OmnibusCloudLaunches");
        Directory.CreateDirectory(outputFolder);

        var request = new MaxSceneLaunchPackageRequest
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

        var result = await Task.Run(() => ApplicationVm.ConnectedRenderService.LaunchRenderAsync(request));

        CurrentJobState = result;
        LaunchVm.ApplyJobState(result);
        DiagnosticsVm.Apply(result.Diagnostics);
        DiagnosticsVm.SetLogText(BuildJobLogText(result));
        LastOutputPath = result.PrimaryArtifactPath;
        UpdateStatus(Guid.TryParse(result.JobId, out _) ? "Render Submitted" : "Launch Failed");
    }

    private async void LoadExecutionScope()
    {
        UpdateStatus("Loading Execution Scope");

        var request = new MaxConnectedExecutionScopeRequest
        {
            CloudUrl = CloudVm.CloudUrl,
            IdentityUrl = CloudVm.IdentityUrl
        };

        var result = await Task.Run(() => ApplicationVm.ConnectedExecutionScopeService.LoadAsync(request));

        CloudVm.ApplyExecutionScope(result);
        LaunchVm.ApplyExecutionScope(result);
        DiagnosticsVm.Apply(result.Diagnostics);
        DiagnosticsVm.SetLogText(BuildExecutionScopeLogText(result));
        UpdateStatus(result.IsSuccess ? "Execution Scope Ready" : "Failed");
    }

    private void Preflight()
    {
        UpdateStatus("Preflight");

        var result = ApplicationVm.ConnectedRenderPreflightService.Run(new MaxSceneLaunchPackageRequest
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
            OutputFolder = Path.Combine(OptionsVm.OutputFolder, "OmnibusCloudLaunches")
        });

        LaunchVm.ApplyPreflight(result);
        DiagnosticsVm.Apply(result.Diagnostics);
        DiagnosticsVm.SetLogText(BuildPreflightLogText(result));
        UpdateStatus(result.CanLaunch ? "Preflight Ready" : "Failed");
    }

    private async void RefreshJob()
    {
        if (CurrentJobState is null)
            return;

        UpdateStatus("Refreshing Job");
        var jobState = CurrentJobState;
        CurrentJobState = await Task.Run(() => ApplicationVm.ConnectedRenderService.RefreshJobAsync(jobState));
        LaunchVm.ApplyJobState(CurrentJobState);
        DiagnosticsVm.Apply(CurrentJobState.Diagnostics);
        DiagnosticsVm.SetLogText(BuildJobLogText(CurrentJobState));
        UpdateStatus("Job Refreshed");
    }

    private void DownloadResult()
    {
        if (CurrentJobState is null)
            return;

        UpdateStatus("Downloading Result");

        var downloadFolder = Path.Combine(OptionsVm.OutputFolder, "OmnibusCloudDownloads");
        Directory.CreateDirectory(downloadFolder);

        var result = ApplicationVm.ConnectedRenderDownloadService.Download(CurrentJobState, downloadFolder);
        LaunchVm.ApplyDownload(result);
        DiagnosticsVm.Apply(result.Diagnostics);
        DiagnosticsVm.SetLogText(BuildDownloadLogText(result));

        if (result.IsSuccess)
            LastOutputPath = result.DownloadedFilePath;

        UpdateStatus(result.IsSuccess ? "Result Downloaded" : "Failed");
    }

    private void UploadPackage()
    {
        if (CurrentJobState is null)
            return;

        UpdateStatus("Uploading Package");

        var result = ApplicationVm.ConnectedRenderPackageUploadService.Upload(CurrentJobState, new MaxConnectedRenderUploadRequest
        {
            CloudUrl = CloudVm.CloudUrl,
            IdentityUrl = CloudVm.IdentityUrl,
            ApiKey = CloudVm.ApiKey,
            PackageArchivePath = CurrentJobState.PackageArchivePath
        });

        LaunchVm.ApplyUpload(result);
        DiagnosticsVm.Apply(result.Diagnostics);
        DiagnosticsVm.SetLogText(BuildUploadLogText(result));

        if (result.IsSuccess)
        {
            CurrentJobState.UploadedPackageBlobId = result.UploadedBlobId;
            CurrentJobState.UploadReceiptPath = result.UploadReceiptPath;
            CurrentJobState.StatusText = result.StatusText;
            CurrentJobState.ProgressPercent = Math.Max(CurrentJobState.ProgressPercent, 45d);
            CurrentJobState.UpdatedUtc = DateTime.UtcNow;
        }

        UpdateStatus(result.IsSuccess ? "Package Uploaded" : "Failed");
    }

    private void OpenDownloadedResult()
    {
        if (string.IsNullOrWhiteSpace(LaunchVm.DownloadedResultPath))
            return;

        Process.Start(new ProcessStartInfo(LaunchVm.DownloadedResultPath) { UseShellExecute = true });
    }

    private void OpenDownloadedResultFolder()
    {
        if (string.IsNullOrWhiteSpace(LaunchVm.DownloadedResultPath))
            return;

        var downloadedResultDirectory = Path.GetDirectoryName(LaunchVm.DownloadedResultPath);
        if (string.IsNullOrWhiteSpace(downloadedResultDirectory))
            return;

        Process.Start(new ProcessStartInfo(downloadedResultDirectory) { UseShellExecute = true });
    }

    private void OpenLaunchPackageFolder()
    {
        if (string.IsNullOrWhiteSpace(LaunchVm.LaunchPackageFolderPath))
            return;

        Process.Start(new ProcessStartInfo(LaunchVm.LaunchPackageFolderPath) { UseShellExecute = true });
    }

    private void OpenPrimaryArtifact()
    {
        if (string.IsNullOrWhiteSpace(LaunchVm.ResultPath))
            return;

        Process.Start(new ProcessStartInfo(LaunchVm.ResultPath) { UseShellExecute = true });
    }

    private void ApplyResult(MaxSceneExportResult result)
    {
        SummaryVm.Apply(result.Summary);
        LaunchVm.ApplySceneDefaults(result.Summary);
        DiagnosticsVm.Apply(result.Diagnostics);
        DiagnosticsVm.SetLogText(BuildLogText(result));
        LastOutputPath = result.OutputPath ?? string.Empty;
        UpdateStatus(result.IsSuccess ? "Ready to Export" : "Failed");
    }

    private static string BuildLogText(MaxSceneExportResult result)
    {
        return $"{result.StatusText}{Environment.NewLine}"
               + $"Scene: {result.Summary.SceneName}{Environment.NewLine}"
               + $"File: {result.Summary.SceneFilePath}{Environment.NewLine}"
               + $"Frames: {result.Summary.FrameStart} - {result.Summary.FrameEnd}{Environment.NewLine}"
               + $"Resolution: {result.Summary.RenderWidth} x {result.Summary.RenderHeight}{Environment.NewLine}"
               + $"Active Camera: {result.Summary.ActiveRenderCameraName}{Environment.NewLine}"
               + $"Materials: {FormatPreview(result.Summary.MaterialNames)}{Environment.NewLine}"
               + $"Textures: {FormatPreview(result.Summary.TextureNames)}{Environment.NewLine}"
               + $"Cameras: {FormatPreview(result.Summary.CameraNames)}{Environment.NewLine}"
               + $"Lights: {FormatPreview(result.Summary.LightNames)}";
    }

    private static string BuildJobLogText(MaxConnectedRenderJobState result)
    {
        return $"{result.StatusText}{Environment.NewLine}"
               + $"Job Id: {result.JobId}{Environment.NewLine}"
               + $"Submitted: {result.SubmittedUtc:u}{Environment.NewLine}"
               + $"Package Folder: {result.PackageFolderPath}{Environment.NewLine}"
               + $"Manifest: {result.ManifestPath}{Environment.NewLine}"
               + $"Submission Receipt: {result.SubmissionReceiptPath}{Environment.NewLine}"
               + $"Primary Artifact: {result.PrimaryArtifactPath}";
    }

    private static string BuildPreflightLogText(MaxConnectedRenderPreflightResult result)
    {
        return $"{result.StatusText}{Environment.NewLine}"
               + $"Can Launch: {result.CanLaunch}{Environment.NewLine}"
               + $"Diagnostics: {result.Diagnostics.Count}";
    }

    private static string BuildExecutionScopeLogText(MaxConnectedExecutionScopeResult result)
    {
        return $"{result.StatusText}{Environment.NewLine}"
               + $"User: {result.UserDisplayName}{Environment.NewLine}"
               + $"Groups: {string.Join(", ", result.Groups.Select(me => me.Name))}{Environment.NewLine}"
               + $"All Clients: {result.CanRunOnAllClients}";
    }

    private static string BuildDownloadLogText(MaxConnectedRenderDownloadResult result)
    {
        return $"{result.StatusText}{Environment.NewLine}"
               + $"Downloaded File: {result.DownloadedFilePath}{Environment.NewLine}"
               + $"Diagnostics: {result.Diagnostics.Count}";
    }

    private static string BuildUploadLogText(MaxConnectedRenderUploadResult result)
    {
        return $"{result.StatusText}{Environment.NewLine}"
               + $"Uploaded Blob Id: {result.UploadedBlobId}{Environment.NewLine}"
               + $"Archive: {result.PackageArchivePath}{Environment.NewLine}"
               + $"Upload Receipt: {result.UploadReceiptPath}{Environment.NewLine}"
               + $"Diagnostics: {result.Diagnostics.Count}";
    }

    private static string FormatPreview(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return "None";

        var preview = string.Join(", ", values.Take(5));
        return values.Count > 5 ? $"{preview}, ..." : preview;
    }

    private void UpdateStatus(string statusText)
    {
        StatusText = statusText;
    }

    #endregion

    #region Properties

    public ExportSummaryViewModel SummaryVm { get; }

    public ExportOptionsViewModel OptionsVm { get; }

    public ExportDiagnosticsViewModel DiagnosticsVm { get; }

    public CloudSessionViewModel CloudVm => ApplicationVm.CloudSessionVm;

    public RenderLaunchViewModel LaunchVm => ApplicationVm.RenderLaunchVm;

    public RelayCommand ValidateCommand { get; private set; } = null!;

    public RelayCommand ExportCommand { get; private set; } = null!;

    public RelayCommand ExportAndOpenFolderCommand { get; private set; } = null!;

    public RelayCommand LoadExecutionScopeCommand { get; private set; } = null!;

    public RelayCommand PreflightCommand { get; private set; } = null!;

    public RelayCommand LaunchRenderCommand { get; private set; } = null!;

    public RelayCommand RefreshJobCommand { get; private set; } = null!;

    public RelayCommand UploadPackageCommand { get; private set; } = null!;

    public RelayCommand DownloadResultCommand { get; private set; } = null!;

    public RelayCommand OpenDownloadedResultCommand { get; private set; } = null!;

    public RelayCommand OpenDownloadedResultFolderCommand { get; private set; } = null!;

    public RelayCommand OpenPrimaryArtifactCommand { get; private set; } = null!;

    public RelayCommand OpenLaunchPackageFolderCommand { get; private set; } = null!;

    public MaxConnectedRenderJobState? CurrentJobState { get; private set; }

    [Notify]
    public string StatusText { get; set; }

    [Notify]
    public string LastOutputPath { get; set; }

    #endregion
}
