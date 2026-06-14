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

    public MaxPluginServices(MaxSceneExportService sceneExportService)
    {
        SceneExportService = sceneExportService;
        Logger = MaxPluginLogging.Logger;
        Settings = MaxPluginSettingsFactory.Create();

        BrowserLauncher = new MaxSystemBrowserLauncherShell();
        CloudSessionService = new MaxCloudSessionService(
            new MaxSessionStoreDpapi(),
            BrowserLauncher,
            () => new MaxAuthorizationCallbackListenerLoopback());
        CloudConnectionService = new MaxCloudConnectionService(CloudSessionService);
        LaunchPreparationService = new MaxSceneLaunchPreparationService(sceneExportService);
        ConnectedRenderPreflightService = new MaxConnectedRenderPreflightService(sceneExportService);
        ConnectedExecutionScopeService = new MaxConnectedExecutionScopeService(CloudSessionService, CloudConnectionService);
        ConnectedRenderSubmissionService = new MaxConnectedRenderSubmissionService(
            new MaxConnectedRenderSubmissionTransportOmnibusCloudSession(CloudConnectionService, new MaxConnectedRenderSceneAttachmentService()));
        ConnectedRenderService = new MaxConnectedRenderService(LaunchPreparationService, ConnectedRenderPreflightService, ConnectedRenderSubmissionService);
        ConnectedRenderPackageUploadService = new MaxConnectedRenderPackageUploadService(new MaxConnectedRenderArchiveUploaderOmnibusCloudApiKey());
        ConnectedRenderDownloadService = new MaxConnectedRenderDownloadService();
    }

    #endregion

    #region Properties

    /// <summary>Scene export/validation (wraps the host-only scene snapshot provider).</summary>
    public MaxSceneExportService SceneExportService { get; }

    /// <summary>Shared Serilog logger writing to the per-user logs directory.</summary>
    public ILogger Logger { get; }

    /// <summary>Per-user plugin preferences.</summary>
    public MaxPluginSettings Settings { get; }

    public IMaxSystemBrowserLauncher BrowserLauncher { get; }

    public IMaxCloudSessionService CloudSessionService { get; }

    public IMaxCloudConnectionService CloudConnectionService { get; }

    public MaxSceneLaunchPreparationService LaunchPreparationService { get; }

    public MaxConnectedRenderPreflightService ConnectedRenderPreflightService { get; }

    public MaxConnectedExecutionScopeService ConnectedExecutionScopeService { get; }

    public MaxConnectedRenderSubmissionService ConnectedRenderSubmissionService { get; }

    public MaxConnectedRenderService ConnectedRenderService { get; }

    public MaxConnectedRenderPackageUploadService ConnectedRenderPackageUploadService { get; }

    public MaxConnectedRenderDownloadService ConnectedRenderDownloadService { get; }

    #endregion
}
