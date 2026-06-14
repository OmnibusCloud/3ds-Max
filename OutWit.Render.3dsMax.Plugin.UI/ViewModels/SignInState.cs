namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

/// <summary>
/// Sign-in dialog state (design 4.4): waiting on the browser, signed in, or the attempt didn't
/// complete (cancelled / timed out / failed).
/// </summary>
public enum SignInState
{
    SigningIn,
    Success,
    Failed
}
