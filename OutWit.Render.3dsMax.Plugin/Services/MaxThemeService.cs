using System;
using Autodesk.Max;
using OutWit.Render.ThreeDsMax.Plugin.UI.Theming;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

/// <summary>
/// Resolves the plugin theme from the 3ds Max color manager (MX-14): light when Max is in a light
/// theme, otherwise the dark Max-native default. The color-theme property's type is not surfaced in the
/// managed SDK metadata, so it is read defensively by name and matched as text — any failure or
/// unrecognised value falls back to <see cref="MaxUiTheme.Dark"/>.
/// </summary>
public sealed class MaxThemeService : IMaxThemeService
{
    #region Fields

    private readonly IGlobal m_global;

    #endregion

    #region Constructors

    public MaxThemeService(IGlobal global)
    {
        m_global = global;
    }

    #endregion

    #region IMaxThemeService

    public MaxUiTheme CurrentTheme
    {
        get
        {
            try
            {
                var colorManager = m_global.ColorManager;
                if (colorManager is null)
                    return MaxUiTheme.Dark;

                var theme = colorManager.GetType()
                    .GetProperty("AppFrameColorTheme")?
                    .GetValue(colorManager)?
                    .ToString();

                if (!string.IsNullOrEmpty(theme) && theme.Contains("light", StringComparison.OrdinalIgnoreCase))
                    return MaxUiTheme.Light;

                return MaxUiTheme.Dark;
            }
            catch
            {
                return MaxUiTheme.Dark;
            }
        }
    }

    #endregion
}
