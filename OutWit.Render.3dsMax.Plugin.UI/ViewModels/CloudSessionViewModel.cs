using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Render.ThreeDsMax.Plugin.Export;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

public sealed class CloudSessionViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public CloudSessionViewModel(ApplicationViewModel applicationVm) : base(applicationVm)
    {
        InitDefault();
        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        CloudUrl = OmnibusCloudDefaults.SERVER_URL;
        IdentityUrl = OmnibusCloudDefaults.IDENTITY_URL;
        ApiKey = string.Empty;
        SessionStatusText = "Signed out";
        UserDisplayName = string.Empty;
        ExecutionScopeSummary = "No scope loaded";
        SignInButtonText = "Sign In";
        IsBusy = false;
    }

    private void InitCommands()
    {
        SignInCommand = new RelayCommand(_ => SignIn(), _ => CanSignIn);
        SignOutCommand = new RelayCommand(_ => SignOut(), _ => CanSignOut);
        OpenCloudCommand = new RelayCommand(_ => OpenCloud(), _ => CanOpenCloud);
        UpdateStatus();
    }

    private void InitEvents()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CloudUrl)
                || e.PropertyName == nameof(IdentityUrl)
                || e.PropertyName == nameof(IsBusy)
                || e.PropertyName == nameof(IsSignedIn))
            {
                UpdateStatus();
            }
        };
    }

    #endregion

    #region Functions

    /// <summary>
    /// Attempts to silently restore the persisted session when the window opens.
    /// </summary>
    public async Task RestoreSessionAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        SessionStatusText = "Restoring session";

        try
        {
            var restored = await Task.Run(() => ApplicationVm.CloudSessionService.TryRestoreSessionAsync());
            if (restored)
                ApplySessionState(ApplicationVm.CloudSessionService.GetState());
            else
                MarkSignedOut();
        }
        catch (Exception ex)
        {
            SessionStatusText = $"Session restore failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Clears the current session presentation and returns the UI to signed-out state.
    /// </summary>
    public void MarkSignedOut()
    {
        IsSignedIn = false;
        UserDisplayName = string.Empty;
        SessionStatusText = "Signed out";
        ExecutionScopeSummary = "No scope loaded";
        SignInButtonText = "Sign In";
        UpdateStatus();
    }

    /// <summary>
    /// Applies the current execution scope load result to the cloud-session presentation.
    /// </summary>
    public void ApplyExecutionScope(MaxConnectedExecutionScopeResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.UserDisplayName))
            UserDisplayName = result.UserDisplayName;

        if (!string.IsNullOrWhiteSpace(result.SessionStatusText))
            SessionStatusText = result.SessionStatusText;

        ExecutionScopeSummary = result.IsSuccess
            ? $"Loaded {result.Groups.Count} groups. All clients: {result.CanRunOnAllClients}"
            : result.StatusText;
        UpdateStatus();
    }

    private async void SignIn()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        SessionStatusText = "Browser sign-in in progress";
        SignInButtonText = "Continue in Browser";

        try
        {
            var identityUrl = IdentityUrl;
            var state = await Task.Run(() => ApplicationVm.CloudSessionService.SignInAsync(identityUrl));
            ApplySessionState(state);
        }
        catch (Exception ex)
        {
            SessionStatusText = $"Sign-in failed: {ex.Message}";
            SignInButtonText = "Sign In";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void SignOut()
    {
        if (IsBusy)
            return;

        IsBusy = true;

        try
        {
            await Task.Run(() => ApplicationVm.CloudSessionService.SignOutAsync());
        }
        finally
        {
            IsBusy = false;
        }

        MarkSignedOut();
    }

    private void OpenCloud()
    {
        ApplicationVm.BrowserLauncher.Open(CloudUrl);
    }

    private void ApplySessionState(MaxConnectedSessionState state)
    {
        IsSignedIn = state.IsSignedIn;

        if (state.IsSignedIn)
        {
            UserDisplayName = state.DisplayName;
            SessionStatusText = $"Signed in as {state.DisplayName}";
            SignInButtonText = "Sign In Again";
        }
        else
        {
            UserDisplayName = string.Empty;
            SessionStatusText = string.IsNullOrWhiteSpace(state.LastError) ? "Signed out" : state.LastError;
            SignInButtonText = "Sign In";
        }

        UpdateStatus();
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        CanSignIn = !IsBusy && !string.IsNullOrWhiteSpace(IdentityUrl);
        CanSignOut = !IsBusy && IsSignedIn;
        CanOpenCloud = !string.IsNullOrWhiteSpace(CloudUrl);

        SignInCommand?.RaiseCanExecuteChanged();
        SignOutCommand?.RaiseCanExecuteChanged();
        OpenCloudCommand?.RaiseCanExecuteChanged();
    }

    #endregion

    #region Properties

    public RelayCommand SignInCommand { get; private set; } = null!;

    public RelayCommand SignOutCommand { get; private set; } = null!;

    public RelayCommand OpenCloudCommand { get; private set; } = null!;

    [Notify]
    public string CloudUrl { get; set; }

    [Notify]
    public string IdentityUrl { get; set; }

    [Notify]
    public string ApiKey { get; set; }

    [Notify]
    public bool IsSignedIn { get; set; }

    [Notify]
    public bool IsBusy { get; set; }

    [Notify]
    public string SessionStatusText { get; set; }

    [Notify]
    public string UserDisplayName { get; set; }

    [Notify]
    public string ExecutionScopeSummary { get; set; }

    [Notify]
    public string SignInButtonText { get; set; }

    [Notify]
    public bool CanSignIn { get; set; }

    [Notify]
    public bool CanSignOut { get; set; }

    [Notify]
    public bool CanOpenCloud { get; set; }

    #endregion
}
