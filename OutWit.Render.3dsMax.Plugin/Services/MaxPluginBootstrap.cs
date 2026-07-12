using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.UI.Views;
using OutWit.Render.ThreeDsMax.Plugin.UI.Theming;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

public sealed class MaxPluginBootstrap
{
    #region Fields

    private readonly MaxPluginCommandService m_commandService;
    private readonly IMaxThemeService m_themeService;
    private readonly Dispatcher m_uiDispatcher;
    private ApplicationViewModel? m_applicationVm;
    private RenderDialog? m_renderDialog;
    private ExportDialog? m_exportDialog;
    private SettingsDialog? m_settingsDialog;
    private SignInDialog? m_signInDialog;
    private DiagnosticsDialog? m_diagnosticsDialog;

    #endregion

    #region Constructors

    public MaxPluginBootstrap()
    {
        // Created from MAXScript on the Max main thread. Session-state changes can surface on
        // worker threads (silent restore continuations); the dispatcher brings AccountStateChanged
        // back to the main thread, where the MAXScript handler touches the menu API.
        m_uiDispatcher = Dispatcher.CurrentDispatcher;
        m_commandService = new MaxPluginCommandService();
        m_themeService = m_commandService.CreateThemeService();
    }

    #endregion

    #region Functions

    public void ShowRenderDialog()
    {
        if (m_renderDialog is null || !m_renderDialog.IsLoaded)
        {
            m_renderDialog = m_commandService.CreateRenderDialog(EnsureApplicationViewModel());
            MaxThemeResources.Apply(m_renderDialog, ResolveEffectiveTheme());
            AttachToHost(m_renderDialog);

            // Details… opens the dedicated Diagnostics dialog over the render VM's fresh data.
            if (m_renderDialog.DataContext is RenderDialogViewModel renderViewModel)
                renderViewModel.DetailsRequested += () => ShowDiagnostics(renderViewModel);

            m_renderDialog.Closed += OnRenderDialogClosed;
            m_renderDialog.Show();
            return;
        }

        if (m_renderDialog.WindowState == WindowState.Minimized)
            m_renderDialog.WindowState = WindowState.Normal;

        ApplyThemeToOpenDialogs();
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
            MaxThemeResources.Apply(m_exportDialog, ResolveEffectiveTheme());
            AttachToHost(m_exportDialog);

            // Wire the VM's close signal to the window here (host-side) so the View stays code-behind-free.
            if (m_exportDialog.DataContext is ExportDialogViewModel exportViewModel)
                exportViewModel.DialogClosed += _ => m_exportDialog?.Close();

            m_exportDialog.Closed += OnExportDialogClosed;
            m_exportDialog.Show();
            return;
        }

        if (m_exportDialog.WindowState == WindowState.Minimized)
            m_exportDialog.WindowState = WindowState.Normal;

