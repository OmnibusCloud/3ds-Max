using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services.Mapping;

/// <summary>
/// Verifies the dedicated VRayMtl reader: the batched MAXScript text, the delimited-payload
/// parser, and the VRayMtl -> Principled snapshot mapping rules.
/// </summary>
[TestFixture]
public sealed class VRayMaterialReaderTests
{
    #region Tools

    // Token order mirrors VRayMaterialReader.PROPERTY_EXPRESSIONS; the script terminates every
    // value (including the last) with a separator.
    private static string BuildPayload(
        string diffuse = "128.0|64.0|32.0",
        string reflection = "0.0|0.0|0.0",
        string reflectionGlossiness = "1.0",
        string useRoughness = "0.0",
        string metalness = "0.0",
        string fresnelOn = "1.0",
        string reflectionIor = "1.6",
        string lockIor = "0.0",
        string reflectionWeight = "1.0",
        string refraction = "0.0|0.0|0.0",
        string refractionGlossiness = "1.0",
        string refractionIor = "1.6",
        string fog = "255.0|255.0|255.0",
        string fogMultiplier = "1.0",
        string selfIllumination = "0.0|0.0|0.0",
        string selfIlluminationMultiplier = "1.0")
    {
        return string.Join('|',
                   diffuse, reflection, reflectionGlossiness, useRoughness, metalness, fresnelOn,
                   reflectionIor, lockIor, reflectionWeight, refraction, refractionGlossiness,
                   refractionIor, fog, fogMultiplier, selfIllumination, selfIlluminationMultiplier)
               + "|";
    }

    private static MaxSceneMaterialSnapshotData ApplyPayload(string payload)
    {
        var values = VRayMaterialReader.TryParseScriptResult(payload);
        Assert.That(values, Is.Not.Null);

        var snapshot = new MaxSceneMaterialSnapshotData();
        VRayMaterialReader.Apply(values!, snapshot);
        return snapshot;
    }

    #endregion

    #region Script Tests

    [Test]
    public void BuildMaxScriptEmbedsHandleAndEveryMappedPropertyTest()
    {
        var script = VRayMaterialReader.BuildMaxScript(421337);

        Assert.That(script, Does.StartWith("("));
        Assert.That(script, Does.EndWith(")"));
        Assert.That(script, Does.Contain("getAnimByHandle 421337"));

        // One try/catch guard per property keeps a missing property (older V-Ray) from failing
        // the whole batched read.
        Assert.That(script, Does.Contain("m.diffuse.r"));
        Assert.That(script, Does.Contain("m.reflection_glossiness"));
        Assert.That(script, Does.Contain("m.brdf_useRoughness"));
        Assert.That(script, Does.Contain("m.reflection_metalness"));
        Assert.That(script, Does.Contain("m.reflection_fresnel"));
        Assert.That(script, Does.Contain("m.reflection_ior"));
        Assert.That(script, Does.Contain("m.reflection_lockIOR"));
        Assert.That(script, Does.Contain("m.reflection_weight"));
        Assert.That(script, Does.Contain("m.refraction_ior"));
        Assert.That(script, Does.Contain("m.refraction_fogColor.b"));
        Assert.That(script, Does.Contain("m.refraction_fogMult"));
        Assert.That(script, Does.Contain("m.selfIllumination_multiplier"));
        Assert.That(script, Does.Not.Contain("\n"), "the script must stay a single-line expression");
    }

    #endregion

    #region Parser Tests

