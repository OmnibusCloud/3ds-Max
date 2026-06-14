namespace OutWit.Render.ThreeDsMax.Plugin.UI.Theming;

/// <summary>
/// Resolves which palette the dialogs use, following the 3ds Max color manager (MX-14). The host
/// implementation reads Max's color theme; the null implementation returns <see cref="MaxUiTheme.Dark"/>
/// (the default) so the UI works without a Max host.
/// </summary>
public interface IMaxThemeService
{
    MaxUiTheme CurrentTheme { get; }
}
