using System;
using System.Globalization;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

/// <summary>
/// Dedicated readers for V-Ray light classes (VRayLight, VRaySun). Same rails as the material
/// readers: the typed GenLight getters are unsafe on foreign classes, but one batched MAXScript
/// evaluation of existing properties per light is safe and fast.
/// </summary>
internal static class VRayLightReader
{
    #region Constants

    private const string MISSING_TOKEN = "?";

    private const char TOKEN_SEPARATOR = '|';

    // VRayLight.type values (V-Ray SDK): 0 plane, 1 dome, 2 sphere, 3 mesh, 4 disc.
    private const int LIGHT_TYPE_PLANE = 0;

    private const int LIGHT_TYPE_DOME = 1;

    private const int LIGHT_TYPE_SPHERE = 2;

    private const int LIGHT_TYPE_DISC = 4;

    private static readonly string[] LIGHT_PROPERTY_EXPRESSIONS =
    [
        "if m.on then 1 else 0",
        "m.type",
        "m.color.r",
        "m.color.g",
        "m.color.b",
        "m.multiplier",
        "m.normalizeColor",
        "m.sizeLength",
        "m.sizeWidth",
        "m.size0",
        "if m.noDecay then 1 else 0",
        "if m.castShadows then 1 else 0",
        "m.size1"
    ];

    private static readonly string[] SUN_PROPERTY_EXPRESSIONS =
    [
        "if m.enabled then 1 else 0",
        "m.intensity_multiplier",
        "m.filter_Color.r",
        "m.filter_Color.g",
        "m.filter_Color.b"
    ];

    #endregion

    #region Scripts

    public static string BuildLightMaxScript(ulong animHandle)
    {
        return BuildScript(animHandle, LIGHT_PROPERTY_EXPRESSIONS);
    }

    public static string BuildSunMaxScript(ulong animHandle)
    {
        return BuildScript(animHandle, SUN_PROPERTY_EXPRESSIONS);
    }

    private static string BuildScript(ulong animHandle, string[] expressions)
    {
        var script = new System.Text.StringBuilder();
        script.Append("(local m = getAnimByHandle ").Append(animHandle.ToString(CultureInfo.InvariantCulture));
        script.Append("; local s = \"\"");

        foreach (var expression in expressions)
        {
            script.Append("; s += (try (((").Append(expression).Append(") as float) as string) catch (\"")
                  .Append(MISSING_TOKEN).Append("\")) + \"").Append(TOKEN_SEPARATOR).Append('"');
        }

        script.Append("; s)");
        return script.ToString();
    }

    private static double?[]? ParseTokens(string? scriptResult, int expectedCount)
    {
        if (string.IsNullOrWhiteSpace(scriptResult))
            return null;

        var tokens = scriptResult.Split(TOKEN_SEPARATOR);
        if (tokens.Length != expectedCount + 1)
            return null;

        var values = new double?[expectedCount];
        for (var i = 0; i < expectedCount; i++)
        {
            var token = tokens[i].Trim();
            if (token == MISSING_TOKEN)
                continue;

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
                return null;

            values[i] = value;
        }

        return values;
    }

    #endregion

    #region VRayLight

