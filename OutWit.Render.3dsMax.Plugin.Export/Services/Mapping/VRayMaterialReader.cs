using System;
using System.Globalization;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

/// <summary>
/// Dedicated VRayMtl reader for the foreign-family gate. V-Ray materials cannot go through the
/// generic parameter heuristics — walking a VRayMtl's param blocks hangs inside the typed
/// IIParamBlock2 getters — but MAXScript property reads of EXISTING VRayMtl properties by anim
/// handle are safe (the wave-A crash trace pinned the hang to the typed getters, and the slow
/// path to per-property script FAILURES). So this reader batches every property the Principled
/// mapping needs into ONE MAXScript execution per material, with a per-property try/catch inside
/// the script, and maps the parsed values onto the neutral material snapshot.
/// </summary>
internal static class VRayMaterialReader
{
    #region Constants

    // Values the script could not read come back as this marker instead of a number.
    private const string MISSING_TOKEN = "?";

    private const char TOKEN_SEPARATOR = '|';

    // MAXScript property expressions evaluated against the material, in parse order. Colour
    // channels are 0-255 floats, booleans are emitted as 1/0. Every name comes from the live
    // VRayMtl property dump (V-Ray 7) — reading an existing property is safe and fast; a missing
    // one (older V-Ray) degrades to the marker via its own try/catch instead of failing the read.
    private static readonly string[] PROPERTY_EXPRESSIONS =
    [
        "m.diffuse.r",
        "m.diffuse.g",
        "m.diffuse.b",
        "m.reflection.r",
        "m.reflection.g",
        "m.reflection.b",
        "m.reflection_glossiness",
        "if m.brdf_useRoughness then 1 else 0",
        "m.reflection_metalness",
        "if m.reflection_fresnel then 1 else 0",
        "m.reflection_ior",
        "if m.reflection_lockIOR then 1 else 0",
        "m.reflection_weight",
        "m.refraction.r",
        "m.refraction.g",
        "m.refraction.b",
        "m.refraction_glossiness",
        "m.refraction_ior",
        "m.refraction_fogColor.r",
        "m.refraction_fogColor.g",
        "m.refraction_fogColor.b",
        "m.refraction_fogMult",
        "m.selfIllumination.r",
        "m.selfIllumination.g",
        "m.selfIllumination.b",
        "m.selfIllumination_multiplier"
    ];

    // Reflection/refraction below this strength is authored OFF, not merely subtle.
    private const double CHANNEL_EPSILON = 0.01d;

    // A VRayMtl with reflection disabled has NO specular response at all; Principled cannot kill
    // its specular lobe (the generator never lowers 'Specular IOR Level' below the default), so
    // render such materials matte instead of leaving the sharp default-roughness highlight.
    private const double NO_REFLECTION_ROUGHNESS = 0.8d;

    #endregion

    #region Script

    /// <summary>
    /// Builds the single MAXScript expression that reads every mapped VRayMtl property of the
    /// material addressed by <paramref name="animHandle"/> into one delimited string.
    /// </summary>
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

    #region Parsing

