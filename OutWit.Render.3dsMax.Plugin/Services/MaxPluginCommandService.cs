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
    public ExportWindow CreateExportWindow()
    {
        var exportService = CreateExportService();
        var applicationVm = new ApplicationViewModel(exportService);
        return new ExportWindow(applicationVm.MainVm);
    }

    #endregion
}
