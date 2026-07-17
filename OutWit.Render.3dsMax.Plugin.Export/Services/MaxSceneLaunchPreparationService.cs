using System.Text.Json;
using System.IO.Compression;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Prepares a local launch package from the current 3ds Max scene for the upcoming OmnibusCloud submission path.
/// </summary>
public sealed class MaxSceneLaunchPreparationService
{
    #region Fields

    private readonly MaxSceneExportService m_sceneExportService;

    #endregion

    #region Constructors

    public MaxSceneLaunchPreparationService(MaxSceneExportService sceneExportService)
    {
        m_sceneExportService = sceneExportService;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Exports the current scene into a local launch package that can later be submitted to OmnibusCloud.
    /// </summary>
    public MaxSceneLaunchPackageResult Prepare(MaxSceneLaunchPackageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OutputFolder))
            throw new InvalidOperationException("Launch package output folder is required.");

        var packageId = $"max-launch-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var packageFolderPath = Path.Combine(request.OutputFolder, packageId);
        Directory.CreateDirectory(packageFolderPath);

        // ONE capture, N serializations: the scene is captured (and validated) once and every
        // artifact serializes from the same DccSceneData — each ExportCurrentScene call used to
        // re-run the full capture, tripling a multi-minute launch on heavy scenes (preflight +
        // Json + MemoryPackGzip).
        var captureOptions = new MaxSceneCaptureOptions { BakeVRayScannedMaterials = request.BakeVRayScannedMaterials };
        var jsonResult = m_sceneExportService.ValidateCurrentScene(captureOptions);

        if (!jsonResult.IsSuccess || jsonResult.Scene is null)
        {
            return new MaxSceneLaunchPackageResult
            {
                IsSuccess = false,
                PackageId = packageId,
                PackageFolderPath = packageFolderPath,
                StatusText = jsonResult.StatusText,
                Diagnostics = [.. jsonResult.Diagnostics]
            };
        }

        jsonResult.OutputPath = m_sceneExportService.ExportScene(jsonResult.Scene, packageFolderPath, MaxSceneExportOutputFormat.Json);
        var memoryPackGzipArtifactPath = m_sceneExportService.ExportScene(jsonResult.Scene, packageFolderPath, MaxSceneExportOutputFormat.MemoryPackGzip);

        var manifest = new MaxSceneLaunchPackageManifest
        {
            PackageId = packageId,
            PreparedUtc = DateTime.UtcNow,
            CloudUrl = request.CloudUrl,
            IdentityUrl = request.IdentityUrl,
            RenderMode = request.RenderMode,
            ResolutionX = request.ResolutionX,
            ResolutionY = request.ResolutionY,
            FrameStart = request.FrameStart,
            FrameEnd = request.FrameEnd,
            Samples = request.Samples,
            UseAllClients = request.UseAllClients,
            SelectedGroupName = request.SelectedGroupName,
            SelectedProjectName = request.SelectedProjectName,
            JsonArtifactPath = jsonResult.OutputPath ?? string.Empty,
            MemoryPackGzipArtifactPath = memoryPackGzipArtifactPath
        };

        var manifestPath = Path.Combine(packageFolderPath, "launch-request.json");
        var packageArchivePath = Path.Combine(request.OutputFolder, $"{packageId}.zip");
        manifest.PackageArchivePath = packageArchivePath;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        if (File.Exists(packageArchivePath))
            File.Delete(packageArchivePath);

        ZipFile.CreateFromDirectory(packageFolderPath, packageArchivePath, CompressionLevel.SmallestSize, false);

        var diagnostics = new List<MaxSceneDiagnosticItem>(jsonResult.Diagnostics)
        {
            new()
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = $"Prepared local launch package '{packageId}'."
            },
            new()
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = $"Saved launch manifest: '{manifestPath}'."
            },
            new()
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = $"Created launch archive: '{packageArchivePath}'."
            }
        };

        return new MaxSceneLaunchPackageResult
        {
            IsSuccess = true,
            PackageId = packageId,
            PackageFolderPath = packageFolderPath,
            ManifestPath = manifestPath,
            PackageArchivePath = packageArchivePath,
            PrimaryArtifactPath = packageArchivePath,
            Scene = jsonResult.Scene,
            SceneFilePath = jsonResult.Summary.SceneFilePath,
            StatusText = "Prepared a local launch package for future OmnibusCloud submission.",
            Diagnostics = diagnostics
        };
    }

    #endregion
}
