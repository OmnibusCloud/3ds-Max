using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

/// <summary>
/// Verifies the UV-coverage estimate behind the scanned-material bake gate: a real unwrap
/// measures a meaningful share of UV space, a collapsed sliver measures near zero.
/// </summary>
[TestFixture]
public sealed class MaxSceneMeshUvCoverageTests
{
    #region Tools

    private static MaxSceneMeshSnapshotData BuildMesh(params (double X, double Y)[] corners)
    {
        var mesh = new MaxSceneMeshSnapshotData();
        foreach (var (x, y) in corners)
            mesh.Uv0.Add(new MaxSceneVector2SnapshotData { X = x, Y = y });
        return mesh;
    }

    #endregion

    [Test]
    public void FullUnitSquareUnwrapMeasuresOneTest()
    {
        var mesh = BuildMesh((0, 0), (1, 0), (1, 1), (0, 0), (1, 1), (0, 1));

        Assert.That(MaxSceneMeshUvCoverage.ComputeUv0Area(mesh), Is.EqualTo(1d).Within(1e-9));
    }

    [Test]
    public void CollapsedSliverMeasuresNearZeroTest()
    {
        // Every face crammed into a hairline strip — the Automotive car body failure mode.
        var mesh = BuildMesh((0.5, 0.1), (0.501, 0.1), (0.5005, 0.9), (0.5, 0.1), (0.501, 0.9), (0.5005, 0.5));

        Assert.That(MaxSceneMeshUvCoverage.ComputeUv0Area(mesh), Is.LessThan(0.01d));
    }

    [Test]
    public void MissingUvLayerMeasuresZeroTest()
    {
        Assert.That(MaxSceneMeshUvCoverage.ComputeUv0Area(new MaxSceneMeshSnapshotData()), Is.EqualTo(0d));
    }

    [Test]
    public void TiledUnwrapMeasuresAboveOneTest()
    {
        // 0..4 tiling: repeating fabric UVs must PASS the gate, not fail it.
        var mesh = BuildMesh((0, 0), (4, 0), (4, 4), (0, 0), (4, 4), (0, 4));

        Assert.That(MaxSceneMeshUvCoverage.ComputeUv0Area(mesh), Is.GreaterThan(1d));
    }
}
