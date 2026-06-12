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

    private const double DEFAULT_IMPORTED_NON_SUN_LIGHT_INTENSITY_MULTIPLIER = 1000d;

    private const double DEFAULT_IMPORTED_POINT_OR_SPOT_RANGE = 25d;

    #endregion

    #region Functions

    public static DccSceneData Create(MaxSceneSummaryData summary)
    {
        var exporterVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var nonMeshTranslationScale = ResolveNonMeshTranslationScale(summary);
        var importedScaleNormalizationFactor = ResolveImportedScaleNormalizationFactor(summary);

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
                Fps = 24,
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
                .. summary.Lights.Select(me => new DccLightData
                {
                    Id = me.Id,
                    Name = me.Name,
                    Kind = me.Kind,
                    Color = new DccColorData { R = me.Color.R, G = me.Color.G, B = me.Color.B, A = me.Color.A },
                     Intensity = ResolveLightIntensity(me, importedScaleNormalizationFactor),
                     Range = ResolveLightRange(me, importedScaleNormalizationFactor),
                    SpotAngleDegrees = me.SpotAngleDegrees
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

    private static double ResolveImportedScaleNormalizationFactor(MaxSceneSummaryData summary)
    {
        var representativeScale = ResolveNonMeshTranslationScale(summary);
        return representativeScale > 0d ? 1d / representativeScale : 1d;
    }

    private static double ResolveLightIntensity(MaxSceneLightSnapshotData light, double importedScaleNormalizationFactor)
    {
        if (light.Kind == DccLightKind.Sun || importedScaleNormalizationFactor <= 1d)
            return light.Intensity;

        return light.Intensity * DEFAULT_IMPORTED_NON_SUN_LIGHT_INTENSITY_MULTIPLIER * importedScaleNormalizationFactor * importedScaleNormalizationFactor;
    }

    private static double ResolveLightRange(MaxSceneLightSnapshotData light, double importedScaleNormalizationFactor)
    {
        if (light.Kind is not DccLightKind.Point and not DccLightKind.Spot)
            return light.Range;

        if (importedScaleNormalizationFactor <= 1d)
            return light.Range;

        return Math.Max(light.Range * importedScaleNormalizationFactor, DEFAULT_IMPORTED_POINT_OR_SPOT_RANGE);
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
