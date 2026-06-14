using System.Windows;
using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.Views;

public partial class ExportDialog : Window
{
    #region Constructors

    public ExportDialog(ExportDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    #endregion
}
