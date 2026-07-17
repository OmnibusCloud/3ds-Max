using System.Windows.Media;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

/// <summary>
/// One entry of the unified Target list — a project (campaign) or a client group. Carries the
/// kind EXPLICITLY (no name-set heuristics: two targets may legitimately share a display name
/// across kinds) plus the colored-dot/label bits the ComboBox item template renders so the two
/// kinds tell apart at a glance (the Blender addon uses enum icons for the same reason).
/// </summary>
public sealed class RenderTargetOption
{
    #region Constants

    // Frozen for cross-thread safety (WPF bindings). Project = the brand lime family, group =
    // a steel blue — both readable on the dark and the light Max theme, and the textual
    // KindLabel keeps the distinction visible without color.
    private static readonly Brush PROJECT_BRUSH = CreateFrozen(0x8B, 0xC3, 0x4A);

    private static readonly Brush GROUP_BRUSH = CreateFrozen(0x64, 0xB5, 0xF6);

    #endregion

    #region Tools

    private static Brush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    #endregion

    #region Properties

    public bool IsProject { get; init; }

    public string Name { get; init; } = string.Empty;

    public string KindLabel => IsProject ? "project" : "group";

    public Brush KindBrush => IsProject ? PROJECT_BRUSH : GROUP_BRUSH;

    #endregion
}
