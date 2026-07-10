using System;
using System.Globalization;
using System.Linq;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

/// <summary>
/// Best-effort VRayScannedMtl reader. A scanned material IS its proprietary measured BRDF
/// (.vrscan) — the true look cannot be reconstructed from parameters, which is a documented
/// limitation. But three honest signals are readable and make the export a recognizable
/// approximation instead of white clay: the artist's paint/filter colour overrides, and the
/// Chaos scan library's own file naming (`Carpaint_Red_1_S.vrscan`, `Metal_Matte_3_S.vrscan`),
/// which encodes the scan type and usually its colour.
/// </summary>
internal static class VRayScannedMaterialReader
{
    #region Constants

    private const string MISSING_TOKEN = "?";

    private const char TOKEN_SEPARATOR = '|';

    // usepaint, paint_color rgb, useflt, filter_Color rgb — the numeric prefix of the payload;
    // the scan filename is appended as the final (string) token.
    private const int NUMERIC_TOKEN_COUNT = 8;

    // Representative display-referred swatches for colour words in Chaos scan file names.
    private static readonly (string Keyword, double R, double G, double B)[] COLOR_KEYWORDS =
    [
        ("red", 0.62d, 0.04d, 0.04d),
        ("green", 0.10d, 0.45d, 0.12d),
        ("blue", 0.08d, 0.20d, 0.60d),
        ("black", 0.04d, 0.04d, 0.04d),
        ("white", 0.92d, 0.92d, 0.92d),
        ("grey", 0.50d, 0.50d, 0.50d),
        ("gray", 0.50d, 0.50d, 0.50d),
        ("silver", 0.78d, 0.78d, 0.80d),
        ("gold", 0.85d, 0.66d, 0.21d),
        ("copper", 0.72d, 0.45d, 0.28d),
        ("bronze", 0.55d, 0.40d, 0.22d),
        ("yellow", 0.90d, 0.78d, 0.12d),
        ("orange", 0.90d, 0.45d, 0.10d),
        ("brown", 0.40d, 0.26d, 0.15d),
        ("beige", 0.80d, 0.73d, 0.60d),
        ("violet", 0.45d, 0.15d, 0.60d),
        ("purple", 0.45d, 0.15d, 0.60d),
        ("pink", 0.90d, 0.55d, 0.65d),
        ("ivory", 0.90d, 0.86d, 0.75d),
        ("cream", 0.90d, 0.86d, 0.75d)
    ];

    #endregion

    #region Script

    /// <summary>
    /// Builds the single MAXScript expression reading the scanned material's overrides and scan
    /// file name. The filename comes last so the parser can treat every earlier token as numeric.
    /// </summary>
    public static string BuildMaxScript(ulong animHandle)
    {
        var script = new System.Text.StringBuilder();
        script.Append("(local m = getAnimByHandle ").Append(animHandle.ToString(CultureInfo.InvariantCulture));
        script.Append("; local s = \"\"");

        string[] numericExpressions =
        [
            "if m.usepaint then 1 else 0",
            "m.paint_color.r",
            "m.paint_color.g",
            "m.paint_color.b",
            "if m.useflt then 1 else 0",
            "m.filter_Color.r",
            "m.filter_Color.g",
            "m.filter_Color.b"
        ];
        foreach (var expression in numericExpressions)
        {
            script.Append("; s += (try (((").Append(expression).Append(") as float) as string) catch (\"")
                  .Append(MISSING_TOKEN).Append("\")) + \"").Append(TOKEN_SEPARATOR).Append('"');
        }

        script.Append("; s += (try (m.filename as string) catch (\"").Append(MISSING_TOKEN)
              .Append("\")) + \"").Append(TOKEN_SEPARATOR).Append('"');
        script.Append("; s)");
        return script.ToString();
    }

    #endregion

    #region Parsing And Mapping

