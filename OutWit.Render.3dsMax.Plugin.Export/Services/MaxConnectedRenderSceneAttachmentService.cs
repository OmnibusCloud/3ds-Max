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

        var diagnostics = new List<MaxSceneDiagnosticItem>();
        var referencedImageAssetIds = scene.Materials
            .SelectMany(me => me.TextureSlots)
            .Select(me => me.ImageAssetId)
            .Where(me => !string.IsNullOrWhiteSpace(me))
            .ToHashSet(StringComparer.Ordinal);
        var uploadedBlobIdsBySourcePath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var imageAsset in scene.ImageAssets.Where(me => referencedImageAssetIds.Contains(me.Id)))
        {
            if (HasMaterializedAttachment(scene, imageAsset))
                continue;

            if (string.IsNullOrWhiteSpace(imageAsset.SourcePath))
            {
                throw new InvalidOperationException(
                    $"Connected render scene image asset '{imageAsset.Id}' does not have a source path and cannot be uploaded as a scene attachment.");
            }

            var resolvedSourcePath = ResolveSourcePath(imageAsset, sceneFilePath);
            if (string.IsNullOrWhiteSpace(resolvedSourcePath))
            {
                throw new InvalidOperationException(
                    $"Connected render scene image asset '{imageAsset.Id}' source file was not found at '{imageAsset.SourcePath}'.");
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

        return diagnostics;
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

        if (Path.IsPathRooted(imageAsset.SourcePath))
            return null;

        var sceneDirectoryPath = Path.GetDirectoryName(sceneFilePath);
        if (string.IsNullOrWhiteSpace(sceneDirectoryPath))
            return null;

        foreach (var ancestorDirectoryPath in EnumerateAncestorDirectories(sceneDirectoryPath))
        {
            var candidatePath = Path.GetFullPath(Path.Combine(ancestorDirectoryPath, imageAsset.SourcePath));
            if (File.Exists(candidatePath))
                return candidatePath;
        }

        return null;
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