        ApplyThemeToOpenDialogs();
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
        ShowSettingsSection(SettingsSection.General);
    }

    /// <summary>About menu item (design 1.2): the same Settings dialog, opened on the About tab.</summary>
    public void ShowAbout()
    {
        ShowSettingsSection(SettingsSection.About);
    }

    private void ShowSettingsSection(SettingsSection section)
    {
        if (m_settingsDialog is null || !m_settingsDialog.IsLoaded)
        {
            m_settingsDialog = m_commandService.CreateSettingsDialog(EnsureApplicationViewModel());
            MaxThemeResources.Apply(m_settingsDialog, ResolveEffectiveTheme());
            AttachToHost(m_settingsDialog);

            if (m_settingsDialog.DataContext is SettingsViewModel settingsViewModel)
            {
                settingsViewModel.Section = section;
                settingsViewModel.DialogClosed += _ => m_settingsDialog?.Close();

                // Live theme preview: picking a theme retints every open dialog immediately;
                // closing without Save reverts via OnSettingsDialogClosed (persisted setting wins).
                settingsViewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SettingsViewModel.SelectedTheme))
                        ApplyThemeToOpenDialogs(ResolveTheme(settingsViewModel.SelectedTheme));
                };
            }

            m_settingsDialog.Closed += OnSettingsDialogClosed;
            m_settingsDialog.Show();
            return;
        }

        if (m_settingsDialog.WindowState == WindowState.Minimized)
            m_settingsDialog.WindowState = WindowState.Normal;

        if (m_settingsDialog.DataContext is SettingsViewModel openViewModel)
            openViewModel.Section = section;

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

        // Settings may have changed ThemeMode — retint whatever is still open right away.
        ApplyThemeToOpenDialogs();
    }

    public void ShowSignIn()
    {
        if (m_signInDialog is null || !m_signInDialog.IsLoaded)
        {
            m_signInDialog = m_commandService.CreateSignInDialog(EnsureApplicationViewModel());
            MaxThemeResources.Apply(m_signInDialog, ResolveEffectiveTheme());
            AttachToHost(m_signInDialog);

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

    /// <summary>
    /// Shows the Details / Diagnostics dialog (design 4.1.5) over the render dialog's data.
    /// </summary>
    public void ShowDiagnostics(RenderDialogViewModel renderViewModel)
    {
        if (m_diagnosticsDialog is null || !m_diagnosticsDialog.IsLoaded)
        {
            var viewModel = new DiagnosticsViewModel(EnsureApplicationViewModel(),
                renderViewModel.RefreshValidation, renderViewModel.RunPreflight);

            m_diagnosticsDialog = new DiagnosticsDialog(viewModel);
            MaxThemeResources.Apply(m_diagnosticsDialog, ResolveEffectiveTheme());
            AttachToHost(m_diagnosticsDialog);

            viewModel.DialogClosed += _ => m_diagnosticsDialog?.Close();
            m_diagnosticsDialog.Closed += OnDiagnosticsDialogClosed;
            m_diagnosticsDialog.Show();
            return;
        }

        m_diagnosticsDialog.Activate();
        m_diagnosticsDialog.Focus();
    }

    private void OnDiagnosticsDialogClosed(object? sender, EventArgs e)
    {
        if (m_diagnosticsDialog is null)
            return;

        m_diagnosticsDialog.Closed -= OnDiagnosticsDialogClosed;
        m_diagnosticsDialog = null;
    }

    /// <summary>
    /// Startup warm-up (called from Initialize.ms at menu registration): builds the shared
    /// ApplicationViewModel, which kicks the silent session restore — so the menu gate reflects the
    /// persisted session without the user having to open a dialog (or sign in) first.
    /// </summary>
    public void WarmUpSession()
    {
        MaxPluginLogging.Logger.Information("Plugin warm-up: restoring the persisted session.");
        EnsureApplicationViewModel();
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

    /// <summary>
    /// Raised on the Max UI thread whenever the signed-in state or the account display name
    /// changes; Initialize.ms refreshes the menu's account header title from it (design 1.1).
    /// </summary>
    public event EventHandler? AccountStateChanged;

    /// <summary>
    /// Display name of the signed-in account; empty when signed out (menu account header).
    /// </summary>
    public string AccountDisplay
    {
        get
        {
            var sessionVm = m_applicationVm?.CloudSessionVm;
            if (sessionVm is null || !sessionVm.IsSignedIn)
                return string.Empty;

            return sessionVm.UserDisplayName ?? string.Empty;
        }
    }

    private void OnCloudSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CloudSessionViewModel.IsSignedIn)
            && e.PropertyName != nameof(CloudSessionViewModel.UserDisplayName))
            return;

        if (m_uiDispatcher.CheckAccess())
            AccountStateChanged?.Invoke(this, EventArgs.Empty);
        else
            m_uiDispatcher.BeginInvoke(new Action(() => AccountStateChanged?.Invoke(this, EventArgs.Empty)));
    }

    /// <summary>
    /// Effective plugin theme (MX-14): the persisted ThemeMode setting wins ("Dark"/"Light");
    /// "FollowMax" (and anything unrecognized) defers to the host color manager.
    /// </summary>
    private MaxUiTheme ResolveEffectiveTheme()
    {
        return ResolveTheme(EnsureApplicationViewModel().Settings.ThemeMode);
    }

    private MaxUiTheme ResolveTheme(string? themeMode)
    {
        return themeMode switch
        {
            "Dark" => MaxUiTheme.Dark,
            "Light" => MaxUiTheme.Light,
            _ => m_themeService.CurrentTheme
        };
    }

    /// <summary>
    /// Live theme switch: re-resolves the effective theme against every open dialog — called after
    /// Settings closes (ThemeMode may have changed) and when re-activating an existing dialog
    /// (the Max theme may have changed since it was opened).
    /// </summary>
    private void ApplyThemeToOpenDialogs()
    {
        ApplyThemeToOpenDialogs(ResolveEffectiveTheme());
    }

    private void ApplyThemeToOpenDialogs(MaxUiTheme theme)
    {
        if (m_renderDialog != null)
            MaxThemeResources.Apply(m_renderDialog, theme);
        if (m_exportDialog != null)
            MaxThemeResources.Apply(m_exportDialog, theme);
        if (m_settingsDialog != null)
            MaxThemeResources.Apply(m_settingsDialog, theme);
        if (m_signInDialog != null)
            MaxThemeResources.Apply(m_signInDialog, theme);
        if (m_diagnosticsDialog != null)
            MaxThemeResources.Apply(m_diagnosticsDialog, theme);
    }

    /// <summary>
    /// Owns a dialog by the 3ds Max main window (MX-4): keeps it above the host, minimizes with it,
    /// centers CenterOwner on Max instead of the screen, and keeps it out of the taskbar. No-op
    /// without a Max host (tests / smoke automation).
    /// </summary>
    private void AttachToHost(Window dialog)
    {
        var handle = m_commandService.GetMaxWindowHandle();
        if (handle == IntPtr.Zero)
            return;

        _ = new WindowInteropHelper(dialog) { Owner = handle };
        dialog.ShowInTaskbar = false;

        // Suspend 3ds Max keyboard accelerators while our dialog is focused. Without this Max eats
        // single-key shortcuts (digits select sub-object levels, Backspace/Delete, etc.) before WPF
        // gets them, so text fields silently drop those keystrokes. Balanced per window: disabled on
        // Activated, re-enabled on Deactivated (user clicked back into Max) and on Closed.
        dialog.Activated += (_, _) => m_commandService.SetMaxAcceleratorsEnabled(false);
        dialog.Deactivated += (_, _) => m_commandService.SetMaxAcceleratorsEnabled(true);
        dialog.Closed += (_, _) => m_commandService.SetMaxAcceleratorsEnabled(true);
    }

    private ApplicationViewModel EnsureApplicationViewModel()
    {
        if (m_applicationVm != null)
            return m_applicationVm;

        m_applicationVm = m_commandService.CreateApplicationViewModel();
        m_applicationVm.CloudSessionVm.PropertyChanged += OnCloudSessionPropertyChanged;

        // One-time silent session restore (persisted refresh token): dialogs open signed-in and the
        // menu gate reflects the restored session without a browser round-trip. Restore handles its
        // own errors, so fire-and-forget is safe here; dialogs await the same memoized task.
        _ = m_applicationVm.CloudSessionVm.EnsureSessionRestoredAsync();

        return m_applicationVm;
    }

    #endregion
}
