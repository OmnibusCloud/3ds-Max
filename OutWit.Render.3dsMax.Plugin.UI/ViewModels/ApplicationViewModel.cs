using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

public sealed class ApplicationViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public ApplicationViewModel(MaxSceneExportService sceneExportService) : base(null!)
    {
        SceneExportService = sceneExportService;
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
        CloudSessionVm = new CloudSessionViewModel(this);
        RenderLaunchVm = new RenderLaunchViewModel(this);
        MainVm = new ExportMainViewModel(this);
    }

    #endregion

    #region Properties

    public ExportMainViewModel MainVm { get; }

    public CloudSessionViewModel CloudSessionVm { get; }

    public RenderLaunchViewModel RenderLaunchVm { get; }

    public MaxSceneExportService SceneExportService { get; }

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
