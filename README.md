# OmnibusCloud 3ds Max Integration

Render 3ds Max scenes on the [OmnibusCloud](https://omnibuscloud.com) distributed-compute
network: the plugin exports the open scene into a neutral DCC payload, uploads it with its
texture assets, and the network renders it across many machines — stills, frame ranges,
tiled stills, or encoded video.

> **Status: public alpha — functionally complete for a first production release.**
> Stills and video render end-to-end from real `.max` scenes: skinned character
> animation, Scanline and PBR materials, **V-Ray materials, lights, environments and
> physical-camera exposure** (including an opt-in local bake for scanned materials),
> raytraced glass and mirrors, mirrored geometry, motion blur, HDRI / procedural /
> screen-mapped environments — validated scene-by-scene against native 3ds Max renders
> and blind-tested on scenes the pipeline has never seen. Scene capture is bulk-read and
> multi-core (heavy scenes export in seconds), and everything the exporter approximates
> leaves a **named diagnostic** instead of failing silently. The Export dialog can also
> return the scene as a self-contained **`.blend` file** with all textures packed.
> See **[Capabilities & Known Limitations](docs/capabilities-and-limitations.md)** for the
> honest feature matrix: what transfers faithfully, what is approximated, what is not
> supported yet, and what is planned. The in-Max UI is minimal for now (a proper
> submission/monitoring/settings experience is the current work — see the sibling
> [Blender integration](https://github.com/OutWitLab/OmnibusCloud-Blender) for the UX this
> repo is converging on) — functional feedback is what this alpha is for.

---

## How it works

The plugin never ships `.max` files to render nodes. Instead it converts the scene into a
**neutral, typed DCC payload** (`DccSceneData` from the public
`OutWit.Controller.Render.Dcc.Model` package):

```
3ds Max scene
  → in-process snapshot (geometry, materials, lights, cameras, render settings)
  → MaxSceneDccSceneMapper → DccSceneData (neutral contract)
  → validation (plugin-side mirror of the server preflight)
  → upload (scene payload + referenced image assets as blob attachments)
  → server builds the render scene from the neutral payload
  → standard OmnibusCloud distributed render pipeline (Cycles / Eevee)
  → frames / video downloaded back
```

Authoring is Windows-only (3ds Max), rendering is cross-platform — the neutral contract is
what makes the heterogeneous network possible.

What travels well today: meshes with full smoothing fidelity (including mirrored
objects), skinned/deforming animation on a frame-exact timeline, Standard/Blinn and PBR
materials (plus Raytrace glass and mirrors), **V-Ray**: VRayMtl, scanned materials (with
an opt-in local bake), VRayLight/VRaySun/dome-HDRI environments and physical-camera
exposure, bitmap and baked procedural textures with authored tiling, standard and
photometric lights with their real decay semantics, motion blur, HDRI / procedural /
screen-mapped environments, and cameras (including a synthetic camera from the active
perspective viewport when the scene has no render camera). The full matrix — including
approximations and unsupported families, each of which now surfaces as a named
diagnostic at export — lives in
[Capabilities & Known Limitations](docs/capabilities-and-limitations.md).

---

## Projects

| Project | What it is |
| --- | --- |
| [`OutWit.Render.3dsMax.Plugin`](OutWit.Render.3dsMax.Plugin/) | The plugin shell: 3ds Max SDK integration (`Autodesk.Max`), scene snapshot collection, `ApplicationPlugins` package template, MaxScript entry points, install scripts. |
| [`OutWit.Render.3dsMax.Plugin.Export`](OutWit.Render.3dsMax.Plugin.Export/) | The export engine: snapshot → `DccSceneData` mapping, validation, launch-package preparation, upload, connected submission and result download. Max-SDK-free (`net10.0`). |
| [`OutWit.Render.3dsMax.Plugin.UI`](OutWit.Render.3dsMax.Plugin.UI/) | The WPF exporter window (MVVM). |
| [`OutWit.Render.3dsMax.Plugin.Export.Tests`](OutWit.Render.3dsMax.Plugin.Export.Tests/) | Unit tests (no 3ds Max required) + `3dsmaxbatch` smoke suites that drive a real 3ds Max install against real sample scenes (see [`@Data/README.md`](@Data/README.md)). |
| [`OutWit.Render.3dsMax.Plugin.LocalTests`](OutWit.Render.3dsMax.Plugin.LocalTests/) | `Live/` suites that submit DCC-scene render jobs to the deployed `engine.omnibuscloud.com` and verify the downloaded results. `[Explicit]`, gated on `OMNIBUSCLOUD_API_KEY`. |

---

## Build & test

Requirements: .NET 10 SDK. For the plugin shell: `Autodesk.Max.dll` dropped into
`OutWit.Render.3dsMax.Plugin/Libs/` (from a 3ds Max 2027 install) or an
`ADSK_3DSMAX_x64_2027` environment variable pointing at it.

```powershell
dotnet build OutWit.slnx                 # everything builds against nuget.org only
dotnet test OutWit.slnx                  # unit tests run anywhere; smokes self-skip without 3ds Max / @Data
```

Install the plugin into a local 3ds Max:

```powershell
.\OutWit.Render.3dsMax.Plugin\Scripts\Install-OutWit.Render.3dsMax.Plugin.ps1
```

This stages the `ApplicationPlugins` package into
`%ProgramData%\Autodesk\ApplicationPlugins\OutWit.Render.3dsMax.Plugin`; restart 3ds Max
and use the **OutWit** menu / macro category to open the exporter window.

---

## For developers

Everything here is open source — use it as-is, or take it as an example of an OmnibusCloud
**initiator** for a DCC host application. The export layer builds against **nuget.org
only** (see [`nuget.config`](nuget.config)), through the same public packages available to
any developer: `OutWit.Cloud.SDK`, `OutWit.Controller.Render.Dcc.Model`,
`OutWit.Controller.Render.Model`, `OutWit.Common.*`.

---

## License

MIT — see [LICENSE](LICENSE).
