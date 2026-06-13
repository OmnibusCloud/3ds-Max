using System;
using System.Reflection;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

internal static class MaxSceneDccSceneMapper
{
    #region Constants

    private const double DEFAULT_CAMERA_NEAR_CLIP = 0.1d;

    private const double DEFAULT_CAMERA_FAR_CLIP = 1000d;

    // Photometric calibration for point/spot lights.
    //
    // The DCC -> Blender generator emits node coordinates verbatim and only sets
    // scene.unit_settings.scale_length, which is a measurement/display setting and does NOT
    // affect Cycles' inverse-square light falloff. So a light sits at its raw scene-unit
    // distance from the subject, and its power must grow with the square of that distance to
    // keep the irradiance at the subject constant regardless of the unit scale the scene was
    // modelled in. (3ds Max's raw light "Multiplier" is ~1.0 and carries no distance meaning,
    // so emitting it as raw Blender watts renders black on any scene modelled in centimetres.)
    //
    // Anchor: a point light of REFERENCE_LIGHT_POWER_WATTS watts at REFERENCE_LIGHT_DISTANCE
    // scene units from the subject yields a well-lit image. Calibrated against the hand-authored
    // verification scene (a 1200 W point light ~sqrt(68) units from the subject renders cleanly
    // at 16 samples + denoise), giving a target irradiance of ~1.4 W/m^2.
    private const double REFERENCE_LIGHT_POWER_WATTS = 1200d;

    private const double REFERENCE_LIGHT_DISTANCE_SQUARED = 68d;

    // A point/spot light effectively sitting on the subject still has to light the scene; floor
    // its characteristic distance so the power never collapses to zero.
    private const double MIN_LIGHT_CHARACTERISTIC_DISTANCE = 1d;

    // Guard against pathological coordinates producing an absurd (though finite) wattage.
    private const double MAX_LIGHT_CHARACTERISTIC_DISTANCE = 100000d;

    // Blender sun strength is an irradiance in W/m^2 and is distance-independent; map the raw
    // Max multiplier to a daylight-key level.
    private const double SUN_REFERENCE_IRRADIANCE = 4d;

    #endregion

    #region Functions

