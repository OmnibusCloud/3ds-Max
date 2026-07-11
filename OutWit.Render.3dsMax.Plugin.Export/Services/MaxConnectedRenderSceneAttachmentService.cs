using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Uploads local 3ds Max scene image dependencies so Render.BuildBlendFromDccScene can materialize them on OmnibusCloud.
/// </summary>
public sealed class MaxConnectedRenderSceneAttachmentService
{
    #region Constants

    private const string IMAGE_ASSET_KIND = "ImageAsset";

    private const string SCENE_ATTACHMENT_BLOB_PACKAGING_STRATEGY = "SceneAttachmentBlob";

    #endregion

    #region Functions

    /// <summary>
    /// Uploads referenced image assets that are not already represented as blob-backed scene attachments.
    /// </summary>
    public async Task<IReadOnlyList<MaxSceneDiagnosticItem>> UploadImageAssetAttachmentsAsync(
        DccSceneData scene,
        string sceneFilePath,
        Func<string, CancellationToken, Task<Guid>> uploadFileAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(uploadFileAsync);

        UniquifyImageAssetRelativePaths(scene);

        var diagnostics = new List<MaxSceneDiagnosticItem>();
        var referencedImageAssetIds = scene.Materials
            .SelectMany(me => me.TextureSlots)
            .Select(me => me.ImageAssetId)
            .Where(me => !string.IsNullOrWhiteSpace(me))
            .ToHashSet(StringComparer.Ordinal);

        // The world environment (HDRI) image is referenced by DccWorldData, not a material texture slot,
        // so it must be uploaded too — otherwise the generator can't load 'textures/<env>.hdr'.
        if (!string.IsNullOrWhiteSpace(scene.World?.EnvironmentImageId))
            referencedImageAssetIds.Add(scene.World!.EnvironmentImageId);
        var uploadedBlobIdsBySourcePath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var missingImageAssetIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var imageAsset in scene.ImageAssets.Where(me => referencedImageAssetIds.Contains(me.Id)))
        {
            if (HasMaterializedAttachment(scene, imageAsset))
                continue;

            // A missing source file degrades that one texture instead of failing the whole
            // submission — 3ds Max itself renders scenes with missing bitmaps (the V-Ray VSphere
            // sample references its dome HDRI on a Chaos-internal network share). The asset and
            // every reference to it are removed so the payload stays consistent, and the user
            // sees a warning naming the file.
            var resolvedSourcePath = string.IsNullOrWhiteSpace(imageAsset.SourcePath)
                ? null
                : ResolveSourcePath(imageAsset, sceneFilePath);
            if (string.IsNullOrWhiteSpace(resolvedSourcePath))
            {
                missingImageAssetIds.Add(imageAsset.Id);
                diagnostics.Add(new MaxSceneDiagnosticItem
                {
                    Severity = MaxSceneDiagnosticSeverity.Warning,
                    Message = $"Scene image asset '{imageAsset.Id}' source file was not found at '{imageAsset.SourcePath}' — the render proceeds without this texture."
                });
                continue;
            }

            if (!uploadedBlobIdsBySourcePath.TryGetValue(resolvedSourcePath, out var blobId))
            {
                blobId = await uploadFileAsync(resolvedSourcePath, cancellationToken);
                uploadedBlobIdsBySourcePath[resolvedSourcePath] = blobId;
            }

            var relativePath = ResolveRelativePath(imageAsset);
            scene.AttachedFiles.Add(new RenderSceneAttachmentRefData
            {
                Kind = string.IsNullOrWhiteSpace(imageAsset.AssetKind) ? IMAGE_ASSET_KIND : imageAsset.AssetKind,
                BlobId = blobId,
                OriginalPath = resolvedSourcePath,
                RelativePath = relativePath,
                PackagingStrategy = SCENE_ATTACHMENT_BLOB_PACKAGING_STRATEGY
            });
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = $"Uploaded scene image asset '{imageAsset.Id}' as blob-backed attachment '{relativePath}'."
            });
        }

        if (missingImageAssetIds.Count > 0)
            RemoveMissingImageAssetReferences(scene, missingImageAssetIds);

        return diagnostics;
    }

    // Two textures both named 'diffuse.jpg' from different folders used to collapse onto the
    // same 'textures/diffuse.jpg' attachment: the second file was never uploaded (the dedup
    // matched by RelativePath) and both materials rendered with the FIRST texture, silently.
    // Distinct source paths therefore get distinct relative paths — a collision keeps its
    // filename stem and gains a suffix derived from the full source path, deterministic across
    // sessions (unlike object hash codes) so re-exports stay byte-identical.
    private static void UniquifyImageAssetRelativePaths(DccSceneData scene)
    {
        var ownerSourcePathsByRelativePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var imageAsset in scene.ImageAssets)
        {
            if (string.IsNullOrWhiteSpace(imageAsset.RelativePath))
            {
                var fileName = TryGetFileName(imageAsset.SourcePath);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue; // No path to key on — the upload loop degrades it as missing.

                imageAsset.RelativePath = $"textures/{fileName}";
            }

            if (!ownerSourcePathsByRelativePath.TryGetValue(imageAsset.RelativePath, out var ownerSourcePath))
            {
                ownerSourcePathsByRelativePath[imageAsset.RelativePath] = imageAsset.SourcePath;
                continue;
            }

            // The same file referenced twice SHOULD share one attachment.
            if (string.Equals(ownerSourcePath, imageAsset.SourcePath, StringComparison.OrdinalIgnoreCase))
                continue;

            var stem = Path.GetFileNameWithoutExtension(imageAsset.RelativePath);
            var extension = Path.GetExtension(imageAsset.RelativePath);
            var candidate = $"textures/{stem}_{ComputeStablePathSuffix(imageAsset.SourcePath)}{extension}";
            for (var attempt = 2; ownerSourcePathsByRelativePath.ContainsKey(candidate); attempt++)
                candidate = $"textures/{stem}_{ComputeStablePathSuffix(imageAsset.SourcePath)}_{attempt}{extension}";

            imageAsset.RelativePath = candidate;
            ownerSourcePathsByRelativePath[candidate] = imageAsset.SourcePath;
        }
    }

    private static string ComputeStablePathSuffix(string sourcePath)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes((sourcePath ?? string.Empty).ToLowerInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 4)).ToLowerInvariant();
    }

    private static string? TryGetFileName(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path.Replace('\\', '/'));
        }
        catch
        {
            return null;
        }
    }

    private static void RemoveMissingImageAssetReferences(DccSceneData scene, HashSet<string> missingImageAssetIds)
    {
        scene.ImageAssets.RemoveAll(me => missingImageAssetIds.Contains(me.Id));

        foreach (var material in scene.Materials)
            material.TextureSlots.RemoveAll(me => me.ImageAssetId is not null && missingImageAssetIds.Contains(me.ImageAssetId));

        if (scene.World is not null
            && !string.IsNullOrWhiteSpace(scene.World.EnvironmentImageId)
            && missingImageAssetIds.Contains(scene.World.EnvironmentImageId))
            scene.World.EnvironmentImageId = null;

        // Removing a slot can orphan dependent material state the server-side contract rejects
        // ("custom normal strength only when a normal texture slot is present") — reset it to
        // the contract defaults so the degraded scene stays VALID, not just consistent.
        foreach (var material in scene.Materials)
        {
            var hasNormalTextureSlot = material.TextureSlots.Any(me => me.Slot is DccTextureSlotKind.Normal or DccTextureSlotKind.Bump);
            if (!hasNormalTextureSlot && (material.NormalStrength != 1d || material.NormalStrengthKeyframes.Count > 0))
            {
                material.NormalStrength = 1d;
                material.NormalStrengthKeyframes.Clear();
            }

            var hasOpacitySource = material.Opacity != 1d
                                   || material.OpacityKeyframes.Count > 0
                                   || material.TextureSlots.Any(me => me.Slot == DccTextureSlotKind.Opacity);
            if (!hasOpacitySource && material.AlphaMode is DccMaterialAlphaMode.Clip or DccMaterialAlphaMode.Hashed)
            {
                material.AlphaMode = DccMaterialAlphaMode.Blend;
                material.AlphaClipThreshold = 0.5d;
            }
        }
    }

    private static bool HasMaterializedAttachment(DccSceneData scene, DccImageAssetData imageAsset)
    {
        return scene.AttachedFiles.Any(me => string.Equals(me.Kind, IMAGE_ASSET_KIND, StringComparison.Ordinal)
                                             && string.Equals(me.PackagingStrategy, SCENE_ATTACHMENT_BLOB_PACKAGING_STRATEGY, StringComparison.Ordinal)
                                             && ((!string.IsNullOrWhiteSpace(imageAsset.RelativePath)
                                                  && string.Equals(me.RelativePath, imageAsset.RelativePath, StringComparison.Ordinal))
                                                 || (!string.IsNullOrWhiteSpace(imageAsset.SourcePath)
                                                     && string.Equals(me.OriginalPath, imageAsset.SourcePath, StringComparison.OrdinalIgnoreCase))));
    }

    private static string ResolveRelativePath(DccImageAssetData imageAsset)
    {
        if (!string.IsNullOrWhiteSpace(imageAsset.RelativePath))
            return imageAsset.RelativePath;

        var fileName = Path.GetFileName(imageAsset.SourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException(
                $"Connected render scene image asset '{imageAsset.Id}' does not have a usable relative path or source file name.");
        }

        return $"textures/{fileName}";
    }

    private static string? ResolveSourcePath(DccImageAssetData imageAsset, string sceneFilePath)
    {
        if (File.Exists(imageAsset.SourcePath))
            return imageAsset.SourcePath;

        // Drive-relative paths ('\Assets\Asphalt_Diffuse.jpg' — rooted but with no drive, the
        // way V-Ray sample scenes author their references) count as rooted yet still resolve
        // against the scene's folders like any relative path; only a fully qualified path that
        // does not exist is a dead end.
        if (Path.IsPathFullyQualified(imageAsset.SourcePath))
            return null;

        var relativeSourcePath = imageAsset.SourcePath.TrimStart('\\', '/');

        var sceneDirectoryPath = Path.GetDirectoryName(sceneFilePath);
        if (string.IsNullOrWhiteSpace(sceneDirectoryPath))
            return null;

        foreach (var ancestorDirectoryPath in EnumerateAncestorDirectories(sceneDirectoryPath))
        {
            var candidatePath = Path.GetFullPath(Path.Combine(ancestorDirectoryPath, relativeSourcePath));
            if (File.Exists(candidatePath))
                return candidatePath;
        }

        // Library-relative paths (e.g. 'Scenes\Design Visualization\Concrete...jpg' from the 3ds Max
        // content library) don't resolve against scene ancestors — search by FILE NAME under the
        // scene's directory and one level of ancestors instead of failing the whole submission.
        var fileName = Path.GetFileName(imageAsset.SourcePath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            foreach (var searchRootPath in EnumerateAncestorDirectories(sceneDirectoryPath).Take(3))
            {
                try
                {
                    var match = Directory.EnumerateFiles(searchRootPath, fileName, SearchOption.AllDirectories).FirstOrDefault();
                    if (match is not null)
                        return match;
                }
                catch
                {
                    // Unreadable directory — keep walking up.
                }
            }

            // Last resort: the 3ds Max install's own map library ('maps' next to 3dsmax.exe) —
            // stock scenes reference it by paths relative to the content root.
            foreach (var installMapsPath in EnumerateMaxInstallMapDirectories())
            {
                try
                {
                    var match = Directory.EnumerateFiles(installMapsPath, fileName, SearchOption.AllDirectories).FirstOrDefault();
                    if (match is not null)
                        return match;
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateMaxInstallMapDirectories()
    {
        var autodeskRootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk");
        if (!Directory.Exists(autodeskRootPath))
            yield break;

        IEnumerable<string> installations;
        try
        {
            installations = Directory.EnumerateDirectories(autodeskRootPath, "3ds Max *");
        }
        catch
        {
            yield break;
        }

        foreach (var installationPath in installations)
        {
            var mapsPath = Path.Combine(installationPath, "maps");
            if (Directory.Exists(mapsPath))
                yield return mapsPath;
        }
    }

    private static IEnumerable<string> EnumerateAncestorDirectories(string directoryPath)
    {
        var currentDirectoryInfo = new DirectoryInfo(directoryPath);

        while (currentDirectoryInfo is not null)
        {
            yield return currentDirectoryInfo.FullName;
            currentDirectoryInfo = currentDirectoryInfo.Parent;
        }
    }

    #endregion
}
