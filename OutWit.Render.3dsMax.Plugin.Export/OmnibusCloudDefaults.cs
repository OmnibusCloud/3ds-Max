namespace OutWit.Render.ThreeDsMax.Plugin.Export;

/// <summary>
/// The single well-known set of OmnibusCloud SaaS defaults the plugin ships with.
/// On-prem installs override these through the plugin UI; nothing else in the
/// codebase should hard-code these URLs.
/// </summary>
public static class OmnibusCloudDefaults
{
    #region Constants

    public const string SERVER_URL = "https://engine.omnibuscloud.com";

    public const string IDENTITY_URL = "https://auth.omnibuscloud.com";

    #endregion
}
