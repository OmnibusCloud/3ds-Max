using Autodesk.Max;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;
using OutWit.Render.ThreeDsMax.Plugin.Export.Snapshots;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace OutWit.Render.ThreeDsMax.Plugin.Services;

internal sealed class MaxSceneSnapshotCollector
{
    #region Fields

    private readonly IGlobal m_global;
    private readonly IInterface m_coreInterface;
    private readonly MaxSceneSnapshotData m_summary;
    private readonly HashSet<object> m_materials;
    private readonly HashSet<object> m_textures;
    private readonly SortedSet<string> m_materialNames;
    private readonly SortedSet<string> m_textureNames;
    private readonly HashSet<object> m_exportedMaterials;
    private readonly HashSet<object> m_exportedTextures;
    private readonly Dictionary<object, string> m_materialIdsByReference;
    private readonly Dictionary<string, string> m_imageAssetIdsByPath;
    private readonly Dictionary<string, string> m_bakedImageAssetIdsByPath;
    private bool? m_isScanlineRenderer;
    private readonly HashSet<string> m_usedMaterialIds;
    private readonly HashSet<string> m_usedImageAssetIds;

    #endregion

    #region Constructors

    public MaxSceneSnapshotCollector(IGlobal global, IInterface coreInterface, MaxSceneSnapshotData summary)
    {
        m_global = global;
        m_coreInterface = coreInterface;
        m_summary = summary;
        m_materials = new HashSet<object>(ReferenceEqualityComparer.Instance);
        m_textures = new HashSet<object>(ReferenceEqualityComparer.Instance);
        m_materialNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        m_textureNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        m_exportedMaterials = new HashSet<object>(ReferenceEqualityComparer.Instance);
        m_exportedTextures = new HashSet<object>(ReferenceEqualityComparer.Instance);
        m_materialIdsByReference = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        m_imageAssetIdsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        m_bakedImageAssetIdsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        m_usedMaterialIds = new HashSet<string>(StringComparer.Ordinal);
        m_usedImageAssetIds = new HashSet<string>(StringComparer.Ordinal);
    }

    #endregion

    #region Functions

    public void Collect(IINode rootNode)
    {
        CollectSceneContent(rootNode, rootNode, null);
        ReadEnvironment();
        ReadEnvironmentMap();
        m_summary.MaterialsCount = m_materials.Count;
        m_summary.TexturesCount = m_textures.Count;
        m_summary.MaterialNames = [.. m_materialNames];
        m_summary.TextureNames = [.. m_textureNames];
        m_summary.UsesScanlineRenderer = IsScanlineRenderer();
    }

    private void ReadEnvironment()
    {
        // 3ds Max scene environment/background colour. The default environment is black; treat a
        // near-black background as "no world" (leave EnvironmentColor null) so default scenes keep
        // the unchanged empty-world behaviour and only scenes with a set background carry a world.
        try
        {
            var background = m_coreInterface.GetBackGround(m_coreInterface.Time, m_global.Interval.Create());
            if (background is null)
                return;

            if (Math.Max(background.X, Math.Max(background.Y, background.Z)) < 0.004d)
                return;

            m_summary.EnvironmentColor = new MaxSceneColorSnapshotData
            {
                R = background.X,
                G = background.Y,
                B = background.Z,
                A = 1d
            };
        }
        catch
        {
            // Leave EnvironmentColor null on failure — the scene renders with the default world.
        }
    }

    private void ReadEnvironmentMap()
    {
        // 3ds Max scene environment map (Rendering > Environment). When a bitmap is assigned and
        // enabled, treat it as the equirectangular HDRI the generator builds the world from; the
        // image asset is registered so the neutral payload carries it. Any failure simply leaves the
        // environment image unset, so the scene falls back to the background colour / empty world.
        try
        {
            if (!m_coreInterface.UseEnvironmentMap)
                return;

            var environmentMap = m_coreInterface.EnvironmentMap;
            if (environmentMap is null)
                return;

            var isScreenMapped = ReadEnvironmentIsScreenMapped();

            var bitmap = environmentMap as IBitmapTex
                         ?? (environmentMap is IISubMap subMap ? FindFirstBitmapTexture(subMap) : null);

            if (bitmap is null || string.IsNullOrWhiteSpace(bitmap.MapName))
            {
                // Procedural environment (Gradient sky etc.) — bake it as an image so the backdrop
                // keeps its authored look instead of collapsing to a flat colour. Screen-mapped
                // environments are 2D backdrops stretched across the render window, so bake those
                // at the render aspect; everything else bakes as a 2:1 equirect panorama.
                // Near-uniform bakes are allowed here: a flat-ish sky is still the authored
                // backdrop (A06's Noise environment reads as an even grey).
                var (bakeWidth, bakeHeight) = isScreenMapped ? ResolveBackdropBakeSize() : ((ushort)1024, (ushort)512);
            var bakedId = TryBakeTexmapToImageAsset(environmentMap, "environment", bakeWidth, bakeHeight, allowNearUniform: true);
                if (!string.IsNullOrWhiteSpace(bakedId))
                {
                    m_summary.EnvironmentImageId = bakedId;
                    m_summary.EnvironmentIsScreenMapped = isScreenMapped;
                }
                return;
            }

            var imageAssetId = GetOrCreateImageAsset(bitmap);
            if (!string.IsNullOrWhiteSpace(imageAssetId))
            {
                m_summary.EnvironmentImageId = imageAssetId;
                m_summary.EnvironmentIsScreenMapped = isScreenMapped;
            }
        }
        catch
        {
            // Leave EnvironmentImageId null on failure — the scene renders with the colour world.
        }
    }

    // A 3ds Max environment map with Environ/Screen coordinates is a 2D backdrop stretched across
    // the render window, not a panorama. The mapping kind lives on the map's UVGen, which the
    // facade does not surface — walk the environment map tree via MAXScript and report whether
    // any UVGen says Environ (mappingType 1) + Screen (mapping 3). Procedural roots (A06's Noise
    // has 3D XYZGen coords) delegate the projection to their submaps, hence the tree walk.
    private bool ReadEnvironmentIsScreenMapped()
    {
        try
        {
            var result = m_global.FPValue.Create();
            const string script =
                "(local found = 0; local stack = #(); if environmentMap != undefined do append stack environmentMap; local guard = 0; " +
                "while stack.count > 0 and found == 0 and guard < 64 do (guard += 1; local m = stack[stack.count]; deleteItem stack stack.count; " +
                "if m != undefined do (try (if m.coords.mappingType == 1 and m.coords.mapping == 3 then found = 1) catch (); " +
                "local n = 0; try (n = getNumSubTexmaps m) catch (); for i = 1 to n do (try (append stack (getSubTexmap m i)) catch ()))); found as float)";
            if (!m_global.ExecuteMAXScriptScript(script, Autodesk.Max.MAXScript.ScriptSource.NonEmbedded, true, result, false))
                return false;

            var value = result.Type switch
            {
                ParamType2.Float => (double)result.F,
                ParamType2.Double => result.Dbl,
                ParamType2.Int => result.I,
                _ => 0d
            };

            return value >= 0.5d;
        }
        catch
        {
            return false;
        }
    }

    // Screen backdrops bake at the render aspect so the picture lands on the window exactly as
    // the source renderer stretches it (a 2:1 equirect bake of a 4:3 backdrop would squash it).
    private (ushort Width, ushort Height) ResolveBackdropBakeSize()
    {
        const ushort width = 1024;
        var renderWidth = m_coreInterface.RendWidth;
        var renderHeight = m_coreInterface.RendHeight;
        if (renderWidth <= 0 || renderHeight <= 0)
            return (width, 768);

        var height = (int)Math.Round(width * (double)renderHeight / renderWidth / 2d) * 2;
        return (width, (ushort)Math.Clamp(height, 64, 2048));
    }

    // Authored map tiling lives on the bitmap's UVGen (Coordinates rollout), separate from the
    // mesh UVs — Butterfly's bark tiles 10×10 there and rendered stretched 10× without it.
    // Only direct bitmaps read it: baked procedurals already carry their coordinate transform
    // in the baked pixels. Offsets and rotation stay untransferred for now — their 3ds Max
    // pivot semantics are centre-anchored (unlike the generator's origin-anchored Mapping
    // node) and no corpus scene authors them; tiling of a tileable map is phase-invariant.
    // RaytraceMaterial predates ParamBlock2, so its authored values are invisible to the
    // paramblock readers (A08's cups exported the GetXParency mean and the default IOR while
    // the UI says transparency (128,0,0) / IOR 1.6). Evaluate the MAXScript property against
    // the material's anim handle instead. Note the SDK's own spelling: the transparency
    // colour property is literally named "Transparecy".
    private MaxSceneColorSnapshotData? TryReadRaytraceTransparencyColor(IMtl material)
    {
        return TryReadMaterialScriptColor(material, "Transparecy");
    }

    // Reads a MAXScript colour property (0-255 per channel) off the material by anim handle.
    private MaxSceneColorSnapshotData? TryReadMaterialScriptColor(IMtl material, string propertyName)
    {
        var r = TryEvaluateMaterialScriptDouble(material, propertyName + ".r");
        var g = TryEvaluateMaterialScriptDouble(material, propertyName + ".g");
        var b = TryEvaluateMaterialScriptDouble(material, propertyName + ".b");
        if (r is null || g is null || b is null || r < 0d || g < 0d || b < 0d)
            return null;

        return new MaxSceneColorSnapshotData
        {
            R = Math.Clamp(r.Value / 255d, 0d, 1d),
            G = Math.Clamp(g.Value / 255d, 0d, 1d),
            B = Math.Clamp(b.Value / 255d, 0d, 1d),
            A = 1d
        };
    }