    [Test]
    public void ParseWellFormedPayloadTest()
    {
        var values = VRayMaterialReader.TryParseScriptResult(BuildPayload(
            diffuse: "255.0|128.0|0.0",
            reflection: "199.0|199.0|199.0",
            reflectionGlossiness: "0.85",
            useRoughness: "0.0",
            metalness: "1.0",
            fresnelOn: "1.0",
            reflectionIor: "1.8",
            refraction: "230.0|230.0|230.0",
            refractionIor: "1.52",
            selfIllumination: "10.0|20.0|30.0",
            selfIlluminationMultiplier: "3.0"));

        Assert.That(values, Is.Not.Null);
        Assert.That(values!.DiffuseR, Is.EqualTo(255d).Within(1e-9));
        Assert.That(values.DiffuseG, Is.EqualTo(128d).Within(1e-9));
        Assert.That(values.ReflectionR, Is.EqualTo(199d).Within(1e-9));
        Assert.That(values.ReflectionGlossiness, Is.EqualTo(0.85d).Within(1e-9));
        Assert.That(values.UseRoughness, Is.False);
        Assert.That(values.Metalness, Is.EqualTo(1d).Within(1e-9));
        Assert.That(values.FresnelOn, Is.True);
        Assert.That(values.ReflectionIor, Is.EqualTo(1.8d).Within(1e-9));
        Assert.That(values.RefractionB, Is.EqualTo(230d).Within(1e-9));
        Assert.That(values.RefractionIor, Is.EqualTo(1.52d).Within(1e-9));
        Assert.That(values.SelfIlluminationB, Is.EqualTo(30d).Within(1e-9));
        Assert.That(values.SelfIlluminationMultiplier, Is.EqualTo(3d).Within(1e-9));
    }

    [Test]
    public void ParseMissingOptionalTokensYieldsNullsTest()
    {
        var values = VRayMaterialReader.TryParseScriptResult(BuildPayload(
            reflection: "?|?|?",
            reflectionGlossiness: "?",
            metalness: "?",
            reflectionWeight: "?",
            selfIlluminationMultiplier: "?"));

        Assert.That(values, Is.Not.Null);
        Assert.That(values!.ReflectionR, Is.Null);
        Assert.That(values.ReflectionGlossiness, Is.Null);
        Assert.That(values.Metalness, Is.Null);
        Assert.That(values.ReflectionWeight, Is.Null);
        Assert.That(values.SelfIlluminationMultiplier, Is.Null);
    }

    [Test]
    public void ParseUnreadableDiffuseFailsTest()
    {
        Assert.That(VRayMaterialReader.TryParseScriptResult(BuildPayload(diffuse: "?|64.0|32.0")), Is.Null);
    }

    [Test]
    public void ParseMalformedPayloadFailsTest()
    {
        Assert.That(VRayMaterialReader.TryParseScriptResult(null), Is.Null);
        Assert.That(VRayMaterialReader.TryParseScriptResult(""), Is.Null);
        Assert.That(VRayMaterialReader.TryParseScriptResult("128.0|64.0"), Is.Null, "wrong token count");
        Assert.That(VRayMaterialReader.TryParseScriptResult(BuildPayload() + "extra|"), Is.Null, "extra tokens");
        Assert.That(VRayMaterialReader.TryParseScriptResult(BuildPayload(reflectionGlossiness: "abc")), Is.Null, "non-numeric token");
    }

    #endregion

    #region Mapping Tests

    [Test]
    public void MatteDiffuseOnlyMaterialTest()
    {
        // Reflection authored OFF: no metallization, and the roughness must NOT follow the
        // glossiness spinner default (1.0 would read as a polished mirror).
        var snapshot = ApplyPayload(BuildPayload(diffuse: "128.0|64.0|32.0"));

        Assert.That(snapshot.BaseColor.R, Is.EqualTo(128d / 255d).Within(1e-9));
        Assert.That(snapshot.BaseColor.G, Is.EqualTo(64d / 255d).Within(1e-9));
        Assert.That(snapshot.BaseColor.B, Is.EqualTo(32d / 255d).Within(1e-9));
        Assert.That(snapshot.Roughness, Is.EqualTo(0.8d).Within(1e-9));
        Assert.That(snapshot.Metallic, Is.Zero);
        Assert.That(snapshot.Transmission, Is.Zero);
        Assert.That(snapshot.EmissionStrength, Is.Zero);
    }

