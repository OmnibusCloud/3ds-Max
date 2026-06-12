using System.Text.Json;
using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Auth;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Real OmnibusCloud submission transport over the signed-in user session: uploads the
/// scene's image-asset attachments, submits the neutral DCC scene to the matching
/// RenderDccScene* script (optionally scoped to one of the user's client groups), and
/// refreshes job state through the jobs API.
/// </summary>
public sealed class MaxConnectedRenderSubmissionTransportOmnibusCloudSession : IMaxConnectedRenderSubmissionTransport
{
    #region Constants

    private const int DEFAULT_TILES_X = 2;

    private const int DEFAULT_TILES_Y = 2;

    #endregion

    #region Fields

    private readonly IMaxCloudConnectionService m_connectionService;

    private readonly MaxConnectedRenderSceneAttachmentService m_sceneAttachmentService;

    #endregion

    #region Constructors

    public MaxConnectedRenderSubmissionTransportOmnibusCloudSession(
        IMaxCloudConnectionService connectionService,
        MaxConnectedRenderSceneAttachmentService sceneAttachmentService)
    {
        m_connectionService = connectionService;
        m_sceneAttachmentService = sceneAttachmentService;
    }

    #endregion

    #region IMaxConnectedRenderSubmissionTransport

    /// <summary>
    /// Submits one prepared launch package to OmnibusCloud through the signed-in user session.
    /// </summary>
    /// <param name="request">The launch request the package was prepared from.</param>
    /// <param name="package">The prepared launch package.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>The trackable connected render job state.</returns>
    public async Task<MaxConnectedRenderJobState> SubmitAsync(MaxSceneLaunchPackageRequest request, MaxSceneLaunchPackageResult package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(package);

        var now = DateTime.UtcNow;
        var diagnostics = new List<MaxSceneDiagnosticItem>(package.Diagnostics);

        if (!package.IsSuccess || package.Scene == null)
        {
            return CreateFailedState(
                package,
                package.IsSuccess ? "Launch package does not carry an exported scene payload." : package.StatusText,
                diagnostics,
                now);
        }

        var client = await m_connectionService.GetClientAsync(request.CloudUrl, cancellationToken);
        if (client == null)
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Sign in to OmnibusCloud before launching a connected render."));
            return CreateFailedState(package, "Connected render submission failed. No signed-in cloud connection.", diagnostics, now);
        }

