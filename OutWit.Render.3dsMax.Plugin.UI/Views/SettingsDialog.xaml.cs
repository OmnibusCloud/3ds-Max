using System.Windows;
using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.Views;

public partial class SettingsDialog : Window
{
    #region Constructors

    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    #endregion
}
