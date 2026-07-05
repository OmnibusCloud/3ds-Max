using System.Text.Json;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Local placeholder transport used until real OmnibusCloud submission and polling are wired for the 3ds Max plugin.
/// </summary>
public sealed class MaxConnectedRenderSubmissionTransportLocalPlaceholder : IMaxConnectedRenderSubmissionTransport
{
    #region IMaxConnectedRenderSubmissionTransport

    /// <summary>
    /// Records a local placeholder submission receipt for the prepared launch package.
    /// </summary>
    public Task<MaxConnectedRenderJobState> SubmitAsync(MaxSceneLaunchPackageRequest request, MaxSceneLaunchPackageResult package, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Submit(request, package));
    }

    /// <summary>
    /// Refreshes one previously recorded local placeholder submission.
    /// </summary>
    public Task<MaxConnectedRenderJobState> RefreshAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Refresh(jobState));
    }

    /// <summary>
    /// Marks one local placeholder submission as cancelled (there is no remote job to stop).
    /// </summary>
    public Task<MaxConnectedRenderJobState> CancelAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        jobState.UpdatedUtc = DateTime.UtcNow;
        jobState.IsCancelled = true;
        jobState.StatusText = "Cancelled local placeholder submission.";
        return Task.FromResult(jobState);
    }

    #endregion

    #region Tools

    private static MaxConnectedRenderJobState Submit(MaxSceneLaunchPackageRequest request, MaxSceneLaunchPackageResult package)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(package);

        if (!package.IsSuccess)
        {
            return new MaxConnectedRenderJobState
            {
                JobId = $"failed-{Guid.NewGuid():N}",
                StatusText = package.StatusText,
                ProgressPercent = 0d,
                IsCompleted = false,
                IsPlaceholderLocalSubmission = true,
                SubmittedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                PackageFolderPath = package.PackageFolderPath,
                ManifestPath = package.ManifestPath,
                PackageArchivePath = package.PackageArchivePath,
                PrimaryArtifactPath = package.PrimaryArtifactPath,
                Diagnostics = [.. package.Diagnostics]
            };
        }

        if (string.IsNullOrWhiteSpace(package.PackageArchivePath) || !File.Exists(package.PackageArchivePath))
            throw new InvalidOperationException("Launch package archive is required before placeholder submission can be recorded.");

        var submittedUtc = DateTime.UtcNow;
        var receipt = new MaxConnectedRenderSubmissionReceipt
        {
            SubmissionId = $"submission-{Guid.NewGuid():N}",
            PackageId = package.PackageId,
            SubmittedUtc = submittedUtc,
            CloudUrl = request.CloudUrl,
            IdentityUrl = request.IdentityUrl,
            RenderMode = request.RenderMode,
            UseAllClients = request.UseAllClients,
            SelectedGroupName = request.SelectedGroupName,
            PackageArchivePath = package.PackageArchivePath,
            StatusText = "Recorded local placeholder submission receipt. Remote OmnibusCloud transport is the next implementation step."
        };

        var receiptPath = Path.Combine(package.PackageFolderPath, "submission-receipt.json");
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));

        var diagnostics = new List<MaxSceneDiagnosticItem>(package.Diagnostics)
        {
            new()
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = $"Recorded local submission receipt: '{receiptPath}'."
            }
        };

        return new MaxConnectedRenderJobState
        {
            JobId = string.IsNullOrWhiteSpace(package.PackageId) ? $"local-{Guid.NewGuid():N}" : $"local-{package.PackageId}",
            StatusText = receipt.StatusText,
            ProgressPercent = 15d,
            IsCompleted = false,
            IsPlaceholderLocalSubmission = true,
            SubmittedUtc = submittedUtc,
            UpdatedUtc = submittedUtc,
            PackageFolderPath = package.PackageFolderPath,
            ManifestPath = package.ManifestPath,
            SubmissionReceiptPath = receiptPath,
            PackageArchivePath = package.PackageArchivePath,
            PrimaryArtifactPath = package.PrimaryArtifactPath,
            Diagnostics = diagnostics
        };
    }

    private static MaxConnectedRenderJobState Refresh(MaxConnectedRenderJobState jobState)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        jobState.UpdatedUtc = DateTime.UtcNow;
        jobState.StatusText = string.IsNullOrWhiteSpace(jobState.SubmissionReceiptPath)
            ? "Local launch package is ready. Remote submit / polling is not wired yet."
            : "Local submission receipt is ready. Remote submit / polling is not wired yet.";

        if (jobState.ProgressPercent < 25d)
            jobState.ProgressPercent = 25d;

        return jobState;
    }

    #endregion
}
