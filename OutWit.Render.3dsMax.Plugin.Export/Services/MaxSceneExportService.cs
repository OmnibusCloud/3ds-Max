using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;
using System.Text;
using MemoryPack;
using OutWit.Controller.Render.Dcc.Services;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;
using OutWit.Render.ThreeDsMax.Plugin.Export.Services.Mapping;
using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Orchestrates scene-summary collection, neutral DCC mapping, validation, and local export for the 3ds Max plugin scaffold.
/// </summary>
public sealed class MaxSceneExportService
{
    #region Fields

    private readonly MaxSceneSummaryService m_sceneSummaryService;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new export service over the provided scene-summary source.
    /// </summary>
    /// <param name="sceneSummaryService">The summary service used as the initial host-scene boundary.</param>
    public MaxSceneExportService(MaxSceneSummaryService sceneSummaryService)
    {
        m_sceneSummaryService = sceneSummaryService;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Collects the current 3ds Max scene summary through the snapshot boundary.
    /// </summary>
    public MaxSceneSummaryData CollectSummary()
    {
        return m_sceneSummaryService.Collect();
    }

    /// <summary>
    /// Builds and validates the current 3ds Max scene against the shared neutral DCC contract.
    /// </summary>
    public MaxSceneExportResult ValidateCurrentScene()
    {
        return ValidateCurrentScene(MaxSceneCaptureOptions.Default);
    }

    /// <summary>
    /// Builds and validates the current 3ds Max scene with capture options.
    /// </summary>
    public MaxSceneExportResult ValidateCurrentScene(MaxSceneCaptureOptions captureOptions)
    {
        var summary = m_sceneSummaryService.Collect(captureOptions);
        var scene = MaxSceneDccSceneMapper.Create(summary);

        try
        {
            DccSceneValidationService.Validate(scene);

            var diagnostics = new List<MaxSceneDiagnosticItem>
            {
                new()
                {
                    Severity = MaxSceneDiagnosticSeverity.Info,
                    Message = "Scene snapshot was converted into a valid DccSceneData payload.",
                    SuggestedAction = "Continue with export or extend the mapper with richer 3ds Max scene extraction."
                }
            };

            AddSummaryDiagnostics(summary, diagnostics);

            return new MaxSceneExportResult
            {
                Summary = summary,
                Scene = scene,
                StatusText = "Scene summary collected and neutral DCC payload validated successfully.",
                IsSuccess = true,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            return new MaxSceneExportResult
            {
                Summary = summary,
                Scene = scene,
                StatusText = "Neutral DCC scene validation failed.",
                IsSuccess = false,
                Diagnostics =
                [
                    new MaxSceneDiagnosticItem
                    {
                        Severity = MaxSceneDiagnosticSeverity.Error,
                        Message = ex.Message,
                        SuggestedAction = "Review the export diagnostics and align the 3ds Max mapping with the supported DCC contract."
                    }
                ]
            };
        }
    }

    /// <summary>
    /// Validates and exports the current 3ds Max scene into a local artifact.
    /// </summary>
    /// <param name="outputFolder">The destination folder for the exported artifact.</param>
    /// <param name="outputFormat">The requested artifact format.</param>
    /// <param name="captureOptions">Optional capture options (defaults preserve the plain export).</param>
    public MaxSceneExportResult ExportCurrentScene(string outputFolder, MaxSceneExportOutputFormat outputFormat, MaxSceneCaptureOptions? captureOptions = null)
    {
        var result = ValidateCurrentScene(captureOptions ?? MaxSceneCaptureOptions.Default);

        if (!result.IsSuccess || result.Scene is null)
            return result;

        Directory.CreateDirectory(outputFolder);
        result.OutputPath = Path.Combine(outputFolder, $"dcc-scene{GetOutputFileExtension(outputFormat)}");
        WriteSceneArtifact(result.Scene, result.OutputPath, outputFormat);
        var outputFileInfo = new FileInfo(result.OutputPath);
        result.StatusText = "Neutral DCC scene exported successfully.";
        result.Diagnostics.Add(new MaxSceneDiagnosticItem
        {
            Severity = MaxSceneDiagnosticSeverity.Info,
            Message = $"Exported scene artifact: '{result.OutputPath}' ({outputFileInfo.Length} bytes)."
        });
        return result;
    }

    private static void WriteSceneArtifact(DccSceneData scene, string outputPath, MaxSceneExportOutputFormat outputFormat)
    {
        var jsonPayload = default(string);

        switch (outputFormat)
        {
            case MaxSceneExportOutputFormat.Json:
                jsonPayload = CreateJsonPayload(scene);
                File.WriteAllText(outputPath, jsonPayload, Encoding.UTF8);
                return;
            case MaxSceneExportOutputFormat.MemoryPack:
                File.WriteAllBytes(outputPath, MemoryPackSerializer.Serialize(scene));
                return;
            case MaxSceneExportOutputFormat.JsonGzip:
                jsonPayload = CreateJsonPayload(scene);
                WriteGzipArtifact(Encoding.UTF8.GetBytes(jsonPayload), outputPath);
                return;
            case MaxSceneExportOutputFormat.MemoryPackGzip:
                WriteGzipArtifact(MemoryPackSerializer.Serialize(scene), outputPath);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(outputFormat), outputFormat, "Unsupported export output format.");
        }
    }

    private static string CreateJsonPayload(DccSceneData scene)
    {
        return JsonSerializer.Serialize(scene, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        });
    }

