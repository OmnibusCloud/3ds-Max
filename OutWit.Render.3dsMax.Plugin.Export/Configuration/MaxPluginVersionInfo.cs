using System.Reflection;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Configuration;

/// <summary>
/// The version CI stamped into the build (tag-derived, incl. the -beta suffix) — shown in Settings ▸
/// About, the sidebar footer and the Diagnostics dialog. The numeric assembly version alone would
/// read "1.0.0" forever, so the informational version wins when present.
/// </summary>
public static class MaxPluginVersionInfo
{
    #region Functions

    public static string Resolve()
    {
        var assembly = typeof(MaxPluginVersionInfo).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        var plusIndex = informational.IndexOf('+');
        return plusIndex > 0 ? informational[..plusIndex] : informational;
    }

    #endregion
}
