using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Formats the host prompt-line text for a render status (design section 3): a branded prefix plus the
/// status line, e.g. <c>OmnibusCloud · Rendering 142/240</c>. Kept separate from the host service so the
/// wording is unit-testable without a Max host.
/// </summary>
public static class MaxStatusBarText
{
    #region Constants

    public const string PREFIX = "OmnibusCloud";

    #endregion

    #region Functions

    /// <summary>Builds the single-line prompt text for the given status.</summary>
    public static string Format(MaxRenderStatus status)
    {
        var line = string.IsNullOrWhiteSpace(status.StatusLine) ? status.Phase.ToString() : status.StatusLine;
        return $"{PREFIX} · {line}";
    }

    #endregion
}
