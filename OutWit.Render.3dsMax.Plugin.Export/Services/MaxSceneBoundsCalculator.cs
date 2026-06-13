using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// The geometric bounds of a scene's renderable meshes, used to calibrate light power, frame the
/// fallback camera, and place synthesized default lights. Computed in summary (pre-mapper) space
/// from mesh node translations plus each mesh's local vertex radius.
/// </summary>
internal readonly record struct MaxSceneBounds(double CenterX, double CenterY, double CenterZ, double Radius)
{
    #region Functions

    /// <summary>
    /// Computes the mesh bounding sphere, or null when the scene has no renderable meshes.
    /// </summary>
    public static MaxSceneBounds? Compute(MaxSceneSummaryData summary)
    {
        var meshesById = summary.Meshes.ToDictionary(me => me.Id, StringComparer.Ordinal);
        var meshNodes = summary.Nodes
            .Where(me => me.Kind == DccNodeKind.Mesh && !string.IsNullOrWhiteSpace(me.MeshId) && meshesById.ContainsKey(me.MeshId!))
            .ToArray();

        if (meshNodes.Length == 0)
            return null;

        var centerX = meshNodes.Average(me => me.LocalTransform.Translation.X);
        var centerY = meshNodes.Average(me => me.LocalTransform.Translation.Y);
        var centerZ = meshNodes.Average(me => me.LocalTransform.Translation.Z);

        var radius = 0d;
        foreach (var node in meshNodes)
        {
            var dx = node.LocalTransform.Translation.X - centerX;
            var dy = node.LocalTransform.Translation.Y - centerY;
            var dz = node.LocalTransform.Translation.Z - centerZ;
            var nodeOffset = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            radius = Math.Max(radius, nodeOffset + ComputeMeshLocalRadius(meshesById[node.MeshId!]));
        }

        return new MaxSceneBounds(centerX, centerY, centerZ, radius);
    }

    private static double ComputeMeshLocalRadius(MaxSceneMeshSnapshotData mesh)
    {
        var radius = 0d;

        foreach (var position in mesh.Positions)
            radius = Math.Max(radius, Math.Sqrt(position.X * position.X + position.Y * position.Y + position.Z * position.Z));

        return radius;
    }

    #endregion
}
