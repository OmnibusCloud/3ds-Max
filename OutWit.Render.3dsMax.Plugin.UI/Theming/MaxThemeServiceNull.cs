namespace OutWit.Render.ThreeDsMax.Plugin.UI.Theming;

/// <summary>
/// Default <see cref="IMaxThemeService"/>: always the dark Max-native palette. Used in tests and when
/// the plugin runs without a Max host. Stateless singleton.
/// </summary>
public sealed class MaxThemeServiceNull : IMaxThemeService
{
    public static readonly MaxThemeServiceNull Instance = new();

    public MaxUiTheme CurrentTheme => MaxUiTheme.Dark;
}
