using System.IO;
using System.IO.Compression;
using MemoryPack;
using OutWit.Controller.Render.Dcc.Model;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Services;

/// <summary>
/// Packs a neutral DCC scene into the gzipped MemoryPack payload the *Packed farm scripts
/// expect (Render.UnzipDccScene expands it server-side). Scene payloads compress roughly
/// 6-10x — deformation-heavy characters drop from ~40 MB to ~7 MB — which is the
/// difference between seconds and minutes on an ordinary uplink.
/// </summary>
public static class MaxScenePayloadPacker
{
    public static byte[] Pack(DccSceneData scene)
    {
        var payload = MemoryPackSerializer.Serialize(scene);

        using var output = new MemoryStream();
        // Optimal balances submission latency against upload size: SmallestSize costs whole
        // extra seconds of CPU on 100+ MB payloads for low single-digit percent gains.
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(payload, 0, payload.Length);

        return output.ToArray();
    }
}
