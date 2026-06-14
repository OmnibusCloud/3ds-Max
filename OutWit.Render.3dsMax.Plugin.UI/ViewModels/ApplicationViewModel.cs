using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;
using Serilog;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

/// <summary>
/// Root composition ViewModel: a pure container of child ViewModels over the plugin's service graph
/// (<see cref="MaxPluginServices"/>). No service construction or business logic lives here — the
/// service properties simply delegate to the composition root so existing child ViewModels keep
/// reaching them through <c>ApplicationVm</c>.
/// </summary>
public sealed class ApplicationViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public ApplicationViewModel(MaxPluginServices services) : base(null!)
    {
        Services = services;
        CloudSessionVm = new CloudSessionViewModel(this);
        RenderLaunchVm = new RenderLaunchViewModel(this);
        MainVm = new ExportMainViewModel(this);
    }

    #endregion

    #region ViewModels

    public ExportMainViewModel MainVm { get; }

    public CloudSessionViewModel CloudSessionVm { get; }

    public RenderLaunchViewModel RenderLaunchVm { get; }

    #endregion

    #region Services

    /// <summary>The service composition root. Construction lives there, not in this container.</summary>
    public MaxPluginServices Services { get; }

    public MaxPluginSettings Settings => Services.Settings;

    public ILogger Logger => Services.Logger;

    public MaxSceneExportService SceneExportService => Services.SceneExportService;

    public IMaxStatusBarService StatusBar => Services.StatusBar;

    public IMaxSystemBrowserLauncher BrowserLauncher => Services.BrowserLauncher;

    public IMaxCloudSessionService CloudSessionService => Services.CloudSessionService;

    public IMaxCloudConnectionService CloudConnectionService => Services.CloudConnectionService;

    public MaxSceneLaunchPreparationService LaunchPreparationService => Services.LaunchPreparationService;

    public MaxConnectedRenderPreflightService ConnectedRenderPreflightService => Services.ConnectedRenderPreflightService;

    public MaxConnectedExecutionScopeService ConnectedExecutionScopeService => Services.ConnectedExecutionScopeService;

    public MaxConnectedRenderSubmissionService ConnectedRenderSubmissionService => Services.ConnectedRenderSubmissionService;

    public MaxConnectedRenderService ConnectedRenderService => Services.ConnectedRenderService;

    public MaxConnectedRenderPackageUploadService ConnectedRenderPackageUploadService => Services.ConnectedRenderPackageUploadService;

    public MaxConnectedRenderDownloadService ConnectedRenderDownloadService => Services.ConnectedRenderDownloadService;

    #endregion
}
