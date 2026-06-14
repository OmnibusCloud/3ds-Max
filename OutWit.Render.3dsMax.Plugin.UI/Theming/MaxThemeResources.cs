using System;
using System.Windows;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.Theming;

/// <summary>
/// Applies the follow-Max theme to a dialog (MX-14). The dialogs merge the dark palette
/// (<c>MaxPalette.xaml</c>) in XAML as the default; when Max is in light mode this appends the light
/// palette over it. Both dictionaries declare the same brush keys and all brushes are referenced via
/// <c>DynamicResource</c>, so the later (light) dictionary simply wins. Applied host-side (the View
/// stays code-behind-free).
/// </summary>
public static class MaxThemeResources
{
    #region Constants

    private const string LIGHT_PALETTE_URI = "/OutWit.Render.3dsMax.Plugin.UI;component/Themes/MaxPaletteLight.xaml";

    #endregion

    #region Functions

    /// <summary>Appends the light palette over a dialog's default (dark) one when the theme is light.</summary>
    public static void Apply(Window window, MaxUiTheme theme)
    {
        if (window is null || theme != MaxUiTheme.Light)
            return;

        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(LIGHT_PALETTE_URI, UriKind.Relative)
        });
    }

    #endregion
}
