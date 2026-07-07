using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Synthesizes a default three-point lighting rig when a 3ds Max scene has no explicit lights.
/// Such scenes render from the viewport on Max's default lighting, which the neutral DCC export
/// cannot preserve — and the Blender generator builds an empty world, so the result would be
/// pitch black. The rig uses SUN lights: their irradiance is distance-independent, so the result
/// is identical at any scene scale — the earlier point-light rig needed distance²-calibrated
/// watts that hit the mapper's power cap on large scenes (Butterfly, radius ~2100 units), which
/// flattened the key/fill/back ratios into an even wash.
/// </summary>
internal static class MaxSceneDefaultLightSynthesizer
{
    #region Constants

    // Distance of the synthesized lights from the scene centre, in bounding-sphere radii. Suns
    // ignore distance; the offset keeps the nodes out of the geometry for inspection/debugging.
    private const double LIGHT_DISTANCE_IN_RADII = 2.5d;

    private const double MIN_LIGHT_DISTANCE = 5d;

    #endregion

    #region Fields

    // A compact three-point rig: key (bright, front-right-above), fill (softer, front-left),
    // back/rim (separates the subject from the empty background). Directions are in the scene's
    // Z-up space; the mapper turns multipliers into sun irradiance (×4 W/m² — the median-of-lights
    // reference floors at 1, so these emit 4 / 1.8 / 2 W/m² deterministically).
    private static readonly IReadOnlyList<SyntheticLight> SYNTHETIC_LIGHTS =
    [
        new SyntheticLight("key", 1d, 0.95d, 0.95d, 0.9d, (1d, -1d, 1d)),
        new SyntheticLight("fill", 0.45d, 0.9d, 0.92d, 1d, (-1d, -0.8d, 0.35d)),
        new SyntheticLight("back", 0.5d, 1d, 0.97d, 0.9d, (-0.2d, 1d, 0.8d))
    ];

    #endregion

    #region Functions

    public static void Apply(MaxSceneSummaryData summary)
    {
        if (summary.Lights.Count > 0)
            return;

        var bounds = MaxSceneBounds.Compute(summary);
        if (bounds == null)
            return;

        var distance = Math.Max(bounds.Value.Radius * LIGHT_DISTANCE_IN_RADII, MIN_LIGHT_DISTANCE);

        // Max's default lighting is a HEADLIGHT — it illuminates whatever the camera sees. Aim
        // the key sun along the camera's view direction so subjects seen from below/backlit
        // (Butterfly against the sky) are lit like the source render instead of silhouetted.
        var cameraBackward = ResolveCameraBackwardDirection(summary);

        foreach (var light in SYNTHETIC_LIGHTS)
        {
            var direction = light.Key == "key" && cameraBackward != null
                ? cameraBackward.Value
                : Normalize(light.Direction);
            var lightId = $"light:default-{light.Key}";
            var nodeId = $"node:default-{light.Key}-light";
            var name = $"DefaultLight{char.ToUpperInvariant(light.Key[0])}{light.Key[1..]}";

            summary.Lights.Add(new MaxSceneLightSnapshotData
            {
                Id = lightId,
                Name = name,
                Kind = DccLightKind.Sun,
                Color = new MaxSceneColorSnapshotData { R = light.ColorR, G = light.ColorG, B = light.ColorB, A = 1d },
                Intensity = light.Multiplier,
                Range = 0.01d,
                SpotAngleDegrees = 45d
            });

            // A sun emits along its node's generator-corrected forward; aim it from the offset
            // position back at the scene centre (same look-at convention as the camera path).
            var rotation = MaxCameraMath.BuildLookAtNodeRotation((-direction.X, -direction.Y, -direction.Z), (0d, 0d, 1d));

            summary.Nodes.Add(new MaxSceneNodeSnapshotData
            {
                Id = nodeId,
                Name = name,
                Kind = DccNodeKind.Light,
                LightId = lightId,
                LocalTransform = new MaxSceneTransformSnapshotData
                {
                    Translation = new MaxSceneVector3SnapshotData
                    {
                        X = bounds.Value.CenterX + direction.X * distance,
                        Y = bounds.Value.CenterY + direction.Y * distance,
                        Z = bounds.Value.CenterZ + direction.Z * distance
                    },
                    Rotation = new MaxSceneQuaternionSnapshotData { W = rotation.W, X = rotation.X, Y = rotation.Y, Z = rotation.Z },
                    Scale = new MaxSceneVector3SnapshotData { X = 1d, Y = 1d, Z = 1d }
                },
                Visible = true,
                Renderable = true
            });

            if (!summary.LightNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                summary.LightNames.Add(name);
        }

        summary.LightsCount = summary.Lights.Count;
        summary.NodesCount = summary.Nodes.Count;
        summary.UsesSyntheticDefaultLights = true;
    }

    #endregion

    #region Tools

    // Direction FROM the scene BACK toward the camera (the rig offsets lights along it, and the
    // key sun aims opposite — along the camera's view). Null when the scene has no camera.
    private static (double X, double Y, double Z)? ResolveCameraBackwardDirection(MaxSceneSummaryData summary)
    {
        var cameraNode = summary.Nodes.FirstOrDefault(me => me.Kind == DccNodeKind.Camera);
        if (cameraNode == null)
            return null;

        var rotation = cameraNode.LocalTransform.Rotation;
        var forward = MaxCameraMath.ComputeGeneratorForward((rotation.W, rotation.X, rotation.Y, rotation.Z));
        var length = Math.Sqrt(forward.X * forward.X + forward.Y * forward.Y + forward.Z * forward.Z);
        if (length <= double.Epsilon)
            return null;

        return (-forward.X / length, -forward.Y / length, -forward.Z / length);
    }

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        var length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length <= double.Epsilon ? (0d, 0d, 1d) : (v.X / length, v.Y / length, v.Z / length);
    }

    #endregion

    #region Models

    private readonly record struct SyntheticLight(string Key, double Multiplier, double ColorR, double ColorG, double ColorB, (double X, double Y, double Z) Direction);

    #endregion
}
