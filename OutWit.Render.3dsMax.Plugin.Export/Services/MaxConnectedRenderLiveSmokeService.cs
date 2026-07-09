using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Runs one real connected-render smoke flow from the current 3ds Max scene through OmnibusCloud.
/// </summary>
public sealed class MaxConnectedRenderLiveSmokeService
{
    #region Constants

    private const string RENDER_DCC_SCENE_STILL_SCRIPT = "RenderDccSceneStillPacked";

    #endregion

    #region Fields

    private static readonly TimeSpan TIMEOUT = TimeSpan.FromMinutes(10);

    private readonly MaxSceneExportService m_sceneExportService;

    private readonly MaxConnectedRenderSceneAttachmentService m_sceneAttachmentService;

    #endregion

    #region Constructors

    public MaxConnectedRenderLiveSmokeService(
        MaxSceneExportService sceneExportService,
        MaxConnectedRenderSceneAttachmentService sceneAttachmentService)
    {
        m_sceneExportService = sceneExportService;
        m_sceneAttachmentService = sceneAttachmentService;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Validates the current scene, submits one real RenderDccSceneStill job, waits for completion, and downloads the final artifact.
    /// </summary>
    public MaxConnectedRenderLiveSmokeResult RunRenderDccSceneStill(string cloudUrl, string identityUrl, string apiKey, string outputFolder)
    {
        return Task.Run(() => RunRenderDccSceneStillAsync(cloudUrl, identityUrl, apiKey, outputFolder)).GetAwaiter().GetResult();
    }

    private async Task<MaxConnectedRenderLiveSmokeResult> RunRenderDccSceneStillAsync(string cloudUrl, string identityUrl, string apiKey, string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(cloudUrl))
            throw new InvalidOperationException("OmnibusCloud URL is required.");

        if (string.IsNullOrWhiteSpace(identityUrl))
            throw new InvalidOperationException("Identity URL is required.");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key is required.");

        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new InvalidOperationException("Output folder is required.");

        Directory.CreateDirectory(outputFolder);
        var traceLogPath = Path.Combine(outputFolder, "render-smoke-trace.log");
        File.WriteAllText(traceLogPath, string.Empty);
        AppendTrace(traceLogPath, $"Starting 3ds Max connected render smoke for output folder '{outputFolder}'.");

        AppendTrace(traceLogPath, "Starting live scene validation.");
        var validation = m_sceneExportService.ValidateCurrentScene();
        AppendTrace(traceLogPath, $"Live scene validation completed. Success={validation.IsSuccess}; Status='{validation.StatusText}'.");
        if (!validation.IsSuccess || validation.Scene is null)
        {
            return new MaxConnectedRenderLiveSmokeResult
            {
                IsSuccess = false,
                StatusText = validation.StatusText,
                TraceLogPath = traceLogPath,
                ErrorMessage = validation.StatusText,
                Diagnostics = [.. validation.Diagnostics]
            };
        }

        using var cancellationSource = new CancellationTokenSource(TIMEOUT);
        var cancellationToken = cancellationSource.Token;
        var client = new WitCloudClient(cloudUrl, identityUrl, apiKey);
        var diagnostics = new List<MaxSceneDiagnosticItem>(validation.Diagnostics);

        try
        {
            AppendTrace(traceLogPath, $"Connecting to OmnibusCloud at '{cloudUrl}' using identity '{identityUrl}'.");
            await client.ConnectAsync(cancellationToken);
            AppendTrace(traceLogPath, "Connected to OmnibusCloud API.");

            AppendTrace(traceLogPath, "Uploading referenced scene image assets as blob-backed attachments.");
            diagnostics.AddRange(await m_sceneAttachmentService.UploadImageAssetAttachmentsAsync(
                validation.Scene,
                validation.Summary.SceneFilePath,
                (filePath, ct) => client.Blobs.UploadBlobFromFileAsync(filePath, ct: ct),
                cancellationToken));
            AppendTrace(traceLogPath, $"Scene attachment upload completed. AttachedFiles={validation.Scene.AttachedFiles.Count}.");

            var frame = ResolveFrame(validation.Scene);
            var options = CreateRenderOptions(validation.Scene);
            AppendTrace(traceLogPath, $"Submitting script '{RENDER_DCC_SCENE_STILL_SCRIPT}' for frame {frame} with {options.ResolutionX}x{options.ResolutionY} and {options.Samples} samples.");
            var handle = await client.Scripts.RunAsync(RENDER_DCC_SCENE_STILL_SCRIPT, MaxScenePayloadPacker.Pack(validation.Scene), frame, options, cancellationToken);
            AppendTrace(traceLogPath, $"Submitted OmnibusCloud job '{handle.JobId}'. Waiting for completion.");
            var waitResult = await handle.WaitAsync<Guid>(pollInterval: TimeSpan.FromSeconds(2), ct: cancellationToken);
            AppendTrace(traceLogPath, $"OmnibusCloud job '{handle.JobId}' finished with status '{waitResult.Status}' and progress {waitResult.OverallProgress:0.###}.");
            diagnostics.AddRange(
            [
                CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Submitted connected render smoke job '{handle.JobId}'."),
                CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Connected render smoke final status: {waitResult.Status}.")
            ]);

            if (waitResult.Status != ProcessingJobStatus.Completed)
            {
                if (!string.IsNullOrWhiteSpace(waitResult.ErrorMessage))
                    diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, waitResult.ErrorMessage));

                return new MaxConnectedRenderLiveSmokeResult
                {
                    IsSuccess = false,
                    StatusText = "Connected render smoke job did not complete successfully.",
                    JobId = handle.JobId.ToString(),
                    FinalJobStatus = waitResult.Status.ToString(),
                    OverallProgress = waitResult.OverallProgress,
                    TraceLogPath = traceLogPath,
                    ErrorMessage = waitResult.ErrorMessage ?? string.Empty,
                    Diagnostics = diagnostics
                };
            }

