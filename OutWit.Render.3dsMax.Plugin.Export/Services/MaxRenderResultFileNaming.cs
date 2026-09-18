using OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Names downloaded results after the format the artist chose. Every still, tiled still and frame used
/// to be saved as .png and every video as .mp4, whatever was selected — a JPEG, EXR, TIFF or WEBP under
/// a .png name opened in the wrong application or not at all, and a WebM or ProRes MOV was labelled MP4.
/// </summary>
public static class MaxRenderResultFileNaming
{
    #region Functions

    /// <summary>The extension the job's result is expected to carry, from what was requested.</summary>
    /// <param name="jobState">The job (its render mode, image format and video preset).</param>
    /// <returns>The extension, including the dot.</returns>
    public static string ExpectedExtension(MaxConnectedRenderJobState jobState) =>
        MaxRenderOutputCatalog.ResultExtension(jobState.RenderMode, jobState.ImageFormat, jobState.VideoPreset);

    /// <summary>
    /// A result that already landed for <paramref name="stem"/> under any result extension — the
    /// idempotency check across refreshes, now that the extension depends on the format (and on the
    /// bytes). Only finished names count: a transfer's temporary file never matches.
    /// </summary>
    /// <param name="folder">The per-job result folder.</param>
    /// <param name="stem">The file name without extension (<c>result</c>, <c>frame_0001</c>).</param>
    /// <returns>The existing path, or null when nothing has landed yet.</returns>
    public static string? FindLanded(string folder, string stem) =>
        MaxRenderOutputCatalog.ResultExtensions
            .Select(me => Path.Combine(folder, stem + me))
            .FirstOrDefault(File.Exists);

    /// <summary>
    /// Makes a freshly downloaded file's name match what its bytes are. It was downloaded under the
    /// requested extension; when the bytes declare another type (a record older than the kept format,
    /// or a farm that fell back), the bytes win and the file is renamed.
    /// </summary>
    /// <param name="downloadedPath">The file as downloaded.</param>
    /// <returns>The final path (unchanged when the name already matches or the type is unknown).</returns>
    public static string NameByContent(string downloadedPath)
    {
        var detected = MaxRenderResultSniffer.DetectExtension(downloadedPath);
        if (detected is null || string.Equals(detected, Path.GetExtension(downloadedPath), StringComparison.OrdinalIgnoreCase))
            return downloadedPath;

        var correctedPath = Path.ChangeExtension(downloadedPath, detected);
        File.Move(downloadedPath, correctedPath, overwrite: true);
        return correctedPath;
    }

    #endregion
}