    public static DccSceneData Create(MaxSceneSummaryData summary)
    {
        var exporterVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var nonMeshTranslationScale = ResolveNonMeshTranslationScale(summary);
        var sceneBounds = MaxSceneBounds.Compute(summary);
        var lightPositionsById = ResolveLightPositions(summary, nonMeshTranslationScale);

        return new DccSceneData
        {
            SceneName = summary.SceneName,
            SourceApplication = new DccApplicationData
            {
                ApplicationFamily = "3dsMax",
                ApplicationVersion = string.IsNullOrWhiteSpace(summary.SourceApplicationVersion) ? summary.SourceApplicationLabel : summary.SourceApplicationVersion,
                ExporterVersion = exporterVersion
            },
            Units = new DccUnitSettingsData
            {
                LinearUnit = "centimeter",
                UnitsPerMeter = 100d
            },
            AxisSystem = new DccAxisSystemData
            {
                Handedness = "right",
                UpAxis = "Z",
                ForwardAxis = "Y"
            },
            RenderSettings = new DccRenderSettingsData
            {
                ResolutionX = summary.RenderWidth,
                ResolutionY = summary.RenderHeight,
                FrameStart = summary.FrameStart,
                FrameEnd = summary.FrameEnd,
                Fps = summary.FrameRate > 0 ? summary.FrameRate : 30,
                Samples = 64,
                TargetEngine = RenderEngine.Cycles
            },
            Nodes =
            [
                .. summary.Nodes.Select(me => new DccNodeData
                {
                    Id = me.Id,
                    Name = me.Name,
                    ParentId = me.ParentId,
                    Kind = me.Kind,
                    LocalTransform = new DccTransformData
                    {
                        Translation = new DccVector3Data
                        {
                            X = ResolveNodeTranslationX(me, nonMeshTranslationScale),
                            Y = ResolveNodeTranslationY(me, nonMeshTranslationScale),
                            Z = ResolveNodeTranslationZ(me, nonMeshTranslationScale)
                        },
                        Rotation = new DccQuaternionData
                        {
                            X = me.LocalTransform.Rotation.X,
                            Y = me.LocalTransform.Rotation.Y,
                            Z = me.LocalTransform.Rotation.Z,
                            W = me.LocalTransform.Rotation.W
                        },
                        Scale = new DccVector3Data
                        {
                            X = ResolveNodeScaleX(me),
                            Y = ResolveNodeScaleY(me),
                            Z = ResolveNodeScaleZ(me)
                        }
                    },
                    MeshId = me.MeshId,
                    CameraId = me.CameraId,
                    LightId = me.LightId,
                    MaterialBindingId = me.MaterialBindingId,
                    Visible = me.Visible,
                    Renderable = me.Renderable
                })
            ],
            Meshes =
            [
                .. summary.Meshes.Select(me => new DccMeshData
                {
                    Id = me.Id,
                    Name = me.Name,
                    Positions = [.. me.Positions.Select(position => new DccVector3Data { X = position.X, Y = position.Y, Z = position.Z })],
                    Normals = [.. me.Normals.Select(normal => new DccVector3Data { X = normal.X, Y = normal.Y, Z = normal.Z })],
                    Uv0 = [.. me.Uv0.Select(uv => new DccVector2Data { X = uv.X, Y = uv.Y })],
                    TriangleIndices = [.. me.TriangleIndices],
                    MaterialIndices = [.. me.MaterialIndices]
                })
            ],
            Cameras =
            [
                .. summary.Cameras.Select(me => new DccCameraData
                {
                    Id = me.Id,
                    Name = me.Name,
                    VerticalFovDegrees = me.VerticalFovDegrees,
                    NearClip = NormalizeCameraNearClip(me.NearClip, me.FarClip),
                    FarClip = NormalizeCameraFarClip(me.NearClip, me.FarClip),
                    IsPerspective = me.IsPerspective
                })
            ],
            Lights =
            [
                .. summary.Lights.Select(me =>
                {
                    var characteristicDistance = ResolveLightCharacteristicDistance(me, lightPositionsById, sceneBounds);
                    return new DccLightData
                    {
                        Id = me.Id,
                        Name = me.Name,
                        Kind = me.Kind,
                        Color = new DccColorData { R = me.Color.R, G = me.Color.G, B = me.Color.B, A = me.Color.A },
                        Intensity = ResolveLightIntensity(me, characteristicDistance),
                        Range = ResolveLightRange(me, characteristicDistance),
                        SpotAngleDegrees = me.SpotAngleDegrees
                    };
                })
            ],
            Materials =
            [
                .. summary.Materials.Select(me => new DccMaterialData
                {
                    Id = me.Id,
                    Name = me.Name,
                    Kind = DccMaterialKind.PrincipledSurface,
                    BaseColor = new DccColorData { R = me.BaseColor.R, G = me.BaseColor.G, B = me.BaseColor.B, A = me.BaseColor.A },
                    Opacity = me.Opacity,
                    Metallic = me.Metallic,
                    Roughness = me.Roughness,
                    NormalStrength = me.NormalStrength,
                    Transmission = me.Transmission,
                    Ior = me.Ior,
                    TextureSlots =
                    [
                        .. me.TextureSlots.Select(slot => new DccTextureSlotData
                        {
                            Slot = slot.Slot,
                            ImageAssetId = slot.ImageAssetId
                        })
                    ]
                })
            ],
            ImageAssets =
            [
                .. summary.ImageAssets.Select(me => new DccImageAssetData
                {
                    Id = me.Id,
                    Name = me.Name,
                    SourcePath = me.SourcePath,
                    RelativePath = me.RelativePath,
                    AssetKind = me.AssetKind
                })
            ]
        };
    }

    private static double NormalizeCameraFarClip(double nearClip, double farClip)
    {
        if (nearClip > 0d && farClip > nearClip)
            return farClip;

        var normalizedNearClip = NormalizeCameraNearClip(nearClip, farClip);
        return Math.Max(DEFAULT_CAMERA_FAR_CLIP, normalizedNearClip + 1d);
    }

    private static double NormalizeCameraNearClip(double nearClip, double farClip)
    {
        if (nearClip > 0d && nearClip < farClip)
            return nearClip;

        if (farClip > DEFAULT_CAMERA_NEAR_CLIP)
            return DEFAULT_CAMERA_NEAR_CLIP;

        return Math.Min(DEFAULT_CAMERA_NEAR_CLIP, Math.Max(farClip * 0.5d, 0.001d));
    }

    private static double ResolveNodeTranslationX(MaxSceneNodeSnapshotData node, double nonMeshTranslationScale)
    {
        return node.Kind == DccNodeKind.Mesh
            ? node.LocalTransform.Translation.X
            : node.LocalTransform.Translation.X * nonMeshTranslationScale;
    }

    private static double ResolveNodeTranslationY(MaxSceneNodeSnapshotData node, double nonMeshTranslationScale)
    {
        return node.Kind == DccNodeKind.Mesh
            ? node.LocalTransform.Translation.Y
            : node.LocalTransform.Translation.Y * nonMeshTranslationScale;
    }

    private static double ResolveNodeTranslationZ(MaxSceneNodeSnapshotData node, double nonMeshTranslationScale)
    {
        return node.Kind == DccNodeKind.Mesh
            ? node.LocalTransform.Translation.Z
            : node.LocalTransform.Translation.Z * nonMeshTranslationScale;
    }

