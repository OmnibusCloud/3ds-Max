using System;
using System.Linq;
using System.Windows;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.Theming;

/// <summary>
/// Applies the plugin theme to a dialog (MX-14). The dialogs merge the dark palette
/// (<c>MaxPalette.xaml</c>) in XAML as the default; light mode appends the light palette over it and
/// dark mode removes it again. Both dictionaries declare the same brush keys and all brushes are
/// referenced via <c>DynamicResource</c>, so the last dictionary wins and the switch is live — safe to
/// re-apply to an open window. Applied host-side (the View stays code-behind-free).
/// </summary>
public static class MaxThemeResources
{
    #region Constants

    private const string LIGHT_PALETTE_URI = "/OutWit.Render.3dsMax.Plugin.UI;component/Themes/MaxPaletteLight.xaml";

    #endregion

    #region Functions

    /// <summary>Toggles the light palette overlay to match the requested theme (idempotent).</summary>
    public static void Apply(Window window, MaxUiTheme theme)
    {
        if (window is null)
            return;

        var dictionaries = window.Resources.MergedDictionaries;
        var lightPalette = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source != null &&
            dictionary.Source.OriginalString.EndsWith("MaxPaletteLight.xaml", StringComparison.OrdinalIgnoreCase));

        if (theme == MaxUiTheme.Light && lightPalette is null)
            dictionaries.Add(new ResourceDictionary { Source = new Uri(LIGHT_PALETTE_URI, UriKind.Relative) });
        else if (theme != MaxUiTheme.Light && lightPalette != null)
            dictionaries.Remove(lightPalette);
    }

    #endregion
}
