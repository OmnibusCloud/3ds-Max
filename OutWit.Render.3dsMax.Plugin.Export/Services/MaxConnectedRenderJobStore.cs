using System.Text.Json;
using OutWit.Render.ThreeDsMax.Plugin.Export.Models;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Per-user record of the LAST launched connected render job, written next to the plugin's session
/// file. The record is what makes a submitted job reachable again: the Render dialog is recreated on
/// every open (and 3ds Max itself restarts), so without it a job whose dialog was closed could never
/// be observed or collected again — it "went nowhere".
/// </summary>
/// <remarks>
/// Every operation is best-effort: a missing, locked or corrupt record must never break a render, so
/// failures degrade to "no tracked job" rather than throwing. Diagnostics are trimmed on write — the
/// record is a handle to the job, not a log.
/// </remarks>
public sealed class MaxConnectedRenderJobStore
{
    #region Constants

    private const string JOB_FILE_NAME = "active-job.json";

    /// <summary>Diagnostics kept in the record; the newest are the ones worth restoring.</summary>
    private const int MAX_DIAGNOSTICS = 40;

    /// <summary>Oldest diagnostics kept when trimming — the submission facts live there.</summary>
    private const int HEAD_DIAGNOSTICS = 10;

    #endregion

    #region Fields

    private static readonly JsonSerializerOptions SERIALIZER_OPTIONS = new() { WriteIndented = true };

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="filePath">Record path; defaults to <see cref="ResolveDefaultFilePath"/>.</param>
    public MaxConnectedRenderJobStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath) ? ResolveDefaultFilePath() : filePath;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the tracked job record.
    /// </summary>
    /// <returns>The stored job state, or null when there is none (or it cannot be read).</returns>
    public MaxConnectedRenderJobState? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var jobState = JsonSerializer.Deserialize<MaxConnectedRenderJobState>(File.ReadAllText(FilePath));

            // A record without a parseable job id cannot be refreshed against the server — it is not a
            // handle to anything (blocked preflight / failed submission never reached the farm).
            return jobState is not null && Guid.TryParse(jobState.JobId, out _) ? jobState : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the tracked job record, replacing any previous one.
    /// </summary>
    /// <param name="jobState">The job state to persist.</param>
    public void Save(MaxConnectedRenderJobState jobState)
    {
        ArgumentNullException.ThrowIfNull(jobState);

        try
        {
            if (!Guid.TryParse(jobState.JobId, out _))
                return;

            TrimDiagnostics(jobState);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(jobState, SERIALIZER_OPTIONS));
        }
        catch
        {
            // Best-effort: a job that cannot be persisted is still tracked for this session.
        }
    }

    /// <summary>Deletes the tracked job record (the user acknowledged the job).</summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// Resolves the default record path: %APPDATA%\OmnibusCloud\3dsMax\active-job.json — the folder
    /// that already holds the plugin's session file.
    /// </summary>
    /// <returns>The absolute record path.</returns>
    public static string ResolveDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = AppContext.BaseDirectory;

        return Path.Combine(appData, "OmnibusCloud", "3dsMax", JOB_FILE_NAME);
    }

    #endregion

    #region Tools

    /// <summary>
    /// Caps the diagnostics list in place. A refresh appends a line per failure, so a long connectivity
    /// outage would otherwise grow the list (and the record) without bound. The head is kept because it
    /// carries the submission facts support asks for (job id, script, target); the discarded middle of
    /// an outage is the part nobody reads.
    /// </summary>
    internal static void TrimDiagnostics(MaxConnectedRenderJobState jobState)
    {
        if (jobState.Diagnostics.Count <= MAX_DIAGNOSTICS)
            return;

        var head = jobState.Diagnostics.Take(HEAD_DIAGNOSTICS);
        var tail = jobState.Diagnostics.Skip(jobState.Diagnostics.Count - (MAX_DIAGNOSTICS - HEAD_DIAGNOSTICS));
        jobState.Diagnostics = [.. head, .. tail];
    }

    #endregion

    #region Properties

    /// <summary>The absolute path of the job record.</summary>
    public string FilePath { get; }

    #endregion
}
