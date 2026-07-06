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

    // Photometric intensity normalized (divided by the scene median) is clamped to this multiplier
    // range so a single outlier photometric light cannot dominate or vanish.
    private const double NORMALIZED_MIN_MULTIPLIER = 0.15d;

    private const double NORMALIZED_MAX_MULTIPLIER = 4d;

    // Hard backstops on emitted power so no calibration edge case blows the render to white. The
    // point-light cap sits well above a legitimately calibrated large-scene wattage (~a few million)
    // but far below the pathological billions seen before normalization.
    private const double MAX_POINT_LIGHT_WATTS = 40_000_000d;

    private const double MAX_SUN_IRRADIANCE = 12d;

    // DccLightData.Range model default — the contract validator requires sun lights to keep it.
    private const double SUN_CONTRACT_RANGE = 10d;

    // Default Blender view transform: AgX tone-maps a wide dynamic range so bright scenes roll off to
    // white instead of clipping hard. Applied when the scene carries no explicit view transform.
    private const string DEFAULT_VIEW_TRANSFORM = "AgX";

    #endregion

    #region Functions

    public static DccSceneData Create(MaxSceneSummaryData summary)
    {
        var exporterVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var nonMeshTranslationScale = ResolveNonMeshTranslationScale(summary);
        var sceneBounds = MaxSceneBounds.Compute(summary);
        var lightPositionsById = ResolveLightPositions(summary, nonMeshTranslationScale);
        var cameraDistancesById = ResolveCameraSceneDistances(summary, sceneBounds, nonMeshTranslationScale);
        var intensityReference = ResolveIntensityReference(summary);

        var scene = new DccSceneData
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
                TargetEngine = RenderEngine.Cycles,
                ViewTransform = DEFAULT_VIEW_TRANSFORM,
                MotionBlur = summary.MotionBlur,
                MotionBlurShutter = summary.MotionBlurShutter > 0d ? summary.MotionBlurShutter : 0.5d
            },
            World = ResolveWorld(summary),
            Nodes =
            [
                .. summary.Nodes.Select(me => new DccNodeData
                {
                    Id = me.Id,
                    Name = me.Name,
                    ParentId = me.ParentId,
                    Kind = me.Kind,
                    LocalTransform = MapNodeTransform(me.Kind, me.LocalTransform, nonMeshTranslationScale),
                    TransformKeyframes =
                    [
                        .. me.TransformKeyframes.Select(kf => new DccTransformKeyframeData
                        {
                            Frame = kf.Frame,
                            Transform = MapNodeTransform(me.Kind, kf.Transform, nonMeshTranslationScale),
                            InterpolationMode = DccKeyframeInterpolationMode.Linear
                        })
                    ],
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
                    Uv1 = [.. me.Uv1.Select(uv => new DccVector2Data { X = uv.X, Y = uv.Y })],
                    TriangleIndices = [.. me.TriangleIndices],
                    MaterialIndices = [.. me.MaterialIndices],
                    Colors = [.. me.Colors.Select(color => new DccColorData { R = color.R, G = color.G, B = color.B, A = color.A })],
                    DeformationFrames =
                    [
                        .. me.DeformationFrames.Select(frame => new DccMeshDeformationFrameData
                        {
                            Frame = frame.Frame,
                            Positions = [.. frame.Positions.Select(position => new DccVector3Data { X = position.X, Y = position.Y, Z = position.Z })]
                        })
                    ]
                })
            ],
            Cameras =
            [
                .. summary.Cameras.Select(me =>
                {
                    var clipPlanes = ResolveCameraClipPlanes(me, cameraDistancesById, sceneBounds);
                    return new DccCameraData
                    {
                        Id = me.Id,
                        Name = me.Name,
                        VerticalFovDegrees = me.VerticalFovDegrees,
                        VerticalFovKeyframes = MapScalarKeyframes(me.VerticalFovKeyframes, value => value),
                        NearClip = clipPlanes.NearClip,
                        NearClipKeyframes = MapScalarKeyframes(me.NearClipKeyframes, value => value),
                        FarClip = clipPlanes.FarClip,
                        FarClipKeyframes = MapScalarKeyframes(me.FarClipKeyframes, value => value),
                        IsPerspective = me.IsPerspective,
                        EnableDepthOfField = me.EnableDepthOfField,
                        FocusDistance = me.FocusDistance,
                        FStop = me.FStop
                    };
                })
            ],
            Lights =
            [
                .. summary.Lights.Select(me =>
                {
                    var characteristicDistance = ResolveLightCharacteristicDistance(me, lightPositionsById, sceneBounds);
                    var intensityFactor = ResolveLightIntensityFactor(me.Kind, characteristicDistance);
                    // Normalize a photometric light's physical intensity to a ~1 multiplier scale (the
                    // calibration assumes ~1) so it no longer blows the render; standard lights keep
                    // their multiplier. Then cap the final wattage as a hard backstop against any
                    // remaining extreme.
                    var normalizedIntensity = NormalizeLightIntensity(me, intensityReference);
                    return new DccLightData
                    {
                        Id = me.Id,
                        Name = me.Name,
                        Kind = me.Kind,
                        Color = new DccColorData { R = me.Color.R, G = me.Color.G, B = me.Color.B, A = me.Color.A },
                        ColorKeyframes = MapColorKeyframes(me.ColorKeyframes),
                        Intensity = ClampLightPower(me.Kind, normalizedIntensity * intensityFactor),
                        IntensityKeyframes = MapScalarKeyframes(me.IntensityKeyframes, value => ClampLightPower(me.Kind, NormalizeRawIntensity(me, value, intensityReference) * intensityFactor)),
                        Range = ResolveLightRangeValue(me.Kind, me.Range, characteristicDistance),
                        RangeKeyframes = MapScalarKeyframes(me.RangeKeyframes, value => ResolveLightRangeValue(me.Kind, value, characteristicDistance)),
                        SpotAngleDegrees = me.SpotAngleDegrees,
                        SpotAngleKeyframes = MapScalarKeyframes(me.SpotAngleKeyframes, value => value),
                        CastShadows = me.CastShadows,
                        AreaWidth = me.AreaWidth,
                        AreaHeight = me.AreaHeight
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
                    // The generator MULTIPLIES a base-color texture by this colour, but a 3ds Max
                    // diffuse map REPLACES the diffuse colour — so a textured material exports a
                    // white base (multiply-by-white == replace) instead of tinting/darkening the
                    // texture with a UI swatch (unfinished wood ships a 0.49 grey swatch).
                    BaseColor = me.TextureSlots.Any(slot => slot.Slot == DccTextureSlotKind.BaseColor)
                        ? new DccColorData { R = 1d, G = 1d, B = 1d, A = 1d }
                        : new DccColorData { R = me.BaseColor.R, G = me.BaseColor.G, B = me.BaseColor.B, A = me.BaseColor.A },
                    Opacity = me.Opacity,
                    Metallic = me.Metallic,
                    Roughness = me.Roughness,
                    NormalStrength = me.NormalStrength,
                    Transmission = me.Transmission,
                    Ior = me.Ior,
                    DisplacementScale = me.DisplacementScale,
                    BackfaceCull = me.BackfaceCull,
                    EmissionColor = new DccColorData { R = me.EmissionColor.R, G = me.EmissionColor.G, B = me.EmissionColor.B, A = me.EmissionColor.A },
                    EmissionStrength = me.EmissionStrength,
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

        // Aim the render camera at the scene so the subject is actually in frame. Max camera
        // orientation does not survive the round trip reliably, so we recompute framing from the
        // geometry bounds rather than trust the captured quaternion.
        MaxSceneCameraFramer.Apply(scene, summary.ActiveRenderCameraName);

        return scene;
    }

    private static (double NearClip, double FarClip) ResolveCameraClipPlanes(
        MaxSceneCameraSnapshotData camera,
        IReadOnlyDictionary<string, double> cameraDistancesById,
        MaxSceneBounds? sceneBounds)
    {
        // Authored clip planes (Max "Clip Manually" on) pass through unchanged. The collector sends
        // a 0/0 sentinel when the flag is off, because Max renders UNCLIPPED in that case — a fixed
        // far=1000 clipped Butterfly's tree (~2100 units from the camera) to an empty sky. Derive
        // planes that cover the whole scene from the camera's distance to the geometry, mirroring
        // the synthesized-viewport-camera formula.
        if (camera.FarClip > 0d)
            return (NormalizeCameraNearClip(camera.NearClip, camera.FarClip), NormalizeCameraFarClip(camera.NearClip, camera.FarClip));

        if (sceneBounds == null || !cameraDistancesById.TryGetValue(camera.Id, out var distance))
            return (DEFAULT_CAMERA_NEAR_CLIP, DEFAULT_CAMERA_FAR_CLIP);

        return (
            Math.Max(DEFAULT_CAMERA_NEAR_CLIP, Math.Min(distance * 0.01d, 10d)),
            Math.Max(DEFAULT_CAMERA_FAR_CLIP, distance + sceneBounds.Value.Radius * 4d + 10d));
    }

    private static Dictionary<string, double> ResolveCameraSceneDistances(MaxSceneSummaryData summary, MaxSceneBounds? sceneBounds, double nonMeshTranslationScale)
    {
        var cameraDistancesById = new Dictionary<string, double>(StringComparer.Ordinal);
        if (sceneBounds == null)
            return cameraDistancesById;

        foreach (var node in summary.Nodes.Where(me => me.Kind == DccNodeKind.Camera && !string.IsNullOrWhiteSpace(me.CameraId)))
        {
            // An animated camera can travel; take the farthest pose so the derived far plane covers
            // the whole flight, not just the parked transform.
            var distance = DistanceToSceneCenter(node.LocalTransform, sceneBounds.Value, nonMeshTranslationScale);
            foreach (var keyframe in node.TransformKeyframes)
                distance = Math.Max(distance, DistanceToSceneCenter(keyframe.Transform, sceneBounds.Value, nonMeshTranslationScale));

            cameraDistancesById[node.CameraId!] = distance;
        }

        return cameraDistancesById;
    }

    private static double DistanceToSceneCenter(MaxSceneTransformSnapshotData transform, MaxSceneBounds sceneBounds, double nonMeshTranslationScale)
    {
        var dx = sceneBounds.CenterX - transform.Translation.X * nonMeshTranslationScale;
        var dy = sceneBounds.CenterY - transform.Translation.Y * nonMeshTranslationScale;
        var dz = sceneBounds.CenterZ - transform.Translation.Z * nonMeshTranslationScale;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
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

    private static DccTransformData MapNodeTransform(DccNodeKind kind, MaxSceneTransformSnapshotData transform, double nonMeshTranslationScale)
    {
        // Every node keeps its full captured TRS. Mesh vertices are OBJECT-space (the collector bakes
        // only the objTM·nodeTM⁻¹ pivot/WSM delta), so dropping the node scale breaks any scaled mesh
        // (a 0.017-scaled prop inflates 60×). Non-mesh nodes (camera/light) additionally get the
        // legacy import scale applied to their translation. Shared by the static transform and every
        // animation keyframe so they stay consistent.
        var isMesh = kind == DccNodeKind.Mesh;
        return new DccTransformData
        {
            Translation = new DccVector3Data
            {
                X = isMesh ? transform.Translation.X : transform.Translation.X * nonMeshTranslationScale,
                Y = isMesh ? transform.Translation.Y : transform.Translation.Y * nonMeshTranslationScale,
                Z = isMesh ? transform.Translation.Z : transform.Translation.Z * nonMeshTranslationScale
            },
            Rotation = new DccQuaternionData
            {
                X = transform.Rotation.X,
                Y = transform.Rotation.Y,
                Z = transform.Rotation.Z,
                W = transform.Rotation.W
            },
            Scale = new DccVector3Data
            {
                X = transform.Scale.X,
                Y = transform.Scale.Y,
                Z = transform.Scale.Z
            }
        };
    }

    private static DccWorldData? ResolveWorld(MaxSceneSummaryData summary)
    {
        // An environment HDRI image takes priority: the generator builds an equirectangular world from
        // it and ignores the constant colour. The id must resolve to an ImageAsset the generator can
        // load (a 1.4.0 contract guard), so only honour it when the asset is actually present.
        var hasEnvironmentImage = !string.IsNullOrWhiteSpace(summary.EnvironmentImageId)
                                  && summary.ImageAssets.Any(me => me.Id == summary.EnvironmentImageId);

        if (hasEnvironmentImage)
        {
            return new DccWorldData
            {
                BackgroundColor = summary.EnvironmentColor is null
                    ? new DccColorData { R = 0d, G = 0d, B = 0d, A = 1d }
                    : new DccColorData { R = summary.EnvironmentColor.R, G = summary.EnvironmentColor.G, B = summary.EnvironmentColor.B, A = summary.EnvironmentColor.A },
                Strength = ResolveEnvironmentStrength(summary),
                EnvironmentImageId = summary.EnvironmentImageId,
                EnvironmentRotationDegrees = summary.EnvironmentRotationDegrees
            };
        }

        // A null environment colour (the default black Max background) maps to "no world" so default
        // scenes render unchanged; a set background becomes the neutral world the generator emits.
        if (summary.EnvironmentColor is null)
            return null;

        return new DccWorldData
        {
            BackgroundColor = new DccColorData
            {
                R = summary.EnvironmentColor.R,
                G = summary.EnvironmentColor.G,
                B = summary.EnvironmentColor.B,
                A = summary.EnvironmentColor.A
            },
            Strength = 1d
        };
    }

    // Auto-expose the environment HDRI: authored HDRs span wildly different absolute levels, so
    // normalize the world strength to a target ambient irradiance (E ≈ π·meanLuminance·strength).
    // Falls back to the authored Strength=1 when the file cannot be found or decoded (.exr etc.).
    private const double TARGET_ENVIRONMENT_IRRADIANCE = 1.6d;

    private const double MIN_ENVIRONMENT_STRENGTH = 0.02d;

    private static double ResolveEnvironmentStrength(MaxSceneSummaryData summary)
    {
        var asset = summary.ImageAssets.FirstOrDefault(me => me.Id == summary.EnvironmentImageId);
        if (asset is null)
            return 1d;

        var path = ResolveImageAssetFile(summary, asset.SourcePath);
        if (path is null || !path.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase))
            return 1d;

        if (!MaxHdrLuminanceReader.TryComputeMeanLuminance(path, out var meanLuminance))
            return 1d;

        return Math.Clamp(TARGET_ENVIRONMENT_IRRADIANCE / (Math.PI * meanLuminance), MIN_ENVIRONMENT_STRENGTH, 1d);
    }

    // The collector stores the texture path as 3ds Max reports it, which is often just a file name.
    // Resolve against the scene file's directory and a few ancestors — the same neighbourhood the
    // attachment uploader searches.
    private static string? ResolveImageAssetFile(MaxSceneSummaryData summary, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        if (System.IO.File.Exists(sourcePath))
            return sourcePath;

        var fileName = System.IO.Path.GetFileName(sourcePath);
        var directory = System.IO.Path.GetDirectoryName(summary.SceneFilePath);

        for (var depth = 0; depth < 4 && !string.IsNullOrEmpty(directory); depth++)
        {
            var candidate = System.IO.Path.Combine(directory, fileName);
            if (System.IO.File.Exists(candidate))
                return candidate;

            directory = System.IO.Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static double ResolveNonMeshTranslationScale(MaxSceneSummaryData summary)
    {
        // Historical compensation for the era when mesh node scale was forced to 1 (cameras had to
        // shrink towards the unscaled geometry). Mesh transforms now keep their true scale, so every
        // node already lives in the same Max world coordinates — any factor here would MISplace
        // cameras and lights.
        return 1d;
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

    // Auto-exposure reference: the median raw intensity across the scene's lights, floored at 1. The
    // power calibration assumes a ~1 multiplier, but Max lights arrive on wildly different scales —
    // photometric candela (hundreds-thousands), physical-sky/Arnold values (thousands+), or a plain
    // multiplier (~1). Dividing every light by this reference centres the typical light on the
    // calibrated exposure (relative brightness between lights preserved) so no scene blows out. The
    // floor at 1 means a genuinely dim scene (median < 1) is left alone rather than brightened.
    private static double ResolveIntensityReference(MaxSceneSummaryData summary)
    {
        var intensities = summary.Lights
            .Where(me => me.Intensity > 0d)
            .Select(me => me.Intensity)
            .OrderBy(me => me)
            .ToArray();

        if (intensities.Length == 0)
            return 1d;

        var mid = intensities.Length / 2;
        var median = intensities.Length % 2 == 1 ? intensities[mid] : (intensities[mid - 1] + intensities[mid]) / 2d;
        return Math.Max(median, 1d);
    }

    private static double NormalizeLightIntensity(MaxSceneLightSnapshotData light, double intensityReference)
    {
        return NormalizeRawIntensity(light, light.Intensity, intensityReference);
    }

    // Normalize a light's raw intensity against the scene reference and clamp so a single outlier
    // light cannot dominate the frame or vanish.
    private static double NormalizeRawIntensity(MaxSceneLightSnapshotData light, double rawValue, double intensityReference)
    {
        var normalized = rawValue / intensityReference;
        return Math.Clamp(normalized, NORMALIZED_MIN_MULTIPLIER, NORMALIZED_MAX_MULTIPLIER);
    }

    // Hard backstop on emitted light power so no calibration edge case can blow the render to white.
    // Sun is an irradiance (W/m^2); point/spot are watts.
    private static double ClampLightPower(DccLightKind kind, double power)
    {
        if (!double.IsFinite(power) || power < 0d)
            return 0d;

        return kind == DccLightKind.Sun
            ? Math.Min(power, MAX_SUN_IRRADIANCE)
            : Math.Min(power, MAX_POINT_LIGHT_WATTS);
    }

    private static double ResolveLightIntensityFactor(DccLightKind kind, double characteristicDistance)
    {
        // Sun light: irradiance in W/m^2, distance-independent.
        if (kind == DccLightKind.Sun)
            return SUN_REFERENCE_IRRADIANCE;

        // Point/spot: scale the raw Max multiplier so irradiance at the subject matches the
        // calibration anchor regardless of how far the light sits in scene units. The factor is
        // linear in the raw multiplier, so it applies identically to the static value and every
        // intensity keyframe.
        return REFERENCE_LIGHT_POWER_WATTS * characteristicDistance * characteristicDistance / REFERENCE_LIGHT_DISTANCE_SQUARED;
    }

    private static double ResolveLightRangeValue(DccLightKind kind, double range, double characteristicDistance)
    {
        // Range is meaningless for a sun (its irradiance is distance-independent) and the Dcc
        // contract requires it to stay at the model default.
        if (kind == DccLightKind.Sun)
            return SUN_CONTRACT_RANGE;

        if (kind is not DccLightKind.Point and not DccLightKind.Spot)
            return range;

        // A cutoff distance shorter than the light-to-subject distance would clip the subject
        // into darkness. Keep a meaningful cutoff only when it comfortably clears the subject;
        // otherwise drop below the generator's threshold so the light keeps an infinite range.
        if (range <= characteristicDistance)
            return 0.01d;

        return range;
    }

    private static List<DccScalarKeyframeData> MapScalarKeyframes(IReadOnlyList<MaxSceneScalarKeyframeSnapshotData> keyframes, Func<double, double> valueSelector)
    {
        // Per-frame samples are dense (the collector samples every integer frame), so linear
        // interpolation between them is exact and avoids spurious Bezier overshoot.
        return [.. keyframes.Select(kf => new DccScalarKeyframeData
        {
            Frame = kf.Frame,
            Value = valueSelector(kf.Value),
            InterpolationMode = DccKeyframeInterpolationMode.Linear
        })];
    }

    private static List<DccColorKeyframeData> MapColorKeyframes(IReadOnlyList<MaxSceneColorKeyframeSnapshotData> keyframes)
    {
        return [.. keyframes.Select(kf => new DccColorKeyframeData
        {
            Frame = kf.Frame,
            Color = new DccColorData { R = kf.Color.R, G = kf.Color.G, B = kf.Color.B, A = kf.Color.A },
            InterpolationMode = DccKeyframeInterpolationMode.Linear
        })];
    }

    private static double Distance(double x, double y, double z, DccVector3Data target)
    {
        var dx = x - target.X;
        var dy = y - target.Y;
        var dz = z - target.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    #endregion
}
