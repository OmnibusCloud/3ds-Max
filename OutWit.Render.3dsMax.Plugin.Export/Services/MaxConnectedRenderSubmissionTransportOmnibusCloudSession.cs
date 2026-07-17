using System.Text.Json;
using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Services;
using OutWit.Controller.Render.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
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
    /// Session probe run BEFORE the heavy scene capture: an expired sign-in used to surface only
    /// after minutes of synchronous main-thread capture work — exactly the late failure the
    /// preflight/prepare split exists to prevent.
    /// </summary>
    /// <param name="request">The launch request about to be prepared.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>Null when a signed-in session is available; the blocking reason otherwise.</returns>
    public async Task<string?> ProbeSubmissionBlockerAsync(MaxSceneLaunchPackageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await m_connectionService.GetClientAsync(request.CloudUrl, cancellationToken);
        return client == null
            ? "Sign in to OmnibusCloud before launching a connected render."
            : null;
    }

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

            // Target resolution runs BEFORE the attachment uploads: a wrong/vanished target must
            // fail in milliseconds with a readable message, not after minutes of pushing textures.
            // EVERY connected mode is scoped now — including ExportBlend, whose dialog grew its own
            // RUN ON picker (the historic unscoped export only ever worked for all-clients accounts).
            var hasGroupName = !string.IsNullOrWhiteSpace(request.SelectedGroupName);
            var hasProjectName = !string.IsNullOrWhiteSpace(request.SelectedProjectName);

            if (hasGroupName && hasProjectName)
                return CreateFailedState(package, "Connected render submission failed. A launch may target a project or a group, not both.", diagnostics, now);

            // Launch-week req 4 (and the silent-degrade fix the Blender addon needed in 1.0.9): a
            // launch with NO target must never fall through to an unscoped all-clients submit — the
            // engine rejects it for accounts without the global grant.
            if (!request.UseAllClients && !hasGroupName && !hasProjectName)
                return CreateFailedState(package, "Connected render submission failed. Select a project or a render group first (or run on the whole network if your account allows it).", diagnostics, now);

            Guid? clientGroupId = null;
            Guid? projectId = null;
            if (!request.UseAllClients && hasProjectName)
            {
                projectId = await ResolveProjectIdAsync(client, request, diagnostics, cancellationToken);
                if (projectId == null)
                    return CreateFailedState(package, $"Connected render submission failed. Project '{request.SelectedProjectName}' was not found in the user's execution scope.", diagnostics, now);
            }
            else if (!request.UseAllClients)
            {
                clientGroupId = await ResolveClientGroupIdAsync(client, request, diagnostics, cancellationToken);
                if (clientGroupId == null)
                    return CreateFailedState(package, $"Connected render submission failed. Group '{request.SelectedGroupName}' was not found in the user's execution scope.", diagnostics, now);
            }

            // Upload progress (design 4.1.3 Uploading card): each attachment advances the fraction;
            // the final scene submission tops it off. The asset count is an upper bound (skipped
            // attachments just make the bar finish early) — honest enough for a progress bar.
            var uploadSteps = (scene.ImageAssets?.Count ?? 0) + 1;
            var uploadedSteps = 0;
            request.UploadProgress?.Invoke(0d);

            diagnostics.AddRange(await m_sceneAttachmentService.UploadImageAssetAttachmentsAsync(
                scene,
                package.SceneFilePath,
                async (filePath, ct) =>
                {
                    var blobId = await client.Blobs.UploadBlobFromFileAsync(filePath, ct: ct);
                    var done = Interlocked.Increment(ref uploadedSteps);
                    request.UploadProgress?.Invoke((double)done / uploadSteps);
                    return blobId;
                },
                cancellationToken));

            // The attachment pass may have DEGRADED the scene (missing textures removed) after
            // the deep validation in Prepare — re-validate so a contract violation fails right
            // here with a readable message instead of on the farm after the whole upload.
            DccSceneValidationService.Validate(scene);

            var submission = CreateSubmission(request, scene, clientGroupId, projectId);
            var handle = await client.Scripts.SubmitAsync(submission, cancellationToken);
            request.UploadProgress?.Invoke(1d);

            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Submitted OmnibusCloud job '{handle.JobId}' for script '{submission.ScriptName}'."));

            var receiptPath = WriteSubmissionReceipt(request, package, submission.ScriptName, handle.JobId, now);
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Saved submission receipt: '{receiptPath}'."));

            return new MaxConnectedRenderJobState
            {
                JobId = handle.JobId.ToString("D"),
                CloudUrl = request.CloudUrl,
                RenderMode = request.RenderMode,
                StatusText = $"Submitted to OmnibusCloud as job '{handle.JobId}'.",
                ProgressPercent = 5d,
                IsCompleted = false,
                IsPlaceholderLocalSubmission = false,
                FrameStart = request.FrameStart,
                FrameEnd = request.FrameEnd,
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
            // The exception message IS the actionable part (e.g. the engine's "not authorized to
            // launch on all clients") — a bare generic line sent operators digging in diagnostics.
            return CreateFailedState(package, $"Connected render submission failed. {ex.Message}", diagnostics, now);
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
            jobState.IsCancelled = info.Status == ProcessingJobStatus.Cancelled;
            jobState.StatusText = string.IsNullOrWhiteSpace(info.ErrorMessage)
                ? $"OmnibusCloud job status: {info.Status}."
                : $"OmnibusCloud job status: {info.Status}. {info.ErrorMessage}";

            if (jobState.IsCompleted)
            {
                // Fetch the result blob(s) to local files so 'Download Result' copies the real cloud
                // output (the .blend for ExportBlend, the image/video for renders, the per-frame
                // images for frame sequences). Idempotent + retried on later refreshes.
                if (jobState.RenderMode == "RenderFrames")
                {
                    await FetchFrameResultBlobIdsAsync(client, jobState, jobId, cancellationToken);
                    await TryDownloadFrameResultBlobsAsync(client, jobState, cancellationToken);
                }
                else
                {
                    if (jobState.ResultBlobId == null)
                    {
                        var resultBlobId = await client.Jobs.GetResultAsync<Guid>(jobId, ct: cancellationToken);
                        if (resultBlobId != Guid.Empty)
                        {
                            jobState.ResultBlobId = resultBlobId;
                            jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Job completed with result blob '{resultBlobId}'."));
                        }
                    }

                    await TryDownloadResultBlobAsync(client, jobState, cancellationToken);
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

    /// <summary>
    /// Requests server-side cancellation of one previously submitted connected render job.
    /// </summary>
    /// <param name="jobState">The job state to cancel.</param>
    /// <param name="cancellationToken">Cancels the cancel request itself.</param>
    /// <returns>The updated job state.</returns>
    public async Task<MaxConnectedRenderJobState> CancelAsync(MaxConnectedRenderJobState jobState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        jobState.UpdatedUtc = DateTime.UtcNow;

        if (!Guid.TryParse(jobState.JobId, out var jobId))
        {
            // Never reached the farm (blocked / failed locally) — cancelling is a local no-op.
            jobState.IsCancelled = true;
            jobState.StatusText = "Cancelled before submission.";
            return jobState;
        }

        var client = await m_connectionService.GetClientAsync(jobState.CloudUrl, cancellationToken);
        if (client == null)
        {
            jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Sign in to OmnibusCloud before cancelling the job."));
            jobState.StatusText = "Cancel failed. No signed-in cloud connection.";
            return jobState;
        }

        try
        {
            await client.Jobs.CancelAsync(jobId, cancellationToken);
            jobState.StatusText = "Cancel requested. Waiting for the farm to stop the job.";
            jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Requested cancellation of OmnibusCloud job '{jobId}'."));
            return jobState;
        }
        catch (Exception ex)
        {
            jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, $"Job cancel failed: {ex.Message}"));
            jobState.StatusText = "Job cancel failed.";
            return jobState;
        }
    }

    #endregion

    #region Tools

    private static WitJobSubmission CreateSubmission(MaxSceneLaunchPackageRequest request, DccSceneData scene, Guid? clientGroupId, Guid? projectId)
    {
        var options = CreateRenderOptions(request, scene);

        // The scene travels as a gzipped MemoryPack payload (6-10x smaller than the inline
        // DccScene parameter); the *Packed scripts expand it server-side before the build.
        var packedScene = MaxScenePayloadPacker.Pack(scene);

        var tilesX = request.TilesX > 0 ? request.TilesX : DEFAULT_TILES_X;
        var tilesY = request.TilesY > 0 ? request.TilesY : DEFAULT_TILES_Y;

        var (scriptName, parameters) = request.RenderMode switch
        {
            "RenderStillTiled" => ("RenderDccSceneStillTiledPacked",
                JobParametersSnapshot.Create(packedScene, request.FrameStart, tilesX, tilesY, options, CreateTileOptions(request))),
            "RenderFrames" => ("RenderDccSceneFramesPacked",
                JobParametersSnapshot.Create(packedScene, request.FrameStart, request.FrameEnd, options)),
            "RenderVideo" => ("RenderDccSceneVideoPacked",
                JobParametersSnapshot.Create(packedScene, request.FrameStart, request.FrameEnd, options, CreateVideoOptions(request, scene))),
            "ExportBlend" => ("RenderDccSceneExportBlendPacked",
                JobParametersSnapshot.Create(packedScene)),
            _ => ("RenderDccSceneStillPacked",
                JobParametersSnapshot.Create(packedScene, request.FrameStart, options))
        };

        return new WitJobSubmission
        {
            ScriptName = scriptName,
            Parameters = parameters,
            ClientGroupId = clientGroupId,
            ProjectId = projectId
        };
    }

    private static RenderOptionsData CreateRenderOptions(MaxSceneLaunchPackageRequest request, DccSceneData scene)
    {
        return new RenderOptionsData
        {
            Format = MaxRenderOutputCatalog.ParseImageFormat(request.ImageFormat),
            Engine = scene.RenderSettings?.TargetEngine ?? RenderEngine.Cycles,
            Samples = request.Samples > 0 ? request.Samples : scene.RenderSettings?.Samples ?? 64,
            ResolutionX = request.ResolutionX > 0 ? request.ResolutionX : 1920,
            ResolutionY = request.ResolutionY > 0 ? request.ResolutionY : 1080,
            Denoise = true
        };
    }

    private static TileOptionsData CreateTileOptions(MaxSceneLaunchPackageRequest request)
    {
        return new TileOptionsData
        {
            OverlapPx = request.TileOverlap > 0 ? request.TileOverlap : 8,
            BlendMode = TileBlendMode.CenterPriorityCrop
        };
    }

    private static VideoOptionsData CreateVideoOptions(MaxSceneLaunchPackageRequest request, DccSceneData scene)
    {
        return new VideoOptionsData
        {
            FrameRate = scene.RenderSettings?.Fps > 0 ? scene.RenderSettings.Fps : 24,
            ConstantRateFactor = request.VideoCrf > 0 ? request.VideoCrf : 23,
            Format = MaxRenderOutputCatalog.ParseVideoPreset(request.VideoPreset)
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

    // Name-resolved against the FRESH scope like the group above — a project that completed (or was
    // unshared) since the dialog loaded fails here with a readable message, not on the farm.
    private static async Task<Guid?> ResolveProjectIdAsync(
        IWitCloudClient client,
        MaxSceneLaunchPackageRequest request,
        List<MaxSceneDiagnosticItem> diagnostics,
        CancellationToken cancellationToken)
    {
        if (request.UseAllClients || string.IsNullOrWhiteSpace(request.SelectedProjectName))
            return null;

        var scope = await client.GetExecutionScopeOptionsAsync(cancellationToken);
        var project = scope.Projects.FirstOrDefault(me => string.Equals(me.Name, request.SelectedProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null || project.ProjectId == Guid.Empty)
            return null;

        diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Resolved launch project '{project.Name}' to '{project.ProjectId}'."));
        return project.ProjectId;
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
            SelectedProjectName = request.SelectedProjectName,
            PackageArchivePath = package.PackageArchivePath,
            StatusText = $"Submitted to OmnibusCloud script '{scriptName}' as job '{jobId}'."
        };

        var receiptPath = Path.Combine(package.PackageFolderPath, "submission-receipt.json");
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        return receiptPath;
    }

    private static async Task FetchFrameResultBlobIdsAsync(
        IWitCloudClient client,
        MaxConnectedRenderJobState jobState,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (jobState.ResultFrameBlobIds.Count > 0)
            return;

        // RenderDccSceneFrames returns a BlobCollection whose deserialized shape varies with the
        // transport path (Guid[] / Guid?[] / List<Guid?>). Probe each shape, most common first —
        // the same proven approach as the live distributed tests.
        var frameBlobIds = await FetchFrameBlobIdsAsync(client, jobId, cancellationToken);
        if (frameBlobIds.Count == 0)
        {
            // A COMPLETED frames job whose result shape cannot be read must not present as a
            // silent success with zero frames — that reads as "the service lost my render".
            jobState.Diagnostics.Add(CreateDiagnostic(
                MaxSceneDiagnosticSeverity.Error,
                $"Job '{jobId}' completed but its frame result payload could not be read — no frames were downloaded. Refresh retries; report this job id if it persists."));
            jobState.StatusText = "Completed, but the frame results could not be read.";
            return;
        }

        jobState.ResultFrameBlobIds = frameBlobIds;
        jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Job completed with {frameBlobIds.Count} frame result blobs."));
    }

    private static async Task<List<Guid>> FetchFrameBlobIdsAsync(IWitCloudClient client, Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.Jobs.GetResultAsync<Guid[]>(jobId, ct: cancellationToken);
            if (result is { Length: > 0 })
                return result.Where(me => me != Guid.Empty).ToList();
        }
        catch
        {
            // Shape mismatch — try the next known result shape.
        }

        try
        {
            var nullableResult = await client.Jobs.GetResultAsync<Guid?[]>(jobId, ct: cancellationToken);
            if (nullableResult is { Length: > 0 })
                return nullableResult.Where(me => me.HasValue && me.Value != Guid.Empty).Select(me => me!.Value).ToList();
        }
        catch
        {
            // Shape mismatch — try the next known result shape.
        }

        try
        {
            var listResult = await client.Jobs.GetResultAsync<List<Guid?>>(jobId, ct: cancellationToken);
            if (listResult is { Count: > 0 })
                return listResult.Where(me => me.HasValue && me.Value != Guid.Empty).Select(me => me!.Value).ToList();
        }
        catch
        {
            // No readable result shape; the next refresh retries.
        }

        return [];
    }

    private static async Task TryDownloadFrameResultBlobsAsync(
        IWitCloudClient client,
        MaxConnectedRenderJobState jobState,
        CancellationToken cancellationToken)
    {
        if (jobState.ResultFrameBlobIds.Count == 0)
            return;

        try
        {
            var resultFolder = BuildResultFolder(jobState, jobState.ResultFrameBlobIds[0]);
            Directory.CreateDirectory(resultFolder);

            var downloaded = 0;
            for (var index = 0; index < jobState.ResultFrameBlobIds.Count; index++)
            {
                var framePath = Path.Combine(resultFolder, $"frame_{jobState.FrameStart + index:D4}.png");

                // Idempotent across refreshes: skip frames that already landed.
                if (!File.Exists(framePath))
                {
                    await client.Blobs.DownloadBlobToFileAsync(jobState.ResultFrameBlobIds[index], framePath, ct: cancellationToken);
                    downloaded++;
                }

                if (index == 0)
                    jobState.PrimaryArtifactPath = framePath;
            }

            if (downloaded > 0)
                jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Downloaded {downloaded} frame results to '{resultFolder}'."));
        }
        catch (Exception ex)
        {
            // Leave the remaining frames for the next refresh to retry.
            jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Warning, $"Frame result download failed (will retry on next refresh): {ex.Message}"));
        }
    }

    private static async Task TryDownloadResultBlobAsync(
        IWitCloudClient client,
        MaxConnectedRenderJobState jobState,
        CancellationToken cancellationToken)
    {
        if (jobState.ResultBlobId is not Guid blobId || blobId == Guid.Empty)
            return;

        try
        {
            var resultPath = BuildResultPath(jobState, blobId);

            // Idempotent across refreshes: if already downloaded, just point at it.
            if (File.Exists(resultPath))
            {
                jobState.PrimaryArtifactPath = resultPath;
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            await client.Blobs.DownloadBlobToFileAsync(blobId, resultPath, ct: cancellationToken);
            jobState.PrimaryArtifactPath = resultPath;
            jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Downloaded job result to '{resultPath}'."));
        }
        catch (Exception ex)
        {
            // Leave PrimaryArtifactPath as-is; the next refresh retries the download.
            jobState.Diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Warning, $"Result download failed (will retry on next refresh): {ex.Message}"));
        }
    }

    private static string BuildResultPath(MaxConnectedRenderJobState jobState, Guid blobId)
    {
        var extension = ResolveResultExtension(jobState.RenderMode);
        return Path.Combine(BuildResultFolder(jobState, blobId), $"result{extension}");
    }

    private static string BuildResultFolder(MaxConnectedRenderJobState jobState, Guid blobId)
    {
        var jobFolder = string.IsNullOrWhiteSpace(jobState.JobId) ? blobId.ToString("N") : jobState.JobId.Replace('-', '_');
        return Path.Combine(Path.GetTempPath(), "OmnibusCloudResults", jobFolder);
    }

    private static string ResolveResultExtension(string renderMode) => renderMode switch
    {
        "ExportBlend" => ".blend",
        "RenderVideo" => ".mp4",
        _ => ".png"
    };

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
