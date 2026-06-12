using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

public sealed class ApplicationViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public ApplicationViewModel(MaxSceneExportService sceneExportService) : base(null!)
    {
        SceneExportService = sceneExportService;
        LaunchPreparationService = new MaxSceneLaunchPreparationService(sceneExportService);
        ConnectedRenderPreflightService = new MaxConnectedRenderPreflightService(sceneExportService);
        ConnectedExecutionScopeService = new MaxConnectedExecutionScopeService();
        ConnectedRenderSubmissionService = new MaxConnectedRenderSubmissionService(new MaxConnectedRenderSubmissionTransportLocalPlaceholder());
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

    public MaxSceneLaunchPreparationService LaunchPreparationService { get; }

    public MaxConnectedRenderPreflightService ConnectedRenderPreflightService { get; }

    public MaxConnectedExecutionScopeService ConnectedExecutionScopeService { get; }

    public MaxConnectedRenderSubmissionService ConnectedRenderSubmissionService { get; }

    public MaxConnectedRenderService ConnectedRenderService { get; }

    public MaxConnectedRenderPackageUploadService ConnectedRenderPackageUploadService { get; }

    public MaxConnectedRenderDownloadService ConnectedRenderDownloadService { get; }

    #endregion
}
