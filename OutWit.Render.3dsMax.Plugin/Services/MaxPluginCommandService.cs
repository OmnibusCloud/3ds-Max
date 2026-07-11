using Autodesk.Max;
using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.UI.Views;
using OutWit.Render.ThreeDsMax.Plugin.UI.Theming;
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
    /// Builds the root composition ViewModel (one per plugin UI session) over a fresh service graph.
    /// Shared across the Render / Export / Settings dialogs so session, settings and services are one.
    /// </summary>
    public ApplicationViewModel CreateApplicationViewModel()
    {
        var exportService = CreateExportService();
        var services = new MaxPluginServices(exportService, CreateStatusBarService());
        return new ApplicationViewModel(services);
    }

    /// <summary>
    /// Creates the host status-bar reporter, or a no-op when no Max host is available (e.g. tests).
    /// </summary>
    private static IMaxStatusBarService CreateStatusBarService()
    {
        try
        {
            return new MaxStatusBarService(GlobalInterface.Instance.COREInterface);
        }
        catch
        {
            return MaxStatusBarServiceNull.Instance;
        }
    }

    /// <summary>
    /// Native 3ds Max main-window handle for WPF dialog ownership (MX-4: correct z-order and
    /// minimize-with-host), or <see cref="IntPtr.Zero"/> when no Max host is available (e.g. tests).
    /// </summary>
    public IntPtr GetMaxWindowHandle()
    {
        try
        {
            return GlobalInterface.Instance.COREInterface.MAXHWnd;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Creates the follow-Max theme service (MX-14), or the dark default when no Max host is available.
    /// </summary>
    public IMaxThemeService CreateThemeService()
    {
        try
        {
            return new MaxThemeService(GlobalInterface.Instance);
        }
        catch
        {
            return MaxThemeServiceNull.Instance;
        }
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
