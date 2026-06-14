using System.Windows;
using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.Views;

public partial class RenderDialog : Window
{
    #region Constructors

    public RenderDialog(RenderDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    #endregion
}
