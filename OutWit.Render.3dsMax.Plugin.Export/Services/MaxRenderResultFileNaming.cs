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
    #region Constants

    /// <summary>Folder (under %TEMP%) the transport downloads results into before they are delivered.</summary>
    private const string DOWNLOAD_ROOT_NAME = "OmnibusCloudResults";

    /// <summary>File name prefix the transport gives each downloaded sequence frame.</summary>
    public const string DOWNLOADED_FRAME_PREFIX = "frame_";

    #endregion

    #region Functions

    /// <summary>The per-job folder the transport downloads a job's result(s) into.</summary>
    /// <param name="jobFolderName">The job id (or blob id) turned into a folder name.</param>
    /// <returns>The folder under the shared download root.</returns>
    public static string DownloadFolder(string jobFolderName) =>
        Path.Combine(DownloadRoot, jobFolderName);

    /// <summary>
    /// True while the path is still in the transport's download area, i.e. not yet delivered to the
    /// folder the artist chose.
    /// </summary>
    /// <param name="path">A result path.</param>
    /// <returns>True when the path lies under the download root.</returns>
    public static bool IsInDownloadArea(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DownloadRoot)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The name a delivered still or video carries: the scene name plus the frame (a still,
    /// "robby_vs_fly_0026") or the frame range (a video, "robby_vs_fly_0001-0340").
    /// </summary>
    /// <param name="jobState">The job (render mode, frame range, result name).</param>
    /// <returns>The file name without extension.</returns>
    public static string DeliveredFileStem(MaxConnectedRenderJobState jobState)
    {
        var stem = MaxConnectedRenderDownloadService.SanitizeFileStem(jobState.ResultName);
        return jobState.RenderMode == "RenderVideo"
            ? $"{stem}_{FrameRange(jobState)}"
            : $"{stem}_{jobState.FrameStart:D4}";
    }

    /// <summary>The subfolder a delivered image sequence goes into, e.g. "robby_vs_fly_0001-0340".</summary>
    /// <param name="jobState">The job (frame range, result name).</param>
    /// <returns>The folder name.</returns>
    public static string DeliveredSequenceFolderName(MaxConnectedRenderJobState jobState) =>
        $"{MaxConnectedRenderDownloadService.SanitizeFileStem(jobState.ResultName)}_{FrameRange(jobState)}";

    /// <summary>
    /// The delivered name of one downloaded sequence frame: "frame_0005.png" becomes
    /// "robby_vs_fly_0005.png". Null when the file is not a downloaded frame.
    /// </summary>
    /// <param name="downloadedFileName">The downloaded frame's file name.</param>
    /// <param name="resultName">The scene name.</param>
    /// <returns>The delivered file name, or null.</returns>
    public static string? DeliveredFrameFileName(string downloadedFileName, string resultName)
    {
        var stem = Path.GetFileNameWithoutExtension(downloadedFileName);
        if (!stem.StartsWith(DOWNLOADED_FRAME_PREFIX, StringComparison.Ordinal))
            return null;

        var frame = stem[DOWNLOADED_FRAME_PREFIX.Length..];
        if (frame.Length == 0 || !frame.All(char.IsDigit))
            return null;

        return $"{MaxConnectedRenderDownloadService.SanitizeFileStem(resultName)}_{frame}{Path.GetExtension(downloadedFileName)}";
    }

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

    private static string FrameRange(MaxConnectedRenderJobState jobState) =>
        $"{jobState.FrameStart:D4}-{Math.Max(jobState.FrameStart, jobState.FrameEnd):D4}";

    #endregion

    #region Properties

    /// <summary>The shared download root, <c>%TEMP%\OmnibusCloudResults</c>.</summary>
    public static string DownloadRoot => Path.Combine(Path.GetTempPath(), DOWNLOAD_ROOT_NAME);

    #endregion
}
