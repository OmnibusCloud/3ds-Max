using System;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

/// <summary>
/// Detects third-party renderer plugin classes (V-Ray, Corona, …) by class-name prefix. Objects
/// of these classes must not run through the generic parameter heuristics — walking their param
/// blocks hangs inside the typed getters — so they take dedicated readers or the minimal safe
/// path (see the collector's foreign-family gate).
/// </summary>
internal static class ForeignRendererClasses
{
    #region Constants

    private static readonly string[] CASE_INSENSITIVE_PREFIXES =
    [
        "VRay", "Corona", "Octane", "Redshift", "Maxwell", "Thea", "finalRender"
    ];

    // finalRender's short-prefix classes are literally "fR…" (lowercase f, capital R). Matching
    // this prefix case-insensitively swallowed the stock "Free …" light classes — a photometric
    // Free Light degraded to the neutral fallback and lost its authored intensity and colour —
    // so this one prefix matches case-SENSITIVELY.
    private const string FINAL_RENDER_SHORT_PREFIX = "fR";

    #endregion

    #region Functions

    public static bool IsForeign(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return false;

        if (className.StartsWith(FINAL_RENDER_SHORT_PREFIX, StringComparison.Ordinal))
            return true;

        foreach (var prefix in CASE_INSENSITIVE_PREFIXES)
        {
            if (className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    #endregion
}
