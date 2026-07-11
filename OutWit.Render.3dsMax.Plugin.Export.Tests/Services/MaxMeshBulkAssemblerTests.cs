using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

/// <summary>
/// Verifies the bulk mesh assembly replicates the legacy per-corner wrapper walk: positions,
/// smoothing-group render-normal resolution, UV/colour channel mapping, material ids from the
/// face flags and the object→node correction arithmetic.
/// </summary>
[TestFixture]
public sealed class MaxMeshBulkAssemblerTests
{
    #region Tools

    private const int RVERT_STRIDE = 48;
    private const int RVERT_RN_OFFSET = 16;

    private static byte[] BuildFaces(params (uint V0, uint V1, uint V2, uint SmoothingGroup, ushort MaterialId)[] faces)
    {
        var bytes = new byte[faces.Length * MaxMeshBulkData.FACE_EXPECTED_STRIDE];
        for (var i = 0; i < faces.Length; i++)
        {
            var record = i * MaxMeshBulkData.FACE_EXPECTED_STRIDE;
            BitConverter.GetBytes(faces[i].V0).CopyTo(bytes, record);
            BitConverter.GetBytes(faces[i].V1).CopyTo(bytes, record + 4);
            BitConverter.GetBytes(faces[i].V2).CopyTo(bytes, record + 8);
            BitConverter.GetBytes(faces[i].SmoothingGroup).CopyTo(bytes, record + 12);
            BitConverter.GetBytes((uint)faces[i].MaterialId << 16).CopyTo(bytes, record + 16);
        }

        return bytes;
    }

    private static byte[] BuildRVerts(params (uint NormalCount, float X, float Y, float Z)[] verts)
    {
        var bytes = new byte[verts.Length * RVERT_STRIDE];
        for (var i = 0; i < verts.Length; i++)
        {
            var record = i * RVERT_STRIDE;
            BitConverter.GetBytes(verts[i].NormalCount).CopyTo(bytes, record);
            BitConverter.GetBytes(verts[i].X).CopyTo(bytes, record + RVERT_RN_OFFSET);
            BitConverter.GetBytes(verts[i].Y).CopyTo(bytes, record + RVERT_RN_OFFSET + 4);
            BitConverter.GetBytes(verts[i].Z).CopyTo(bytes, record + RVERT_RN_OFFSET + 8);
        }

        return bytes;
    }

    private static byte[] BuildErnArray(params (float X, float Y, float Z, uint SmoothingMask)[] normals)
    {
        var bytes = new byte[normals.Length * MaxMeshBulkData.RNORMAL_STRIDE];
        for (var i = 0; i < normals.Length; i++)
        {
            var record = i * MaxMeshBulkData.RNORMAL_STRIDE;
            BitConverter.GetBytes(normals[i].X).CopyTo(bytes, record);
            BitConverter.GetBytes(normals[i].Y).CopyTo(bytes, record + 4);
            BitConverter.GetBytes(normals[i].Z).CopyTo(bytes, record + 8);
            BitConverter.GetBytes(normals[i].SmoothingMask).CopyTo(bytes, record + MaxMeshBulkData.RNORMAL_SMOOTHING_GROUP_OFFSET);
        }

        return bytes;
    }

    private static MaxMeshBulkData BuildSingleTriangle(uint smoothingGroup = 0, ushort materialId = 0)
    {
        return new MaxMeshBulkData
        {
            FaceCount = 1,
            VertexCount = 3,
            Faces = BuildFaces((0, 1, 2, smoothingGroup, materialId)),
            Vertices = [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f],
            FaceNormals = [0f, 0f, 1f],
            RVerts = BuildRVerts((1, 0f, 0f, 1f), (1, 0f, 0f, 1f), (1, 0f, 0f, 1f)),
            RVertStride = RVERT_STRIDE,
            RVertRnOffset = RVERT_RN_OFFSET
        };
    }

    #endregion

    [Test]
    public void AssemblesPositionsIndicesAndMaterialIdsTest()
    {
        var data = BuildSingleTriangle(materialId: 7);
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.AppendMaterialIndices(data, mesh.MaterialIndices);
        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Positions, Has.Count.EqualTo(3));
        Assert.That(mesh.Positions[1].X, Is.EqualTo(1d));
        Assert.That(mesh.Positions[2].Y, Is.EqualTo(1d));
        Assert.That(mesh.TriangleIndices, Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(mesh.MaterialIndices, Is.EqualTo(new[] { 7 }));
        Assert.That(mesh.Uv0, Has.Count.EqualTo(3));
        Assert.That(mesh.Uv1, Is.Empty);
        Assert.That(mesh.Colors, Is.Empty);
    }