    /// <summary>
    /// Parses the script payload and applies the approximation. Returns false (caller keeps the
    /// minimal safe fallback) when the payload is malformed.
    /// </summary>
    public static bool TryApply(string? scriptResult, MaxSceneMaterialSnapshotData snapshot)
    {
        if (string.IsNullOrWhiteSpace(scriptResult))
            return false;

        var tokens = scriptResult.Split(TOKEN_SEPARATOR);
        if (tokens.Length != NUMERIC_TOKEN_COUNT + 2)
            return false;

        var values = new double?[NUMERIC_TOKEN_COUNT];
        for (var i = 0; i < NUMERIC_TOKEN_COUNT; i++)
        {
            var token = tokens[i].Trim();
            if (token == MISSING_TOKEN)
                continue;

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
                return false;

            values[i] = value;
        }

        // The artist's material name is as descriptive as the scan file name and survives when
        // the scan file was renamed or relinked (ChairCloth's 'VRScan_MetalSheet' points at a
        // scan file with no type word) — search both.
        var scanName = ExtractScanName(tokens[NUMERIC_TOKEN_COUNT])
                       + " " + snapshot.Name.ToLowerInvariant();

        // Scan TYPE sets the response curve the measured BRDF would have carried.
        double metallic;
        double roughness;
        if (scanName.Contains("carpaint") || scanName.Contains("carbon"))
        {
            // Pigment (or woven carbon fibre) under clearcoat: metallic with a polished finish.
            metallic = 0.85d;
            roughness = 0.3d;
        }
        else if (scanName.Contains("metal") || scanName.Contains("chrome") || scanName.Contains("alu"))
        {
            metallic = 1d;
            roughness = scanName.Contains("matte") ? 0.5d
                : scanName.Contains("polished") || scanName.Contains("shiny") || scanName.Contains("mirror") ? 0.15d
                : 0.35d;
        }
        else if (scanName.Contains("fabric") || scanName.Contains("suede") || scanName.Contains("cloth")
                 || scanName.Contains("leather") || scanName.Contains("velvet") || scanName.Contains("silk"))
        {
            metallic = 0d;
            roughness = 0.85d;
        }
        else
        {
            metallic = 0d;
            roughness = 0.6d;
        }

        // Base colour priority: the artist's enabled paint override wins, then an enabled
        // non-white filter tint, then the scan file's own colour word; metals default to a
        // silvery base, everything else to a neutral light grey.
        (double R, double G, double B)? baseColor = null;
        if (values[0] is > 0.5d && values[1] is not null && values[2] is not null && values[3] is not null)
            baseColor = (values[1]!.Value / 255d, values[2]!.Value / 255d, values[3]!.Value / 255d);

        if (baseColor is null && values[4] is > 0.5d && values[5] is not null && values[6] is not null && values[7] is not null)
        {
            var filter = (R: values[5]!.Value / 255d, G: values[6]!.Value / 255d, B: values[7]!.Value / 255d);
            if (Math.Min(filter.R, Math.Min(filter.G, filter.B)) < 0.98d)
                baseColor = filter;
        }

        if (baseColor is null)
        {
            var keywordTokens = scanName.Split(['_', '-', ' ', '.'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var (keyword, r, g, b) in COLOR_KEYWORDS)
            {
                if (keywordTokens.Contains(keyword))
                {
                    baseColor = (r, g, b);
                    break;
                }
            }
        }

        baseColor ??= metallic > 0.5d ? (0.78d, 0.78d, 0.80d) : (0.72d, 0.72d, 0.72d);

        snapshot.BaseColor = new MaxSceneColorSnapshotData
        {
            R = Math.Clamp(baseColor.Value.R, 0d, 1d),
            G = Math.Clamp(baseColor.Value.G, 0d, 1d),
            B = Math.Clamp(baseColor.Value.B, 0d, 1d),
            A = 1d
        };
        snapshot.Metallic = metallic;
        snapshot.Roughness = roughness;
        return true;
    }

    /// <summary>
    /// Whether the scan's look lives in its diffuse response (fabric/leather/generic surfaces) —
    /// the only scans worth the RTT diffuse-filter bake. Car paint, carbon and metals carry their
    /// look in the specular response: their diffuse filter is legitimately near black, and baking
    /// it would replace a good parametric approximation with a black smear (the black-car bug).
    /// </summary>
    public static bool IsDiffuseDominantScan(string? scriptResult, string materialName)
    {
        var scanName = string.Empty;
        var tokens = (scriptResult ?? string.Empty).Split(TOKEN_SEPARATOR);
        if (tokens.Length == NUMERIC_TOKEN_COUNT + 2)
            scanName = ExtractScanName(tokens[NUMERIC_TOKEN_COUNT]);

        scanName = scanName + " " + (materialName ?? string.Empty).ToLowerInvariant();
        return !scanName.Contains("carpaint") && !scanName.Contains("carbon")
               && !scanName.Contains("metal") && !scanName.Contains("chrome") && !scanName.Contains("alu");
    }

    // "\Assets\VRScans\Carpaint_Red_1_S.vrscan" -> "carpaint_red_1_s"
    private static string ExtractScanName(string filenameToken)
    {
        var token = filenameToken.Trim();
        if (token.Length == 0 || token == MISSING_TOKEN)
            return string.Empty;

        var lastSeparator = token.LastIndexOfAny(['\\', '/']);
        var fileName = lastSeparator >= 0 ? token[(lastSeparator + 1)..] : token;

        var extensionIndex = fileName.LastIndexOf('.');
        if (extensionIndex > 0)
            fileName = fileName[..extensionIndex];

        return fileName.ToLowerInvariant();
    }

    #endregion
}
