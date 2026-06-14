using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.UI.Views;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

public sealed class MaxPluginCommandService
{
    #region Functions

    /// <summary>
    /// Creates the export service used by both the interactive window flow and batch smoke-test automation.
    /// </summary>
    public MaxSceneExportService CreateExportService()
    {
        return new MaxSceneExportService(new MaxSceneSummaryService(new MaxHostApplicationService()));
    }

    /// <summary>
    /// Creates the launch-package preparation service used by batch smoke-test automation and future connected launch flows.
    /// </summary>
    public MaxSceneLaunchPreparationService CreateLaunchPreparationService()
    {
        return new MaxSceneLaunchPreparationService(CreateExportService());
    }

    /// <summary>
    /// Creates the connected-render package upload service used by batch smoke-test automation and future connected plugin flows.
    /// </summary>
    public MaxConnectedRenderPackageUploadService CreateConnectedRenderPackageUploadService()
    {
        return new MaxConnectedRenderPackageUploadService(new MaxConnectedRenderArchiveUploaderOmnibusCloudApiKey());
    }

    /// <summary>
    /// Creates the real connected-render smoke service used by batch automation to validate end-to-end render execution.
    /// </summary>
    public MaxConnectedRenderLiveSmokeService CreateConnectedRenderLiveSmokeService()
    {
        return new MaxConnectedRenderLiveSmokeService(CreateExportService(), new MaxConnectedRenderSceneAttachmentService());
    }

    /// <summary>
    /// Creates the interactive exporter window.
    /// </summary>
    /// <summary>
    /// Builds the root composition ViewModel (one per plugin UI session) over a fresh service graph.
    /// Shared across the Render / Export / Settings dialogs so session, settings and services are one.
    /// </summary>
    public ApplicationViewModel CreateApplicationViewModel()
    {
        var exportService = CreateExportService();
        var services = new MaxPluginServices(exportService);
        return new ApplicationViewModel(services);
    }

    public ExportWindow CreateExportWindow()
    {
        var applicationVm = CreateApplicationViewModel();
        return new ExportWindow(applicationVm.MainVm);
    }

    /// <summary>
    /// Creates the Render dialog over the shared application ViewModel (design 4.1).
    /// </summary>
    public RenderDialog CreateRenderDialog(ApplicationViewModel applicationVm)
    {
        var viewModel = new RenderDialogViewModel(applicationVm);
        return new RenderDialog(viewModel);
    }

    /// <summary>
    /// Creates the Export dialog over the shared application ViewModel (design 4.2).
    /// </summary>
    public ExportDialog CreateExportDialog(ApplicationViewModel applicationVm)
    {
        var viewModel = new ExportDialogViewModel(applicationVm);
        return new ExportDialog(viewModel);
    }

    /// <summary>
    /// Creates the Settings dialog over the shared application ViewModel (design 4.3).
    /// </summary>
    public SettingsDialog CreateSettingsDialog(ApplicationViewModel applicationVm)
    {
        var viewModel = new SettingsViewModel(applicationVm);
        return new SettingsDialog(viewModel);
    }

    /// <summary>
    /// Creates the Sign-in dialog over the shared application ViewModel (design 4.4).
    /// </summary>
    public SignInDialog CreateSignInDialog(ApplicationViewModel applicationVm)
    {
        var viewModel = new SignInViewModel(applicationVm);
        return new SignInDialog(viewModel);
    }

    #endregion
}