            var resultBlobId = waitResult.Result;
            if (resultBlobId == Guid.Empty)
                resultBlobId = await handle.GetResultAsync<Guid>(ct: cancellationToken);

            if (resultBlobId == Guid.Empty)
            {
                diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, "Connected render smoke did not return a result blob id."));
                return new MaxConnectedRenderLiveSmokeResult
                {
                    IsSuccess = false,
                    StatusText = "Connected render smoke did not return a result blob id.",
                    JobId = handle.JobId.ToString(),
                    FinalJobStatus = waitResult.Status.ToString(),
                    OverallProgress = waitResult.OverallProgress,
                    TraceLogPath = traceLogPath,
                    Diagnostics = diagnostics
                };
            }

            var downloadedFilePath = Path.Combine(outputFolder, $"renderdccscenestill-{handle.JobId:N}-{resultBlobId:N}.png");
            AppendTrace(traceLogPath, $"Downloading result blob '{resultBlobId}' to '{downloadedFilePath}'.");
            await client.Blobs.DownloadBlobToFileAsync(resultBlobId, downloadedFilePath, ct: cancellationToken);
            AppendTrace(traceLogPath, $"Downloaded render result to '{downloadedFilePath}'.");
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Info, $"Downloaded connected render smoke artifact to '{downloadedFilePath}'."));

            return new MaxConnectedRenderLiveSmokeResult
            {
                IsSuccess = true,
                StatusText = "Connected render smoke completed successfully.",
                JobId = handle.JobId.ToString(),
                FinalJobStatus = waitResult.Status.ToString(),
                OverallProgress = waitResult.OverallProgress,
                ResultBlobId = resultBlobId,
                DownloadedFilePath = downloadedFilePath,
                TraceLogPath = traceLogPath,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            AppendTrace(traceLogPath, $"Connected render smoke failed before completion: {ex}");
            diagnostics.Add(CreateDiagnostic(MaxSceneDiagnosticSeverity.Error, ex.Message));

            return new MaxConnectedRenderLiveSmokeResult
            {
                IsSuccess = false,
                StatusText = "Connected render smoke failed before completion.",
                TraceLogPath = traceLogPath,
                ErrorMessage = ex.ToString(),
                Diagnostics = diagnostics
            };
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    private static int ResolveFrame(DccSceneData scene)
    {
        if (scene.RenderSettings?.FrameStart > 0)
            return scene.RenderSettings.FrameStart;

        return 1;
    }

    private static RenderOptionsData CreateRenderOptions(DccSceneData scene)
    {
        var renderSettings = scene.RenderSettings;

        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = renderSettings?.TargetEngine ?? RenderEngine.Cycles,
            Samples = renderSettings?.Samples ?? 64,
            ResolutionX = renderSettings?.ResolutionX ?? 1920,
            ResolutionY = renderSettings?.ResolutionY ?? 1080,
            Denoise = true
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

    private static void AppendTrace(string traceLogPath, string message)
    {
        File.AppendAllText(traceLogPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
    }

    #endregion
}
