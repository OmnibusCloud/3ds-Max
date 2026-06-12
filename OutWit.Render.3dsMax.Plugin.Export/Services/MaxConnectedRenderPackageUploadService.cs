using System.Text.Json;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Uploads prepared connected-render archives to OmnibusCloud through the current plugin-side upload boundary.
/// </summary>
public sealed class MaxConnectedRenderPackageUploadService
{
    #region Fields

    private readonly IMaxConnectedRenderArchiveUploader m_archiveUploader;

    #endregion

    #region Constructors

    public MaxConnectedRenderPackageUploadService(IMaxConnectedRenderArchiveUploader archiveUploader)
    {
        m_archiveUploader = archiveUploader;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Uploads the prepared connected-render archive referenced by the current job state.
    /// </summary>
    public MaxConnectedRenderUploadResult Upload(MaxConnectedRenderJobState jobState, MaxConnectedRenderUploadRequest request)
    {
        ArgumentNullException.ThrowIfNull(jobState);
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<MaxSceneDiagnosticItem>();
        var archivePath = !string.IsNullOrWhiteSpace(request.PackageArchivePath)
            ? request.PackageArchivePath
            : !string.IsNullOrWhiteSpace(jobState.PackageArchivePath)
                ? jobState.PackageArchivePath
                : jobState.PrimaryArtifactPath;

        if (string.IsNullOrWhiteSpace(request.CloudUrl) || string.IsNullOrWhiteSpace(request.IdentityUrl))
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Cloud URL and Identity URL are required before uploading a connected-render package."));
            return CreateFailureResult("Connected render upload failed. Cloud endpoints are missing.", archivePath, diagnostics);
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "API key fallback is required before uploading a connected-render package to OmnibusCloud."));
            return CreateFailureResult("Connected render upload failed. API key is missing.", archivePath, diagnostics);
        }

        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Prepared connected-render archive is missing. Launch render before uploading."));
            return CreateFailureResult("Connected render upload failed. Package archive is missing.", archivePath, diagnostics);
        }

        request.PackageArchivePath = archivePath;
        var uploadedBlobId = m_archiveUploader.UploadArchive(request);
        var uploadReceiptPath = WriteUploadReceipt(jobState, request, archivePath, uploadedBlobId);

        diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Uploaded connected-render archive to OmnibusCloud blob '{uploadedBlobId}'."));
        diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Saved upload receipt: '{uploadReceiptPath}'."));

        return new MaxConnectedRenderUploadResult
        {
            IsSuccess = true,
            StatusText = "Connected render package uploaded to OmnibusCloud.",
            UploadedBlobId = uploadedBlobId,
            PackageArchivePath = archivePath,
            UploadReceiptPath = uploadReceiptPath,
            Diagnostics = diagnostics
        };
    }

    private static string WriteUploadReceipt(MaxConnectedRenderJobState jobState, MaxConnectedRenderUploadRequest request, string archivePath, Guid uploadedBlobId)
    {
        var receiptDirectory = !string.IsNullOrWhiteSpace(jobState.PackageFolderPath)
            ? jobState.PackageFolderPath
            : Path.GetDirectoryName(archivePath) ?? throw new InvalidOperationException("Upload receipt directory could not be resolved.");
        Directory.CreateDirectory(receiptDirectory);

        var receipt = new MaxConnectedRenderUploadReceipt
        {
            UploadedBlobId = uploadedBlobId,
            UploadedUtc = DateTime.UtcNow,
            CloudUrl = request.CloudUrl,
            IdentityUrl = request.IdentityUrl,
            PackageArchivePath = archivePath
        };

        var receiptPath = Path.Combine(receiptDirectory, "upload-receipt.json");
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        return receiptPath;
    }

    private static MaxConnectedRenderUploadResult CreateFailureResult(string statusText, string archivePath, List<MaxSceneDiagnosticItem> diagnostics)
    {
        return new MaxConnectedRenderUploadResult
        {
            IsSuccess = false,
            StatusText = statusText,
            PackageArchivePath = archivePath,
            Diagnostics = diagnostics
        };
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