    [Test]
    public void HardFaceKeepsFaceNormalTest()
    {
        var data = BuildSingleTriangle(smoothingGroup: 0);
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Normals.All(me => me.Z == 1d), Is.True);
    }

    [Test]
    public void SingleRenderNormalIsUsedForSmoothedFaceTest()
    {
        var data = BuildSingleTriangle(smoothingGroup: 1);
        data.RVerts = BuildRVerts((1, 1f, 0f, 0f), (1, 1f, 0f, 0f), (1, 1f, 0f, 0f));
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Normals.All(me => me.X == 1d && me.Z == 0d), Is.True);
    }

    [Test]
    public void SplitVertexPicksTheMatchingSmoothingGroupNormalTest()
    {
        var data = BuildSingleTriangle(smoothingGroup: 2);
        data.RVerts = BuildRVerts((2, 0f, 0f, 0f), (1, 0f, 1f, 0f), (1, 0f, 1f, 0f));
        data.ErnNormals[0] = BuildErnArray((1f, 0f, 0f, 4), (0f, 1f, 0f, 2));
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        // Vertex 0 is split: mask 4 does not match group 2, mask 2 does.
        Assert.That(mesh.Normals[0].Y, Is.EqualTo(1d));
        Assert.That(mesh.Normals[0].X, Is.EqualTo(0d));
    }

    [Test]
    public void SplitVertexWithoutMatchingMaskFallsBackToFaceNormalTest()
    {
        var data = BuildSingleTriangle(smoothingGroup: 8);
        data.RVerts = BuildRVerts((2, 0f, 0f, 0f), (1, 0f, 1f, 0f), (1, 0f, 1f, 0f));
        data.ErnNormals[0] = BuildErnArray((1f, 0f, 0f, 4), (0f, 1f, 0f, 2));
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Normals[0].Z, Is.EqualTo(1d));
    }

    [Test]
    public void ZeroExtraNormalFallsBackToFaceNormalTest()
    {
        var data = BuildSingleTriangle(smoothingGroup: 2);
        data.RVerts = BuildRVerts((2, 0f, 0f, 0f), (1, 0f, 1f, 0f), (1, 0f, 1f, 0f));
        data.ErnNormals[0] = BuildErnArray((0f, 0f, 0f, 2));
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Normals[0].Z, Is.EqualTo(1d));
    }

    [Test]
    public void UvChannelMapsThroughTvFacesTest()
    {
        var data = BuildSingleTriangle();
        data.TvFaces = [2, 0, 1];
        data.TVerts = [0.1f, 0.2f, 0f, 0.3f, 0.4f, 0f, 0.5f, 0.6f, 0f];
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Uv0[0].X, Is.EqualTo(0.5f));
        Assert.That(mesh.Uv0[1].Y, Is.EqualTo(0.2f));
        Assert.That(mesh.Uv0[2].X, Is.EqualTo(0.3f));
    }

    [Test]
    public void OutOfRangeUvIndexDefaultsToZeroTest()
    {
        var data = BuildSingleTriangle();
        data.TvFaces = [9, 0, 0];
        data.TVerts = [0.1f, 0.2f, 0f];
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Uv0[0].X, Is.EqualTo(0d));
        Assert.That(mesh.Uv0[1].X, Is.EqualTo(0.1f));
    }

    [Test]
    public void RequestedButEmptySecondaryChannelsStillAlignWithCornersTest()
    {
        // Legacy added a default entry per corner when the channel was supported but unreadable —
        // Colors staying 1:1 with Positions is a contract guard.
        var data = BuildSingleTriangle();
        data.Uv1Requested = true;
        data.ColorsRequested = true;
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Uv1, Has.Count.EqualTo(3));
        Assert.That(mesh.Colors, Has.Count.EqualTo(3));
        // The snapshot default is opaque white — the same instance the legacy per-corner
        // fallback produced.
        Assert.That(mesh.Colors[0].A, Is.EqualTo(1d));
        Assert.That(mesh.Colors[0].R, Is.EqualTo(1d));
    }

    [Test]
    public void VertexColorsCarryAlphaOneTest()
    {
        var data = BuildSingleTriangle();
        data.ColorsRequested = true;
        data.ColorFaces = [0, 1, 2];
        data.ColorVerts = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Colors[0].R, Is.EqualTo(1d));
        Assert.That(mesh.Colors[0].A, Is.EqualTo(1d));
        Assert.That(mesh.Colors[2].B, Is.EqualTo(1d));
    }

    [Test]
    public void CorrectionAppliesObjectThenInverseNodeTransformTest()
    {
        var data = BuildSingleTriangle();
        // objTM: translate +10 on X; nodeTM⁻¹: translate -2 on Y (row-vector convention rows 0..3).
        data.CorrectionObjectTm = [1, 0, 0, 0, 1, 0, 0, 0, 1, 10, 0, 0];
        data.CorrectionInverseNodeTm = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, -2, 0];
        var mesh = new MaxSceneMeshSnapshotData();

        MaxMeshBulkAssembler.Assemble(data, mesh);

        Assert.That(mesh.Positions[0].X, Is.EqualTo(10d));
        Assert.That(mesh.Positions[0].Y, Is.EqualTo(-2d));
        // Normals ignore translation and stay normalized.
        Assert.That(mesh.Normals[0].Z, Is.EqualTo(1d));
    }

    [Test]
    public void CornerPositionsReturnNullOnOutOfRangeVertexTest()
    {
        var data = BuildSingleTriangle();
        data.Faces = BuildFaces((0, 1, 9, 0, 0));

        Assert.That(MaxMeshBulkAssembler.AssembleCornerPositions(data), Is.Null);
    }

    [Test]
    public void MirrorBakeNegatesXAndFlipsWindingKeepingCornerDataAlignedTest()
    {
        var data = BuildSingleTriangle(smoothingGroup: 0);
        data.TvFaces = [0, 1, 2];
        data.TVerts = [0.1f, 0.1f, 0f, 0.9f, 0.1f, 0f, 0.5f, 0.9f, 0f];
        var mesh = new MaxSceneMeshSnapshotData();
        MaxMeshBulkAssembler.Assemble(data, mesh);

        MaxMeshBulkAssembler.ApplyMirrorBake(mesh);

        // Corner order flips 0,1,2 -> 0,2,1 and X negates; each corner keeps ITS uv.
        Assert.That(mesh.Positions[0].X, Is.EqualTo(0d));
        Assert.That(mesh.Positions[1].Y, Is.EqualTo(1d));   // was corner 2 (0,1,0)
        Assert.That(mesh.Positions[2].X, Is.EqualTo(-1d));  // was corner 1 (1,0,0), X negated
        Assert.That(mesh.Uv0[1].Y, Is.EqualTo(0.9f));       // uv followed its corner
        Assert.That(mesh.Uv0[2].X, Is.EqualTo(0.9f));
        Assert.That(mesh.Normals.All(me => me.Z == 1d), Is.True); // face normal Z untouched
        Assert.That(mesh.TriangleIndices, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void MirrorBakeReproducesTheOriginalWorldPositionsUnderTheFoldedTransformTest()
    {
        // v·TM == v'·(D·TM) with v' = v·D: emulate the collector's contract numerically for a
        // mirrored TM (negative X scale + translation).
        var data = BuildSingleTriangle();
        var mesh = new MaxSceneMeshSnapshotData();
        MaxMeshBulkAssembler.Assemble(data, mesh);
        var originalWorld = mesh.Positions
            .Select(me => (X: me.X * -2d + 5d, Y: me.Y * 3d, Z: me.Z))  // TM = scale(-2,3,1) + translate(5,0,0)
            .ToArray();

        MaxMeshBulkAssembler.ApplyMirrorBake(mesh);
        var foldedWorld = mesh.Positions
            .Select(me => (X: me.X * 2d + 5d, Y: me.Y * 3d, Z: me.Z))   // D·TM = scale(+2,3,1) + translate(5,0,0)
            .ToArray();

        // Same world triangle, corners re-ordered 0,2,1 by the winding flip.
        Assert.That(foldedWorld[0], Is.EqualTo(originalWorld[0]));
        Assert.That(foldedWorld[1], Is.EqualTo(originalWorld[2]));
        Assert.That(foldedWorld[2], Is.EqualTo(originalWorld[1]));
    }

    [Test]
    public void CornerPositionsMatchAssembledPositionsTest()
    {
        var data = BuildSingleTriangle();
        var mesh = new MaxSceneMeshSnapshotData();
        MaxMeshBulkAssembler.Assemble(data, mesh);

        var positions = MaxMeshBulkAssembler.AssembleCornerPositions(data);

        Assert.That(positions, Is.Not.Null);
        Assert.That(positions!.Select(me => (me.X, me.Y, me.Z)),
            Is.EqualTo(mesh.Positions.Select(me => (me.X, me.Y, me.Z))));
    }
}
