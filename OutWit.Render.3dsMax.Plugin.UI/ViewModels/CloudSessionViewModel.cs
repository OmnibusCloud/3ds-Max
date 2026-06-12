using System.Diagnostics;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
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
        CloudUrl = string.Empty;
        IdentityUrl = string.Empty;
        ApiKey = string.Empty;
        SessionStatusText = "Signed out";
        UserDisplayName = string.Empty;
        ExecutionScopeSummary = "No scope loaded";
        SignInButtonText = "Sign In";
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
                || e.PropertyName == nameof(SessionStatusText))
            {
                UpdateStatus();
            }
        };
    }

    #endregion

    #region Functions

    /// <summary>
    /// Marks the UI as having started the browser-based sign-in flow.
    /// </summary>
    public void MarkInteractiveSignInStarted()
    {
        SessionStatusText = "Browser sign-in started";
        ExecutionScopeSummary = "Waiting for authenticated session";
        SignInButtonText = "Continue in Browser";
        UpdateStatus();
    }

    /// <summary>
    /// Clears the current session presentation and returns the UI to signed-out state.
    /// </summary>
    public void MarkSignedOut()
    {
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
        UserDisplayName = result.UserDisplayName;
        SessionStatusText = result.SessionStatusText;
        ExecutionScopeSummary = result.IsSuccess
            ? $"Loaded {result.Groups.Count} groups. All clients: {result.CanRunOnAllClients}"
            : result.StatusText;
        UpdateStatus();
    }

    private void SignIn()
    {
        var signInUrl = ResolveSignInUrl();

        Process.Start(new ProcessStartInfo(signInUrl)
        {
            UseShellExecute = true
        });

        MarkInteractiveSignInStarted();
    }

    private void SignOut()
    {
        MarkSignedOut();
    }

    private void OpenCloud()
    {
        Process.Start(new ProcessStartInfo(CloudUrl)
        {
            UseShellExecute = true
        });
    }

    private string ResolveSignInUrl()
    {
        if (!string.IsNullOrWhiteSpace(IdentityUrl))
            return IdentityUrl;

        if (!string.IsNullOrWhiteSpace(CloudUrl))
            return CloudUrl;

        throw new InvalidOperationException("Either OmnibusCloud URL or Identity URL must be provided before sign-in.");
    }

    private void UpdateStatus()
    {
        CanSignIn = !string.IsNullOrWhiteSpace(IdentityUrl) || !string.IsNullOrWhiteSpace(CloudUrl);
        CanSignOut = !string.IsNullOrWhiteSpace(SessionStatusText) && !SessionStatusText.Equals("Signed out", StringComparison.OrdinalIgnoreCase);
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
