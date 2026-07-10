using System;
using System.Globalization;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

/// <summary>
/// Reads the authored exposure (EV100) off a physical camera — the stock Physical Camera or a
/// VRayPhysicalCamera — in one batched MAXScript evaluation. Non-physical cameras answer every
/// probe with the marker and resolve to null. The EV itself is absolute (authored against
/// physical light intensities the export normalizes away); the mapper carries only its
/// DEVIATION from a photographic reference.
/// </summary>
internal static class PhysicalCameraExposureReader
{
    #region Constants

    private const string MISSING_TOKEN = "?";

    private const char TOKEN_SEPARATOR = '|';

    // VRayPhysicalCamera.exposure modes.
    private const int VRAY_EXPOSURE_OFF = 0;

    private const int VRAY_EXPOSURE_FROM_CAMERA = 1;

    private const int VRAY_EXPOSURE_FROM_VALUE = 2;

    private static readonly string[] PROPERTY_EXPRESSIONS =
    [
        "m.exposure_value",
        "m.exposure_gain_type",
        "m.ISO",
        "m.f_number",
        "m.shutter_length_seconds",
        "m.shutter_speed",
        "m.exposure"
    ];

    #endregion

    #region Script

    public static string BuildMaxScript(ulong animHandle)
    {
        var script = new System.Text.StringBuilder();
        script.Append("(local m = getAnimByHandle ").Append(animHandle.ToString(CultureInfo.InvariantCulture));
        script.Append("; local s = \"\"");

        foreach (var expression in PROPERTY_EXPRESSIONS)
        {
            script.Append("; s += (try (((").Append(expression).Append(") as float) as string) catch (\"")
                  .Append(MISSING_TOKEN).Append("\")) + \"").Append(TOKEN_SEPARATOR).Append('"');
        }

        script.Append("; s)");
        return script.ToString();
    }

    #endregion

    #region Resolution

    /// <summary>
    /// Resolves the camera's EV100 from the script payload, or null when the camera carries no
    /// usable exposure (not a physical camera, V-Ray exposure disabled, degenerate physicals).
    /// </summary>
    public static double? TryResolveExposureValue(string? scriptResult)
    {
        if (string.IsNullOrWhiteSpace(scriptResult))
            return null;

        var tokens = scriptResult.Split(TOKEN_SEPARATOR);
        if (tokens.Length != PROPERTY_EXPRESSIONS.Length + 1)
            return null;

        var values = new double?[PROPERTY_EXPRESSIONS.Length];
        for (var i = 0; i < PROPERTY_EXPRESSIONS.Length; i++)
        {
            var token = tokens[i].Trim();
            if (token == MISSING_TOKEN)
                continue;

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
                continue;

            values[i] = value;
        }

        var exposureValue = values[0];
        var iso = values[2];
        var fNumber = values[3];

        // VRayPhysicalCamera: the exposure combo says whether (and how) exposure applies —
        // shutter_speed is 1/seconds there.
        if (values[6] is not null)
        {
            var mode = (int)Math.Round(values[6]!.Value);
            if (mode == VRAY_EXPOSURE_OFF)
                return null;

            if (mode == VRAY_EXPOSURE_FROM_VALUE)
                return exposureValue;

            if (mode == VRAY_EXPOSURE_FROM_CAMERA)
                return ComputeEv100(fNumber, values[5] is > 0d ? 1d / values[5]!.Value : null, iso);

            return null;
        }

        // Stock Physical Camera. Per the MAXScript reference the gain combo is 0 = Manual (ISO),
        // 1 = Target (EV, the default): Target mode keeps the authored EV the camera reports,
        // Manual mode derives EV100 from the physicals (the Target spinner is inactive there and
        // may hold a stale value — Max only syncs it while the UI is open).
        if (values[1] is not null)
        {
            var gainType = (int)Math.Round(values[1]!.Value);
            if (gainType == 1 && exposureValue is not null)
                return exposureValue;

            return ComputeEv100(fNumber, values[4], iso) ?? exposureValue;
        }

        return null;
    }

    // EV100 = log2(N² / t · 100 / ISO).
    private static double? ComputeEv100(double? fNumber, double? shutterSeconds, double? iso)
    {
        if (fNumber is not > 0d || shutterSeconds is not > 0d || iso is not > 0d)
            return null;

        var ev = Math.Log2(fNumber.Value * fNumber.Value / shutterSeconds.Value * 100d / iso.Value);
        return double.IsFinite(ev) ? ev : null;
    }

    #endregion
}
