# OmnibusCloud Blender Integration

Render Blender scenes on the [OmnibusCloud](https://omnibuscloud.com) distributed-compute
network without leaving Blender: sign in, pick the output, press **Render** — the scene is
uploaded, split across the network, and the finished frames or video come back straight into
Blender.

Two cooperating components ship in a single extension zip:

- **`OutWit.Render.BlenderAddon`** — a pure-Python Blender extension
  (`outwit_render_bridge`, Blender 4.0+, no third-party Python dependencies). Adds an
  **OmnibusCloud** tab to the 3D-viewport sidebar.
- **`OutWit.Render.BlenderBridge`** — a .NET sidecar process bundled inside the zip,
  launched and supervised by the addon. It owns the OIDC sign-in (PKCE, system browser,
  token refresh) and all cloud communication, and serves a loopback REST API the addon
  talks to. Tokens are encrypted at rest and never enter Blender.

---

## Install

1. Download the zip for your platform from the
   [latest release](../../releases/latest) — `win-x64`, `linux-x64`, or `osx-arm64`
   (self-contained; no .NET installation required).
2. In Blender: `Edit → Preferences → Add-ons → ⌄ → Install from Disk…`, pick the zip,
   and enable **OmnibusCloud Render Bridge**.
3. Open the **OmnibusCloud** tab in the 3D-viewport sidebar (`N` key) and click
   **Sign In** — the system browser opens, sign in with your OmnibusCloud account.

Every release ships a `SHA256SUMS` file with a detached GPG signature; the macOS bridge
binary is code-signed and notarized.

---

## Using it

- **Output** — `Image` (a single frame, optionally split into tiles across machines) or
  `Animation` (a frame range, delivered as an image sequence or an encoded video).
- **Target** — the whole network, or one of the client groups you are authorized to use.
- **Render** — one click. The addon packs the saved `.blend` with its assets and uploads
  it through the bridge off the UI thread; the server splits the work and dispatches it
  across the selected machines.
- **Progress** — live progress and **Cancel** right in the panel.
- **Results** — frames or video download back through the bridge; open the folder or
  load the result straight into Blender.

---

## Capabilities

### Sign-in & session

- OIDC sign-in through the system browser (PKCE, loopback callback) — tokens never
  enter Blender.
- The refresh token is stored encrypted per OS user; the session survives Blender
  restarts and addon updates, so you sign in once.
- Account display and sign-out right in the panel.

### Bridge lifecycle

- The bundled bridge starts automatically the first time the panel is shown and serves
  the whole Blender session.
- An already-running bridge is adopted (after opening another `.blend` or restarting
  Blender) instead of spawning a duplicate.
- The bridge watches its owning Blender process and exits when it dies, with a grace
  period for an active job.

### Output modes & formats

- **Image** — render the current frame.
  - Optional **frame splitting**: the frame is cut into tiles (configurable grid and
    overlap), rendered across machines, and stitched server-side (PNG, JPEG).
- **Animation** — render the scene frame range, delivered as an **image sequence** or
  an **encoded video**.
- Image formats: PNG, OpenEXR, JPEG, TIFF, WebP — alpha (including Film → Transparent)
  and bit depth travel with the scene.
- Video presets: MP4 / H.264, MP4 / H.265, WebM / VP9, MOV / ProRes 422 HQ,
  MOV / ProRes 4444 (carries alpha). Frame rate comes from the scene; quality (CRF) is
  adjustable where the codec supports it.
- Verified with Cycles, Eevee, and Grease Pencil scenes.

### Targeting

- Render on the whole network, or on one of your authorized client groups — the choice
  is driven by your actual authorization scope.

### Scene handling

- The saved `.blend` is packed with its assets and uploaded automatically.
- Upload caching: the scene is re-uploaded only when the file or its output settings
  actually changed.
- A live save gate: unsaved changes surface a one-click **Save scene** button before
  render.
- Pre-launch checks mirror the server's preflight: unsupported combinations (e.g. a
  tiled WebP still) are blocked before submission, with a one-click fix where possible.

### Job lifecycle

- Packing, upload, and submission run off the UI thread — Blender stays responsive, and
  the panel flips into the progress view immediately.
- Live distributed progress; a confirmation prompt guards very large frame batches.
- **Cancel** propagates to the assigned machines and interrupts the distributed batch.
- **Reset** recovers from any error state without restarting Blender.
- Results download through the bridge: open the file, open the folder, or load the
  image straight into Blender.

### Panel & settings

- One status line and at most one actionable blocker (with a fix button) at any moment;
  diagnostics live in a collapsed Advanced section (connection control, account & scope,
  scene summary, manual validate/preflight, last error, component versions).
- The panel suggests a suitable mode for the scene you open (e.g. Animation for a
  video-format scene) but never launches anything by itself.
- Render settings (frame splitting, tiles, video preset, quality, target) persist per
  OS user across sessions; scene-bound values (format, frame, FPS) live in the `.blend`
  itself. A master toggle in the add-on preferences turns persistence off.

---

## For developers

Everything here is open source — use it as-is, or take it as an example of an
OmnibusCloud **initiator** (an application that submits work to the network). The bridge
builds against **nuget.org only** (see [`nuget.config`](nuget.config)), through the same
public packages available to any developer.

| Project | What it is |
| --- | --- |
| [`OutWit.Render.BlenderAddon`](OutWit.Render.BlenderAddon/) | The Blender extension package + packaging scripts. Headless Python tests in `Tests/`. |
| [`OutWit.Render.BlenderBridge`](OutWit.Render.BlenderBridge/) | The .NET sidecar: OIDC auth, blob upload/download, job submit/monitor/cancel; loopback REST server; lease watchdog (exits when its Blender owner dies). |
| [`OutWit.Render.BlenderBridge.Tests`](OutWit.Render.BlenderBridge.Tests/) | Unit tests: REST transport, settings store, lease and connection-context lifecycle. |
| [`OutWit.Render.BlenderBridge.LocalTests`](OutWit.Render.BlenderBridge.LocalTests/) | Integration tests, including `Live/` suites that run against the deployed `engine.omnibuscloud.com`, and runnable end-to-end examples with committed test scenes ([`Canonical/`](OutWit.Render.BlenderBridge.LocalTests/Canonical/)). |

---

## Build & packaging

The shipped artifact is a per-platform Blender extension zip with the bridge bundled
inside (`outwit_render_bridge/bridge/<rid>/...`):

- Local: [`OutWit.Render.BlenderAddon/Build-BlenderAddonPackage.ps1`](OutWit.Render.BlenderAddon/Build-BlenderAddonPackage.ps1)
  → zips in `dist/`. See the [addon README](OutWit.Render.BlenderAddon/README.md) for
  options and details.
- CI: [`.github/workflows/addon.yml`](.github/workflows/addon.yml) — an `addon-v*` tag
  builds all three platforms, signs and notarizes the macOS bridge, generates
  `SHA256SUMS` (+ GPG signature), and attaches everything to the GitHub Release.

---

## License

MIT — see [LICENSE](LICENSE).