    // Evaluates "<anim>.<propertyPath>" via MAXScript, addressing the object by its anim
    // handle. Returns null when the property is missing or the evaluation fails (-100000 marker).
    private double? TryEvaluateMaterialScriptDouble(IAnimatable material, string propertyPath)
    {
        try
        {
            var handle = m_global.Animatable.GetHandleByAnim(material);
            var script = $"(try (((getAnimByHandle {handle.ToUInt64()}).{propertyPath}) as float) catch (-100000.0))";
            var result = m_global.FPValue.Create();
            if (!m_global.ExecuteMAXScriptScript(script, Autodesk.Max.MAXScript.ScriptSource.NonEmbedded, true, result, false))
                return null;

            double value = result.Type switch
            {
                ParamType2.Float => result.F,
                ParamType2.Double => result.Dbl,
                ParamType2.Int => result.I,
                _ => -100000d
            };

            return value <= -99999d ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private (double UScale, double VScale, double UOffset, double VOffset) ReadBitmapUvTiling(IBitmapTex bitmap)
    {
        try
        {
            if (bitmap.TheUVGen is not IStdUVGen uvGen)
                return (1d, 1d, 0d, 0d);

            // Take the authored transform straight from the UVGen matrix — it already composes
            // tiling, the centre anchor AND the authored offsets (the dragon sky is
            // scale(3, 2.4) + translation(−1.45, −0.892); reconstructing that from the spinners
            // shifted the repeat phases and grew seams Max hides). The generator's Mapping node
            // runs in 'TEXTURE' mode with tex = (uv − Location) / Scale, and Max computes
            // tex = uv·T + Tr, so Scale = 1/T and Location = −Tr/T.
            var matrix = m_global.Matrix3.Create();
            uvGen.GetUVTransform(matrix);
            var rowU = matrix.GetRow(0);
            var rowV = matrix.GetRow(1);
            var translation = matrix.GetRow(3);

            // Authored UV rotation would put off-diagonal terms here — the Mapping pivot
            // semantics differ, so leave those maps untransformed rather than mis-rotate them.
            if (Math.Abs(rowU.Y) > 1e-4 || Math.Abs(rowV.X) > 1e-4)
                return (1d, 1d, 0d, 0d);

            double uTiling = rowU.X;
            double vTiling = rowV.Y;
            if (!double.IsFinite(uTiling) || uTiling == 0d || !double.IsFinite(vTiling) || vTiling == 0d)
                return (1d, 1d, 0d, 0d);

            return (1d / uTiling, 1d / vTiling, -translation.X / uTiling, -translation.Y / vTiling);
        }
        catch
        {
            return (1d, 1d, 0d, 0d);
        }
    }

    // Walks the node hierarchy. Only mesh/camera/light nodes are emitted; non-geometry parents
    // (dummies, groups, bones, point helpers) are skipped. Every emitted node carries its WORLD
    // transform (and world-sampled keyframes) with no ParentId: rebuilding Max's parent chains in
    // Blender accumulated per-link TRS decomposition error (a scaled/stretched bone chain sheared
    // its children — Maxine's bone-parented hair and eyes drifted ~64 units off the head), and
    // world-space sampling also captures motion a static-local child INHERITS from an animated
    // ancestor. effectiveParentNode always stays the scene root.
    private void CollectSceneContent(IINode parentNode, IINode effectiveParentNode, string? effectiveParentId)
    {
        for (var i = 0; i < parentNode.NumberOfChildren; i++)
        {
            var childNode = parentNode.GetChildNode(i);
            m_summary.NodesCount++;
            DetectMotionBlur(childNode);

            // EvalWorldState (NOT ObjectRef.Eval) so world-space modifiers are included: a Flex
            // teapot bound to a bounce space warp deforms only in the WSM stage, and PathDeform'd
            // tools park their node TM while the WSM carries the real placement.
            var objectState = childNode.EvalWorldState(m_coreInterface.Time, true);
            var sceneObject = objectState.Obj;
            var nodeId = $"node:{childNode.Handle}";
            var parentId = effectiveParentId;
            var localTransform = ExtractLocalTransform(childNode, effectiveParentNode);
            var transformKeyframes = SampleTransformKeyframes(childNode, effectiveParentNode);
            var added = false;

            if (sceneObject is ICameraObject cameraObject)
            {
                added = true;
                m_summary.CamerasCount++;
                AddName(m_summary.CameraNames, childNode.Name);
                var cameraId = $"camera:{childNode.Handle}";

                // A 3ds Max camera looks down its local -Z; the Blender generator applies
                // rotation @ RotX(-90°) before assigning it. Compose RotX(+90°) here so the pair
                // cancels and the camera keeps its exact authored orientation (including roll),
                // for free, target and parented cameras alike.
                ComposeCameraLightAxisCorrection(localTransform, transformKeyframes);

                m_summary.Nodes.Add(new MaxSceneNodeSnapshotData
                {
                    Id = nodeId,
                    Name = childNode.Name,
                    ParentId = parentId,
                    Kind = DccNodeKind.Camera,
                    LocalTransform = localTransform,
                    TransformKeyframes = transformKeyframes,
                    CameraId = cameraId,
                    Visible = !childNode.IsNodeHidden(false),
                    Renderable = childNode.Renderable
                });
                m_summary.Cameras.Add(ExtractCamera(childNode, cameraObject, cameraId));
            }

            if (sceneObject is ILightObject lightObject)
            {
                var lightId = $"light:{childNode.Handle}";
                var lightSnapshot = ExtractLight(childNode, lightObject, lightId);

                // A light that is off or carries no positive intensity contributes nothing and would
                // fail Dcc validation ("light requires positive intensity"), aborting the whole scene.
                // Drop it here so the rest of the scene still renders; the export service surfaces a
                // Warning from SkippedInactiveLightCount.
                if (IsLightActive(lightObject) && lightSnapshot.Intensity > 0d)
                {
                    added = true;
                    m_summary.LightsCount++;
                    AddName(m_summary.LightNames, childNode.Name);

                    // Lights share the generator's rotation @ RotX(-90°) camera path; compose the
                    // cancelling RotX(+90°) so spot/sun directions survive exactly (see camera above).
                    ComposeCameraLightAxisCorrection(localTransform, transformKeyframes);

                    m_summary.Nodes.Add(new MaxSceneNodeSnapshotData
                    {
                        Id = nodeId,
                        Name = childNode.Name,
                        ParentId = parentId,
                        Kind = DccNodeKind.Light,
                        LocalTransform = localTransform,
                        TransformKeyframes = transformKeyframes,
                        LightId = lightId,
                        Visible = !childNode.IsNodeHidden(false),
                        Renderable = childNode.Renderable
                    });
                    m_summary.Lights.Add(lightSnapshot);
                }
                else
                {
                    m_summary.SkippedInactiveLightCount++;
                }
            }

            // Helper objects (Crowd/Delegate gizmos and the like) can still convert to a TriObject,
            // but Max never renders them — Butterfly's Crowd helper rendered as a floating white
            // diamond. Dummies already fail the TriObject conversion; this catches the rest.
            // Shape (spline) objects go through GetRenderMesh inside ExtractMesh: the TriObject
            // conversion yields a FILLED planar disc while Max renders the "Render Thickness"
            // tube (Maxine's rig circles are 0.1-unit threads, not discs).
            if (sceneObject.CanConvertToType(m_global.TriObjectClassID) == 1 && sceneObject is not IHelperObject)
            {
                var meshId = $"mesh:{childNode.Handle}";
                var meshSnapshot = ExtractMesh(childNode, sceneObject, meshId);

                // A mesh with no vertices (a helper/degenerate object that still converts to a
                // TriObject) fails Dcc validation ("mesh requires positions"), aborting the whole
                // scene. Drop it so the rest renders; the export service surfaces a Warning from
                // SkippedEmptyMeshCount. Children re-parent onto the nearest INCLUDED ancestor.
                if (meshSnapshot.Positions.Count == 0)
                {
                    m_summary.SkippedEmptyMeshCount++;
                }
                else
                {
                    added = true;
                    m_summary.MeshesCount++;
                    meshSnapshot.SubdivisionLevels = ResolveRenderSubdivisionLevels(childNode);
                    SampleDeformationFrames(childNode, meshSnapshot);
                    var materialBindingMap = GetMaterialBindingMap(childNode.Mtl);
                    NormalizeMaterialIndices(meshSnapshot, materialBindingMap);
                    var materialBindingId = ResolveMaterialBindingId(meshSnapshot, materialBindingMap.MaterialIds);

                    // Generator contract: a mesh WITH MaterialIndices indexes the SCENE-level
                    // materials list (per-face multi-material) and its node binding is ignored; a
                    // mesh WITHOUT indices uses MaterialBindingId. The compacted per-node indices
                    // above are NOT scene indices, so translate: single-material meshes drop their
                    // (all-equal) indices and rely on the binding — keeping them made every mesh
                    // render as scene.Materials[first index] (hardwood: 51 amber objects); true
                    // multi-material meshes remap each compact index to the scene position.
                    if (UsesPerTriangleMaterialBinding(meshSnapshot))
                    {
                        materialBindingId = null;
                        if (!TryRemapMaterialIndicesToSceneOrder(meshSnapshot, materialBindingMap))
                        {
                            // A compact index did not resolve to a collected material — fall back
                            // to the single-binding path so the mesh still renders.
                            meshSnapshot.MaterialIndices.Clear();
                            materialBindingId = materialBindingMap.MaterialIds.Count > 0 ? materialBindingMap.MaterialIds[0] : null;
                        }
                    }
                    else
                    {
                        meshSnapshot.MaterialIndices.Clear();
                    }

                    m_summary.Nodes.Add(new MaxSceneNodeSnapshotData
                    {
                        Id = nodeId,
                        Name = childNode.Name,
                        ParentId = parentId,
                        Kind = DccNodeKind.Mesh,
                        LocalTransform = localTransform,
                        TransformKeyframes = transformKeyframes,
                        MeshId = meshId,
                        MaterialBindingId = materialBindingId,
                        Visible = !childNode.IsNodeHidden(false),
                        // Max excludes hidden geometry from renders by default ("Render Hidden
                        // Geometry" off), but the generator maps Visible to hide_viewport only —
                        // fold hidden into Renderable so hidden rig helpers (Maxine's muscle
                        // meshes) stop appearing in our renders.
                        Renderable = childNode.Renderable && !childNode.IsNodeHidden(false),
                        // Max renders material-less objects in their wirecolor (robby's gold and
                        // purple teapots); capture it so the mapper can synthesize the material.
                        WireColor = materialBindingId is null && meshSnapshot.MaterialIndices.Count == 0
                            ? TryReadWireColor(childNode)
                            : null
                    });
                    m_summary.Meshes.Add(meshSnapshot);
                }
            }

            // World-space export: children never re-parent — transforms stay relative to the scene
            // root regardless of what was emitted above.
            _ = added;
            CollectSceneContent(childNode, effectiveParentNode, null);
        }
    }

    private static int ResolveRenderSubdivisionLevels(IINode node)
    {
        // MeshSmooth/TurboSmooth apply MORE subdivision at render time than in the viewport when
        // "Render Iterations" is on (Ape's body: two modifiers with iterations=0/renderIters=1 —
        // the exported viewport-state mesh is two levels coarser than the native render, giving
        // faceted arms). The exported vertices already carry the VIEWPORT iterations, so export
        // only the render-minus-viewport delta; the generator applies it as a Blender Subsurf.
        try
        {
            var extraLevels = 0;
            var current = node.ObjectRef;

            while (current is IIDerivedObject derivedObject)
            {
                for (var modifierIndex = 0; modifierIndex < derivedObject.NumModifiers; modifierIndex++)
                {
                    var modifier = derivedObject.GetModifier(modifierIndex);
                    if (modifier == null || !IsSubdivisionSmoothingModifier(modifier))
                        continue;

                    extraLevels += ResolveModifierRenderSubdivisionDelta(modifier);
                }

                current = derivedObject.ObjRef;
            }

            return Math.Clamp(extraLevels, 0, MAX_RENDER_SUBDIVISION_LEVELS);
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsSubdivisionSmoothingModifier(IModifier modifier)
    {
        var className = modifier.ClassName(false)?.ToLowerInvariant();
        return className is not null
               && (className.Contains("meshsmooth") || className.Contains("turbosmooth") || className.Contains("opensubdiv"));
    }

    private static int ResolveModifierRenderSubdivisionDelta(IModifier modifier)
    {
        // A modifier disabled outright contributes to neither the viewport mesh nor the render.
        try
        {
            if (!modifier.IsEnabled)
                return 0;
        }
        catch
        {
        }

        var viewportIterations = TryReadModifierParamBlockInt(modifier, "iterations", "iters") ?? 0;
        var useRenderIterations = (TryReadModifierParamBlockInt(modifier, "userenderiterations", "userenderiters") ?? 0) != 0;
        var renderIterations = useRenderIterations
            ? TryReadModifierParamBlockInt(modifier, "renderiterations", "renderiters") ?? viewportIterations
            : viewportIterations;

        return Math.Max(0, renderIterations - viewportIterations);
    }

    private static int? TryReadModifierParamBlockInt(IModifier modifier, params string[] names)
    {
        try
        {
            for (var blockIndex = 0; blockIndex < modifier.NumParamBlocks; blockIndex++)
            {
                if (modifier.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is null || Array.IndexOf(names, name) < 0)
                        continue;

                    return block.GetInt(def.Id, 0, 0);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    // Number of sides for exported spline render tubes (matches the Max default render_sides=3;
    // these are hairline threads, not showcase geometry).
    private const int SHAPE_TUBE_SIDES = 3;

    private MaxSceneMeshSnapshotData? TryExtractShapeRenderMesh(IINode node, IShapeObject shapeObject, IObject sceneObject, string meshId)
    {
        try
        {
            var meshData = new MaxSceneMeshSnapshotData
            {
                Id = meshId,
                Name = $"{node.Name}Mesh"
            };

            // "Enable In Renderer" off → Max never renders the shape; an empty mesh feeds the
            // existing empty-mesh skip. The typed ShapeObject flag is authoritative; the pb2 param
            // scan is a fallback (the rendering-rollout params hide behind non-obvious IntNames).
            var shapeRenderable = TryReadShapeRenderableFlag(shapeObject)
                                  ?? ((TryReadObjectParamBlockInt(sceneObject, "render_renderable", "renderable") ?? 0) != 0);
            if (!shapeRenderable)
                return meshData;

            var thickness = TryReadObjectParamBlockFloat(sceneObject, "render_thickness", "thickness") ?? 0.1d;
            var radius = Math.Max(thickness, 0.001d) / 2d;

            var polyShape = m_global.PolyShape.Create();
            shapeObject.MakePolyShape(m_coreInterface.Time, polyShape, -1, false);

        var correction = ComputeObjectToNodeCorrection(node, m_coreInterface.Time);

            for (var lineIndex = 0; lineIndex < polyShape.NumLines; lineIndex++)
            {
                var line = polyShape.Lines[lineIndex];
                if (line == null || line.NumPts < 2)
                    continue;

                AppendPolylineTube(meshData, line, radius, correction);
            }

            return meshData;
        }
        catch
        {
            // Unreadable shape — fall back to the TriObject conversion path.
            return null;
        }
    }

    private static void AppendPolylineTube(
        MaxSceneMeshSnapshotData meshData,
        IPolyLine line,
        double radius,
        (IMatrix3 ObjectTm, IMatrix3 InverseNodeTm)? correction)
    {
        var count = line.NumPts;
        var closed = line.IsClosed;
        var points = new (double X, double Y, double Z)[count];
        for (var index = 0; index < count; index++)
        {
            var point = line.Pts[index].P;
            points[index] = (point.X, point.Y, point.Z);
        }

        // Ring of SHAPE_TUBE_SIDES vertices around every point, oriented by the local tangent.
        var rings = new (double X, double Y, double Z)[count][];
        for (var index = 0; index < count; index++)
        {
            var previous = points[index == 0 ? (closed ? count - 1 : 0) : index - 1];
            var next = points[index == count - 1 ? (closed ? 0 : count - 1) : index + 1];
            var tangent = Normalize((next.X - previous.X, next.Y - previous.Y, next.Z - previous.Z));
            if (tangent == (0d, 0d, 0d))
                tangent = (0d, 0d, 1d);

            var reference = Math.Abs(tangent.Z) < 0.9d ? (X: 0d, Y: 0d, Z: 1d) : (X: 1d, Y: 0d, Z: 0d);
            var side = Normalize(Cross(tangent, (reference.X, reference.Y, reference.Z)));
            var up = Cross(side, tangent);

            var ring = new (double X, double Y, double Z)[SHAPE_TUBE_SIDES];
            for (var corner = 0; corner < SHAPE_TUBE_SIDES; corner++)
            {
                var angle = corner * 2d * Math.PI / SHAPE_TUBE_SIDES;
                var (sin, cos) = (Math.Sin(angle), Math.Cos(angle));
                ring[corner] = (
                    points[index].X + (side.X * cos + up.X * sin) * radius,
                    points[index].Y + (side.Y * cos + up.Y * sin) * radius,
                    points[index].Z + (side.Z * cos + up.Z * sin) * radius);
            }

            rings[index] = ring;
        }

        var segmentCount = closed ? count : count - 1;
        for (var segment = 0; segment < segmentCount; segment++)
        {
            var ringA = rings[segment];
            var ringB = rings[(segment + 1) % count];
            for (var corner = 0; corner < SHAPE_TUBE_SIDES; corner++)
            {
                var nextCorner = (corner + 1) % SHAPE_TUBE_SIDES;
                AppendTubeTriangle(meshData, ringA[corner], ringB[corner], ringB[nextCorner], correction);
                AppendTubeTriangle(meshData, ringA[corner], ringB[nextCorner], ringA[nextCorner], correction);
            }
        }
    }

    private static void AppendTubeTriangle(
        MaxSceneMeshSnapshotData meshData,
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b,
        (double X, double Y, double Z) c,
        (IMatrix3 ObjectTm, IMatrix3 InverseNodeTm)? correction)
    {
        var normal = Normalize(Cross((b.X - a.X, b.Y - a.Y, b.Z - a.Z), (c.X - a.X, c.Y - a.Y, c.Z - a.Z)));
        if (normal == (0d, 0d, 0d))
            normal = (0d, 0d, 1d);

        foreach (var vertex in new[] { a, b, c })
        {
            meshData.Positions.Add(correction == null
                ? new MaxSceneVector3SnapshotData { X = vertex.X, Y = vertex.Y, Z = vertex.Z }
                : ApplyCorrection(correction.Value.ObjectTm, correction.Value.InverseNodeTm, vertex.X, vertex.Y, vertex.Z));
            meshData.Normals.Add(correction == null
                ? new MaxSceneVector3SnapshotData { X = normal.X, Y = normal.Y, Z = normal.Z }
                : ApplyCorrectionToTupleNormal(correction.Value.ObjectTm, correction.Value.InverseNodeTm, normal));
            meshData.Uv0.Add(new MaxSceneVector2SnapshotData { X = 0d, Y = 0d });
            meshData.TriangleIndices.Add(meshData.TriangleIndices.Count);
        }
    }

    private static MaxSceneVector3SnapshotData ApplyCorrectionToTupleNormal(IMatrix3 objectTm, IMatrix3 inverseNodeTm, (double X, double Y, double Z) normal)
    {
        var a = TransformVectorRaw(objectTm, normal.X, normal.Y, normal.Z);
        var b = TransformVectorRaw(inverseNodeTm, a.X, a.Y, a.Z);
        var length = Math.Sqrt(b.X * b.X + b.Y * b.Y + b.Z * b.Z);
        if (length < 1e-9d)
            return new MaxSceneVector3SnapshotData { X = normal.X, Y = normal.Y, Z = normal.Z };
        return new MaxSceneVector3SnapshotData { X = b.X / length, Y = b.Y / length, Z = b.Z / length };
    }

    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) left, (double X, double Y, double Z) right)
    {
        return (
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
    }

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) vector)
    {
        var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
        return length <= double.Epsilon ? (0d, 0d, 0d) : (vector.X / length, vector.Y / length, vector.Z / length);
    }

    private static bool? TryReadShapeRenderableFlag(IShapeObject shapeObject)
    {
        try
        {
            return shapeObject.Renderable;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryReadObjectParamBlockInt(IObject sceneObject, params string[] names)
    {
        try
        {
            for (var blockIndex = 0; blockIndex < sceneObject.NumParamBlocks; blockIndex++)
            {
                if (sceneObject.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is null || Array.IndexOf(names, name) < 0)
                        continue;

                    return block.GetInt(def.Id, 0, 0);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static double? TryReadObjectParamBlockFloat(IObject sceneObject, params string[] names)
    {
        try
        {
            for (var blockIndex = 0; blockIndex < sceneObject.NumParamBlocks; blockIndex++)
            {
                if (sceneObject.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is null || Array.IndexOf(names, name) < 0)
                        continue;

                    return block.GetFloat(def.Id, 0, 0);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private MaxSceneMeshSnapshotData ExtractMesh(IINode node, IObject sceneObject, string meshId)
    {
        // Shape (spline) objects: the TriObject conversion yields a FILLED planar disc, but Max
        // renders the shape's "Enable In Renderer" tube — rebuild that thread from the shape's
        // polylines (Maxine's rig circles are 0.1-unit hairlines in the native render, not solid
        // discs floating around her waist).
        if (sceneObject is IShapeObject shapeObject)
        {
            var shapeMesh = TryExtractShapeRenderMesh(node, shapeObject, sceneObject, meshId);
            if (shapeMesh != null)
                return shapeMesh;
        }

        IMesh? mesh;
        {
            var convertedObject = sceneObject;

            if (convertedObject.CanConvertToType(m_global.TriObjectClassID) == 1)
                convertedObject = convertedObject.ConvertToType(m_coreInterface.Time, m_global.TriObjectClassID);

            if (convertedObject is not ITriObject triObject)
            {
                return new MaxSceneMeshSnapshotData
                {
                    Id = meshId,
                    Name = $"{node.Name}Mesh"
                };
            }

            mesh = triObject.Mesh;
        }

        var meshData = new MaxSceneMeshSnapshotData
        {
            Id = meshId,
            Name = $"{node.Name}Mesh"
        };

        // 3ds Max places geometry with the OBJECT TM (node TM ∘ pivot offset ∘ world-space
        // modifiers), not the node TM alone: moved pivots and WSM-deformed objects (e.g. the
        // hardwood paint tools) live in the difference. Bake objTM·nodeTM⁻¹ into the vertices so
        // the exported node transform is sufficient to place the mesh exactly.
        var correction = ComputeObjectToNodeCorrection(node, m_coreInterface.Time);

        // Compute the smoothing-group-aware vertex normals so curved surfaces export smooth and
        // hard edges (smoothing-group boundaries / unsmoothed faces) stay hard.
        mesh.BuildNormals();

        // Map channel 1 is the primary UV (Uv0); channel 2 is the optional second UV set. Only
        // populate Uv1 when the mesh actually carries channel 2, so meshes with a single UV set
        // keep an empty Uv1 (the mapper then emits no second layer).
        var hasSecondUv = MeshSupportsMapChannel(mesh, 2);

        // Map channel 0 is the vertex-colour channel (a colour stored per map-vert as RGB). The flag
        // gates every corner uniformly so Colors stays aligned 1:1 with Positions (a 1.4.0 contract
        // guard) — either every corner gets a colour or none do.
        var hasVertexColors = MeshSupportsMapChannel(mesh, 0);

        for (var faceIndex = 0; faceIndex < mesh.NumFaces; faceIndex++)
        {
            var face = mesh.GetFace(faceIndex);
            var faceNormal = mesh.GetFaceNormal(faceIndex);

            for (var vertexIndex = 0; vertexIndex < 3; vertexIndex++)
            {
                var sourceVertexIndex = (int)face.GetVert(vertexIndex);
                var point = mesh.GetVert(sourceVertexIndex);
                var normal = ResolveVertexNormal(mesh, face, sourceVertexIndex, faceNormal);
                meshData.Positions.Add(correction == null
                    ? new MaxSceneVector3SnapshotData { X = point.X, Y = point.Y, Z = point.Z }
                    : ApplyCorrection(correction.Value.ObjectTm, correction.Value.InverseNodeTm, point.X, point.Y, point.Z));
                meshData.Normals.Add(correction == null
                    ? new MaxSceneVector3SnapshotData { X = normal.X, Y = normal.Y, Z = normal.Z }
                    : ApplyCorrectionToNormal(correction.Value.ObjectTm, correction.Value.InverseNodeTm, normal));
                meshData.Uv0.Add(ExtractUv(mesh, faceIndex, vertexIndex));
                if (hasSecondUv)
                    meshData.Uv1.Add(ExtractUvFromChannel(mesh, 2, faceIndex, vertexIndex));
                if (hasVertexColors)
                    meshData.Colors.Add(ExtractVertexColor(mesh, faceIndex, vertexIndex));
                meshData.TriangleIndices.Add(meshData.TriangleIndices.Count);
            }

            meshData.MaterialIndices.Add(mesh.GetFaceMtlIndex(faceIndex));
        }

        return meshData;
    }

    // Looks up a colour parameter by internal name across the material's parameter blocks —
    // renderer-agnostic (PhysicalMaterial and Arnold both name it "base_color"). Returns null when
    // no such parameter exists or the read fails, so callers keep the legacy IMtl fallback.
    private static MaxSceneColorSnapshotData? TryReadParamBlockColor(IMtl material, int time, params string[] names)
    {
        try
        {
            for (var blockIndex = 0; blockIndex < material.NumParamBlocks; blockIndex++)
            {
                if (material.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is null || Array.IndexOf(names, name) < 0)
                        continue;

                    try
                    {
                        var color = block.GetColor(def.Id, time, 0);
                        if (color is not null)
                            return new MaxSceneColorSnapshotData { R = color.R, G = color.G, B = color.B, A = 1d };
                    }
                    catch
                    {
                        // Parameter is not a colour after all — keep scanning.
                    }

                    try
                    {
                        var point = block.GetPoint3(def.Id, time, 0);
                        if (point is not null)
                            return new MaxSceneColorSnapshotData { R = point.X, G = point.Y, B = point.Z, A = 1d };
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    // Bool/int material parameters must be read with GetInt: GetFloat on a TYPE_BOOL param
    // silently returns 0 (the spline-Renderable lesson).
    private static int? TryReadParamBlockInt(IMtl material, int time, params string[] names)
    {
        try
        {
            for (var blockIndex = 0; blockIndex < material.NumParamBlocks; blockIndex++)
            {
                if (material.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is null || Array.IndexOf(names, name) < 0)
                        continue;

                    return block.GetInt(def.Id, time, 0);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static double? TryReadParamBlockFloat(IMtl material, int time, params string[] names)
    {
        try
        {
            for (var blockIndex = 0; blockIndex < material.NumParamBlocks; blockIndex++)
            {
                if (material.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is null || Array.IndexOf(names, name) < 0)
                        continue;

                    return block.GetFloat(def.Id, time, 0);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static double ReadMetalness(IMtl material, int time)
    {
        // The legacy IMtl surface has no PBR metalness; modern materials (Physical Material) expose
        // it as a parameter-block float named "metalness". Look it up by parameter internal name so
        // it works regardless of the material class, clamp, and treat any non-PBR material or read
        // failure as non-metallic.
        try
        {
            for (var blockIndex = 0; blockIndex < material.NumParamBlocks; blockIndex++)
            {
                if (material.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is "metalness" or "metallic" or "metal")
                        return Math.Clamp(block.GetFloat(def.Id, time, 0), 0d, 1d);
                }
            }
        }
        catch
        {
        }

        return 0d;
    }

    private void ReadMaterialTextureSlots(IMtl material, MaxSceneMaterialSnapshotData materialSnapshot)
    {
        // Route each of the material's texture slots to the matching neutral slot by its slot
        // name, so PBR maps (normal/roughness/metalness/opacity) survive — not just the base
        // colour. Slot-name matching is renderer-agnostic (Standard, Physical, and most third-party
        // shaders all name their slots descriptively).
        // Foreign renderer families only contribute DIRECT bitmaps (walking their submap trees
        // for class/file names is safe) — baking their procedural maps is not attempted.
        var foreignFamily = IsForeignPluginClass(material.ClassName(false));
        var assignedSlots = new HashSet<DccTextureSlotKind>();

        for (var i = 0; i < material.NumSubTexmaps; i++)
        {
            var slotName = material.GetSubTexmapSlotName(i, false);
            var slotKind = ClassifyTextureSlot(slotName);
            var texmap = material.GetSubTexmap(i);

            // A Vertex Color texmap's data already travels as mesh colour attributes; record WHERE
            // the artist wired it (diffuse → base colour, self-illumination → emission) so the
            // generator binds a Color Attribute node — lighting_vertex carries its entire baked
            // lighting this way. Checked before the slot-kind gate: self-illum slots classify to
            // no slot kind but still matter here.
            if (texmap is not null && IsVertexColorTexmap(texmap))
            {
                if (slotKind == DccTextureSlotKind.BaseColor)
                    materialSnapshot.BaseColorFromVertexColors = true;
                else if (slotName?.Contains("illum", StringComparison.OrdinalIgnoreCase) == true
                         || slotName?.Contains("emission", StringComparison.OrdinalIgnoreCase) == true)
                    materialSnapshot.EmissionFromVertexColors = true;

                continue;
            }

            if (slotKind is null || assignedSlots.Contains(slotKind.Value))
                continue;

            // A "Normal Bump" texmap in the bump channel carries true normal VECTORS, not heights.
            if (slotKind == DccTextureSlotKind.Bump
                && texmap?.ClassName(false)?.Contains("normal", StringComparison.OrdinalIgnoreCase) == true)
            {
                slotKind = DccTextureSlotKind.Normal;
                if (assignedSlots.Contains(slotKind.Value))
                    continue;
            }
            var bitmap = texmap as IBitmapTex
                         ?? (texmap is IISubMap subMap ? FindFirstBitmapTexture(subMap) : null);

            // Procedural maps (Noise, Gradient, Mix, Smoke, Checker…) carry no file — bake the
            // texmap to a PNG so the artist's pattern survives instead of silently dropping it.
            // Foreign-family materials skip the bake: rendering their maps outside the native
            // renderer context is where the crash class lives.
            if (foreignFamily && (bitmap is null || string.IsNullOrWhiteSpace(bitmap.MapName)))
            {
                // V-Ray scenes carry their files in VRayBitmap texmaps — a V-Ray class the
                // IBitmapTex walk can't see. Class names and a script filename read are safe
                // on foreign texmaps (unlike baking), so route the file into the slot.
                var vrayBitmap = texmap is null ? null : FindFirstVRayBitmapTexmap(texmap, 0);
                var vrayBitmapPath = vrayBitmap is null ? null : TryReadVRayBitmapFilePath(vrayBitmap);
                if (vrayBitmap is not null && !string.IsNullOrWhiteSpace(vrayBitmapPath))
                {
                    var vrayImageAssetId = GetOrCreateImageAssetFromPath(vrayBitmapPath, vrayBitmap);
                    if (!string.IsNullOrWhiteSpace(vrayImageAssetId))
                    {
                        materialSnapshot.TextureSlots.Add(new MaxSceneTextureSlotSnapshotData
                        {
                            Slot = slotKind.Value,
                            ImageAssetId = vrayImageAssetId
                        });
                        assignedSlots.Add(slotKind.Value);
                        continue;
                    }
                }

                if (texmap is not null)
                    RecordUnmappedPluginClass("texmap", texmap.ClassName(false), material.Name);
                continue;
            }

            if ((bitmap is null || string.IsNullOrWhiteSpace(bitmap.MapName)) && texmap is not null)
            {
                // A dark opacity bake would hide the object outright — require a bright-enough map.
                var minimumMeanLuminance = slotKind.Value == DccTextureSlotKind.Opacity ? 0.2d : 0d;
                var bakedAssetId = TryBakeTexmapToImageAsset(texmap, $"{material.Name}_{slotKind.Value}", 512, 512, minimumMeanLuminance);
                if (!string.IsNullOrWhiteSpace(bakedAssetId))
                {
                    materialSnapshot.TextureSlots.Add(new MaxSceneTextureSlotSnapshotData
                    {
                        Slot = slotKind.Value,
                        ImageAssetId = bakedAssetId
                    });
                    assignedSlots.Add(slotKind.Value);
                }

                continue;
            }

            if (bitmap is null || string.IsNullOrWhiteSpace(bitmap.MapName))
                continue;

            var imageAssetId = GetOrCreateImageAsset(bitmap);
            if (string.IsNullOrWhiteSpace(imageAssetId))
                continue;

            var (uvScaleX, uvScaleY, uvOffsetX, uvOffsetY) = ReadBitmapUvTiling(bitmap);
            materialSnapshot.TextureSlots.Add(new MaxSceneTextureSlotSnapshotData
            {
                Slot = slotKind.Value,
                ImageAssetId = imageAssetId,
                UvScaleX = uvScaleX,
                UvScaleY = uvScaleY,
                UvOffsetX = uvOffsetX,
                UvOffsetY = uvOffsetY
            });
            assignedSlots.Add(slotKind.Value);

            if (slotKind.Value == DccTextureSlotKind.Displacement)
                materialSnapshot.DisplacementScale = ReadDisplacementScale(material, m_coreInterface.Time, i);

            // A Max BUMP map routed to the normal slot must keep the authored bump amount: applying
            // a grey height map as a full-strength normal map carves fake canyons into flat wood
            // (hardwood's beech planks rendered as black marble). Physical Material's amount lives
            // in 'bump_map_amt' (default 0.3); when unreadable, 0.3 is the sane approximation.
            if (slotKind.Value == DccTextureSlotKind.Normal
                && material.GetSubTexmapSlotName(i, false)?.Contains("bump", StringComparison.OrdinalIgnoreCase) == true)
            {
                var bumpAmount = TryReadParamBlockFloat(material, m_coreInterface.Time, "bump_map_amt", "bump_amount", "bump_amt");
                materialSnapshot.NormalStrength = Math.Clamp(bumpAmount ?? 0.3d, 0d, 2d);
            }
        }

        // Fallback: a material with a bitmap but no recognised slot name still gets its base
        // colour. Walked per slot rather than tree-first: environment/reflection/refraction
        // slots hold what the material REFLECTS, never its albedo — C03's chrome carries the
        // mountain photo as a per-material Environment map, and the blind tree-walk painted
        // the photo onto the ball as its base colour.
        if (!assignedSlots.Contains(DccTextureSlotKind.BaseColor))
        {
            for (var i = 0; i < material.NumSubTexmaps; i++)
            {
                var slotName = material.GetSubTexmapSlotName(i, false);
                if (slotName?.Contains("environment", StringComparison.OrdinalIgnoreCase) == true
                    || slotName?.Contains("reflect", StringComparison.OrdinalIgnoreCase) == true
                    || slotName?.Contains("refract", StringComparison.OrdinalIgnoreCase) == true)
                    continue;

                var texmap = material.GetSubTexmap(i);
                if (texmap is null)
                    continue;

                var fallbackBitmap = texmap as IBitmapTex
                                     ?? (texmap is IISubMap subMap ? FindFirstBitmapTexture(subMap) : null);
                if (fallbackBitmap is null || string.IsNullOrWhiteSpace(fallbackBitmap.MapName))
                    continue;

                var imageAssetId = GetOrCreateImageAsset(fallbackBitmap);
                if (string.IsNullOrWhiteSpace(imageAssetId))
                    continue;

                var (uvScaleX, uvScaleY, uvOffsetX, uvOffsetY) = ReadBitmapUvTiling(fallbackBitmap);
                materialSnapshot.TextureSlots.Add(new MaxSceneTextureSlotSnapshotData
                {
                    Slot = DccTextureSlotKind.BaseColor,
                    ImageAssetId = imageAssetId,
                    UvScaleX = uvScaleX,
                    UvScaleY = uvScaleY,
                    UvOffsetX = uvOffsetX,
                    UvOffsetY = uvOffsetY
                });
                break;
            }
        }
    }

    private static DccTextureSlotKind? ClassifyTextureSlot(string? slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
            return null;

        var name = slotName.ToLowerInvariant();

        if (name.Contains("base color") || name.Contains("base colour") || name.Contains("diffuse") || name.Contains("albedo"))
            return DccTextureSlotKind.BaseColor;

        // A Max "Bump" slot carries a grayscale HEIGHT map (the generator perturbs normals from
        // heights); only slots explicitly named "normal" hold true normal-vector maps. Feeding a
        // height map into a normal-map node carves black craters (hardwood's paint-swatch board).
        if (name.Contains("normal"))
            return DccTextureSlotKind.Normal;

        if (name.Contains("bump"))
            return DccTextureSlotKind.Bump;

        if (name.Contains("roughness"))
            return DccTextureSlotKind.Roughness;

        if (name.Contains("metal"))
            return DccTextureSlotKind.Metallic;

        if (name.Contains("opacity") || name.Contains("transparency") || name.Contains("cutout"))
            return DccTextureSlotKind.Opacity;

        // Checked after "bump" so a plain bump map still routes to the normal slot; only a slot named
        // explicitly for displacement drives real geometry displacement.
        if (name.Contains("displacement") || name.Contains("displace"))
            return DccTextureSlotKind.Displacement;

        return null;
    }

    private IPoint3 ResolveVertexNormal(IMesh mesh, IFace face, int vertexIndex, IPoint3 faceNormal)
    {
        // 3ds Max stores per-vertex render normals split by smoothing group. A face with no
        // smoothing group (0) is hard → keep the face normal. A vertex that resolves to a single
        // render normal is fully smooth → use it. A vertex split across smoothing groups keeps
        // one render normal PER group in the Ern array — pick the one whose mask matches this
        // face, so a Tube's side stays smooth against its hard cap rim. (The old face-normal
        // fallback faceted every rim-adjacent vertex — on a two-ring primitive that is the
        // whole wall: A08's cups rendered as vertical bands.)
        try
        {
            var smoothingGroup = face.SmGroup;
            if (smoothingGroup == 0)
                return faceNormal;

            var renderVertex = mesh.GetRVertPtr(vertexIndex);
            if (renderVertex == null)
                return faceNormal;

            var normalCount = (int)(renderVertex.RFlags & 0xFFFF);
            if (normalCount == 1)
                return renderVertex.Rn.Normal;

            // The facade wraps the native RNormal* array as a single IRNormal (the first entry),
            // so walk it via the native pointer. Native layout (maxsdk mesh.h): Point3 normal
            // (3 × float), DWORD smGroup, DWORD mtlIndex → 20-byte stride, mask at offset 12.
            var firstEntry = renderVertex.Ern;
            if (firstEntry == null)
                return faceNormal;

            const int stride = 20;
            const int maskOffset = 12;
            var basePointer = firstEntry.NativePointer;
            for (var i = 0; i < normalCount; i++)
            {
                var entry = basePointer + i * stride;
                var mask = unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(entry, maskOffset));
                if ((mask & smoothingGroup) == 0)
                    continue;

                var x = BitConverter.Int32BitsToSingle(System.Runtime.InteropServices.Marshal.ReadInt32(entry, 0));
                var y = BitConverter.Int32BitsToSingle(System.Runtime.InteropServices.Marshal.ReadInt32(entry, 4));
                var z = BitConverter.Int32BitsToSingle(System.Runtime.InteropServices.Marshal.ReadInt32(entry, 8));
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)
                    || (x == 0f && y == 0f && z == 0f))
                    return faceNormal;

                return m_global.Point3.Create(x, y, z);
            }

            return faceNormal;
        }
        catch
        {
            return faceNormal;
        }
    }

    private MaxMaterialBindingMap GetMaterialBindingMap(IMtl? material)
    {
        if (material is null)
            return new MaxMaterialBindingMap();

        if (material.NumSubMtls <= 0)
        {
            var materialId = TryExtractMaterialBinding(material);
            return string.IsNullOrWhiteSpace(materialId)
                ? new MaxMaterialBindingMap()
                : new MaxMaterialBindingMap
                {
                    MaterialIds = [materialId],
                    CompactMaterialIndexByRawIndex =
                    {
                        [0] = 0,
                        [1] = 0
                    }
                };
        }

        var materialBindingMap = new MaxMaterialBindingMap { RawSubMaterialCount = material.NumSubMtls };

        for (var i = 0; i < material.NumSubMtls; i++)
        {
            var subMaterial = material.GetSubMtl(i);
            if (subMaterial is null)
                continue;

            var materialId = TryExtractMaterialBinding(subMaterial);
            if (string.IsNullOrWhiteSpace(materialId))
                continue;

            var compactMaterialIndex = materialBindingMap.MaterialIds.Count;
            materialBindingMap.MaterialIds.Add(materialId);
            // Raw sub index only — the old extra [i+1] alias collapsed adjacent sub-materials
            // (a 2-sub soccer ball rendered all-white because raw ID 1 resolved to sub 0).
            materialBindingMap.CompactMaterialIndexByRawIndex.TryAdd(i, compactMaterialIndex);
        }

        if (materialBindingMap.MaterialIds.Count > 0)
            return materialBindingMap;

        var fallbackMaterialId = TryExtractMaterialBinding(material);
        return string.IsNullOrWhiteSpace(fallbackMaterialId)
            ? new MaxMaterialBindingMap()
            : new MaxMaterialBindingMap
            {
                MaterialIds = [fallbackMaterialId],
                CompactMaterialIndexByRawIndex =
                {
                    [0] = 0,
                    [1] = 0
                }
            };
    }

    private MaxSceneCameraSnapshotData ExtractCamera(IINode node, ICameraObject cameraObject, string cameraId)
    {
        var time = m_coreInterface.Time;

        // Max only applies clip planes when the camera's "Clip Manually" flag is on; with the flag
        // off GetClipDist returns stale junk (Ape's Camera01 stores near=1e-6, far=1 — everything
        // beyond one unit clipped to an empty frame). Max renders UNCLIPPED in that case, so emit a
        // 0/0 sentinel: the mapper derives planes that cover the scene from its actual bounds (a
        // fixed far=1000 clipped Butterfly's tree at ~2100 units to an empty sky).
        var manualClip = IsManualClipEnabled(cameraObject);

        var snapshot = new MaxSceneCameraSnapshotData
        {
            Id = cameraId,
            Name = node.Name,
            // Max GetFOV is the HORIZONTAL field of view; the neutral model stores vertical.
            VerticalFovDegrees = HorizontalToVerticalFovDegrees(RadiansToDegrees(cameraObject.GetFOV(time))),
            NearClip = manualClip ? ResolveCameraClipDistance(cameraObject, time, 0, 0.1d) : 0d,
            FarClip = manualClip ? ResolveCameraClipDistance(cameraObject, time, 1, 1000d) : 0d,
            IsPerspective = !cameraObject.IsOrtho
        };

        ReadCameraDepthOfField(cameraObject, time, snapshot);
        SampleCameraPropertyKeyframes(cameraObject, snapshot, manualClip);
        return snapshot;
    }

    private void ReadCameraDepthOfField(ICameraObject cameraObject, int time, MaxSceneCameraSnapshotData snapshot)
    {
        // 3ds Max exposes DOF through the multi-pass camera effect. When it is enabled and the
        // camera has a target distance, treat that as the focus distance; read the effect's f-stop
        // from its parameter block if present, otherwise leave the default. Any read failure simply
        // leaves DOF disabled rather than failing the export.
        try
        {
            var enabled = cameraObject.GetMultiPassEffectEnabled(time, m_global.Interval.Create());
            var focusDistance = cameraObject.GetTDist(time);
            if (!enabled || focusDistance <= 0d)
                return;

            snapshot.EnableDepthOfField = true;
            snapshot.FocusDistance = focusDistance;

            var fStop = ReadCameraFStop(cameraObject, time);
            if (fStop is > 0d)
                snapshot.FStop = fStop.Value;
        }
        catch
        {
            // Leave DOF disabled on failure.
        }
    }

    private static double? ReadCameraFStop(ICameraObject cameraObject, int time)
    {
        try
        {
            var effect = cameraObject.IMultiPassCameraEffect;
            if (effect is null)
                return null;

            for (var blockIndex = 0; blockIndex < effect.NumParamBlocks; blockIndex++)
            {
                if (effect.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is not null && (name.Contains("fstop") || name.Contains("f_stop") || name.Contains("fnumber")))
                    {
                        var value = block.GetFloat(def.Id, time, 0);
                        if (value > 0d)
                            return value;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private bool ReadDecayTypeIsNone(ILightObject lightObject)
    {
        // The runtime wrapper is the base LightObject: GenLight's DecayType is unreachable and
        // ClassName degrades to a generic "Light", so classify by CLASS ID — standard Max lights
        // (omni 0x1011, fspot 0x1012, tspot 0x1013, dir 0x1014, tdir 0x1015) default to decay
        // "None".
        try
        {
            var classIdPartA = lightObject.ClassID?.PartA ?? 0;
            if (classIdPartA is < 0x1011 or > 0x1015)
                return false;

            // Read the authored decay kind via the anim handle (1 = None, 2 = Inverse,
            // 3 = Inverse Square). Attenuation RANGES do not change the model: a decay-None
            // light is CONSTANT until its far-attenuation window — the ape stands at 850 units
            // under spots whose fade only begins at 2486, yet the old "has attenuation → go
            // physical" fallback pushed all 44 of them into inverse-square and starved the
            // whole character. The fade window itself stays unmodelled (subjects inside the
            // window render slightly brighter than native — a far smaller error than a wrong
            // falloff curve).
            var decayKind = TryEvaluateMaterialScriptDouble(lightObject, "attenDecay");
            if (decayKind is not null)
                return Math.Round(decayKind.Value) == 1d;

            // Script read failed — keep the conservative legacy heuristic.
            var useAttenProperty = lightObject.GetType().GetProperty("UseAtten");
            var usesAttenuation = useAttenProperty?.GetValue(lightObject) is bool useAtten && useAtten;
            return !usesAttenuation;
        }
        catch
        {
            return false;
        }
    }

    private MaxSceneLightSnapshotData ExtractLight(IINode node, ILightObject lightObject, string lightId)
    {
        var time = m_coreInterface.Time;

        // Foreign renderer light families (VRayLight, VRaySun, CoronaLight, …): the typed
        // GenLight getters and the area-light param walk are unsafe on them (same hang class
        // as the material param blocks). Until a family has a dedicated reader, export a
        // neutral constant point light so the scene is lit rather than black, and record the
        // class for diagnostics.
        var lightClassName = lightObject.ClassName(false);
        if (IsForeignPluginClass(lightClassName))
        {
            RecordUnmappedPluginClass("light", lightClassName, node.Name);
            return new MaxSceneLightSnapshotData
            {
                Id = lightId,
                Name = node.Name,
                Kind = DccLightKind.Point,
                Color = new MaxSceneColorSnapshotData { R = 1d, G = 1d, B = 1d, A = 1d },
                Intensity = 1d,
                Range = 0.01d,
                NoDecay = true,
                CastShadows = true
            };
        }

        var color = lightObject.GetRGBColor(time);
        var hotspotDegrees = RadiansToDegrees(lightObject.GetHotspot(time));
        // Max spots have a hard HOTSPOT cone inside a soft FALLOFF cone; Blender's spot_size is
        // the OUTER cone with spot_blend as the soft fraction. Export the falloff as the cone and
        // the relative difference as the blend (TeaPotBounce's pool of light fades at the edges).
        var falloffDegrees = RadiansToDegrees(lightObject.GetFallsize(time));
        var spotConeDegrees = Math.Max(hotspotDegrees, falloffDegrees);
        var spotBlend = spotConeDegrees > 0.1d ? Math.Clamp(1d - hotspotDegrees / spotConeDegrees, 0d, 1d) : 0d;

        var isArea = TryResolveAreaLight(lightObject, time, out var areaWidth, out var areaHeight);
        var kind = isArea ? DccLightKind.Area : ResolveLightKind(node, lightObject, hotspotDegrees);

        var snapshot = new MaxSceneLightSnapshotData
        {
            Id = lightId,
            Name = node.Name,
            Kind = kind,
            Color = new MaxSceneColorSnapshotData { R = color.X, G = color.Y, B = color.Z, A = 1d },
            Intensity = lightObject.GetIntensity(time),
            // Photometric (ILightscapeLight) lights report intensity in physical units (candela/lux),
            // orders of magnitude above a standard light's ~1 multiplier. The mapper normalizes these
            // so the power calibration (which assumes a ~1 multiplier) does not blow out the render.
            IsPhotometric = lightObject is ILightscapeLight,
            Range = Math.Max(lightObject.GetTDist(time), 0.01d),
            SpotAngleDegrees = ResolveSpotAngleDegrees(kind, spotConeDegrees),
            SpotBlend = kind == DccLightKind.Spot ? spotBlend : 0d,
            // Max standard lights default to decay "None" (constant illumination at any distance);
            // photometric lights are physically inverse-square and keep the physical model.
            NoDecay = kind is DccLightKind.Point or DccLightKind.Spot
                      && lightObject is not ILightscapeLight
                      && ReadDecayTypeIsNone(lightObject),
            CastShadows = ReadCastShadows(lightObject),
            AreaWidth = isArea ? areaWidth : 1d,
            AreaHeight = isArea ? areaHeight : 1d
        };

        SampleLightPropertyKeyframes(lightObject, snapshot);
        return snapshot;
    }

    private static bool TryResolveAreaLight(ILightObject lightObject, int time, out double width, out double height)
    {
        // Photometric lights (ILightscapeLight) carry an explicit area shape. A rectangle/area light
        // has a real width and length; disc/sphere/cylinder lights expose a radius (mapped to a
        // square area). Point/linear photometric lights have neither and fall through to the
        // point/spot heuristic. Non-photometric lights are never area lights.
        width = 1d;
        height = 1d;

        try
        {
            if (lightObject is not ILightscapeLight photometric)
                return false;

            var w = photometric.GetWidth(time);
            var l = photometric.GetLength(time);
            if (w > 0.001d && l > 0.001d)
            {
                width = w;
                height = l;
                return true;
            }

            var radius = photometric.GetRadius(time);
            if (radius > 0.001d)
            {
                width = radius * 2d;
                height = radius * 2d;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadCastShadows(ILightObject lightObject)
    {
        try
        {
            return lightObject.Shadow != 0;
        }
        catch
        {
            return true;
        }
    }

    // The generator assigns the STATIC camera/light rotation as `rotation @ RotX(-90°)` (Blender
    // cameras look down local -Z, Max cameras/lights too, but the neutral capture convention is
    // local -Y forward). Composing RotX(+90°) here makes that pair cancel exactly, so the authored
    // orientation — including roll — survives for free, target and parented cameras and directed
    // lights. Hamilton product q ⊗ (s,0,0,s) with s = sin45° = cos45°.
    //
    // Animation keyframes are deliberately NOT composed: the generator's animation path assigns
    // keyframed rotations via plain set_transform (no RotX(-90) correction), and since Max and
    // Blender cameras share the -Z look axis the true quaternion is already correct there.
    // Composing keyframes pitched every animated camera 90° off (troll/dragon rendered the sky).
    private static void ComposeCameraLightAxisCorrection(
        MaxSceneTransformSnapshotData localTransform,
        List<MaxSceneTransformKeyframeSnapshotData> transformKeyframes)
    {
        _ = transformKeyframes;
        localTransform.Rotation = ComposeRotXPlus90(localTransform.Rotation);
    }

    private static MaxSceneQuaternionSnapshotData ComposeRotXPlus90(MaxSceneQuaternionSnapshotData q)
    {
        const double s = 0.70710678118654752d;
        return new MaxSceneQuaternionSnapshotData
        {
            X = (q.W + q.X) * s,
            Y = (q.Y + q.Z) * s,
            Z = (q.Z - q.Y) * s,
            W = (q.W - q.X) * s
        };
    }

    // Detects whether the production renderer is the Default Scanline renderer (the only stock
    // renderer that culls backfaces). Resolved reflectively — the renderer accessor varies across
    // managed SDK versions — and cached; any failure means "not scanline" (double-sided, safe).
    private bool IsScanlineRenderer()
    {
        if (m_isScanlineRenderer.HasValue)
            return m_isScanlineRenderer.Value;

        try
        {
            var renderer = m_coreInterface.GetCurrentRenderer(false);
            var className = renderer?.ClassName(false);
            m_isScanlineRenderer = className?.Contains("scanline", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            m_isScanlineRenderer = false;
        }

        return m_isScanlineRenderer.Value;
    }

    private static bool IsTwoSidedMaterial(IMtl material)
    {
        try
        {
            if (material.GetType().GetProperty("TwoSided", BindingFlags.Instance | BindingFlags.Public)?.GetValue(material) is bool viaProperty)
                return viaProperty;

            if (material.GetType().GetMethod("GetTwoSided", Type.EmptyTypes)?.Invoke(material, null) is bool viaMethod)
                return viaMethod;
        }
        catch
        {
        }

        return false;
    }

    private bool IsLightActive(ILightObject lightObject)
    {
        // A light switched off in 3ds Max (the "On" checkbox) contributes nothing and would export a
        // dead source. GetUseLight reads that flag (0 = off). Its managed overload varies (no-arg vs
        // time), so resolve it reflectively and default to active on any failure — an API quirk must
        // never silently drop a real light.
        try
        {
            var method = lightObject.GetType().GetMethod("GetUseLight", [typeof(int)]);
            if (method != null && method.Invoke(lightObject, [m_coreInterface.Time]) is int withTime)
                return withTime != 0;

            method = lightObject.GetType().GetMethod("GetUseLight", Type.EmptyTypes);
            if (method?.Invoke(lightObject, null) is int noArg)
                return noArg != 0;
        }
        catch
        {
        }

        return true;
    }

    private string? TryExtractMaterialBinding(IMtl? material)
    {
        if (material is null)
            return null;

        CollectMaterialInfo(material);

        if (m_materialIdsByReference.TryGetValue(material, out var existingMaterialId))
            return existingMaterialId;

        var materialId = CreateUniquePrefixedId(m_usedMaterialIds, "material", string.IsNullOrWhiteSpace(material.Name) ? "material" : material.Name);
        m_materialIdsByReference[material] = materialId;

        if (!m_exportedMaterials.Add(material))
            return materialId;

        var materialSnapshot = new MaxSceneMaterialSnapshotData
        {
            Id = materialId,
            Name = string.IsNullOrWhiteSpace(material.Name) ? materialId : material.Name
        };

        // V-Ray wrapper materials (Bump/2Sided/Blend/Override) delegate their base look to a
        // sub-material; read appearance and texture slots from the wrapped material so a tire
        // (VRayBumpMtl over VRayMtl) doesn't export as the wrapper's defaults. The wrapper's own
        // layer (bump map, back side, blend coats) is dropped — recorded for diagnostics.
        var effectiveMaterial = ResolveForeignWrapperMaterial(material);
        if (!ReferenceEquals(effectiveMaterial, material))
            RecordUnmappedPluginClass("material-wrapper", material.ClassName(false), material.Name);

        ReadMaterialAppearance(effectiveMaterial, materialSnapshot);
        ReadMaterialTextureSlots(effectiveMaterial, materialSnapshot);

        // Scanline hides backfaces unless the material is flagged 2-Sided; interiors are routinely
        // authored with inward-facing walls the camera looks through from outside. Carry that
        // single-sidedness so the generator can mirror it (Cycles renders double-sided by default,
        // which turns such walls into view blockers). Arnold/ART render double-sided — no cull.
        materialSnapshot.BackfaceCull = IsScanlineRenderer() && !IsTwoSidedMaterial(material);

        m_summary.Materials.Add(materialSnapshot);
        return materialId;
    }

    // Third-party renderer plugin families (V-Ray, Corona, …) must not run through the
    // generic parameter heuristics: walking a VRayMtl's param blocks hangs inside
    // IIParamBlock2.GetColor and never returns (the Automotive sample crashed 3ds Max with a
    // minidump after 20 minutes), and the anim-handle MAXScript reads cost seconds per
    // material. Until a family has a dedicated reader, its materials/lights take a minimal
    // safe path and the class is recorded for diagnostics.
    private static readonly string[] FOREIGN_PLUGIN_CLASS_PREFIXES =
    [
        "VRay", "Corona", "Octane", "Redshift", "Maxwell", "Thea", "fR", "finalRender"
    ];

    private bool IsForeignPluginClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return false;

        foreach (var prefix in FOREIGN_PLUGIN_CLASS_PREFIXES)
        {
            if (className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void RecordUnmappedPluginClass(string kind, string? className, string objectName)
    {
        var key = $"{kind}:{className ?? "unknown"}";
        m_summary.UnmappedPluginClasses.TryGetValue(key, out var count);
        m_summary.UnmappedPluginClasses[key] = count + 1;
    }

    // V-Ray wrapper materials delegate their base look to a sub-material. Unwrap to the first
    // non-null sub-material (slot 0 is the base/front on every V-Ray wrapper), preferring a
    // VRayMtl when one is present, so the wrapped material's dedicated reader can run.
    // Depth-guarded: wrappers nest (Bump over Blend over VRayMtl).
    private static readonly string[] VRAY_WRAPPER_CLASS_NAMES =
    [
        "VRayBumpMtl", "VRay2SidedMtl", "VRayBlendMtl", "VRayOverrideMtl"
    ];

    private IMtl ResolveForeignWrapperMaterial(IMtl material)
    {
        var current = material;
        for (var depth = 0; depth < 4; depth++)
        {
            var className = current.ClassName(false);
            if (Array.IndexOf(VRAY_WRAPPER_CLASS_NAMES, className) < 0)
                return current;

            IMtl? firstSubMaterial = null;
            IMtl? firstVRayMtl = null;
            for (var i = 0; i < current.NumSubMtls; i++)
            {
                var subMaterial = current.GetSubMtl(i);
                if (subMaterial is null)
                    continue;

                firstSubMaterial ??= subMaterial;
                if (subMaterial.ClassName(false) == "VRayMtl")
                {
                    firstVRayMtl = subMaterial;
                    break;
                }
            }

            var next = firstVRayMtl ?? firstSubMaterial;
            if (next is null)
                return current;

            current = next;
        }

        return current;
    }

    // Reads a V-Ray material's mapped properties in ONE batched MAXScript execution per material
    // and applies the Principled mapping. Returns false (caller falls back to the minimal safe
    // read) for classes without a dedicated reader or when the script read fails.
    private bool TryReadVRayMtlAppearance(IMtl material, string? materialClassName, MaxSceneMaterialSnapshotData snapshot)
    {
        try
        {
            switch (materialClassName)
            {
                case "VRayMtl":
                {
                    var handle = m_global.Animatable.GetHandleByAnim(material);
                    var raw = TryEvaluateScriptString(VRayMaterialReader.BuildMaxScript(handle.ToUInt64()));
                    var values = VRayMaterialReader.TryParseScriptResult(raw);
                    if (values is null)
                        return false;

                    VRayMaterialReader.Apply(values, snapshot);
                    return true;
                }

                case "VRayScannedMtl":
                {
                    var handle = m_global.Animatable.GetHandleByAnim(material);
                    var raw = TryEvaluateScriptString(VRayScannedMaterialReader.BuildMaxScript(handle.ToUInt64()));
                    if (!VRayScannedMaterialReader.TryApply(raw, snapshot))
                        return false;

                    // The measured BRDF itself is not reconstructable — keep the class visible in
                    // diagnostics as an approximation even though the export got a plausible look.
                    RecordUnmappedPluginClass("material-approximated", materialClassName, material.Name);
                    return true;
                }

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    // Evaluates a MAXScript expression expected to return a string. Returns null when the
    // evaluation fails or yields a non-string value.
    private string? TryEvaluateScriptString(string script)
    {
        try
        {
            var result = m_global.FPValue.Create();
            if (!m_global.ExecuteMAXScriptScript(script, Autodesk.Max.MAXScript.ScriptSource.NonEmbedded, true, result, false))
                return null;

            return result.S;
        }
        catch
        {
            return null;
        }
    }

    private void ReadMaterialAppearance(IMtl material, MaxSceneMaterialSnapshotData snapshot)
    {
        // Read the standard material parameters 3ds Max exposes on IMtl. Materials that do not
        // support them (or throw) keep the snapshot defaults rather than failing the export.
        try
        {
            var time = m_coreInterface.Time;

            // Foreign renderer families: dedicated script readers where we have one (VRayMtl),
            // otherwise a minimal safe read only — the viewport diffuse swatch survives
            // GetDiffuse; every heuristic beyond that is unsafe (see the gate note).
            var materialClassName = material.ClassName(false);
            if (IsForeignPluginClass(materialClassName))
            {
                if (TryReadVRayMtlAppearance(material, materialClassName, snapshot))
                    return;

                RecordUnmappedPluginClass("material", materialClassName, material.Name);
                var foreignDiffuse = material.GetDiffuse(time, false);
                snapshot.BaseColor = new MaxSceneColorSnapshotData { R = foreignDiffuse.R, G = foreignDiffuse.G, B = foreignDiffuse.B, A = 1d };
                return;
            }

            // Renderer-specific PBR materials (Arnold ai_standard_surface, some Physical variants)
            // do not surface their colour through the legacy GetDiffuse (it returns the class
            // default), so prefer an explicit base-colour parameter when the material carries one.
            var paramBaseColor = TryReadParamBlockColor(material, time, "base_color", "basecolor", "diffuse_color");
            var diffuse = material.GetDiffuse(time, false);
            snapshot.BaseColor = paramBaseColor
                                 ?? new MaxSceneColorSnapshotData { R = diffuse.R, G = diffuse.G, B = diffuse.B, A = 1d };

            // Material-level transparency in 3ds Max is refractive (glass), so map it to the
            // neutral material's transmission rather than alpha — alpha transparency comes from
            // opacity texture maps, handled via texture slots. Opacity stays 1 for transmissive
            // materials so the renderer refracts instead of alpha-blending.
            var transparency = Math.Clamp(material.GetXParency(time, false), 0d, 1d);
            snapshot.Transmission = transparency;
            snapshot.Opacity = 1d;

            // RaytraceMaterial's transparency is a colour-valued FILTER weighting the blend:
            // pixel = diffuse·(1−T) + T⊙background. GetXParency flattens that colour to its
            // mean and loses the per-material difference (A08's three cups all read 0.167
            // while their authored looks range from dark translucent to nearly opaque; A06's
            // near-white filter over a grey diffuse is true glass that reads black against its
            // dark backdrop). Principled has one transmission scalar and one albedo, so carry
            // the dominant filter channel as the transmission weight — the filter COLOUR then
            // tints the refraction via the base colour, which blends the diffuse remainder:
            // Base = diffuse·(1−t) + filter.
            // No class-name gate: the "Transparecy" property (the SDK's own typo) exists only on
            // RaytraceMaterial, so the read itself is the detector — it returns null elsewhere.
            var raytraceTransparencyApplied = false;
            {
                var transparencyColor = TryReadRaytraceTransparencyColor(material);
                if (transparencyColor is not null)
                {
                    var filterStrength = Math.Clamp(Math.Max(transparencyColor.R, Math.Max(transparencyColor.G, transparencyColor.B)), 0d, 1d);
                    snapshot.Transmission = filterStrength;
                    snapshot.BaseColor = new MaxSceneColorSnapshotData
                    {
                        R = Math.Clamp(diffuse.R * (1d - filterStrength) + transparencyColor.R, 0d, 1d),
                        G = Math.Clamp(diffuse.G * (1d - filterStrength) + transparencyColor.G, 0d, 1d),
                        B = Math.Clamp(diffuse.B * (1d - filterStrength) + transparencyColor.B, 0d, 1d),
                        A = 1d
                    };
                    raytraceTransparencyApplied = true;

                    var raytraceIor = TryEvaluateMaterialScriptDouble(material, "Index_of_Refraction");
                    if (raytraceIor is >= 1.0d and <= 3.0d)
                        snapshot.Ior = raytraceIor.Value;
                }
            }

            // 3ds Max glass shaders (e.g. Raytrace) keep their visible tint in a separate
            // transparency/filter channel and leave the diffuse near-black. A black base color on
            // a transmissive material reads as opaque black glass that absorbs all light, so when
            // a material is meaningfully transmissive but has a near-black diffuse, treat it as
            // clear glass (white base) rather than letting it swallow the scene. Skipped when the
            // authored Raytrace transparency colour was read — the tint is exact there.
            if (!raytraceTransparencyApplied
                && snapshot.Transmission > 0.1d
                && Math.Max(diffuse.R, Math.Max(diffuse.G, diffuse.B)) < 0.05d)
                snapshot.BaseColor = new MaxSceneColorSnapshotData { R = 1d, G = 1d, B = 1d, A = 1d };

            // PBR transmission (Arnold/Physical glass) is a parameter, not legacy transparency —
            // hardwood's red ball is ai_standard_surface with transmission 0.6 and a red
            // transmission_color while its base_color is grey. Principled tints transmission via
            // Base Color, so carry the transmission colour there.
            if (snapshot.Transmission <= 0.01d)
            {
                var paramTransmission = TryReadParamBlockFloat(material, time, "transmission");
                if (paramTransmission is > 0.01d)
                {
                    snapshot.Transmission = Math.Clamp(paramTransmission.Value, 0d, 1d);
                    snapshot.Opacity = 1d;
                    var transmissionColor = TryReadParamBlockColor(material, time, "transmission_color");
                    if (transmissionColor is not null)
                        snapshot.BaseColor = transmissionColor;

                    // A dominant subsurface layer sits ON TOP of transmission in layered PBR
                    // shaders (ai_standard_surface): the surface reads as an opaque waxy solid,
                    // not glass — hardwood's red ball (subsurface 1.0, transmission 0.6,
                    // transmission_depth 10) is a solid red sphere in the native render, ours
                    // rendered as translucent glass. Principled has no comparable layering, so
                    // keep the transmission TINT in the base colour and drop the transmissivity.
                    var subsurface = TryReadParamBlockFloat(material, time, "subsurface");
                    if (subsurface is >= 0.5d)
                        snapshot.Transmission = 0d;
                }
            }

            // Transmissive materials carry an authored index of refraction (Raytrace glass names
            // it in the UI; A06's material is literally called "IOR equal 1.6" while we shipped
            // the contract default).
            if (snapshot.Transmission > 0.01d)
            {
                var authoredIor = TryReadParamBlockFloat(material, time, "ior", "trans_ior", "index_of_refraction", "refraction_index", "specular_ior");
                if (authoredIor is >= 1.0d and <= 3.0d)
                    snapshot.Ior = authoredIor.Value;
            }

            // PBR materials expose roughness directly; legacy materials only carry glossiness
            // (0..1, higher = sharper highlight), which maps inversely to Blender roughness.
            var paramRoughness = TryReadParamBlockFloat(material, time, "roughness", "specular_roughness");
            if (paramRoughness is not null)
            {
                snapshot.Roughness = Math.Clamp(paramRoughness.Value, 0d, 1d);

                // Physical Material's "Inv" toggle turns the spinner into GLOSSINESS. Ignoring it
                // mirrors every surface finish in the scene: robby's floor (0.02 inverted = matte
                // 0.98) rendered as a mirror while the copper robot (0.72 inverted = polished
                // 0.28) rendered dull.
                var roughnessInverted = TryReadParamBlockInt(material, time, "roughness_inv", "inv_roughness", "roughness_inversion");
                if (roughnessInverted == 1)
                    snapshot.Roughness = 1d - snapshot.Roughness;
            }
            else
            {
                var shininess = material.GetShininess(time, false);
                snapshot.Roughness = Math.Clamp(1d - shininess, 0d, 1d);

                // Blinn "Specular Level" scales the highlight strength (100% = nominal, can go
                // above): the ape's eye whites are largely a 150% specular blowout in Scanline,
                // and the legacy getter returns it as a fraction. Only meaningful on the legacy
                // (non-PBR) branch — PBR materials express their response via roughness.
                var specularLevel = material.GetShinStr(time, false);
                snapshot.Specular = Math.Clamp(specularLevel, 0d, 2d);
            }

            // Scanline's raytrace transparency is a sharp filter — it never frosts. The legacy
            // glossiness→roughness mapping above would blur the refraction into milk (A06's
            // clear cup rendered frosted), so transmissive raytrace materials keep a polished
            // surface.
            if (raytraceTransparencyApplied && snapshot.Transmission > 0.3d)
                snapshot.Roughness = Math.Min(snapshot.Roughness, 0.05d);

            snapshot.Metallic = ReadMetalness(material, time);

            // Legacy mirror materials (Raytrace "chrome", Standard with a full-strength reflection)
            // carry their look in a reflection amount the PBR mapping otherwise ignores — the A02
            // chrome balls exported as flat black. Fold a strong reflection into Metallic with a
            // polished roughness so they read as mirrors.
            var reflection = TryReadParamBlockFloat(material, time, "reflectionmapamount", "reflection_amount", "reflect_amount", "reflectionamount", "reflectamount");
            if (reflection is null)
            {
                var reflectColor = TryReadParamBlockColor(material, time, "reflect", "reflection", "reflect_color", "reflection_color");
                if (reflectColor is not null)
                    reflection = Math.Max(reflectColor.R, Math.Max(reflectColor.G, reflectColor.B));
            }
            if (reflection is null)
            {
                // RaytraceMaterial exposes its reflectivity through neither a named param block
                // nor the legacy getters — read the authored reflect COLOUR via the anim handle.
                // Its luminance is the honest mirror strength (the old blanket 1.0 chromed even
                // reflect-black materials), and since Scanline ADDS the reflection, a dominant
                // reflect colour is the visible look of the mirror: promote it into the base so
                // C03's white chrome doesn't render as a mirror tinted by its grey diffuse.
                var raytraceReflect = TryReadMaterialScriptColor(material, "reflect");
                if (raytraceReflect is not null)
                {
                    var reflectLuminance = Math.Max(raytraceReflect.R, Math.Max(raytraceReflect.G, raytraceReflect.B));
                    reflection = reflectLuminance;

                    var currentBaseLuminance = Math.Max(snapshot.BaseColor.R, Math.Max(snapshot.BaseColor.G, snapshot.BaseColor.B));
                    if (reflectLuminance > 0.5d
                        && currentBaseLuminance >= 0.1d
                        && reflectLuminance > currentBaseLuminance
                        && snapshot.Transmission <= 0.1d)
                        snapshot.BaseColor = raytraceReflect;
                }
            }
            // Never metallize a transmissive material: Metallic 1 on the Principled BSDF disables
            // transmission outright, so A06's black raytrace glass rendered as smoky rough metal.
            // Its strong reflection is already carried by the glass fresnel.
            if (reflection is not null && snapshot.Transmission <= 0.1d)
            {
                var reflectionFraction = reflection.Value > 1.001d ? reflection.Value / 100d : reflection.Value;
                if (reflectionFraction > 0.5d)
                {
                    snapshot.Metallic = Math.Max(snapshot.Metallic, Math.Clamp(reflectionFraction, 0d, 1d));
                    snapshot.Roughness = Math.Min(snapshot.Roughness, 0.15d);

                    // A metallic surface takes its look from the base colour; Raytrace chrome keeps
                    // its visible TINT in the reflect colour while the diffuse stays near-black
                    // (which would render as a black mirror). Promote the tint into the base.
                    var baseLuminance = Math.Max(snapshot.BaseColor.R, Math.Max(snapshot.BaseColor.G, snapshot.BaseColor.B));
                    if (baseLuminance < 0.1d)
                    {
                        var tint = TryReadParamBlockColor(material, time, "reflect", "reflection", "reflect_color", "reflection_color");
                        if (tint is null || Math.Max(tint.R, Math.Max(tint.G, tint.B)) <= 0.1d)
                        {
                            // Raytrace keeps the mirror tint in the specular channel.
                            var specular = material.GetSpecular(time, false);
                            tint = new MaxSceneColorSnapshotData { R = specular.R, G = specular.G, B = specular.B, A = 1d };
                        }
                        snapshot.BaseColor = Math.Max(tint.R, Math.Max(tint.G, tint.B)) > 0.1d
                            ? tint
                            : new MaxSceneColorSnapshotData { R = 0.9d, G = 0.9d, B = 0.9d, A = 1d };
                    }
                }
            }

            // Physical Material expresses "metal" two ways: the metalness parameter (read above),
            // or a TINTED reflection colour — refl_color defaults to white, and an artist matching
            // it to the base colour is asking for coloured reflections, the defining trait of a
            // metal (robby's "Copper Bot": metalness 0, refl_color = base copper). Reflectivity
            // alone is useless as a signal (its default is 1.0 — blind mapping would chrome half
            // the corpus), so only a chromatic refl_color promotes it into Metallic.
            if (snapshot.Metallic < 0.01d)
            {
                var physicalReflectColor = TryReadParamBlockColor(material, time, "refl_color");
                if (physicalReflectColor is not null && IsChromatic(physicalReflectColor))
                {
                    var reflectivity = TryReadParamBlockFloat(material, time, "reflectivity") ?? 1d;
                    snapshot.Metallic = Math.Clamp(reflectivity, 0d, 1d);
                }
            }

            // Self-illuminated materials (sky domes, glowing signs) render full-bright in Max;
            // carry that as emission so e.g. the dragon's cloud dome is lit from within instead of
            // rendering as a dark unlit interior.
            var selfIllum = Math.Clamp(material.GetSelfIllum(time, false), 0d, 1d);
            if (selfIllum > 0.05d)
            {
                snapshot.EmissionColor = new MaxSceneColorSnapshotData
                {
                    R = snapshot.BaseColor.R,
                    G = snapshot.BaseColor.G,
                    B = snapshot.BaseColor.B,
                    A = 1d
                };
                snapshot.EmissionStrength = selfIllum;
            }
        }
        catch
        {
            // Leave the snapshot defaults in place for material types that do not expose the
            // legacy IMtl getters (e.g. some renderer-specific shaders).
        }
    }

    private static void NormalizeMaterialIndices(MaxSceneMeshSnapshotData meshSnapshot, MaxMaterialBindingMap materialBindingMap)
    {
        if (meshSnapshot.MaterialIndices.Count == 0)
            return;

        // With no resolvable material binding (e.g. the node has no material, or a Multi material whose
        // sub-materials didn't resolve) the raw per-face indices are meaningless and could be anything
        // 3ds Max left on the faces (sparse Multi/Sub-Object ids reach into the hundreds). The mesh then
        // has no materials at all, so ANY per-face index is out of range for the generator — drop them
        // entirely so the mesh renders with a single default material. When a binding exists,
        // NormalizeMaterialIndex clamps each index into range below.
        if (materialBindingMap.MaterialIds.Count == 0)
        {
            meshSnapshot.MaterialIndices.Clear();
            return;
        }

        for (var i = 0; i < meshSnapshot.MaterialIndices.Count; i++)
        {
            meshSnapshot.MaterialIndices[i] = NormalizeMaterialIndex(meshSnapshot.MaterialIndices[i], materialBindingMap);
        }
    }

    private static int NormalizeMaterialIndex(int rawMaterialIndex, MaxMaterialBindingMap materialBindingMap)
    {
        // 3ds Max assigns sub-materials by face MtlID modulo the Multi material's slot count (the
        // SDK's GetFaceMtlIndex is 0-based). Wrap first, then look the slot up in the raw→compact
        // dictionary; a slot whose sub-material did not resolve falls back to the first material.
        if (materialBindingMap.MaterialIds.Count == 0)
            return 0;

        var wrapped = rawMaterialIndex;
        var slotCount = Math.Max(materialBindingMap.RawSubMaterialCount, 1);
        if (wrapped < 0 || wrapped >= slotCount)
            wrapped = ((wrapped % slotCount) + slotCount) % slotCount;

        return materialBindingMap.CompactMaterialIndexByRawIndex.TryGetValue(wrapped, out var compactMaterialIndex)
            ? compactMaterialIndex
            : 0;
    }

    private static string? ResolveMaterialBindingId(MaxSceneMeshSnapshotData meshSnapshot, IReadOnlyList<string> materialBindingIds)
    {
        if (materialBindingIds.Count == 0)
            return null;

        if (materialBindingIds.Count == 1)
            return materialBindingIds[0];

        var distinctMaterialIndices = meshSnapshot.MaterialIndices.Distinct().ToArray();
        if (distinctMaterialIndices.Length != 1)
            return materialBindingIds[0];

        var materialIndex = distinctMaterialIndices[0];
        if (materialIndex < 0 || materialIndex >= materialBindingIds.Count)
            return null;

        return materialBindingIds[materialIndex];
    }

    // Bakes a (procedural) texmap into a PNG under the session bake directory and registers it as
    // an ImageAsset. The attachment uploader resolves the absolute SourcePath, so the baked file
    // travels to the farm exactly like an authored bitmap. Returns null when baking fails — the
    // slot is then simply omitted, matching the previous silent-drop behaviour.
    private string? TryBakeTexmapToImageAsset(ITexmap texmap, string bakeName, ushort width, ushort height, double minimumMeanLuminance = 0d, bool allowNearUniform = false)
    {
        try
        {
            var bakeDirectory = Path.Combine(Path.GetTempPath(), "OutWitRender", "bakes");
            Directory.CreateDirectory(bakeDirectory);
            var fileName = $"{SanitizeId(bakeName)}_{texmap.GetHashCode():x8}.png";
            var bakePath = Path.Combine(bakeDirectory, fileName);

            if (m_bakedImageAssetIdsByPath.TryGetValue(bakePath, out var existingId))
                return existingId;

            if (!File.Exists(bakePath) && !TryRenderTexmapToFile(texmap, bakePath, width, height))
                return null;

            // A near-uniform bake means the map needed 3D/geometry context RenderBitmap cannot give
            // (Mix by vertex position, Falloff…). A flat texture would then OVERRIDE the material's
            // real colour (textured materials export a white base), so drop it and let the material
            // colour stand. Opacity bakes additionally require a bright-enough mean so a mis-baked
            // map never hides the whole object. ENVIRONMENT bakes are exempt (allowNearUniform):
            // there is no colour underneath to protect, and a flat-ish sky beats the black void a
            // dropped environment leaves (A06's procedural Noise background).
            if (!TryAnalyzeImage(bakePath, out var isNearUniform, out var meanLuminance)
                || (isNearUniform && !allowNearUniform)
                || meanLuminance < minimumMeanLuminance)
            {
                try { File.Delete(bakePath); } catch { }
                m_bakedImageAssetIdsByPath[bakePath] = string.Empty;
                return null;
            }

            var imageAssetId = CreateUniquePrefixedId(m_usedImageAssetIds, "image", Path.GetFileNameWithoutExtension(fileName));
            m_bakedImageAssetIdsByPath[bakePath] = imageAssetId;
            m_summary.ImageAssets.Add(new MaxSceneImageAssetSnapshotData
            {
                Id = imageAssetId,
                Name = Path.GetFileNameWithoutExtension(fileName),
                SourcePath = bakePath,
                RelativePath = $"textures/{fileName}",
                AssetKind = "ImageAsset"
            });

            return imageAssetId;
        }
        catch
        {
            return null;
        }
    }

    // Node wirecolor via the typed IINode property; null on any facade hiccup so the mapper's
    // default-white path stays intact.
    private static MaxSceneColorSnapshotData? TryReadWireColor(IINode node)
    {
        try
        {
            var color = node.WireColor;
            return new MaxSceneColorSnapshotData
            {
                R = color.R / 255d,
                G = color.G / 255d,
                B = color.B / 255d,
                A = 1d
            };
        }
        catch
        {
            return null;
        }
    }

    // A colour whose channels meaningfully diverge (tinted, not a grey). Used to tell an authored
    // metal tint from Physical Material's neutral defaults.
    private static bool IsChromatic(MaxSceneColorSnapshotData color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        return max - min > 0.08d;
    }

    // Class name check for vertex-colour maps ("Vertex Color" in stock Max): their data already
    // travels as mesh colour attributes, so baking them is both wrong and lossy.
    private static bool IsVertexColorTexmap(ITexmap texmap)
    {
        try
        {
            var className = texmap.ClassName(false);
            return className?.Contains("vertex", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    // Samples a 32×32 grid of the PNG via WPF imaging (no extra dependencies in a WPF plugin):
    // reports whether all sampled pixels sit within a narrow band per channel, plus the mean
    // luminance. Returns false when the image cannot be decoded (callers then keep the bake).
    private static bool TryAnalyzeImage(string path, out bool isNearUniform, out double meanLuminance)
    {
        isNearUniform = false;
        meanLuminance = 1d;

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();

            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0d);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            byte minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
            var luminanceSum = 0d;
            var sampleCount = 0;
            for (var y = 0; y < height; y += Math.Max(1, height / 32))
            {
                for (var x = 0; x < width; x += Math.Max(1, width / 32))
                {
                    var offset = y * stride + x * 4;
                    var b = pixels[offset];
                    var g = pixels[offset + 1];
                    var r = pixels[offset + 2];
                    luminanceSum += 0.2126d * r + 0.7152d * g + 0.0722d * b;
                    sampleCount++;
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;
                    if (g < minG) minG = g;
                    if (g > maxG) maxG = g;
                    if (b < minB) minB = b;
                    if (b > maxB) maxB = b;
                }
            }

            const int threshold = 10;
            isNearUniform = maxR - minR < threshold && maxG - minG < threshold && maxB - minB < threshold;
            meanLuminance = luminanceSum / Math.Max(sampleCount, 1) / 255d;
            return true;
        }
        catch
        {
            // Cannot judge — keep the bake.
            return true;
        }
    }

    private bool TryRenderTexmapToFile(ITexmap texmap, string filePath, ushort width, ushort height)
    {
        try
        {
            var bitmapInfo = m_global.BitmapInfo.Create();
            bitmapInfo.SetWidth(width);
            bitmapInfo.SetHeight(height);
            bitmapInfo.SetType(6); // BMM_TRUE_32
            bitmapInfo.SetName(filePath);

            var bitmap = m_global.TheManager.Create(bitmapInfo);
            if (bitmap is null)
                return false;

            texmap.RenderBitmap(m_coreInterface.Time, bitmap, 1f, true);

            bitmap.OpenOutput(bitmapInfo);
            bitmap.Write(bitmapInfo, -2000000); // BMM_SINGLEFRAME
            bitmap.Close(bitmapInfo, 1);        // BMM_CLOSE_COMPLETE

            return File.Exists(filePath);
        }
        catch
        {
            return false;
        }
    }

    private string GetOrCreateImageAsset(IBitmapTex bitmapTexture)
    {
        return GetOrCreateImageAssetFromPath(bitmapTexture.MapName, bitmapTexture);
    }

    private string GetOrCreateImageAssetFromPath(string? sourcePath, object textureKey)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        if (m_imageAssetIdsByPath.TryGetValue(sourcePath, out var existingImageAssetId))
            return existingImageAssetId;

        var fileName = Path.GetFileName(sourcePath);
        var imageAssetId = CreateUniquePrefixedId(m_usedImageAssetIds, "image", Path.GetFileNameWithoutExtension(fileName));
        m_imageAssetIdsByPath[sourcePath] = imageAssetId;

        if (m_exportedTextures.Add(textureKey))
        {
            m_summary.ImageAssets.Add(new MaxSceneImageAssetSnapshotData
            {
                Id = imageAssetId,
                Name = string.IsNullOrWhiteSpace(fileName) ? imageAssetId : Path.GetFileNameWithoutExtension(fileName),
                SourcePath = sourcePath,
                RelativePath = string.IsNullOrWhiteSpace(fileName) ? sourcePath : $"textures/{fileName}",
                AssetKind = "ImageAsset"
            });
        }

        return imageAssetId;
    }

    // The object→node correction as a matrix PAIR (objTM, nodeTM⁻¹) applied sequentially per
    // vertex. The out-parameter conventions of IGlobal.MatrixMultiply/Inverse are ambiguous in the
    // managed wrapper (the original single-matrix implementation silently produced an identity and
    // the whole correction was dead); the returning Inverse overload plus two explicit transforms
    // sidestep that entirely. Null when the two TMs coincide (the common pivot-clean case).
    private (IMatrix3 ObjectTm, IMatrix3 InverseNodeTm)? ComputeObjectToNodeCorrection(IINode node, int time)
    {
        try
        {
            var objectTm = node.GetObjTMAfterWSM(time, m_global.Interval.Create());
            var nodeTm = node.GetNodeTM(time, m_global.Interval.Create());
            var inverseNodeTm = m_global.Inverse(nodeTm);

            // Probe: a correction that maps two probe points onto themselves is an identity.
            var probeA = ApplyCorrection(objectTm, inverseNodeTm, 1.234, -2.345, 3.456);
            var probeB = ApplyCorrection(objectTm, inverseNodeTm, -4.2, 0.57, -9.11);
            const double epsilon = 1e-4;
            if (Math.Abs(probeA.X - 1.234) < epsilon && Math.Abs(probeA.Y + 2.345) < epsilon && Math.Abs(probeA.Z - 3.456) < epsilon
                && Math.Abs(probeB.X + 4.2) < epsilon && Math.Abs(probeB.Y - 0.57) < epsilon && Math.Abs(probeB.Z + 9.11) < epsilon)
            {
                return null;
            }

            return (objectTm, inverseNodeTm);
        }
        catch
        {
            return null;
        }
    }

    // v' = (v · objTM) · nodeTM⁻¹ in row-vector convention.
    private static MaxSceneVector3SnapshotData ApplyCorrection(IMatrix3 objectTm, IMatrix3 inverseNodeTm, double x, double y, double z)
    {
        var world = TransformPointRaw(objectTm, x, y, z);
        return TransformPointRaw(inverseNodeTm, world.X, world.Y, world.Z);
    }

    private static MaxSceneVector3SnapshotData TransformPointRaw(IMatrix3 matrix, double x, double y, double z)
    {
        var row0 = matrix.GetRow(0);
        var row1 = matrix.GetRow(1);
        var row2 = matrix.GetRow(2);
        var row3 = matrix.GetRow(3);
        return new MaxSceneVector3SnapshotData
        {
            X = x * row0.X + y * row1.X + z * row2.X + row3.X,
            Y = x * row0.Y + y * row1.Y + z * row2.Y + row3.Y,
            Z = x * row0.Z + y * row1.Z + z * row2.Z + row3.Z
        };
    }

    // Rotation/scale part only through both matrices, renormalized.
    private static MaxSceneVector3SnapshotData ApplyCorrectionToNormal(IMatrix3 objectTm, IMatrix3 inverseNodeTm, IPoint3 normal)
    {
        var a = TransformVectorRaw(objectTm, normal.X, normal.Y, normal.Z);
        var b = TransformVectorRaw(inverseNodeTm, a.X, a.Y, a.Z);
        var length = Math.Sqrt(b.X * b.X + b.Y * b.Y + b.Z * b.Z);
        if (length < 1e-9d)
            return new MaxSceneVector3SnapshotData { X = normal.X, Y = normal.Y, Z = normal.Z };
        return new MaxSceneVector3SnapshotData { X = b.X / length, Y = b.Y / length, Z = b.Z / length };
    }

    private static MaxSceneVector3SnapshotData TransformVectorRaw(IMatrix3 matrix, double x, double y, double z)
    {
        var row0 = matrix.GetRow(0);
        var row1 = matrix.GetRow(1);
        var row2 = matrix.GetRow(2);
        return new MaxSceneVector3SnapshotData
        {
            X = x * row0.X + y * row1.X + z * row2.X,
            Y = x * row0.Y + y * row1.Y + z * row2.Y,
            Z = x * row0.Z + y * row1.Z + z * row2.Z
        };
    }

    private static bool IsIdentityMatrix(IMatrix3 matrix)
    {
        const double epsilon = 1e-5;
        var row0 = matrix.GetRow(0);
        var row1 = matrix.GetRow(1);
        var row2 = matrix.GetRow(2);
        var row3 = matrix.GetRow(3);
        return Math.Abs(row0.X - 1d) < epsilon && Math.Abs(row0.Y) < epsilon && Math.Abs(row0.Z) < epsilon
               && Math.Abs(row1.X) < epsilon && Math.Abs(row1.Y - 1d) < epsilon && Math.Abs(row1.Z) < epsilon
               && Math.Abs(row2.X) < epsilon && Math.Abs(row2.Y) < epsilon && Math.Abs(row2.Z - 1d) < epsilon
               && Math.Abs(row3.X) < epsilon && Math.Abs(row3.Y) < epsilon && Math.Abs(row3.Z) < epsilon;
    }



    private MaxSceneTransformSnapshotData ExtractLocalTransform(IINode node, IINode parentNode)
    {
        return ExtractLocalTransformAtTime(node, parentNode, m_coreInterface.Time);
    }

    private MaxSceneTransformSnapshotData ExtractLocalTransformAtTime(IINode node, IINode parentNode, int time)
    {
        try
        {
            var nodeTm = node.GetNodeTM(time, m_global.Interval.Create());
            var localTm = nodeTm;

            if (!parentNode.IsRootNode)
            {
                var parentTm = parentNode.GetNodeTM(time, m_global.Interval.Create());
                var inverseParentTm = m_global.Matrix3.Create();
                m_global.Inverse(parentTm, inverseParentTm);
                var resultTm = m_global.Matrix3.Create();
                m_global.MatrixMultiply(nodeTm, inverseParentTm, resultTm);
                localTm = resultTm;
            }

            return ConvertMatrixToTransform(localTm);
        }
        catch
        {
            return new MaxSceneTransformSnapshotData();
        }
    }

    private List<MaxSceneTransformKeyframeSnapshotData> SampleTransformKeyframes(IINode node, IINode parentNode)
    {
        var keyframes = new List<MaxSceneTransformKeyframeSnapshotData>();

        var frameStart = m_summary.FrameStart;
        var frameEnd = m_summary.FrameEnd;
        if (frameEnd <= frameStart)
            return keyframes;

        var ticksPerFrame = 4800 / Math.Max(m_summary.FrameRate, 1);

        var samples = new List<MaxSceneTransformKeyframeSnapshotData>();
        for (var frame = frameStart; frame <= frameEnd; frame++)
        {
            samples.Add(new MaxSceneTransformKeyframeSnapshotData
            {
                Frame = frame,
                Transform = ExtractLocalTransformAtTime(node, parentNode, frame * ticksPerFrame)
            });
        }

        // Only emit keyframes when the node actually moves over the range; a static node keeps just
        // its single LocalTransform and produces no animation.
        if (!IsTransformAnimated(samples))
            return keyframes;

        return samples;
    }

    private static bool IsTransformAnimated(List<MaxSceneTransformKeyframeSnapshotData> samples)
    {
        if (samples.Count < 2)
            return false;

        var first = samples[0].Transform;
        for (var i = 1; i < samples.Count; i++)
        {
            var t = samples[i].Transform;
            if (!IsClose(t.Translation, first.Translation)
                || !IsClose(t.Scale, first.Scale)
                || !IsClose(t.Rotation, first.Rotation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsClose(MaxSceneVector3SnapshotData a, MaxSceneVector3SnapshotData b)
    {
        const double epsilon = 1e-4;
        return Math.Abs(a.X - b.X) < epsilon && Math.Abs(a.Y - b.Y) < epsilon && Math.Abs(a.Z - b.Z) < epsilon;
    }

    private static bool IsClose(MaxSceneQuaternionSnapshotData a, MaxSceneQuaternionSnapshotData b)
    {
        const double epsilon = 1e-4;
        return Math.Abs(a.X - b.X) < epsilon && Math.Abs(a.Y - b.Y) < epsilon
            && Math.Abs(a.Z - b.Z) < epsilon && Math.Abs(a.W - b.W) < epsilon;
    }

    private MaxSceneTransformSnapshotData ConvertMatrixToTransform(IMatrix3 matrix)
    {
        var translation = matrix.Trans;
        var row0 = matrix.GetRow(0);
        var row1 = matrix.GetRow(1);
        var row2 = matrix.GetRow(2);
        var scaleX = m_global.Length(row0);
        var scaleY = m_global.Length(row1);
        var scaleZ = m_global.Length(row2);
        var rotationMatrix = m_global.Matrix3.Create(true);

        rotationMatrix.SetRow(0, NormalizeVector(row0, scaleX));
        rotationMatrix.SetRow(1, NormalizeVector(row1, scaleY));
        rotationMatrix.SetRow(2, NormalizeVector(row2, scaleZ));
        rotationMatrix.SetTrans(0, 0f);
        rotationMatrix.SetTrans(1, 0f);
        rotationMatrix.SetTrans(2, 0f);

        var rotation = m_global.Quat.Create(rotationMatrix);

        // Quat.Create returns the quaternion in 3ds Max's row-vector convention — the CONJUGATE of
        // the Hamilton local→world quaternion Blender's rotation_quaternion applies. Store the
        // conjugate so downstream consumers get the true rotation. Proven per-node against Max
        // ground truth (camera aim dot=1.000 only under conjugation; mesh Box63 world bbox matches
        // at 0.1 units conjugated vs 3.7 raw).
        return new MaxSceneTransformSnapshotData
        {
            Translation = new MaxSceneVector3SnapshotData { X = translation.X, Y = translation.Y, Z = translation.Z },
            Rotation = new MaxSceneQuaternionSnapshotData { X = -rotation.X, Y = -rotation.Y, Z = -rotation.Z, W = rotation.W },
            Scale = new MaxSceneVector3SnapshotData
            {
                X = scaleX <= 0d ? 1d : scaleX,
                Y = scaleY <= 0d ? 1d : scaleY,
                Z = scaleZ <= 0d ? 1d : scaleZ
            }
        };
    }

    private IPoint3 NormalizeVector(IPoint3 vector, double length)
    {
        if (length <= 0d)
            return m_global.Point3.Create(0d, 0d, 0d);

        return m_global.Point3.Create(vector.X / length, vector.Y / length, vector.Z / length);
    }

    private MaxSceneVector2SnapshotData ExtractUv(IMesh mesh, int faceIndex, int vertexIndex)
    {
        try
        {
            if (!mesh.MapSupport(1))
                return new MaxSceneVector2SnapshotData();

            var mapFace = mesh.GetType().GetMethod("GetTVFace", BindingFlags.Instance | BindingFlags.Public)?.Invoke(mesh, [faceIndex]) as ITVFace
                          ?? TryGetIndexedValue(mesh.MapFaces(1), faceIndex) as ITVFace;

            if (mapFace is null)
                return new MaxSceneVector2SnapshotData();

            var tvIndex = (int)mapFace.GetTVert(vertexIndex);
            var uv = mesh.GetTVert(tvIndex);
            return new MaxSceneVector2SnapshotData { X = uv.X, Y = uv.Y };
        }
        catch
        {
            return new MaxSceneVector2SnapshotData();
        }
    }

    private static bool MeshSupportsMapChannel(IMesh mesh, int channel)
    {
        try
        {
            return mesh.MapSupport(channel);
        }
        catch
        {
            return false;
        }
    }

    // Channel-agnostic UV read (used for map channel 2+). GetTVert is channel-1-specific, so for
    // other channels we go through MapFaces(channel)/MapVerts(channel). The map-vert element type
    // is not a typed interface in the managed SDK, so X/Y are read reflectively.
    private MaxSceneVector2SnapshotData ExtractUvFromChannel(IMesh mesh, int channel, int faceIndex, int vertexIndex)
    {
        try
        {
            if (!mesh.MapSupport(channel))
                return new MaxSceneVector2SnapshotData();

            if (TryGetIndexedValue(mesh.MapFaces(channel), faceIndex) is not ITVFace mapFace)
                return new MaxSceneVector2SnapshotData();

            var tvIndex = (int)mapFace.GetTVert(vertexIndex);
            var vert = TryGetIndexedValue(mesh.MapVerts(channel), tvIndex);
            if (vert is null)
                return new MaxSceneVector2SnapshotData();

            var vertType = vert.GetType();
            var x = Convert.ToDouble(vertType.GetProperty("X")?.GetValue(vert) ?? 0d);
            var y = Convert.ToDouble(vertType.GetProperty("Y")?.GetValue(vert) ?? 0d);
            return new MaxSceneVector2SnapshotData { X = x, Y = y };
        }
        catch
        {
            return new MaxSceneVector2SnapshotData();
        }
    }

    private static object? TryGetIndexedValue(object? source, int index)
    {
        if (source is null)
            return null;

        if (source is Array array)
            return index >= 0 && index < array.Length ? array.GetValue(index) : null;

        var sourceType = source.GetType();
        var itemProperty = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(me => me.Name == "Item" && me.GetIndexParameters().Length == 1 && me.GetIndexParameters()[0].ParameterType == typeof(int));

        if (itemProperty is not null)
            return itemProperty.GetValue(source, [index]);

        var getValueMethod = sourceType.GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public, [typeof(int)])
                             ?? sourceType.GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Public, [typeof(int)]);

        return getValueMethod?.Invoke(source, [index]);
    }

    // Finds the first VRayBitmap (né VRayHDRI) in the texmap tree. Walking foreign submap
    // trees for CLASS NAMES is safe — only typed paramblock getters and render bakes hang.
    private static ITexmap? FindFirstVRayBitmapTexmap(ITexmap texmap, int depth)
    {
        if (depth > 8)
            return null;

        var className = texmap.ClassName(false);
        if (className is "VRayBitmap" or "VRayHDRI")
            return texmap;

        if (texmap is IISubMap subMap)
        {
            for (var i = 0; i < subMap.NumSubTexmaps; i++)
            {
                var child = subMap.GetSubTexmap(i);
                if (child is null)
                    continue;

                var nested = FindFirstVRayBitmapTexmap(child, depth + 1);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    // VRayBitmap keeps its file in the HDRIMapName property (the class began life as VRayHDRI);
    // read it by anim handle — the typed facade accessors don't apply to a V-Ray class.
    private string? TryReadVRayBitmapFilePath(ITexmap texmap)
    {
        try
        {
            var handle = m_global.Animatable.GetHandleByAnim(texmap);
            var script = $"(local m = getAnimByHandle {handle.ToUInt64()}; local s = (try (m.HDRIMapName as string) catch (\"?\")); if s == \"?\" or s == \"undefined\" do (s = (try (m.fileName as string) catch (\"?\"))); s)";
            var result = TryEvaluateScriptString(script);
            if (string.IsNullOrWhiteSpace(result) || result == "?" || result == "undefined")
                return null;

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static IBitmapTex? FindFirstBitmapTexture(IISubMap subMap)
    {
        for (var i = 0; i < subMap.NumSubTexmaps; i++)
        {
            var texture = subMap.GetSubTexmap(i);

            if (texture is IBitmapTex bitmapTexture && !string.IsNullOrWhiteSpace(bitmapTexture.MapName))
                return bitmapTexture;

            if (texture is IISubMap childSubMap)
            {
                var nestedBitmapTexture = FindFirstBitmapTexture(childSubMap);

                if (nestedBitmapTexture is not null)
                    return nestedBitmapTexture;
            }
        }

        return null;
    }

    private void CollectMaterialInfo(IMtlBase materialBase)
    {
        if (!m_materials.Add(materialBase))
            return;

        if (!string.IsNullOrWhiteSpace(materialBase.Name))
            m_materialNames.Add(materialBase.Name);

        if (materialBase is IMtl material)
        {
            for (var i = 0; i < material.NumSubMtls; i++)
            {
                var subMaterial = material.GetSubMtl(i);

                if (subMaterial is not null)
                    CollectMaterialInfo(subMaterial);
            }
        }

        if (materialBase is IISubMap subMap)
        {
            for (var i = 0; i < subMap.NumSubTexmaps; i++)
            {
                var texture = subMap.GetSubTexmap(i);

                if (texture is null || !m_textures.Add(texture))
                    continue;

                if (!string.IsNullOrWhiteSpace(texture.Name))
                    m_textureNames.Add(texture.Name);

                if (texture is IISubMap textureSubMap)
                    CollectTextureInfo(textureSubMap);
            }
        }
    }

    private void CollectTextureInfo(IISubMap subMap)
    {
        for (var i = 0; i < subMap.NumSubTexmaps; i++)
        {
            var texture = subMap.GetSubTexmap(i);

            if (texture is null || !m_textures.Add(texture))
                continue;

            if (!string.IsNullOrWhiteSpace(texture.Name))
                m_textureNames.Add(texture.Name);

            if (texture is IISubMap childSubMap)
                CollectTextureInfo(childSubMap);
        }
    }

    private static void AddName(List<string> names, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!names.Contains(value, StringComparer.OrdinalIgnoreCase))
            names.Add(value);
    }

    private DccLightKind ResolveLightKind(IINode node, ILightObject lightObject, double hotspotDegrees)
    {
        if (IsSunLike(node, lightObject))
            return DccLightKind.Sun;

        return hotspotDegrees > 0.1d || lightObject.GetFallsize(0) > 0.1d
            ? DccLightKind.Spot
            : DccLightKind.Point;
    }

    private static bool IsSunLike(IINode node, ILightObject lightObject)
    {
        // A real Daylight/Sunlight system (IES Sun, MR/Physical Sun, Sun Positioner) exposes its
        // directional nature through the light object's class name. Match that first, then keep the
        // node-name heuristic as a fallback for lights a user has simply named "Sun".
        try
        {
            var className = lightObject.ClassName(false);
            if (!string.IsNullOrEmpty(className)
                && (className.Contains("sun", StringComparison.OrdinalIgnoreCase)
                    || className.Contains("daylight", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch
        {
            // Fall through to the node-name heuristic when the class name is unavailable.
        }

        return node.Name.Contains("sun", StringComparison.OrdinalIgnoreCase)
               || node.Name.Contains("daylight", StringComparison.OrdinalIgnoreCase);
    }

    private static double ResolveCameraClipDistance(ICameraObject cameraObject, int time, int which, double fallback)
    {
        try
        {
            return cameraObject.GetClipDist(time, which);
        }
        catch
        {
            return fallback;
        }
    }

    // The managed surface for the manual-clip flag varies (GetManualClip() vs a ManualClip
    // property), so resolve it reflectively; default to disabled so stale stored clip planes
    // never black out a render.
    private static bool IsManualClipEnabled(ICameraObject cameraObject)
    {
        try
        {
            var method = cameraObject.GetType().GetMethod("GetManualClip", Type.EmptyTypes);
            if (method?.Invoke(cameraObject, null) is int viaMethod)
                return viaMethod != 0;

            var property = cameraObject.GetType().GetProperty("ManualClip", BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(cameraObject) is int viaProperty)
                return viaProperty != 0;
            if (property?.GetValue(cameraObject) is bool viaBool)
                return viaBool;
        }
        catch
        {
        }

        return false;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180d / Math.PI;
    }

    // vFov = 2·atan(tan(hFov/2) · height/width). Falls back to the horizontal value when the render
    // resolution is not known yet — a wider frame beats a degenerate one.
    private double HorizontalToVerticalFovDegrees(double horizontalFovDegrees)
    {
        var width = (double)m_summary.RenderWidth;
        var height = (double)m_summary.RenderHeight;
        if (width <= 0d || height <= 0d || horizontalFovDegrees <= 0d || horizontalFovDegrees >= 180d)
            return horizontalFovDegrees;

        var halfHorizontalRadians = horizontalFovDegrees * Math.PI / 360d;
        var verticalRadians = 2d * Math.Atan(Math.Tan(halfHorizontalRadians) * height / width);
        return verticalRadians * 180d / Math.PI;
    }

    private static string SanitizeId(string value)
    {
        return string.Concat(value.Trim().ToLowerInvariant().Select(me => char.IsLetterOrDigit(me) ? me : '_')).Trim('_');
    }

    private static double ResolveSpotAngleDegrees(DccLightKind kind, double hotspotDegrees)
    {
        if (kind != DccLightKind.Spot)
            return 45d;

        if (hotspotDegrees <= 0d || hotspotDegrees > 180d)
            return 45d;

        return hotspotDegrees;
    }

    private static bool UsesPerTriangleMaterialBinding(MaxSceneMeshSnapshotData meshSnapshot)
    {
        return meshSnapshot.MaterialIndices.Distinct().Skip(1).Any();
    }

    // Rewrites the compacted per-node material indices as indices into the collected scene-level
    // material list (m_summary.Materials order — the order the mapper and generator preserve).
    private bool TryRemapMaterialIndicesToSceneOrder(MaxSceneMeshSnapshotData meshSnapshot, MaxMaterialBindingMap materialBindingMap)
    {
        var scenePositionByMaterialId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < m_summary.Materials.Count; i++)
            scenePositionByMaterialId[m_summary.Materials[i].Id] = i;

        var scenePositionByCompactIndex = new Dictionary<int, int>();
        for (var compactIndex = 0; compactIndex < materialBindingMap.MaterialIds.Count; compactIndex++)
        {
            if (!scenePositionByMaterialId.TryGetValue(materialBindingMap.MaterialIds[compactIndex], out var scenePosition))
                return false;
            scenePositionByCompactIndex[compactIndex] = scenePosition;
        }

        for (var i = 0; i < meshSnapshot.MaterialIndices.Count; i++)
        {
            if (!scenePositionByCompactIndex.TryGetValue(meshSnapshot.MaterialIndices[i], out var scenePosition))
                return false;
            meshSnapshot.MaterialIndices[i] = scenePosition;
        }

        return true;
    }

    private static string CreateUniquePrefixedId(HashSet<string> usedIds, string prefix, string value)
    {
        var sanitizedValue = SanitizeId(value);

        if (string.IsNullOrWhiteSpace(sanitizedValue))
            sanitizedValue = prefix;

        var baseId = $"{prefix}:{sanitizedValue}";

        if (usedIds.Add(baseId))
            return baseId;

        for (var suffix = 2; ; suffix++)
        {
            var candidateId = $"{baseId}_{suffix}";

            if (usedIds.Add(candidateId))
                return candidateId;
        }
    }

    private void DetectMotionBlur(IINode node)
    {
        // Any renderable node with 3ds Max object/image motion blur enabled flips the scene-level
        // motion-blur flag. The blur KIND (image post-smear vs object shutter blur) is counted
        // once per export in MaxHostApplicationService via MAXScript — the facade's per-node
        // MotBlur accessor is unreliable.
        if (m_summary.MotionBlur)
            return;

        try
        {
            if (node.GetMotBlurOnOff(m_coreInterface.Time))
                m_summary.MotionBlur = true;
        }
        catch
        {
            // Leave motion blur unset on failure — the render runs without motion blur.
        }
    }

    private MaxSceneColorSnapshotData ExtractVertexColor(IMesh mesh, int faceIndex, int vertexIndex)
    {
        // Vertex colours live in map channel 0; each map-vert is a Point3 whose X/Y/Z carry R/G/B. The
        // map-vert element is not a typed interface in the managed SDK, so the components are read
        // reflectively — the same approach ExtractUvFromChannel uses for non-primary UV channels.
        try
        {
            if (!mesh.MapSupport(0))
                return new MaxSceneColorSnapshotData();

            if (TryGetIndexedValue(mesh.MapFaces(0), faceIndex) is not ITVFace mapFace)
                return new MaxSceneColorSnapshotData();

            var colorVertIndex = (int)mapFace.GetTVert(vertexIndex);
            var vert = TryGetIndexedValue(mesh.MapVerts(0), colorVertIndex);
            if (vert is null)
                return new MaxSceneColorSnapshotData();

            var vertType = vert.GetType();
            var r = Convert.ToDouble(vertType.GetProperty("X")?.GetValue(vert) ?? 1d);
            var g = Convert.ToDouble(vertType.GetProperty("Y")?.GetValue(vert) ?? 1d);
            var b = Convert.ToDouble(vertType.GetProperty("Z")?.GetValue(vert) ?? 1d);
            return new MaxSceneColorSnapshotData { R = r, G = g, B = b, A = 1d };
        }
        catch
        {
            return new MaxSceneColorSnapshotData();
        }
    }

    private static double ReadDisplacementScale(IMtl material, int time, int slotIndex)
    {
        // The displacement amount is renderer-specific (Standard/Physical/third-party each name it
        // differently) with no typed managed getter, so look it up by parameter internal name in the
        // material's parameter blocks. Default to the contract default (1.0) when not found.
        try
        {
            for (var blockIndex = 0; blockIndex < material.NumParamBlocks; blockIndex++)
            {
                if (material.GetParamBlock(blockIndex) is not IIParamBlock2 block)
                    continue;

                for (var paramIndex = 0u; paramIndex < block.NumParams; paramIndex++)
                {
                    var def = block.GetParamDefByIndex(paramIndex);
                    var name = def.IntName?.ToLowerInvariant();
                    if (name is null || !name.Contains("displac"))
                        continue;

                    if (name.Contains("amount") || name.Contains("amt") || name.Contains("scale")
                        || name.Contains("strength") || name.Contains("height") || name.Contains("depth"))
                    {
                        var value = block.GetFloat(def.Id, time, 0);
                        if (value > 0d)
                            return value;
                    }
                }
            }
        }
        catch
        {
        }

        // Standard materials keep the displacement amount in the texmap-amount table (indexed by
        // the same slot the map sits in — shader-dependent, so use the classified slot index, NOT
        // a fixed channel id); the fraction converts to scene units via an empirical factor
        // calibrated on the Displacement-MoonRock reference (19% amount -> pronounced spikes).
        try
        {
            var method = material.GetType().GetMethod("GetTexmapAmt", [typeof(int), typeof(int)]);
            if (method?.Invoke(material, [slotIndex, time]) is float amount && amount > 0f && amount < 1f)
                return amount * DISPLACEMENT_AMOUNT_TO_UNITS;
        }
        catch
        {
        }

        return 1d;
    }

    // Scanline's Displacement amount is a percentage where full white at 100% displaces by
    // 100 world units, i.e. amount-fraction × 100 units (MoonRock: 19% ≈ 19 units of relief,
    // matching the native silhouette; the previous empirical 30 was calibrated against a
    // midlevel-0.5 pipeline that also halved the effective height).
    private const double DISPLACEMENT_AMOUNT_TO_UNITS = 100d;

    // Cap on exported render-only subdivision levels: each level quadruples the render-time face
    // count, and beyond 3 the visual gain is nil while the render cost explodes.
    private const int MAX_RENDER_SUBDIVISION_LEVELS = 3;

    private void SampleDeformationFrames(IINode node, MaxSceneMeshSnapshotData meshSnapshot)
    {
        // Bake per-frame object-space vertex positions (approach A). Re-evaluating the node's object at
        // each frame captures the result of any deformation (skin/morph/cloth/sim) as geometry. Object
        // space keeps deformation separate from the node's rigid transform (sampled as transform
        // keyframes). Only emitted when the mesh actually deforms and its topology stays constant over
        // the range — otherwise it cannot be represented as Blender shape keys.
        var frameStart = m_summary.FrameStart;
        var frameEnd = m_summary.FrameEnd;
        if (frameEnd <= frameStart || meshSnapshot.Positions.Count == 0)
            return;

        var ticksPerFrame = 4800 / Math.Max(m_summary.FrameRate, 1);
        var baseCornerCount = meshSnapshot.Positions.Count;
        var frames = new List<MaxSceneMeshDeformationFrameSnapshotData>();
        var anyDeformation = false;

        for (var frame = frameStart; frame <= frameEnd; frame++)
        {
            var positions = SampleMeshCornerPositionsAtTime(node, frame * ticksPerFrame, baseCornerCount);
            if (positions is null)
                return; // topology changed or read failed → skip deformation entirely

            frames.Add(new MaxSceneMeshDeformationFrameSnapshotData { Frame = frame, Positions = positions });

            if (!anyDeformation && CornersDiffer(positions, meshSnapshot.Positions))
                anyDeformation = true;
        }

        if (anyDeformation)
            meshSnapshot.DeformationFrames = frames;
    }

    private List<MaxSceneVector3SnapshotData>? SampleMeshCornerPositionsAtTime(IINode node, int time, int expectedCornerCount)
    {
        try
        {
            var sceneObject = node.EvalWorldState(time, true).Obj;
            if (sceneObject is null || sceneObject.CanConvertToType(m_global.TriObjectClassID) != 1)
                return null;

            if (sceneObject.ConvertToType(time, m_global.TriObjectClassID) is not ITriObject triObject)
                return null;

            var mesh = triObject.Mesh;
            var positions = new List<MaxSceneVector3SnapshotData>(expectedCornerCount);

            // WSMs can be animated, so the object→node correction is re-resolved at this frame.
            var correction = ComputeObjectToNodeCorrection(node, time);

            for (var faceIndex = 0; faceIndex < mesh.NumFaces; faceIndex++)
            {
                var face = mesh.GetFace(faceIndex);
                for (var vertexIndex = 0; vertexIndex < 3; vertexIndex++)
                {
                    var point = mesh.GetVert((int)face.GetVert(vertexIndex));
                    positions.Add(correction == null
                        ? new MaxSceneVector3SnapshotData { X = point.X, Y = point.Y, Z = point.Z }
                        : ApplyCorrection(correction.Value.ObjectTm, correction.Value.InverseNodeTm, point.X, point.Y, point.Z));
                }
            }

            return positions.Count == expectedCornerCount ? positions : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool CornersDiffer(List<MaxSceneVector3SnapshotData> candidate, List<MaxSceneVector3SnapshotData> baseline)
    {
        const double epsilon = 1e-4;
        if (candidate.Count != baseline.Count)
            return true;

        for (var i = 0; i < candidate.Count; i++)
        {
            if (Math.Abs(candidate[i].X - baseline[i].X) > epsilon
                || Math.Abs(candidate[i].Y - baseline[i].Y) > epsilon
                || Math.Abs(candidate[i].Z - baseline[i].Z) > epsilon)
            {
                return true;
            }
        }

        return false;
    }

    private void SampleLightPropertyKeyframes(ILightObject lightObject, MaxSceneLightSnapshotData snapshot)
    {
        var frameStart = m_summary.FrameStart;
        var frameEnd = m_summary.FrameEnd;
        if (frameEnd <= frameStart)
            return;

        var ticksPerFrame = 4800 / Math.Max(m_summary.FrameRate, 1);

        snapshot.IntensityKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => lightObject.GetIntensity(time));
        snapshot.RangeKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => Math.Max(lightObject.GetTDist(time), 0.01d));
        snapshot.ColorKeyframes = SampleColorChannel(frameStart, frameEnd, ticksPerFrame, time =>
        {
            var color = lightObject.GetRGBColor(time);
            return new MaxSceneColorSnapshotData { R = color.X, G = color.Y, B = color.Z, A = 1d };
        });

        if (snapshot.Kind == DccLightKind.Spot)
        {
            snapshot.SpotAngleKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame,
                time => ResolveSpotAngleDegrees(DccLightKind.Spot, Math.Max(RadiansToDegrees(lightObject.GetHotspot(time)), RadiansToDegrees(lightObject.GetFallsize(time)))));
        }
    }

    private void SampleCameraPropertyKeyframes(ICameraObject cameraObject, MaxSceneCameraSnapshotData snapshot, bool manualClip)
    {
        var frameStart = m_summary.FrameStart;
        var frameEnd = m_summary.FrameEnd;
        if (frameEnd <= frameStart)
            return;

        var ticksPerFrame = 4800 / Math.Max(m_summary.FrameRate, 1);

        snapshot.VerticalFovKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => HorizontalToVerticalFovDegrees(RadiansToDegrees(cameraObject.GetFOV(time))));

        // Clip distances only mean anything with "Clip Manually" on — otherwise GetClipDist is
        // stale junk and sampling it would animate the mapper's scene-derived planes away.
        if (!manualClip)
            return;

        snapshot.NearClipKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => ResolveCameraClipDistance(cameraObject, time, 0, 0.1d));
        snapshot.FarClipKeyframes = SampleScalarChannel(frameStart, frameEnd, ticksPerFrame, time => ResolveCameraClipDistance(cameraObject, time, 1, 1000d));
    }

    private static List<MaxSceneScalarKeyframeSnapshotData> SampleScalarChannel(int frameStart, int frameEnd, int ticksPerFrame, Func<int, double> sampler)
    {
        // Sample the channel at every integer frame; emit keyframes only when the value actually varies
        // so static channels add nothing. A read failure on any frame drops the whole channel (no
        // partial/misaligned keyframes) and the static value is used instead.
        try
        {
            var samples = new List<MaxSceneScalarKeyframeSnapshotData>(frameEnd - frameStart + 1);
            for (var frame = frameStart; frame <= frameEnd; frame++)
                samples.Add(new MaxSceneScalarKeyframeSnapshotData { Frame = frame, Value = sampler(frame * ticksPerFrame) });

            return ScalarChannelVaries(samples) ? samples : [];
        }
        catch
        {
            return [];
        }
    }

    private static List<MaxSceneColorKeyframeSnapshotData> SampleColorChannel(int frameStart, int frameEnd, int ticksPerFrame, Func<int, MaxSceneColorSnapshotData> sampler)
    {
        try
        {
            var samples = new List<MaxSceneColorKeyframeSnapshotData>(frameEnd - frameStart + 1);
            for (var frame = frameStart; frame <= frameEnd; frame++)
                samples.Add(new MaxSceneColorKeyframeSnapshotData { Frame = frame, Color = sampler(frame * ticksPerFrame) });

            return ColorChannelVaries(samples) ? samples : [];
        }
        catch
        {
            return [];
        }
    }

    private static bool ScalarChannelVaries(List<MaxSceneScalarKeyframeSnapshotData> samples)
    {
        if (samples.Count < 2)
            return false;

        var first = samples[0].Value;
        return samples.Skip(1).Any(me => Math.Abs(me.Value - first) > 1e-4);
    }

    private static bool ColorChannelVaries(List<MaxSceneColorKeyframeSnapshotData> samples)
    {
        if (samples.Count < 2)
            return false;

        var first = samples[0].Color;
        return samples.Skip(1).Any(me => Math.Abs(me.Color.R - first.R) > 1e-4
                                         || Math.Abs(me.Color.G - first.G) > 1e-4
                                         || Math.Abs(me.Color.B - first.B) > 1e-4);
    }

    #endregion
}
