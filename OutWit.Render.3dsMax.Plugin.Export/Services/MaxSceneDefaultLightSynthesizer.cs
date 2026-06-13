using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Synthesizes a default three-point lighting rig when a 3ds Max scene has no explicit lights.
/// Such scenes render from the viewport on Max's default lighting, which the neutral DCC export
/// cannot preserve — and the Blender generator builds an empty world, so the result would be
/// pitch black. The lights are placed around the scene bounding sphere with raw multipliers; the
/// mapper's photometric conversion turns those into scene-scale-correct Blender watts.
/// </summary>
internal static class MaxSceneDefaultLightSynthesizer
{
    #region Constants

    // Distance of the synthesized lights from the scene centre, in bounding-sphere radii.
    private const double LIGHT_DISTANCE_IN_RADII = 2.5d;

    private const double MIN_LIGHT_DISTANCE = 5d;

    #endregion

    #region Fields

    // A compact three-point rig: key (bright, front-right-above), fill (softer, front-left),
    // back/rim (separates the subject from the empty background). Directions are in the scene's
    // Z-up space; multipliers feed the mapper's distance-calibrated power.
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

        foreach (var light in SYNTHETIC_LIGHTS)
        {
            var direction = Normalize(light.Direction);
            var lightId = $"light:default-{light.Key}";
            var nodeId = $"node:default-{light.Key}-light";
            var name = $"DefaultLight{char.ToUpperInvariant(light.Key[0])}{light.Key[1..]}";

            summary.Lights.Add(new MaxSceneLightSnapshotData
            {
                Id = lightId,
                Name = name,
                Kind = DccLightKind.Point,
                Color = new MaxSceneColorSnapshotData { R = light.ColorR, G = light.ColorG, B = light.ColorB, A = 1d },
                Intensity = light.Multiplier,
                Range = 0.01d,
                SpotAngleDegrees = 45d
            });

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
                    Rotation = new MaxSceneQuaternionSnapshotData { W = 1d },
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
