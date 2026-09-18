using OutWit.Cloud.Auth;
using OutWit.Cloud.Auth.Browser;
using OutWit.Cloud.Auth.Callbacks;
using OutWit.Cloud.Auth.Interfaces;
using OutWit.Cloud.Auth.Sessions;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;
using Serilog;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Composition root for the plugin's runtime service graph. Keeping construction here lets the
/// <c>ApplicationViewModel</c> stay a pure container of ViewModels (no service construction or
/// business logic). Built once per UI session from the host-provided <see cref="MaxSceneExportService"/>
/// (which carries the host-only scene snapshot provider); also owns the per-user settings and the
/// shared logger. Mirrors the desktop client's runtime bootstrap.
/// </summary>
public sealed class MaxPluginServices
{
    #region Constructors

    public MaxPluginServices(
        MaxSceneExportService sceneExportService,
        IMaxStatusBarService? statusBar = null,
        IMaxTimeSliderService? timeSlider = null)
    {
        SceneExportService = sceneExportService;
        StatusBar = statusBar ?? MaxStatusBarServiceNull.Instance;
        TimeSlider = timeSlider ?? MaxTimeSliderServiceNull.Instance;
        Logger = MaxPluginLogging.Logger;
        Settings = MaxPluginSettingsFactory.Create();
        MaxPluginLogging.ApplyMinimumLevel(Settings.LogLevel);

        // The shared OutWit.Cloud.Auth stack (browser PKCE + loopback callback + encrypted
        // session store); the plugin keeps its own registered client id and session file.
        BrowserLauncher = new SystemBrowserLauncherShell(Logger);
        CloudSessionService = new MaxCloudSessionService(
            new TokenService(
                Logger,
                BrowserLauncher,
                new AuthorizationCallbackListenerFactoryLoopback(Logger),
                MaxCloudSessionService.CLIENT_ID),
            new SessionStore(MaxCloudSessionService.ResolveDefaultSessionFilePath(), Logger));
        CloudConnectionService = new MaxCloudConnectionService(CloudSessionService);
        LaunchPreparationService = new MaxSceneLaunchPreparationService(sceneExportService);
        ConnectedRenderPreflightService = new MaxConnectedRenderPreflightService(sceneExportService);
        ConnectedExecutionScopeService = new MaxConnectedExecutionScopeService(CloudSessionService, CloudConnectionService);
        ConnectedRenderSubmissionService = new MaxConnectedRenderSubmissionService(
            new MaxConnectedRenderSubmissionTransportOmnibusCloudSession(CloudConnectionService, new MaxConnectedRenderSceneAttachmentService()));
        ConnectedRenderService = new MaxConnectedRenderService(LaunchPreparationService, ConnectedRenderPreflightService, ConnectedRenderSubmissionService);
        ConnectedRenderPackageUploadService = new MaxConnectedRenderPackageUploadService(new MaxConnectedRenderArchiveUploaderOmnibusCloudApiKey());
        ConnectedRenderDownloadService = new MaxConnectedRenderDownloadService();

        // The job lifecycle is session-scoped, not dialog-scoped: the Render dialog is rebuilt on every
        // open, so a job tracked by it stopped being reachable the moment it closed.
        ConnectedRenderJobStore = new MaxConnectedRenderJobStore();
        ConnectedRenderJobTracker = new MaxConnectedRenderJobTracker(
            ConnectedRenderService, ConnectedRenderJobStore, StatusBar, Logger,
            downloadService: ConnectedRenderDownloadService);
    }

    #endregion

    #region Properties

    /// <summary>Scene export/validation (wraps the host-only scene snapshot provider).</summary>
    public MaxSceneExportService SceneExportService { get; }

    /// <summary>Host prompt-line status reporting (no-op when there is no Max host).</summary>
    public IMaxStatusBarService StatusBar { get; }

    /// <summary>The host time slider a still render follows (no-op when there is no Max host).</summary>
    public IMaxTimeSliderService TimeSlider { get; }

    /// <summary>Shared Serilog logger writing to the per-user logs directory.</summary>
    public ILogger Logger { get; }

    /// <summary>Per-user plugin preferences.</summary>
    public MaxPluginSettings Settings { get; }

    public ISystemBrowserLauncher BrowserLauncher { get; }

    public IMaxCloudSessionService CloudSessionService { get; }

    public IMaxCloudConnectionService CloudConnectionService { get; }

    public MaxSceneLaunchPreparationService LaunchPreparationService { get; }

    public MaxConnectedRenderPreflightService ConnectedRenderPreflightService { get; }

    public MaxConnectedExecutionScopeService ConnectedExecutionScopeService { get; }

    public MaxConnectedRenderSubmissionService ConnectedRenderSubmissionService { get; }

    public MaxConnectedRenderService ConnectedRenderService { get; }

    public MaxConnectedRenderPackageUploadService ConnectedRenderPackageUploadService { get; }

    public MaxConnectedRenderDownloadService ConnectedRenderDownloadService { get; }

    /// <summary>Per-user record of the last launched job (survives the dialog and a Max restart).</summary>
    public MaxConnectedRenderJobStore ConnectedRenderJobStore { get; }

    /// <summary>Session-scoped job lifecycle: launch, poll loop, cancel, restore.</summary>
    public MaxConnectedRenderJobTracker ConnectedRenderJobTracker { get; }

    #endregion
}
