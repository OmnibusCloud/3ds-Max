namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Raw 3ds Max mesh arrays captured in bulk from the native SDK buffers. The capture side
/// (plugin, main thread) copies the arrays with a handful of memcpys and VERIFIES every stride
/// and field offset against the per-element wrapper API before trusting them; the assembly side
/// (<see cref="MaxMeshBulkAssembler"/>) is pure managed code safe to run on a worker thread.
/// </summary>
public sealed class MaxMeshBulkData
{
    #region Constants

    // maxsdk mesh.h Face: DWORD v[3], DWORD smGroup, DWORD flags (material id in the high word).
    public const int FACE_VERT_OFFSET = 0;
    public const int FACE_SMOOTHING_GROUP_OFFSET = 12;
    public const int FACE_FLAGS_OFFSET = 16;
    public const int FACE_EXPECTED_STRIDE = 20;

    // maxsdk mesh.h RNormal: Point3 normal, DWORD smGroup, DWORD mtlIndex.
    public const int RNORMAL_STRIDE = 20;
    public const int RNORMAL_SMOOTHING_GROUP_OFFSET = 12;

    #endregion

    #region Properties

    public int FaceCount { get; set; }

    public int VertexCount { get; set; }

    /// <summary>FaceCount × FaceStride bytes of native Face records.</summary>
    public byte[] Faces { get; set; } = [];

    public int FaceStride { get; set; } = FACE_EXPECTED_STRIDE;

    /// <summary>VertexCount × 3 floats.</summary>
    public float[] Vertices { get; set; } = [];

    /// <summary>FaceCount × 3 floats (after Mesh.BuildNormals).</summary>
    public float[] FaceNormals { get; set; } = [];

    /// <summary>VertexCount × RVertStride bytes of native RVertex records (after BuildNormals).</summary>
    public byte[] RVerts { get; set; } = [];

    public int RVertStride { get; set; }

    /// <summary>Offset of the embedded RNormal inside an RVertex record (normal floats first).</summary>
    public int RVertRnOffset { get; set; }

    /// <summary>
    /// Per-vertex copy of the extra RNormal array (RNORMAL_STRIDE bytes each) for vertices split
    /// across smoothing groups. Only vertices whose normal count is above one have an entry.
    /// </summary>
    public Dictionary<int, byte[]> ErnNormals { get; set; } = new();

    /// <summary>Channel-1 UV vertices (× 3 floats), null when the mesh has no primary UV layer.</summary>
    public float[]? TVerts { get; set; }

    /// <summary>FaceCount × 3 channel-1 tvert indices, null when the mesh has no primary UV layer.</summary>
    public int[]? TvFaces { get; set; }

    /// <summary>
    /// The mesh reports a second UV channel (legacy added an Uv1 entry per corner even when the
    /// channel arrays turned out empty — the entries then default to zero).
    /// </summary>
    public bool Uv1Requested { get; set; }

    public float[]? Uv1Verts { get; set; }

    public int[]? Uv1Faces { get; set; }

    /// <summary>Same contract as <see cref="Uv1Requested"/> for the vertex-colour channel.</summary>
    public bool ColorsRequested { get; set; }

    public float[]? ColorVerts { get; set; }

    public int[]? ColorFaces { get; set; }

    /// <summary>Rows 0..3 of the object TM as 12 doubles (row-major XYZ), null → no correction.</summary>
    public double[]? CorrectionObjectTm { get; set; }

    public double[]? CorrectionInverseNodeTm { get; set; }

    #endregion
}
