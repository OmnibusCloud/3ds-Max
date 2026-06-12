using OutWit.Render.ThreeDsMax.Plugin.UI.Views;
using System.Windows;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

public sealed class MaxPluginBootstrap
{
    #region Fields

    private readonly MaxPluginCommandService m_commandService;
    private ExportWindow? m_exportWindow;

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

    #endregion
}