    /// <summary>
    /// Parses the script result back into values. Returns null (caller falls back to the minimal
    /// safe read) when the payload is not the expected token stream or the diffuse colour — the
    /// one property every VRayMtl version carries — is unreadable.
    /// </summary>
    public static VRayMtlValues? TryParseScriptResult(string? scriptResult)
    {
        if (string.IsNullOrWhiteSpace(scriptResult))
            return null;

        var tokens = scriptResult.Split(TOKEN_SEPARATOR);

        // The script appends a separator after every value, so a well-formed payload has one
        // trailing empty token.
        if (tokens.Length != PROPERTY_EXPRESSIONS.Length + 1)
            return null;

        var values = new double?[PROPERTY_EXPRESSIONS.Length];
        for (var i = 0; i < PROPERTY_EXPRESSIONS.Length; i++)
        {
            var token = tokens[i].Trim();
            if (token == MISSING_TOKEN)
                continue;

            // Comma-decimal safety for non-English workstations: tokens are single numbers, so a
            // locale that prints '0,5' parses after normalization and never poisons the payload.
            if (!double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
                return null;

            values[i] = value;
        }

        if (values[0] is null || values[1] is null || values[2] is null)
            return null;

        return new VRayMtlValues
        {
            DiffuseR = values[0]!.Value,
            DiffuseG = values[1]!.Value,
            DiffuseB = values[2]!.Value,
            ReflectionR = values[3],
            ReflectionG = values[4],
            ReflectionB = values[5],
            ReflectionGlossiness = values[6],
            UseRoughness = values[7] is > 0.5d,
            Metalness = values[8],
            FresnelOn = values[9] is > 0.5d,
            ReflectionIor = values[10],
            LockIor = values[11] is > 0.5d,
            ReflectionWeight = values[12],
            RefractionR = values[13],
            RefractionG = values[14],
            RefractionB = values[15],
            RefractionGlossiness = values[16],
            RefractionIor = values[17],
            FogR = values[18],
            FogG = values[19],
            FogB = values[20],
            FogMultiplier = values[21],
            SelfIlluminationR = values[22],
            SelfIlluminationG = values[23],
            SelfIlluminationB = values[24],
            SelfIlluminationMultiplier = values[25]
        };
    }

    #endregion

    #region Light Material

    // VRayLightMtl: colour, multiplier (raw units, can far exceed 1), and the direct-light toggle.
    private static readonly string[] LIGHT_MATERIAL_PROPERTY_EXPRESSIONS =
    [
        "m.color.r",
        "m.color.g",
        "m.color.b",
        "m.multiplier"
    ];

    public static string BuildLightMaterialMaxScript(ulong animHandle)
    {
        var script = new System.Text.StringBuilder();
        script.Append("(local m = getAnimByHandle ").Append(animHandle.ToString(CultureInfo.InvariantCulture));
        script.Append("; local s = \"\"");

        foreach (var expression in LIGHT_MATERIAL_PROPERTY_EXPRESSIONS)
        {
            script.Append("; s += (try (((").Append(expression).Append(") as float) as string) catch (\"")
                  .Append(MISSING_TOKEN).Append("\")) + \"").Append(TOKEN_SEPARATOR).Append('"');
        }

        script.Append("; s)");
        return script.ToString();
    }

    /// <summary>
    /// Maps a VRayLightMtl (a pure emissive material — glowing panels, screens, lightboxes) onto
    /// the snapshot. Returns false when the payload is malformed.
    /// </summary>
    public static bool TryApplyLightMaterial(string? scriptResult, MaxSceneMaterialSnapshotData snapshot)
    {
        if (string.IsNullOrWhiteSpace(scriptResult))
            return false;

        var tokens = scriptResult.Split(TOKEN_SEPARATOR);
        if (tokens.Length != LIGHT_MATERIAL_PROPERTY_EXPRESSIONS.Length + 1)
            return false;

        var values = new double?[LIGHT_MATERIAL_PROPERTY_EXPRESSIONS.Length];
        for (var i = 0; i < LIGHT_MATERIAL_PROPERTY_EXPRESSIONS.Length; i++)
        {
            var token = tokens[i].Trim();
            if (token == MISSING_TOKEN)
                continue;

            // Comma-decimal safety for non-English workstations: tokens are single numbers, so a
            // locale that prints '0,5' parses after normalization and never poisons the payload.
            if (!double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
                return false;

            values[i] = value;
        }

        if (values[0] is null || values[1] is null || values[2] is null)
            return false;

        var color = (R: values[0]!.Value / 255d, G: values[1]!.Value / 255d, B: values[2]!.Value / 255d);
        snapshot.BaseColor = new MaxSceneColorSnapshotData
        {
            R = Math.Clamp(color.R, 0d, 1d),
            G = Math.Clamp(color.G, 0d, 1d),
            B = Math.Clamp(color.B, 0d, 1d),
            A = 1d
        };
        snapshot.EmissionColor = snapshot.BaseColor;
        snapshot.EmissionStrength = Math.Clamp(values[3] ?? 1d, 0d, 100d);
        snapshot.Roughness = 0.8d;
        return true;
    }

    #endregion

    #region Mapping

    /// <summary>
    /// Maps the raw VRayMtl values onto the neutral material snapshot. Colours stay
    /// display-referred 0..1 — the Dcc mapper linearizes every swatch colour downstream.
    /// </summary>
    public static void Apply(VRayMtlValues values, MaxSceneMaterialSnapshotData snapshot)
    {
        var diffuse = (R: values.DiffuseR / 255d, G: values.DiffuseG / 255d, B: values.DiffuseB / 255d);

        var reflectionWeight = Math.Clamp(values.ReflectionWeight ?? 1d, 0d, 1d);
        var reflection = (
            R: (values.ReflectionR ?? 0d) / 255d * reflectionWeight,
            G: (values.ReflectionG ?? 0d) / 255d * reflectionWeight,
            B: (values.ReflectionB ?? 0d) / 255d * reflectionWeight);
        var reflectionStrength = Math.Max(reflection.R, Math.Max(reflection.G, reflection.B));

        var transmission = Math.Max(values.RefractionR ?? 0d, Math.Max(values.RefractionG ?? 0d, values.RefractionB ?? 0d)) / 255d;

        var baseColor = diffuse;

        // Glossiness spinner is sharpness (1 = mirror) unless the material is authored in
        // roughness mode. Only meaningful when reflection is authored on: the spinner default
        // (1.0) on a reflection-less material would otherwise read as a polished mirror.
        double roughness;
        if (reflectionStrength > CHANNEL_EPSILON)
        {
            var glossiness = Math.Clamp(values.ReflectionGlossiness ?? 1d, 0d, 1d);
            roughness = values.UseRoughness ? glossiness : 1d - glossiness;
        }
        else
        {
            roughness = NO_REFLECTION_ROUGHNESS;
        }

        // V-Ray's PBR metalness carries the metal look with the diffuse as the metal colour —
        // exactly Principled's semantics. Never metallize a transmissive material (Metallic 1
        // disables Principled transmission outright — the same trap the Raytrace glass hit).
        // The gate mirrors the transmission branch below EXACTLY: with mismatched thresholds a
        // faintly-refractive rough metal fell into both branches and rendered as a polished
        // mirror (the refraction glossiness default overwrote its authored roughness).
        var metallic = 0d;
        if (transmission <= CHANNEL_EPSILON && reflectionStrength > CHANNEL_EPSILON)
        {
            metallic = Math.Clamp(values.Metalness ?? 0d, 0d, 1d);

            // Fresnel OFF + strong reflection is the legacy V-Ray mirror workflow (constant
            // reflectivity, look carried by the reflection colour over a near-black diffuse).
            // Fold it into Metallic like the Raytrace chrome path, promoting the reflection
            // tint into the base so the mirror doesn't render as black.
            if (!values.FresnelOn && reflectionStrength > 0.5d)
            {
                metallic = Math.Max(metallic, reflectionStrength);
                roughness = Math.Min(roughness, 0.15d);

                var baseLuminance = Math.Max(baseColor.R, Math.Max(baseColor.G, baseColor.B));
                if (baseLuminance < 0.1d)
                    baseColor = reflection;
            }
        }

        if (transmission > CHANNEL_EPSILON)
        {
            snapshot.Transmission = transmission;
            snapshot.Opacity = 1d;

            // For a transmissive material the refraction glossiness IS the visible finish
            // (clear vs frosted); the reflection-derived roughness (or the matte fallback when
            // reflection is off) would frost clear glass.
            roughness = 1d - Math.Clamp(values.RefractionGlossiness ?? 1d, 0d, 1d);

            // The refraction colour is a weighting filter (the Raytrace pattern): the visible
            // pixel blends the diffuse remainder with the filtered background, and Principled
            // tints transmission via the base colour.
            baseColor = (
                R: Math.Clamp(diffuse.R * (1d - transmission) + (values.RefractionR ?? 0d) / 255d, 0d, 1d),
                G: Math.Clamp(diffuse.G * (1d - transmission) + (values.RefractionG ?? 0d) / 255d, 0d, 1d),
                B: Math.Clamp(diffuse.B * (1d - transmission) + (values.RefractionB ?? 0d) / 255d, 0d, 1d));

            // Fog is V-Ray's absorption tint — approximate it as a base-colour filter (white
            // fog or zero multiplier is a no-op).
            if (values.FogMultiplier is > 0d)
            {
                baseColor = (
                    R: baseColor.R * Math.Clamp((values.FogR ?? 255d) / 255d, 0d, 1d),
                    G: baseColor.G * Math.Clamp((values.FogG ?? 255d) / 255d, 0d, 1d),
                    B: baseColor.B * Math.Clamp((values.FogB ?? 255d) / 255d, 0d, 1d));
            }

            if (values.RefractionIor is >= 1.0d and <= 3.0d)
                snapshot.Ior = values.RefractionIor.Value;
        }
        else if (values.FresnelOn)
        {
            // Lock IOR means the Fresnel curve follows the refraction IOR spinner.
            var fresnelIor = values.LockIor ? values.RefractionIor : values.ReflectionIor;
            if (fresnelIor is >= 1.0d and <= 3.0d)
                snapshot.Ior = fresnelIor.Value;
        }

        snapshot.BaseColor = new MaxSceneColorSnapshotData { R = baseColor.R, G = baseColor.G, B = baseColor.B, A = 1d };
        snapshot.Roughness = Math.Clamp(roughness, 0d, 1d);
        snapshot.Metallic = metallic;

        // VRayMtl self-illumination is an additive emission layer.
        var selfIllumination = (
            R: (values.SelfIlluminationR ?? 0d) / 255d,
            G: (values.SelfIlluminationG ?? 0d) / 255d,
            B: (values.SelfIlluminationB ?? 0d) / 255d);
        if (Math.Max(selfIllumination.R, Math.Max(selfIllumination.G, selfIllumination.B)) > CHANNEL_EPSILON)
        {
            snapshot.EmissionColor = new MaxSceneColorSnapshotData { R = selfIllumination.R, G = selfIllumination.G, B = selfIllumination.B, A = 1d };
            snapshot.EmissionStrength = Math.Clamp(values.SelfIlluminationMultiplier ?? 1d, 0d, 100d);
        }
    }

    #endregion
}

/// <summary>
/// Raw VRayMtl property values read via MAXScript. Colour channels are 0-255 floats exactly as
/// MAXScript reports them; null means the property was unreadable (older V-Ray build).
/// </summary>
internal sealed class VRayMtlValues
{
    #region Properties

    public double DiffuseR { get; init; }

    public double DiffuseG { get; init; }

    public double DiffuseB { get; init; }

    public double? ReflectionR { get; init; }

    public double? ReflectionG { get; init; }

    public double? ReflectionB { get; init; }

    public double? ReflectionGlossiness { get; init; }

    public bool UseRoughness { get; init; }

    public double? Metalness { get; init; }

    public bool FresnelOn { get; init; }

    public double? ReflectionIor { get; init; }

    public bool LockIor { get; init; }

    public double? ReflectionWeight { get; init; }

    public double? RefractionR { get; init; }

    public double? RefractionG { get; init; }

    public double? RefractionB { get; init; }

    public double? RefractionGlossiness { get; init; }

    public double? RefractionIor { get; init; }

    public double? FogR { get; init; }

    public double? FogG { get; init; }

    public double? FogB { get; init; }

    public double? FogMultiplier { get; init; }

    public double? SelfIlluminationR { get; init; }

    public double? SelfIlluminationG { get; init; }

    public double? SelfIlluminationB { get; init; }

    public double? SelfIlluminationMultiplier { get; init; }

    #endregion
}