    /// <summary>
    /// Parses the VRayLight payload and fills the snapshot. The returned role tells the caller
    /// how the light contributes: a node light, a dome (environment, snapshot intensity zeroed
    /// so the node is dropped), or an unsupported shape (caller falls back to the neutral read).
    /// The type and multiplier — the two values the mapping cannot proceed without — must parse.
    /// </summary>
    public static VRayLightRole Apply(string? scriptResult, MaxSceneLightSnapshotData snapshot)
    {
        var values = ParseTokens(scriptResult, LIGHT_PROPERTY_EXPRESSIONS.Length);
        if (values is null || values[1] is null || values[5] is null)
            return VRayLightRole.Unsupported;

        var isOn = values[0] is null or > 0.5d;
        var type = (int)Math.Round(values[1]!.Value);
        var multiplier = Math.Max(values[5]!.Value, 0d);

        snapshot.Color = new MaxSceneColorSnapshotData
        {
            R = Math.Clamp((values[2] ?? 255d) / 255d, 0d, 1d),
            G = Math.Clamp((values[3] ?? 255d) / 255d, 0d, 1d),
            B = Math.Clamp((values[4] ?? 255d) / 255d, 0d, 1d),
            A = 1d
        };
        snapshot.Intensity = isOn ? multiplier : 0d;
        // Any non-default unit mode (lumens/luminance/watts/radiance) is a physical value the
        // mapper's photometric normalization handles; the default "image" mode behaves like a
        // standard multiplier.
        snapshot.IsPhotometric = values[6] is not null && (int)Math.Round(values[6]!.Value) != 0;
        snapshot.NoDecay = values[10] is > 0.5d;
        snapshot.CastShadows = values[11] is null or > 0.5d;

        switch (type)
        {
            case LIGHT_TYPE_PLANE:
                snapshot.Kind = DccLightKind.Area;
                // sizeLength/sizeWidth are the FULL extents (verified: 2× the size0/size1
                // half-extents on the live dump); older V-Ray builds without them fall back
                // to doubling the half-extent spinners.
                snapshot.AreaWidth = Math.Max(values[7] ?? ((values[9] ?? 0.5d) * 2d), 0.01d);
                snapshot.AreaHeight = Math.Max(values[8] ?? ((values[12] ?? 0.5d) * 2d), 0.01d);
                return VRayLightRole.Light;

            case LIGHT_TYPE_DISC:
                snapshot.Kind = DccLightKind.Area;
                var discDiameter = Math.Max((values[9] ?? 0.5d) * 2d, 0.01d);
                snapshot.AreaWidth = discDiameter;
                snapshot.AreaHeight = discDiameter;
                return VRayLightRole.Light;

            case LIGHT_TYPE_SPHERE:
                snapshot.Kind = DccLightKind.Point;
                return VRayLightRole.Light;

            case LIGHT_TYPE_DOME:
                // A dome is the scene's environment, not a node light. The snapshot keeps the
                // colour/multiplier for the caller's environment capture; the caller zeroes the
                // intensity afterwards so the light node is dropped.
                snapshot.Kind = DccLightKind.Point;
                return isOn && multiplier > 0d ? VRayLightRole.DomeEnvironment : VRayLightRole.Light;

            default:
                return VRayLightRole.Unsupported;
        }
    }

    #endregion

    #region VRaySun

    /// <summary>
    /// Parses the VRaySun payload into a sun snapshot. The sun direction travels on the node
    /// transform like every stock directional light; only intensity and filter colour are read.
    /// </summary>
    public static bool TryApplySun(string? scriptResult, MaxSceneLightSnapshotData snapshot)
    {
        var values = ParseTokens(scriptResult, SUN_PROPERTY_EXPRESSIONS.Length);
        if (values is null || values[1] is null)
            return false;

        var isEnabled = values[0] is null or > 0.5d;

        snapshot.Kind = DccLightKind.Sun;
        snapshot.Color = new MaxSceneColorSnapshotData
        {
            R = Math.Clamp((values[2] ?? 255d) / 255d, 0d, 1d),
            G = Math.Clamp((values[3] ?? 255d) / 255d, 0d, 1d),
            B = Math.Clamp((values[4] ?? 255d) / 255d, 0d, 1d),
            A = 1d
        };
        snapshot.Intensity = isEnabled ? Math.Max(values[1]!.Value, 0d) : 0d;
        snapshot.CastShadows = true;
        return true;
    }

    #endregion
}

/// <summary>How a V-Ray light contributes to the exported scene.</summary>
internal enum VRayLightRole
{
    /// <summary>A regular node light (plane/disc/sphere) — export the snapshot as-is.</summary>
    Light,

    /// <summary>A dome light — route into the scene environment; the node itself is dropped.</summary>
    DomeEnvironment,

    /// <summary>No dedicated mapping (mesh lights, parse failures) — use the neutral fallback.</summary>
    Unsupported
}
