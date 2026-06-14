using System.ComponentModel;
using System.Windows.Input;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

/// <summary>
/// Sign-in dialog (design 4.4): a UI gate over the existing OIDC/PKCE browser flow owned by
/// <see cref="CloudSessionViewModel"/>. No business logic in the View — this drives the OIDC attempt
/// and reflects its outcome (SigningIn → Success / Failed), raising <see cref="DialogClosed"/>.
/// </summary>
public sealed class SignInViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event Action<bool>? DialogClosed;

    #endregion

    #region Fields

    private bool m_attemptInProgress;

    #endregion

    #region Constructors

    public SignInViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
        InitDefault();
        InitEvents();
        InitCommands();

        StartSignIn();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        State = SignInState.SigningIn;
    }

    private void InitEvents()
    {
        CloudVm.PropertyChanged += OnCloudPropertyChanged;
    }

    private void InitCommands()
    {
        ReopenBrowserCommand = new RelayCommand(_ => StartSignIn());
        CancelCommand = new RelayCommand(_ => Cancel());
        DoneCommand = new RelayCommand(_ => Done());
        UpdateStatus();
    }

    #endregion

    #region Functions

    private void StartSignIn()
    {
        // Already signed in (e.g. a session was restored) — show success immediately.
        if (CloudVm.IsSignedIn)
        {
            Resolve();
            return;
        }

        State = SignInState.SigningIn;
        UpdateStatus();

        if (!CloudVm.SignInCommand.CanExecute(null))
        {
            Resolve();
            return;
        }

        m_attemptInProgress = true;
        CloudVm.SignInCommand.Execute(null);
    }

    private void Resolve()
    {
        m_attemptInProgress = false;
        State = CloudVm.IsSignedIn ? SignInState.Success : SignInState.Failed;
        UpdateStatus();
    }

    private void Cancel()
    {
        DialogClosed?.Invoke(CloudVm.IsSignedIn);
    }

    private void Done()
    {
        DialogClosed?.Invoke(true);
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        IsSigningIn = State == SignInState.SigningIn;
        IsSuccess = State == SignInState.Success;
        IsFailed = State == SignInState.Failed;
        StatusMessage = CloudVm.SessionStatusText;
        UserDisplayName = CloudVm.UserDisplayName;
    }

    #endregion

    #region Event Handlers

    private void OnCloudPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CloudSessionViewModel.IsBusy))
        {
            if (m_attemptInProgress && !CloudVm.IsBusy)
                Resolve();

            return;
        }

        if (e.PropertyName is nameof(CloudSessionViewModel.SessionStatusText)
            or nameof(CloudSessionViewModel.UserDisplayName))
        {
            UpdateStatus();
        }
    }

    #endregion

    #region Properties

    public CloudSessionViewModel CloudVm => ApplicationVm.CloudSessionVm;

    [Notify]
    public SignInState State { get; set; }

    [Notify]
    public bool IsSigningIn { get; set; }

    [Notify]
    public bool IsSuccess { get; set; }

    [Notify]
    public bool IsFailed { get; set; }

    [Notify]
    public string StatusMessage { get; set; } = string.Empty;

    [Notify]
    public string UserDisplayName { get; set; } = string.Empty;

    #endregion

    #region Commands

    public ICommand ReopenBrowserCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand DoneCommand { get; private set; } = null!;

    #endregion
}