    private static void WriteGzipArtifact(byte[] payload, string outputPath)
    {
        using var fileStream = File.Create(outputPath);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize);
        gzipStream.Write(payload, 0, payload.Length);
    }

    private static string GetOutputFileExtension(MaxSceneExportOutputFormat outputFormat)
    {
        return outputFormat switch
        {
            MaxSceneExportOutputFormat.Json => ".json",
            MaxSceneExportOutputFormat.MemoryPack => ".mpack",
            MaxSceneExportOutputFormat.JsonGzip => ".json.gz",
            MaxSceneExportOutputFormat.MemoryPackGzip => ".mpack.gz",
            _ => throw new ArgumentOutOfRangeException(nameof(outputFormat), outputFormat, "Unsupported export output format.")
        };
    }

    private static void AddSummaryDiagnostics(MaxSceneSummaryData summary, List<MaxSceneDiagnosticItem> diagnostics)
    {
        const double MIN_SUSPICIOUS_LIGHT_RANGE = 0.01d;

        if (string.IsNullOrWhiteSpace(summary.SceneFilePath))
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Warning,
                Message = "The current 3ds Max scene is unsaved. Attachment and texture-path portability may be incomplete.",
                SuggestedAction = "Save the .max file before exporting richer scene content."
            });
        }

        if (summary.SkippedEmptyMeshCount > 0)
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Warning,
                Message = $"Skipped {summary.SkippedEmptyMeshCount} empty mesh object(s) with no geometry.",
                SuggestedAction = "These carry no renderable vertices (helper/degenerate objects); they are excluded so the rest of the scene renders."
            });
        }

        if (summary.SkippedInactiveLightCount > 0)
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Warning,
                Message = $"Skipped {summary.SkippedInactiveLightCount} light(s) that are switched off or have no positive intensity.",
                SuggestedAction = "Turn the light on or raise its multiplier in 3ds Max if it should contribute to the render."
            });
        }

        if (string.IsNullOrWhiteSpace(summary.ActiveRenderCameraName) && summary.CamerasCount > 0)
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = "Cameras are present, but no active render camera was resolved from current render settings.",
                SuggestedAction = "Confirm the intended render camera in 3ds Max before final export or submission."
            });
        }

        if (summary.UsesSyntheticViewportCamera)
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = $"Synthesized render camera '{summary.ActiveRenderCameraName}' from the active viewport ({summary.ActiveViewportType}).",
                SuggestedAction = "Assign an explicit render camera in 3ds Max if you need a stable camera independent of the current active viewport."
            });
        }

        if (summary.UsesSyntheticDefaultLights)
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = "Synthesized default three-point lighting because the 3ds Max scene had no explicit lights.",
                SuggestedAction = "Add explicit render lights in 3ds Max for full control; the synthesized rig only prevents a black render."
            });
        }
        else if (summary.LightsCount == 0)
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Warning,
                Message = "No explicit lights were discovered in the 3ds Max scene.",
                SuggestedAction = "Neutral DCC export does not preserve 3ds Max viewport/default lighting, so add explicit render lights before relying on connected render output."
            });
        }
        else if (summary.Lights.Any(me => (me.Kind == DccLightKind.Point || me.Kind == DccLightKind.Spot) && me.Range <= MIN_SUSPICIOUS_LIGHT_RANGE))
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Warning,
                Message = "One or more explicit 3ds Max lights use a near-zero attenuation range.",
                SuggestedAction = "Review light attenuation/range in 3ds Max because connected neutral DCC renders may appear almost black when the effective light distance is extremely small."
            });
        }

        if (summary.MaterialNames.Count > 0)
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = $"Discovered materials: {string.Join(", ", summary.MaterialNames.Take(5))}",
                SuggestedAction = summary.MaterialNames.Count > 5 ? "Additional materials were omitted from this preview list." : null
            });
        }

        if (summary.TextureNames.Count > 0)
        {
            diagnostics.Add(new MaxSceneDiagnosticItem
            {
                Severity = MaxSceneDiagnosticSeverity.Info,
                Message = $"Discovered textures: {string.Join(", ", summary.TextureNames.Take(5))}",
                SuggestedAction = summary.TextureNames.Count > 5 ? "Additional textures were omitted from this preview list." : null
            });
        }
    }

    #endregion
}
