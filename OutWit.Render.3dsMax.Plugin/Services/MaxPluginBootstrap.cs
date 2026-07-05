using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.UI.Views;
using OutWit.Render.ThreeDsMax.Plugin.UI.Theming;
using System.Windows;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

public sealed class MaxPluginBootstrap
{
    #region Fields

    private readonly MaxPluginCommandService m_commandService;
    private readonly IMaxThemeService m_themeService;
    private ApplicationViewModel? m_applicationVm;
    private ExportWindow? m_exportWindow;
    private RenderDialog? m_renderDialog;
    private ExportDialog? m_exportDialog;
    private SettingsDialog? m_settingsDialog;
    private SignInDialog? m_signInDialog;

    #endregion

    #region Constructors

    public MaxPluginBootstrap()
    {
        m_commandService = new MaxPluginCommandService();
        m_themeService = m_commandService.CreateThemeService();
    }

    #endregion

    #region Functions

    public ExportWindow CreateExportWindow()
    {
        return m_commandService.CreateExportWindow();
    }

    public void ShowExportWindow()
    {
        if (m_exportWindow is null || !m_exportWindow.IsLoaded)
        {
            m_exportWindow = CreateExportWindow();
            m_exportWindow.Closed += OnExportWindowClosed;
            m_exportWindow.Show();
            return;
        }

        if (m_exportWindow.WindowState == WindowState.Minimized)
            m_exportWindow.WindowState = WindowState.Normal;

        m_exportWindow.Activate();
        m_exportWindow.Focus();
    }

    private void OnExportWindowClosed(object? sender, EventArgs e)
    {
        if (m_exportWindow is null)
            return;

        m_exportWindow.Closed -= OnExportWindowClosed;
        m_exportWindow = null;
    }

    public void ShowRenderDialog()
    {
        if (m_renderDialog is null || !m_renderDialog.IsLoaded)
        {
            m_renderDialog = m_commandService.CreateRenderDialog(EnsureApplicationViewModel());
            MaxThemeResources.Apply(m_renderDialog, m_themeService.CurrentTheme);
            m_renderDialog.Closed += OnRenderDialogClosed;
            m_renderDialog.Show();
            return;
        }

        if (m_renderDialog.WindowState == WindowState.Minimized)
            m_renderDialog.WindowState = WindowState.Normal;

        m_renderDialog.Activate();
        m_renderDialog.Focus();
    }

    private void OnRenderDialogClosed(object? sender, EventArgs e)
    {
        if (m_renderDialog is null)
            return;

        m_renderDialog.Closed -= OnRenderDialogClosed;
        (m_renderDialog.DataContext as IDisposable)?.Dispose();
        m_renderDialog = null;
    }

    public void ShowExportDialog()
    {
        if (m_exportDialog is null || !m_exportDialog.IsLoaded)
        {
            m_exportDialog = m_commandService.CreateExportDialog(EnsureApplicationViewModel());
            MaxThemeResources.Apply(m_exportDialog, m_themeService.CurrentTheme);

            // Wire the VM's close signal to the window here (host-side) so the View stays code-behind-free.
            if (m_exportDialog.DataContext is ExportDialogViewModel exportViewModel)
                exportViewModel.DialogClosed += _ => m_exportDialog?.Close();

            m_exportDialog.Closed += OnExportDialogClosed;
            m_exportDialog.Show();
            return;
        }

        if (m_exportDialog.WindowState == WindowState.Minimized)
            m_exportDialog.WindowState = WindowState.Normal;

        m_exportDialog.Activate();
        m_exportDialog.Focus();
    }

    private void OnExportDialogClosed(object? sender, EventArgs e)
    {
        if (m_exportDialog is null)
            return;

        m_exportDialog.Closed -= OnExportDialogClosed;
        (m_exportDialog.DataContext as IDisposable)?.Dispose();
        m_exportDialog = null;
    }

    public void ShowSettings()
    {
        if (m_settingsDialog is null || !m_settingsDialog.IsLoaded)
        {
            m_settingsDialog = m_commandService.CreateSettingsDialog(EnsureApplicationViewModel());
            MaxThemeResources.Apply(m_settingsDialog, m_themeService.CurrentTheme);

            if (m_settingsDialog.DataContext is SettingsViewModel settingsViewModel)
                settingsViewModel.DialogClosed += _ => m_settingsDialog?.Close();

            m_settingsDialog.Closed += OnSettingsDialogClosed;
            m_settingsDialog.Show();
            return;
        }

        if (m_settingsDialog.WindowState == WindowState.Minimized)
            m_settingsDialog.WindowState = WindowState.Normal;

        m_settingsDialog.Activate();
        m_settingsDialog.Focus();
    }

    private void OnSettingsDialogClosed(object? sender, EventArgs e)
    {
        if (m_settingsDialog is null)
            return;

        m_settingsDialog.Closed -= OnSettingsDialogClosed;
        (m_settingsDialog.DataContext as IDisposable)?.Dispose();
        m_settingsDialog = null;
    }

    public void ShowSignIn()
    {
        if (m_signInDialog is null || !m_signInDialog.IsLoaded)
        {
            m_signInDialog = m_commandService.CreateSignInDialog(EnsureApplicationViewModel());
            MaxThemeResources.Apply(m_signInDialog, m_themeService.CurrentTheme);

            if (m_signInDialog.DataContext is SignInViewModel signInViewModel)
                signInViewModel.DialogClosed += _ => m_signInDialog?.Close();

            m_signInDialog.Closed += OnSignInDialogClosed;
            m_signInDialog.Show();
            return;
        }

        m_signInDialog.Activate();
        m_signInDialog.Focus();
    }

    private void OnSignInDialogClosed(object? sender, EventArgs e)
    {
        if (m_signInDialog is null)
            return;

        m_signInDialog.Closed -= OnSignInDialogClosed;
        (m_signInDialog.DataContext as IDisposable)?.Dispose();
        m_signInDialog = null;
    }

    public void SignOut()
    {
        var applicationVm = EnsureApplicationViewModel();
        if (applicationVm.CloudSessionVm.SignOutCommand.CanExecute(null))
            applicationVm.CloudSessionVm.SignOutCommand.Execute(null);
    }

    public void OpenPortal()
    {
        var applicationVm = EnsureApplicationViewModel();
        if (applicationVm.CloudSessionVm.OpenCloudCommand.CanExecute(null))
            applicationVm.CloudSessionVm.OpenCloudCommand.Execute(null);
    }

    /// <summary>
    /// Whether a cloud session is active — drives the menu gate (Render/Export require sign-in).
    /// </summary>
    public bool IsSignedIn => m_applicationVm?.CloudSessionVm.IsSignedIn ?? false;

    private ApplicationViewModel EnsureApplicationViewModel()
    {
        if (m_applicationVm != null)
            return m_applicationVm;

        m_applicationVm = m_commandService.CreateApplicationViewModel();

        // One-time silent session restore (persisted refresh token): dialogs open signed-in and the
        // menu gate reflects the restored session without a browser round-trip. Restore handles its
        // own errors, so fire-and-forget is safe here; dialogs await the same memoized task.
        _ = m_applicationVm.CloudSessionVm.EnsureSessionRestoredAsync();

        return m_applicationVm;
    }

    #endregion
}
