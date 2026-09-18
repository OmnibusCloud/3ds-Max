using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
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
            TryRemoveEmptyFolder(Path.GetDirectoryName(sourcePath) ?? string.Empty);

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
    /// Delivers a completed render into the folder the artist chose when it was launched (the job's
    /// <c>ResultFolder</c>). Render results used to stay in <c>%TEMP%\OmnibusCloudResults\&lt;job&gt;</c>
    /// for good: the Render dialog had no "Save to" at all, and Settings ▸ Output ▸ Save to — labelled
    /// "Defaults for Render and Export" — never reached it.
    /// </summary>
    /// <remarks>
    /// A still or tiled still becomes <c>&lt;scene&gt;_&lt;frame&gt;.&lt;ext&gt;</c>, a video
    /// <c>&lt;scene&gt;_&lt;start&gt;-&lt;end&gt;.&lt;ext&gt;</c>, and an image sequence a subfolder of that name
    /// holding <c>&lt;scene&gt;_&lt;frame&gt;.&lt;ext&gt;</c> files. Nothing existing is ever overwritten. A job
    /// with no result folder (older records, batch flows) or a result already delivered is left as it is.
    /// </remarks>
    /// <param name="jobState">The completed job; its <c>PrimaryArtifactPath</c> is updated on success.</param>
    /// <returns>The delivery result; unsuccessful when there was nothing to deliver or it could not be moved.</returns>
    public MaxConnectedRenderDownloadResult DeliverRender(MaxConnectedRenderJobState jobState)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        if (string.IsNullOrWhiteSpace(jobState.ResultFolder) || !MaxRenderResultFileNaming.IsInDownloadArea(jobState.PrimaryArtifactPath))
        {
            return new MaxConnectedRenderDownloadResult
            {
                IsSuccess = false,
                StatusText = "Nothing to deliver.",
                DownloadedFilePath = jobState.PrimaryArtifactPath
            };
        }

        return jobState.RenderMode == "RenderFrames"
            ? DeliverSequence(jobState)
            : Deliver(jobState, jobState.ResultFolder, MaxRenderResultFileNaming.DeliveredFileStem(jobState));
    }

    /// <summary>
    /// The first free folder for <paramref name="folderName"/> in <paramref name="parent"/>:
    /// <c>name</c>, then <c>name (2)</c>, <c>name (3)</c>…
    /// </summary>
    /// <param name="parent">The parent folder.</param>
    /// <param name="folderName">The wanted folder name.</param>
    /// <returns>A path that does not exist yet.</returns>
    public static string ResolveAvailableFolder(string parent, string folderName)
    {
        var candidate = Path.Combine(parent, folderName);
        for (var index = 2; Directory.Exists(candidate) || File.Exists(candidate); index++)
            candidate = Path.Combine(parent, $"{folderName} ({index})");

        return candidate;
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
    /// Moves every downloaded frame of a sequence into a new subfolder of the result folder, renaming
    /// <c>frame_0005.png</c> to <c>&lt;scene&gt;_0005.png</c>. The job points at the first frame as soon as
    /// it has moved, so a failure half-way still leaves it pointing at a real file.
    /// </summary>
    private MaxConnectedRenderDownloadResult DeliverSequence(MaxConnectedRenderJobState jobState)
    {
        var notReady = RejectUnready(jobState);
        if (notReady is not null)
            return notReady;

        var sourceFolder = Path.GetDirectoryName(jobState.PrimaryArtifactPath)!;
        var targetFolder = string.Empty;
        var moved = 0;

        try
        {
            Directory.CreateDirectory(jobState.ResultFolder);
            targetFolder = ResolveAvailableFolder(jobState.ResultFolder, MaxRenderResultFileNaming.DeliveredSequenceFolderName(jobState));
            Directory.CreateDirectory(targetFolder);

            var frames = Directory.EnumerateFiles(sourceFolder)
                .Where(me => MaxRenderOutputCatalog.ResultExtensions.Contains(Path.GetExtension(me).ToLowerInvariant()))
                .Select(me => (Source: me, Name: MaxRenderResultFileNaming.DeliveredFrameFileName(Path.GetFileName(me), jobState.ResultName)))
                .Where(me => me.Name is not null)
                .OrderBy(me => me.Name, StringComparer.Ordinal)
                .ToList();

            foreach (var (source, name) in frames)
            {
                var destination = Path.Combine(targetFolder, name!);
                File.Move(source, destination);

                if (moved++ == 0)
                    jobState.PrimaryArtifactPath = destination;
            }

            if (moved == 0)
                throw new InvalidOperationException("no downloaded frames were found");

            TryRemoveEmptyFolder(sourceFolder);

            return new MaxConnectedRenderDownloadResult
            {
                IsSuccess = true,
                StatusText = "Frames saved.",
                DownloadedFilePath = jobState.PrimaryArtifactPath,
                Diagnostics = [CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Saved {moved} frames to '{targetFolder}'.")]
            };
        }
        catch (Exception ex)
        {
            TryRemoveEmptyFolder(targetFolder);

            return new MaxConnectedRenderDownloadResult
            {
                IsSuccess = false,
                StatusText = $"Could not save the frames to '{jobState.ResultFolder}': {ex.Message}",
                DownloadedFilePath = jobState.PrimaryArtifactPath,
                Diagnostics = [CreateDiagnostic(MaxSceneDiagnosticSeverity.Warning, $"Saved {moved} frames before failing ({ex.Message}); the rest are still in '{sourceFolder}'.")]
            };
        }
    }

    /// <summary>
    /// Drops the per-job download folder the move just emptied. Non-recursive on purpose: a folder
    /// that still holds anything (other frames, a Blender backup) is left exactly as it is.
    /// </summary>
    private static void TryRemoveEmptyFolder(string folder)
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