        try
        {
            var scene = package.Scene;

            diagnostics.AddRange(await m_sceneAttachmentService.UploadImageAssetAttachmentsAsync(
                scene,
                package.SceneFilePath,
                (filePath, ct) => client.Blobs.UploadBlobFromFileAsync(filePath, ct: ct),
                cancellationToken));

            var clientGroupId = await ResolveClientGroupIdAsync(client, request, diagnostics, cancellationToken);
            if (!request.UseAllClients && !string.IsNullOrWhiteSpace(request.SelectedGroupName) && clientGroupId == null)
                return CreateFailedState(package, $"Connected render submission failed. Group '{request.SelectedGroupName}' was not found in the user's execution scope.", diagnostics, now);

            var submission = CreateSubmission(request, scene, clientGroupId);
            var handle = await client.Scripts.SubmitAsync(submission, cancellationToken);

            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Submitted OmnibusCloud job '{handle.JobId}' for script '{submission.ScriptName}'."));

            var receiptPath = WriteSubmissionReceipt(request, package, submission.ScriptName, handle.JobId, now);
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Saved submission receipt: '{receiptPath}'."));

            return new MaxConnectedRenderJobState
            {
                JobId = handle.JobId.ToString("D"),
                CloudUrl = request.CloudUrl,
                StatusText = $"Submitted to OmnibusCloud as job '{handle.JobId}'.",
                ProgressPercent = 5d,
                IsCompleted = false,
                IsPlaceholderLocalSubmission = false,
                SubmittedUtc = now,
                UpdatedUtc = DateTime.UtcNow,
                PackageFolderPath = package.PackageFolderPath,
                ManifestPath = package.ManifestPath,
                SubmissionReceiptPath = receiptPath,
                PackageArchivePath = package.PackageArchivePath,
                PrimaryArtifactPath = package.PrimaryArtifactPath,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, $"Connected render submission failed: {ex.Message}"));
            return CreateFailedState(package, "Connected render submission failed.", diagnostics, now);
        }
    }

    /// <summary>
    /// Refreshes one previously submitted connected render job through the jobs API.
    /// </summary>
    /// <param name="jobState">The job state to refresh.</param>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>The refreshed job state.</returns>
    public async Task<MaxConnectedRenderJobState> RefreshAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        jobState.UpdatedUtc = DateTime.UtcNow;

        if (!Guid.TryParse(jobState.JobId, out var jobId))
        {
            jobState.StatusText = "Job was not submitted to OmnibusCloud; nothing to refresh.";
            return jobState;
        }

        var client = await m_connectionService.GetClientAsync(jobState.CloudUrl, cancellationToken);
        if (client == null)
        {
            jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Sign in to OmnibusCloud before refreshing the job."));
            jobState.StatusText = "Job refresh failed. No signed-in cloud connection.";
            return jobState;
        }

        try
        {
            var info = await client.Jobs.GetStatusAsync(jobId, cancellationToken);

            jobState.ProgressPercent = Math.Clamp(info.OverallProgress * 100d, 0d, 100d);
            jobState.IsCompleted = info.Status == ProcessingJobStatus.Completed;
            jobState.StatusText = string.IsNullOrWhiteSpace(info.ErrorMessage)
                ? $"OmnibusCloud job status: {info.Status}."
                : $"OmnibusCloud job status: {info.Status}. {info.ErrorMessage}";

            if (jobState.IsCompleted && jobState.ResultBlobId == null)
            {
                var resultBlobId = await client.Jobs.GetResultAsync<Guid>(jobId, ct: cancellationToken);
                if (resultBlobId != Guid.Empty)
                {
                    jobState.ResultBlobId = resultBlobId;
                    jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Job completed with result blob '{resultBlobId}'."));
                }
            }

            return jobState;
        }
        catch (Exception ex)
        {
            jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, $"Job refresh failed: {ex.Message}"));
            jobState.StatusText = "Job refresh failed.";
            return jobState;
        }
    }

    #endregion

    #region Tools

    private static WitJobSubmission CreateSubmission(MaxSceneLaunchPackageRequest request, DccSceneData scene, Guid? clientGroupId)
    {
        var options = CreateRenderOptions(request, scene);

        var (scriptName, parameters) = request.RenderMode switch
        {
            "RenderStillTiled" => ("RenderDccSceneStillTiled",
                JobParametersSnapshot.Create(scene, request.FrameStart, DEFAULT_TILES_X, DEFAULT_TILES_Y, options, CreateTileOptions())),
            "RenderFrames" => ("RenderDccSceneFrames",
                JobParametersSnapshot.Create(scene, request.FrameStart, request.FrameEnd, options)),
            "RenderVideo" => ("RenderDccSceneVideo",
                JobParametersSnapshot.Create(scene, request.FrameStart, request.FrameEnd, options, CreateVideoOptions(scene))),
            _ => ("RenderDccSceneStill",
                JobParametersSnapshot.Create(scene, request.FrameStart, options))
        };

        return new WitJobSubmission
        {
            ScriptName = scriptName,
            Parameters = parameters,
            ClientGroupId = clientGroupId
        };
    }

    private static RenderOptionsData CreateRenderOptions(MaxSceneLaunchPackageRequest request, DccSceneData scene)
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = scene.RenderSettings?.TargetEngine ?? RenderEngine.Cycles,
            Samples = request.Samples > 0 ? request.Samples : scene.RenderSettings?.Samples ?? 64,
            ResolutionX = request.ResolutionX > 0 ? request.ResolutionX : 1920,
            ResolutionY = request.ResolutionY > 0 ? request.ResolutionY : 1080,
            Denoise = true
        };
    }

    private static TileOptionsData CreateTileOptions()
    {
        return new TileOptionsData
        {
            OverlapPx = 8,
            BlendMode = TileBlendMode.CenterPriorityCrop
        };
    }

    private static VideoOptionsData CreateVideoOptions(DccSceneData scene)
    {
        return new VideoOptionsData
        {
            FrameRate = scene.RenderSettings?.Fps > 0 ? scene.RenderSettings.Fps : 24,
            ConstantRateFactor = 23
        };
    }

    private static async Task<Guid?> ResolveClientGroupIdAsync(
        IWitCloudClient client,
        MaxSceneLaunchPackageRequest request,
        List<MaxSceneDiagnosticItem> diagnostics,
        CancellationToken cancellationToken)
    {
        if (request.UseAllClients || string.IsNullOrWhiteSpace(request.SelectedGroupName))
            return null;

        var scope = await client.GetExecutionScopeOptionsAsync(cancellationToken);
        var group = scope.Groups.FirstOrDefault(me => string.Equals(me.Name, request.SelectedGroupName, StringComparison.OrdinalIgnoreCase));

        if (group?.GroupId == null)
            return null;

        diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Resolved launch group '{group.Name}' to '{group.GroupId}'."));
        return group.GroupId;
    }

    private static string WriteSubmissionReceipt(
        MaxSceneLaunchPackageRequest request,
        MaxSceneLaunchPackageResult package,
        string scriptName,
        Guid jobId,
        DateTime submittedUtc)
    {
        var receipt = new MaxConnectedRenderSubmissionReceipt
        {
            SubmissionId = jobId.ToString("D"),
            PackageId = package.PackageId,
            SubmittedUtc = submittedUtc,
            CloudUrl = request.CloudUrl,
            IdentityUrl = request.IdentityUrl,
            RenderMode = request.RenderMode,
            UseAllClients = request.UseAllClients,
            SelectedGroupName = request.SelectedGroupName,
            PackageArchivePath = package.PackageArchivePath,
            StatusText = $"Submitted to OmnibusCloud script '{scriptName}' as job '{jobId}'."
        };

        var receiptPath = Path.Combine(package.PackageFolderPath, "submission-receipt.json");
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        return receiptPath;
    }

    private static MaxConnectedRenderJobState CreateFailedState(
        MaxSceneLaunchPackageResult package,
        string statusText,
        List<MaxSceneDiagnosticItem> diagnostics,
        DateTime now)
    {
        return new MaxConnectedRenderJobState
        {
            JobId = $"failed-{Guid.NewGuid():N}",
            StatusText = statusText,
            ProgressPercent = 0d,
            IsCompleted = false,
            IsPlaceholderLocalSubmission = false,
            SubmittedUtc = now,
            UpdatedUtc = DateTime.UtcNow,
            PackageFolderPath = package.PackageFolderPath,
            ManifestPath = package.ManifestPath,
            PackageArchivePath = package.PackageArchivePath,
            PrimaryArtifactPath = package.PrimaryArtifactPath,
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
