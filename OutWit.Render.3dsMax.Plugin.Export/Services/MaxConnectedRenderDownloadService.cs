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

        var diagnostics = new List<MaxSceneDiagnosticItem>();
        if (string.IsNullOrWhiteSpace(jobState.PrimaryArtifactPath) || !File.Exists(jobState.PrimaryArtifactPath))
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Primary connected render artifact is missing. Launch render before attempting download."));
            return new MaxConnectedRenderDownloadResult
            {
                IsSuccess = false,
                StatusText = "Connected render download failed. No artifact is available.",
                Diagnostics = diagnostics
            };
        }

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
