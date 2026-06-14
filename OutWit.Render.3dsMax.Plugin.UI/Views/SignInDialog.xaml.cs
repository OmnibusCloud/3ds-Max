using System.Windows;
using OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.Views;

public partial class SignInDialog : Window
{
    #region Constructors

    public SignInDialog(SignInViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    #endregion
}
