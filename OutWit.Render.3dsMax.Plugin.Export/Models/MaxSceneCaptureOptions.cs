namespace OutWit.Render.ThreeDsMax.Plugin.Export.Models;

/// <summary>
/// User-facing options that shape how the current scene is captured into a snapshot.
/// </summary>
public sealed class MaxSceneCaptureOptions
{
    #region Constants

    public static readonly MaxSceneCaptureOptions Default = new();

    #endregion

    #region Properties

    // Render each V-Ray scanned (.vrscan) material's diffuse into a texture with the user's
    // LOCAL V-Ray (render-to-texture) before export, so the measured material's spatial detail
    // (fabric weave, suede nap) survives the neutral conversion. Opt-in: it adds local V-Ray
    // render time to the export, which the UI states explicitly.
    public bool BakeVRayScannedMaterials { get; set; }

    #endregion
}
