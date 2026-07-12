using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

/// <summary>
/// Serilog bootstrap (MX-19): a rolling daily log file under <c>%APPDATA%\OmnibusCloud\Logs</c>,
/// mirroring the desktop client's logging so <see cref="MaxDiagnosticsLauncher"/> finds the same
/// files. The logger is created once on first access and shared across the plugin.
/// </summary>
public static class MaxPluginLogging
{
    #region Constants

    private const string LOG_FILE_NAME = "3dsmax-plugin-.log";

    private const string OUTPUT_TEMPLATE =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    #endregion

    #region Fields

    private static readonly object m_lock = new();

    private static ILogger? m_logger;

    // The Settings ▸ Diagnostics "Level" knob controls this switch live (default matches the
    // embedded LogLevel setting default of Information).
    private static readonly LoggingLevelSwitch m_levelSwitch = new(LogEventLevel.Information);

    #endregion

    #region Functions

    /// <summary>
    /// The shared plugin logger, created on first access. Never throws: a failure to open the log
    /// file falls back to a silent logger so logging can never break the plugin.
    /// </summary>
    public static ILogger Logger
    {
        get
        {
            if (m_logger is not null)
                return m_logger;

            lock (m_lock)
            {
                m_logger ??= CreateLogger();
            }

            return m_logger;
        }
    }

    /// <summary>
    /// Applies the persisted "LogLevel" setting ("Information"/"Debug"/"Warning"/"Error") to the
    /// live level switch. Unknown values fall back to Information; safe before or after the logger
    /// is created.
    /// </summary>
    public static void ApplyMinimumLevel(string? logLevel)
    {
        m_levelSwitch.MinimumLevel = logLevel?.Trim().ToLowerInvariant() switch
        {
            "debug" => LogEventLevel.Debug,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            _ => LogEventLevel.Information
        };
    }

    #endregion

    #region Tools

    private static ILogger CreateLogger()
    {
        try
        {
            var logDirectory = MaxDiagnosticsLauncher.GetLogsDirectory();
            Directory.CreateDirectory(logDirectory);

            return new LoggerConfiguration()
                .MinimumLevel.ControlledBy(m_levelSwitch)
                .WriteTo.File(
                    Path.Combine(logDirectory, LOG_FILE_NAME),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: OUTPUT_TEMPLATE)
                .CreateLogger();
        }
        catch
        {
            // A logger must never break the host — fall back to a sink-less (silent) logger.
            return new LoggerConfiguration().CreateLogger();
        }
    }

    #endregion
}
