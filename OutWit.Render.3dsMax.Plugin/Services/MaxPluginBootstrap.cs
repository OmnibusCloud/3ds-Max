using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.UI.Views;
using System.Windows;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

public sealed class MaxPluginBootstrap
{
    #region Fields

    private readonly MaxPluginCommandService m_commandService;
    private ApplicationViewModel? m_applicationVm;
    private ExportWindow? m_exportWindow;
    private RenderDialog? m_renderDialog;

    #endregion

    #region Constructors

    public MaxPluginBootstrap()
    {
        m_commandService = new MaxPluginCommandService();
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

    private ApplicationViewModel EnsureApplicationViewModel()
    {
        return m_applicationVm ??= m_commandService.CreateApplicationViewModel();
    }

    #endregion
}