    [Test]
    public void FresnelReflectionMapsGlossinessAndIorTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            reflection: "199.0|199.0|199.0",
            reflectionGlossiness: "0.8",
            fresnelOn: "1.0",
            reflectionIor: "1.8"));

        Assert.That(snapshot.Roughness, Is.EqualTo(0.2d).Within(1e-9));
        Assert.That(snapshot.Ior, Is.EqualTo(1.8d).Within(1e-9));
        Assert.That(snapshot.Metallic, Is.Zero, "fresnel reflection is a dielectric, not a mirror");
    }

    [Test]
    public void UseRoughnessModeKeepsSpinnerAsRoughnessTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            reflection: "199.0|199.0|199.0",
            reflectionGlossiness: "0.3",
            useRoughness: "1.0"));

        Assert.That(snapshot.Roughness, Is.EqualTo(0.3d).Within(1e-9));
    }

    [Test]
    public void LockedIorFollowsRefractionSpinnerTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            reflection: "199.0|199.0|199.0",
            fresnelOn: "1.0",
            reflectionIor: "2.5",
            lockIor: "1.0",
            refractionIor: "1.33"));

        Assert.That(snapshot.Ior, Is.EqualTo(1.33d).Within(1e-9));
    }

    [Test]
    public void MetalnessWorkflowTest()
    {
        // PBR metal: diffuse carries the metal colour, metalness carries the look.
        var snapshot = ApplyPayload(BuildPayload(
            diffuse: "255.0|180.0|60.0",
            reflection: "255.0|255.0|255.0",
            reflectionGlossiness: "0.75",
            metalness: "1.0",
            fresnelOn: "1.0"));

        Assert.That(snapshot.Metallic, Is.EqualTo(1d).Within(1e-9));
        Assert.That(snapshot.BaseColor.R, Is.EqualTo(1d).Within(1e-9));
        Assert.That(snapshot.BaseColor.G, Is.EqualTo(180d / 255d).Within(1e-9));
        Assert.That(snapshot.Roughness, Is.EqualTo(0.25d).Within(1e-9));
    }

    [Test]
    public void LegacyMirrorWithoutFresnelMetallizesAndPromotesTintTest()
    {
        // Fresnel OFF + strong reflection over a near-black diffuse is the legacy chrome
        // workflow: constant reflectivity carrying the look in the reflection colour.
        var snapshot = ApplyPayload(BuildPayload(
            diffuse: "5.0|5.0|5.0",
            reflection: "230.0|230.0|255.0",
            reflectionGlossiness: "0.98",
            fresnelOn: "0.0"));

        Assert.That(snapshot.Metallic, Is.EqualTo(1d).Within(1e-2));
        Assert.That(snapshot.Roughness, Is.LessThanOrEqualTo(0.15d));
        Assert.That(snapshot.BaseColor.B, Is.EqualTo(1d).Within(1e-9), "reflection tint promoted into the base");
        Assert.That(snapshot.BaseColor.R, Is.EqualTo(230d / 255d).Within(1e-9));
    }

    [Test]
    public void WeakReflectionWithoutFresnelStaysDielectricTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            diffuse: "0.0|0.0|0.0",
            reflection: "40.0|40.0|40.0",
            reflectionGlossiness: "0.65",
            fresnelOn: "0.0"));

        Assert.That(snapshot.Metallic, Is.Zero);
        Assert.That(snapshot.Roughness, Is.EqualTo(0.35d).Within(1e-9));
    }

    [Test]
    public void RefractiveGlassTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            diffuse: "0.0|0.0|0.0",
            reflection: "255.0|255.0|255.0",
            reflectionGlossiness: "1.0",
            fresnelOn: "1.0",
            refraction: "230.0|230.0|230.0",
            refractionIor: "1.52"));

        var transmission = 230d / 255d;
        Assert.That(snapshot.Transmission, Is.EqualTo(transmission).Within(1e-9));
        Assert.That(snapshot.Ior, Is.EqualTo(1.52d).Within(1e-9));
        Assert.That(snapshot.Metallic, Is.Zero, "a transmissive material must never metallize");
        Assert.That(snapshot.Opacity, Is.EqualTo(1d).Within(1e-9), "transmission refracts instead of alpha-blending");
        Assert.That(snapshot.BaseColor.R, Is.EqualTo(transmission).Within(1e-9), "refraction filter blends into the base");
        Assert.That(snapshot.Roughness, Is.Zero, "clear glass stays polished");
    }

    [Test]
    public void RefractionOnlyGlassStaysPolishedTest()
    {
        // Reflection authored OFF must not push the matte fallback roughness onto clear glass.
        var snapshot = ApplyPayload(BuildPayload(
            diffuse: "0.0|0.0|0.0",
            refraction: "255.0|255.0|255.0",
            refractionGlossiness: "1.0"));

        Assert.That(snapshot.Roughness, Is.Zero);
        Assert.That(snapshot.Transmission, Is.EqualTo(1d).Within(1e-9));
    }

    [Test]
    public void FrostedGlassCarriesRefractionGlossinessTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            diffuse: "0.0|0.0|0.0",
            reflection: "255.0|255.0|255.0",
            reflectionGlossiness: "1.0",
            refraction: "255.0|255.0|255.0",
            refractionGlossiness: "0.6"));

        Assert.That(snapshot.Roughness, Is.EqualTo(0.4d).Within(1e-9));
    }

    [Test]
    public void TransmissiveMaterialIgnoresMetalnessTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            reflection: "255.0|255.0|255.0",
            metalness: "1.0",
            refraction: "255.0|255.0|255.0"));

        Assert.That(snapshot.Metallic, Is.Zero);
        Assert.That(snapshot.Transmission, Is.EqualTo(1d).Within(1e-9));
    }

    [Test]
    public void FogColourTintsTransmissiveBaseTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            diffuse: "0.0|0.0|0.0",
            refraction: "255.0|255.0|255.0",
            fog: "255.0|128.0|128.0",
            fogMultiplier: "1.0"));

        Assert.That(snapshot.BaseColor.R, Is.EqualTo(1d).Within(1e-9));
        Assert.That(snapshot.BaseColor.G, Is.EqualTo(128d / 255d).Within(1e-9));
    }

    [Test]
    public void ZeroFogMultiplierLeavesBaseUntintedTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            diffuse: "0.0|0.0|0.0",
            refraction: "255.0|255.0|255.0",
            fog: "255.0|0.0|0.0",
            fogMultiplier: "0.0"));

        Assert.That(snapshot.BaseColor.G, Is.EqualTo(1d).Within(1e-9));
    }

    [Test]
    public void SelfIlluminationMapsToEmissionTest()
    {
        var snapshot = ApplyPayload(BuildPayload(
            selfIllumination: "255.0|200.0|100.0",
            selfIlluminationMultiplier: "3.0"));

        Assert.That(snapshot.EmissionColor.R, Is.EqualTo(1d).Within(1e-9));
        Assert.That(snapshot.EmissionColor.G, Is.EqualTo(200d / 255d).Within(1e-9));
        Assert.That(snapshot.EmissionColor.B, Is.EqualTo(100d / 255d).Within(1e-9));
        Assert.That(snapshot.EmissionStrength, Is.EqualTo(3d).Within(1e-9));
    }

    [Test]
    public void MissingOptionalValuesFallBackToSafeDefaultsTest()
    {
        // An older V-Ray build missing the modern spinners must still export the diffuse look.
        var snapshot = ApplyPayload(BuildPayload(
            diffuse: "128.0|128.0|128.0",
            reflection: "?|?|?",
            reflectionGlossiness: "?",
            useRoughness: "?",
            metalness: "?",
            fresnelOn: "?",
            reflectionIor: "?",
            lockIor: "?",
            reflectionWeight: "?",
            refraction: "?|?|?",
            refractionIor: "?",
            fog: "?|?|?",
            fogMultiplier: "?",
            selfIllumination: "?|?|?",
            selfIlluminationMultiplier: "?"));

        Assert.That(snapshot.BaseColor.R, Is.EqualTo(128d / 255d).Within(1e-9));
        Assert.That(snapshot.Roughness, Is.EqualTo(0.8d).Within(1e-9));
        Assert.That(snapshot.Metallic, Is.Zero);
        Assert.That(snapshot.Transmission, Is.Zero);
        Assert.That(snapshot.Ior, Is.EqualTo(1.45d).Within(1e-9), "contract default IOR untouched");
    }

    #endregion
}
