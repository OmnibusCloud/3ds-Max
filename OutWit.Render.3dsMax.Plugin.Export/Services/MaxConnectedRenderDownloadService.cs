using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Downloads the current connected render artifact for the phased 3ds Max plugin flow.
/// </summary>
public sealed class MaxConnectedRenderDownloadService
{
    #region Functions

    /// <summary>
    /// Copies the current primary connected-render artifact to a dedicated local download folder.
    /// </summary>
    public MaxConnectedRenderDownloadResult Download(MaxConnectedRenderJobState jobState, string downloadRootFolder)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        if (string.IsNullOrWhiteSpace(downloadRootFolder))
            throw new InvalidOperationException("Connected render download folder is required.");

        // Until the job completes, PrimaryArtifactPath still points at the INPUT launch archive
        // — handing that back as "the downloaded artifact" told users their result was ready
        // when it wasn't (and for ExportBlend the file wasn't even a .blend). The placeholder
        // local flow keeps its explicit archive behaviour and its own warning below.
        var notReady = RejectUnready(jobState);
        if (notReady is not null)
            return notReady;

        var diagnostics = new List<MaxSceneDiagnosticItem>();
        var jobFolderName = string.IsNullOrWhiteSpace(jobState.JobId)
            ? $"download-{Guid.NewGuid():N}"
            : SanitizePathPart(jobState.JobId);
        var destinationDirectory = Path.Combine(downloadRootFolder, jobFolderName);
        Directory.CreateDirectory(destinationDirectory);

        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(jobState.PrimaryArtifactPath));
        File.Copy(jobState.PrimaryArtifactPath, destinationPath, true);

        diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Downloaded current connected render artifact to '{destinationPath}'."));

        if (jobState.IsPlaceholderLocalSubmission)
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, "The downloaded file is the local launch archive placeholder until remote OmnibusCloud result download is wired."));
        }

        return new MaxConnectedRenderDownloadResult
        {
            IsSuccess = true,
            StatusText = "Connected render artifact downloaded locally.",
            DownloadedFilePath = destinationPath,
            Diagnostics = diagnostics
        };
    }

    /// <summary>
    /// Moves a completed result into the folder the artist chose, named after the scene. The Export
    /// dialog's "Save to" used to be decorative: the .blend always stayed where the transport downloaded
    /// it (<c>%TEMP%\OmnibusCloudResults\&lt;job&gt;\result.blend</c>) while only the launch package
    /// landed in the chosen folder. An existing file is never overwritten — the new one takes the next
    /// free name, the way Explorer names copies.
    /// </summary>
    /// <param name="jobState">The completed job; its <c>PrimaryArtifactPath</c> is updated on success.</param>
    /// <param name="destinationFolder">The folder the artist chose (created when missing).</param>
    /// <param name="fileStem">The file name without extension, normally the scene name.</param>
    /// <returns>The delivery result; on failure the artifact stays where it was downloaded.</returns>
    public MaxConnectedRenderDownloadResult Deliver(MaxConnectedRenderJobState jobState, string destinationFolder, string fileStem)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new InvalidOperationException("A destination folder is required.");

        var notReady = RejectUnready(jobState);
        if (notReady is not null)
            return notReady;

        var sourcePath = jobState.PrimaryArtifactPath;

        try
        {
            Directory.CreateDirectory(destinationFolder);

            var destinationPath = ResolveAvailablePath(destinationFolder, SanitizeFileStem(fileStem), Path.GetExtension(sourcePath));
            File.Move(sourcePath, destinationPath);
            jobState.PrimaryArtifactPath = destinationPath;
            TryRemoveEmptyFolder(Path.GetDirectoryName(sourcePath));

            return new MaxConnectedRenderDownloadResult
            {
                IsSuccess = true,
                StatusText = "Result saved.",
                DownloadedFilePath = destinationPath,
                Diagnostics = [CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Saved the result to '{destinationPath}'.")]
            };
        }
        catch (Exception ex)
        {
            return new MaxConnectedRenderDownloadResult
            {
                IsSuccess = false,
                StatusText = $"Could not save the result to '{destinationFolder}': {ex.Message}",
                DownloadedFilePath = sourcePath,
                Diagnostics = [CreateDiagnostic(MaxSceneDiagnosticSeverity.Warning, $"Could not save the result to '{destinationFolder}' ({ex.Message}); it is still at '{sourcePath}'.")]
            };
        }
    }

    /// <summary>
    /// The first free path for <paramref name="fileStem"/> in <paramref name="folder"/>:
    /// <c>stem.ext</c>, then <c>stem (2).ext</c>, <c>stem (3).ext</c>…
    /// </summary>
    /// <param name="folder">The target folder.</param>
    /// <param name="fileStem">The file name without extension.</param>
    /// <param name="extension">The extension, including the dot (may be empty).</param>
    /// <returns>A path that does not exist yet.</returns>
    public static string ResolveAvailablePath(string folder, string fileStem, string extension)
    {
        var candidate = Path.Combine(folder, fileStem + extension);
        for (var index = 2; File.Exists(candidate); index++)
            candidate = Path.Combine(folder, $"{fileStem} ({index}){extension}");

        return candidate;
    }

    /// <summary>A file-name-safe stem: invalid characters become '_', and an empty name becomes "scene".</summary>
    /// <param name="fileStem">The raw stem, normally the scene name.</param>
    /// <returns>The sanitized stem.</returns>
    public static string SanitizeFileStem(string? fileStem)
    {
        var stem = SanitizePathPart((fileStem ?? string.Empty).Trim()).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(stem) ? "scene" : stem;
    }

    /// <summary>
    /// Drops the per-job download folder the move just emptied. Non-recursive on purpose: a folder
    /// that still holds anything (other frames, a Blender backup) is left exactly as it is.
    /// </summary>
    private static void TryRemoveEmptyFolder(string? folder)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder, false);
        }
        catch
        {
            // Cosmetic: an empty folder in %TEMP% is harmless.
        }
    }

    private static MaxConnectedRenderDownloadResult? RejectUnready(MaxConnectedRenderJobState jobState)
    {
        if (string.IsNullOrWhiteSpace(jobState.PrimaryArtifactPath) || !File.Exists(jobState.PrimaryArtifactPath))
        {
            return new MaxConnectedRenderDownloadResult
            {
                IsSuccess = false,
                StatusText = "Connected render download failed. No artifact is available.",
                Diagnostics = [CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Primary connected render artifact is missing. Launch render before attempting download.")]
            };
        }

        // Until the job completes, PrimaryArtifactPath still points at the INPUT launch archive.
        if (!jobState.IsPlaceholderLocalSubmission
            && (!jobState.IsCompleted
                || string.Equals(jobState.PrimaryArtifactPath, jobState.PackageArchivePath, StringComparison.OrdinalIgnoreCase)))
        {
            return new MaxConnectedRenderDownloadResult
            {
                IsSuccess = false,
                StatusText = "Connected render result is not ready to download.",
                Diagnostics = [CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "The job has not produced a downloadable result yet — wait for it to complete (refresh retries the result download).")]
            };
        }

        return null;
    }

    private static string SanitizePathPart(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return new string(value.Select(me => invalidCharacters.Contains(me) ? '_' : me).ToArray());
    }

    private static MaxSceneDiagnosticItem CreateDiagnostic(MaxSceneDiagnosticSeverity severity, string message)
    {
        return new MaxSceneDiagnosticItem
        {
            Severity = severity,
            Message = message
        };
    }

    #endregion
}
