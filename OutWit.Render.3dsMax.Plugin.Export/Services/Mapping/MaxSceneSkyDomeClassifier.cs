using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;

/// <summary>
/// Detects sky-dome geometry — a textured shell that encloses both the scene and the camera — and
/// makes its material self-illuminating. 3ds Max scenes light such domes with default no-decay
/// lights, so the sky bitmap reads at full brightness; Cycles point lights are inverse-square and
/// leave the dome interior almost black (MotionBlur-Dragon's "Sky" sphere).
/// </summary>
internal static class MaxSceneSkyDomeClassifier
{
    #region Constants

    // A dome must clearly dwarf the largest mesh it encloses (a room box barely bigger than its
    // furniture must NOT classify).
    private const double MIN_RADIUS_RATIO_TO_LARGEST_CONTENT = 1.5d;

    // Containment slack: enclosed meshes may poke marginally through the shell.
    private const double CONTAINMENT_TOLERANCE = 1.05d;

    // A dome is a volume, not a wall: its smallest bbox extent must be a meaningful fraction of
    // the largest. Flat backdrop panels and enclosure walls (hardwood's lightbox) stay untouched.
    private const double MIN_SHAPE_EXTENT_RATIO = 0.25d;

    private const double DOME_EMISSION_STRENGTH = 1d;

    #endregion

    #region Functions

    public static void Apply(DccSceneData scene)
    {
        var meshesById = scene.Meshes.ToDictionary(me => me.Id, StringComparer.Ordinal);
        var candidates = new List<(DccNodeData Node, double Radius, (double X, double Y, double Z) Center, double ExtentRatio)>();

        foreach (var node in scene.Nodes.Where(me => me.Kind == DccNodeKind.Mesh && me.Renderable && !string.IsNullOrWhiteSpace(me.MeshId)))
        {
            if (!meshesById.TryGetValue(node.MeshId!, out var mesh) || mesh.Positions.Count == 0)
                continue;

            var scale = MaxAbsoluteScale(node);
            var (radius, extentRatio) = MeasureLocalShape(mesh);
            var translation = node.LocalTransform.Translation;
            candidates.Add((node, radius * scale, (translation.X, translation.Y, translation.Z), extentRatio));
        }

        if (candidates.Count < 2)
            return;

        var cameraNode = scene.Nodes.FirstOrDefault(me => me.Kind == DccNodeKind.Camera);

        foreach (var candidate in candidates)
        {
            if (candidate.ExtentRatio < MIN_SHAPE_EXTENT_RATIO)
                continue;

            // The dome's bounding sphere must contain every other mesh's bounding sphere and
            // clearly dominate the largest of them — a direct enclosure test, robust even when
            // the scene is just "subject + dome" (a median/outlier split is not).
            var others = candidates.Where(me => !ReferenceEquals(me.Node, candidate.Node)).ToList();
            var containsAll = others.All(me => Distance(me.Center, candidate.Center) + me.Radius <= candidate.Radius * CONTAINMENT_TOLERANCE);
            if (!containsAll)
                continue;

            var largestContentRadius = others.Max(me => me.Radius);
            if (candidate.Radius < Math.Max(largestContentRadius, 1d) * MIN_RADIUS_RATIO_TO_LARGEST_CONTENT)
                continue;

            if (cameraNode != null)
            {
                var cameraTranslation = cameraNode.LocalTransform.Translation;
                if (Distance((cameraTranslation.X, cameraTranslation.Y, cameraTranslation.Z), candidate.Center) > candidate.Radius)
                    continue;
            }

            MakeDomeMaterialEmissive(scene, candidate.Node);
        }
    }

    #endregion

    #region Tools

    private static void MakeDomeMaterialEmissive(DccSceneData scene, DccNodeData node)
    {
        // Only the simple single-binding case: a dome with per-face multi-materials is ambiguous
        // and mutating a shared material would light unrelated objects.
        if (string.IsNullOrWhiteSpace(node.MaterialBindingId))
            return;

        var material = scene.Materials.FirstOrDefault(me => me.Id == node.MaterialBindingId);
        if (material == null || material.EmissionStrength > 0d)
            return;

        if (!material.TextureSlots.Any(me => me.Slot == DccTextureSlotKind.BaseColor))
            return;

        // The generator drives Emission Color from the base-color texture when a strength is set,
        // so the dome emits its own sky bitmap. Backdrop ray visibility keeps the glowing shell
        // from acting as a giant area light around the scene. A black base kills the DIFFUSE
        // response (the generator multiplies the texture by it): scene lights hitting the shell
        // were adding a lit-diffuse wash on top of the emission and bleaching the sky.
        material.BaseColor = new DccColorData { R = 0d, G = 0d, B = 0d, A = 1d };
        material.EmissionColor = new DccColorData { R = 1d, G = 1d, B = 1d, A = 1d };
        material.EmissionStrength = DOME_EMISSION_STRENGTH;
        material.EmissionCameraOnly = true;
        node.IsBackdrop = true;
    }

    private static (double Radius, double ExtentRatio) MeasureLocalShape(DccMeshData mesh)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        var radiusSquared = 0d;

        foreach (var position in mesh.Positions)
        {
            minX = Math.Min(minX, position.X); maxX = Math.Max(maxX, position.X);
            minY = Math.Min(minY, position.Y); maxY = Math.Max(maxY, position.Y);
            minZ = Math.Min(minZ, position.Z); maxZ = Math.Max(maxZ, position.Z);
            radiusSquared = Math.Max(radiusSquared, position.X * position.X + position.Y * position.Y + position.Z * position.Z);
        }

        var extents = new[] { maxX - minX, maxY - minY, maxZ - minZ };
        var largestExtent = extents.Max();
        var smallestExtent = extents.Min();
        var extentRatio = largestExtent <= double.Epsilon ? 0d : smallestExtent / largestExtent;

        return (Math.Sqrt(radiusSquared), extentRatio);
    }

    private static double MaxAbsoluteScale(DccNodeData node)
    {
        var scale = node.LocalTransform.Scale;
        return Math.Max(Math.Max(Math.Abs(scale.X), Math.Abs(scale.Y)), Math.Abs(scale.Z));
    }

    private static double Distance((double X, double Y, double Z) left, (double X, double Y, double Z) right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    #endregion
}