    private static double ResolveNonMeshTranslationScale(MaxSceneSummaryData summary)
    {
        var meshNodeScales = summary.Nodes
            .Where(me => me.Kind == DccNodeKind.Mesh)
            .SelectMany(me => new[]
            {
                me.LocalTransform.Scale.X,
                me.LocalTransform.Scale.Y,
                me.LocalTransform.Scale.Z
            })
            .Where(me => me > 0d)
            .ToArray();

        if (meshNodeScales.Length == 0)
            return 1d;

        var representativeScale = meshNodeScales.Average();
        return representativeScale is > 0d and < 0.5d ? representativeScale : 1d;
    }

    private static Dictionary<string, DccVector3Data> ResolveLightPositions(MaxSceneSummaryData summary, double nonMeshTranslationScale)
    {
        var lightPositionsById = new Dictionary<string, DccVector3Data>(StringComparer.Ordinal);

        foreach (var node in summary.Nodes.Where(me => me.Kind == DccNodeKind.Light && !string.IsNullOrWhiteSpace(me.LightId)))
        {
            // Lights are emitted at their scaled (output) translation, so calibrate against the
            // same coordinate space the generator and the scene bounds live in.
            lightPositionsById[node.LightId!] = new DccVector3Data
            {
                X = node.LocalTransform.Translation.X * nonMeshTranslationScale,
                Y = node.LocalTransform.Translation.Y * nonMeshTranslationScale,
                Z = node.LocalTransform.Translation.Z * nonMeshTranslationScale
            };
        }

        return lightPositionsById;
    }

    private static double ResolveLightCharacteristicDistance(MaxSceneLightSnapshotData light, IReadOnlyDictionary<string, DccVector3Data> lightPositionsById, MaxSceneBounds? sceneBounds)
    {
        var centerX = sceneBounds?.CenterX ?? 0d;
        var centerY = sceneBounds?.CenterY ?? 0d;
        var centerZ = sceneBounds?.CenterZ ?? 0d;

        var distanceToSceneCenter = lightPositionsById.TryGetValue(light.Id, out var position)
            ? Distance(position.X, position.Y, position.Z, new DccVector3Data { X = centerX, Y = centerY, Z = centerZ })
            : 0d;

        // The light must at least cover the scene's own extent, so floor the distance by the
        // scene radius (handles lights placed inside or at the centre of the geometry).
        var characteristicDistance = Math.Max(distanceToSceneCenter, sceneBounds?.Radius ?? 0d);
        return Math.Clamp(characteristicDistance, MIN_LIGHT_CHARACTERISTIC_DISTANCE, MAX_LIGHT_CHARACTERISTIC_DISTANCE);
    }

    private static double ResolveLightIntensity(MaxSceneLightSnapshotData light, double characteristicDistance)
    {
        // Sun light: irradiance in W/m^2, distance-independent.
        if (light.Kind == DccLightKind.Sun)
            return light.Intensity * SUN_REFERENCE_IRRADIANCE;

        // Point/spot: scale the raw Max multiplier so irradiance at the subject matches the
        // calibration anchor regardless of how far the light sits in scene units.
        return light.Intensity * REFERENCE_LIGHT_POWER_WATTS * characteristicDistance * characteristicDistance / REFERENCE_LIGHT_DISTANCE_SQUARED;
    }

    private static double ResolveLightRange(MaxSceneLightSnapshotData light, double characteristicDistance)
    {
        if (light.Kind is not DccLightKind.Point and not DccLightKind.Spot)
            return light.Range;

        // A cutoff distance shorter than the light-to-subject distance would clip the subject
        // into darkness. Keep a meaningful cutoff only when it comfortably clears the subject;
        // otherwise drop below the generator's threshold so the light keeps an infinite range.
        if (light.Range <= characteristicDistance)
            return 0.01d;

        return light.Range;
    }

    private static double Distance(double x, double y, double z, DccVector3Data target)
    {
        var dx = x - target.X;
        var dy = y - target.Y;
        var dz = z - target.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double ResolveNodeScaleX(MaxSceneNodeSnapshotData node)
    {
        return node.Kind == DccNodeKind.Mesh ? 1d : node.LocalTransform.Scale.X;
    }

    private static double ResolveNodeScaleY(MaxSceneNodeSnapshotData node)
    {
        return node.Kind == DccNodeKind.Mesh ? 1d : node.LocalTransform.Scale.Y;
    }

    private static double ResolveNodeScaleZ(MaxSceneNodeSnapshotData node)
    {
        return node.Kind == DccNodeKind.Mesh ? 1d : node.LocalTransform.Scale.Z;
    }

    #endregion
}
