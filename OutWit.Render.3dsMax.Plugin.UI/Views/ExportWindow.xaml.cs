using System.Windows;
using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.Views;

public partial class ExportWindow : Window
{
    #region Constructors

    public ExportWindow(ExportMainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.CloudVm.RestoreSessionAsync();
    }

    #endregion
}
