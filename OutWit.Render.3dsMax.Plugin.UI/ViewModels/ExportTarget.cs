namespace OutWit.Render.ThreeDsMax.Plugin.UI.ViewModels;

/// <summary>
/// Export dialog target (design 4.2): a Blender <c>.blend</c> built on the server from the neutral DCC
/// scene (the default, a standalone 3ds Max → Blender converter), or the raw neutral DCC JSON written
/// locally.
/// </summary>
public enum ExportTarget
{
    Blend,
    DccJson
}
