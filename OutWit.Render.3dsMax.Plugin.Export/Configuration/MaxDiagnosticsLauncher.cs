using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

/// <summary>
/// Diagnostics helpers (MX-19): locate the plugin log directory / latest log file and open a folder
/// or file in the OS shell. A 3ds Max mirror of the desktop client's <c>DiagnosticsLauncher</c> so the
/// Settings ▸ Diagnostics tab behaves identically. The log directory matches the path
/// <see cref="MaxPluginLogging"/> writes to (shared <c>%APPDATA%\OmnibusCloud\Logs</c>).
/// </summary>
public static class MaxDiagnosticsLauncher
{
    #region Functions

    /// <summary>The rolling-log directory (mirror of the path set in <see cref="MaxPluginLogging"/>).</summary>
    public static string GetLogsDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OmnibusCloud", "Logs");
    }

    /// <summary>The most recently written <c>*.log</c> file, or null if none exist.</summary>
    public static string? GetLatestLogFile()
    {
        var directory = GetLogsDirectory();
        if (!Directory.Exists(directory))
            return null;

        try
        {
            return new DirectoryInfo(directory)
                .EnumerateFiles("*.log")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Opens the logs directory in the OS file browser. Returns false on failure.</summary>
    public static bool OpenLogsFolder()
    {
        var directory = GetLogsDirectory();
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch
        {
            // Best-effort — still try to open whatever exists.
        }

        return OpenPath(directory);
    }

    /// <summary>Opens the latest log file in the default app, falling back to the folder.</summary>
    public static bool OpenLatestLog()
    {
        var latest = GetLatestLogFile();
        return latest is not null ? OpenPath(latest) : OpenLogsFolder();
    }

    /// <summary>Opens a folder or file path in the OS shell.</summary>
    public static bool OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", QuoteArgument(path));
            }
            else
            {
                Process.Start("xdg-open", QuoteArgument(path));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Tools

    private static string QuoteArgument(string path) => $"\"{path}\"";

    #endregion
}
