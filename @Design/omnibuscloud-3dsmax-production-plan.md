# OmnibusCloud 3ds Max — Production-Readiness Plan

**Date:** 2026-07-04. Based on three code audits run this day: (1) Render.Dcc controller audit (Controllers repo), (2) plugin functional audit, (3) design-compliance audit vs `@Design/` mockups. Versions audited: plugin @ `eedb911` (2026-06-14), Render.Dcc 1.4.0 / Dcc.Model 1.4.0 / Dcc.Scripts 1.1.0, against production Render 1.23.10 / Render.Model 1.6.1 / Render.Scripts 1.4.0 / Grid 1.1.1 (WitCloud v1.6.35-beta).

---

## Audit verdicts (summary)

### Render.Dcc controller — no functional extension needed; hardening only
Dcc builds a **self-contained** `.blend` (`pack_all()` embeds every texture/HDRI) and hands it to the standard `Render.SplitBatched`/`SplitTiles` + `Grid.ForEach` pipeline. Therefore every recent Render improvement applies automatically or is irrelevant:
- Stage-1 node-side attachment delivery: **not needed** — assets travel inside the blend blob; `Split*` tolerates the missing sidecar.
- Grid 1.1.1 fault-tolerant ForEach + GPU device fallback: **inherited for free** (node/dispatch-side; Dcc ships zero node-side code).
- Delegated bake (`Render.BakeSimulation`): **not applicable** — Dcc scenes are pre-baked by construction (per-frame vertex caches → shape keys); Max sims cannot be re-simulated in Blender, so baking is the exporter's job.
- Script family: mode parity complete (`ExportBlend`, `Still`, `StillTiled`, `Frames`, `Video`); engine comes from `RenderOptions` at render time.

Hardening findings (Controllers repo): quadratic generated Python for UV/vertex-color layers (**DCC-H1**); no large-scene ref path (**DCC-H2**, demand-driven); no explicit `[MemoryPackOrder]` in Dcc.Model (**DCC-M1** — wire-compat risk, plugin serializes these very types); missing ADR-001 bracket on Render ≥ 1.18 (**DCC-M2**); temp workdir leak on failed builds (**DCC-M3**); NaN/Inf produce invalid Python (**DCC-M4**); AxisSystem validated but ignored (**DCC-M5**); missing texture fails late inside Blender (**DCC-M6**); stale README (**DCC-L1**); minor sweep items (**DCC-L2..L8**). Tests: 104 + 230 green (incl. real headless-Blender fixtures).

### Plugin — pipeline core is sound; four hard breaks explain "doesn't really work"
Collector → Dcc mapping → OIDC → submission to the correct production scripts is implemented and contract-aligned (script names/args match the shipped `.wit`; SDK 1.1.3 matches). The breaks:
- **PLG-H1** ExportBlend (the Export dialog's *default* target) always fails preflight — `"ExportBlend"` missing from `VALID_RENDER_MODES`, and no group/all-clients set for export jobs.
- **PLG-H2** Persisted DPAPI session never restored on the production path — users must browser-sign-in every Max session; menus stay greyed.
- **PLG-H3** 3ds Max SDK called from thread-pool threads (`Task.Run` → preflight/collector → `GlobalInterface.Instance`) — the most plausible source of instability/crashes.
- **PLG-H4** Frame-sequence results never delivered — transport always reads `GetResultAsync<Guid>`, but `RenderDccSceneFrames` returns a blob collection.
- **PLG-H5** Cancel doesn't cancel — `Jobs.CancelAsync` never invoked; farm keeps rendering; UI stuck at "Cancelling…".
Plus: substring-matching failure detection (M3), no job re-attach after dialog close (M2), silent drop of topology-changing/simulated content (M1), dead settings façade — tiles/codec/CRF persisted but hardcoded in transport (M5), Serilog bootstrapped with zero log calls (M6), bitmap-file-only dependency collection — IES/procedural/XRef dropped silently (M7), harness `ExportWindow` shipped with its own detached session (M8), NU1903 MessagePack vulnerability (M9), fire-and-forget VM init (M4).

### Design compliance — palette правильная, but stock WPF chrome + missing surfaces
- **DSN-H1** No control styles at all: theme dictionaries hold brushes only, so buttons/combos/checkboxes/progressbars render default light-grey Aero on dark windows. Root cause of most of the perceived mismatch.
- **DSN-H2** Toolbar (section 2) entirely missing; macros have no icons.
- **DSN-H3** Render dialog lifecycle states (7 same-size cards: Blocker/Submitting/Uploading/Running/Finalizing/Completed/Failed/Cancelling) not built — phase model exists, View shows only a 4-px progress bar.
- **DSN-H4** Diagnostics/Details dialog (544×520) missing — "Details…" visibly does nothing.
- **DSN-H5** Menu lacks account header, separators, About item, dynamic Sign in/out label.
- Medium: Output quick settings absent (Format/Tiles/Codec/FPS/Range), duplicate in-window header, settings sidebar/account card/Image-format row, export dialog deltas, no owner HWND/modality, light Navy token. What matches (don't redo): all palette hexes, dialog geometry, theme service, Sign-in dialog, Target group, menu gating.

---

## Work plan — four milestones

### Milestone 1 — Make it work (plugin hard breaks + Dcc correctness)
| ID | Item | Size | Where |
|---|---|---|---|
| 1.1 | ExportBlend preflight: allow mode with export-appropriate checks; transport test | S | Plugin |
| 1.2 | Session restore: `TryRestoreSessionAsync` on bootstrap + before browser flow; menu gate refresh on auth change | S/M | Plugin |
| 1.3 | Marshal all Max-SDK access to the main thread (capture synchronously before `Task.Run`, or dispatcher decorator); audit every `Task.Run` reaching `Capture()`/preflight | M | Plugin |
| 1.4 | Frames result retrieval: mode-aware result shape + per-frame download folder + naming | S/M | Plugin |
| 1.5 | Real cancel: `Jobs.CancelAsync`, `Cancelled` terminal state, CancellationTokens through VM chain | S | Plugin |
| 1.6 | DCC-H1: hoist UV/vertex-color list literals in `DccBlenderNodeEmitter` (mirror deformation pattern) | S | Controllers |
| 1.7 | DCC-M3: delete build workdir on failure | S | Controllers |
| 1.8 | DCC-M4+M6: fail-fast validation — every ImageAsset resolves to an attachment; reject non-finite doubles; `FormatDouble` throws on NaN/Inf | S | Controllers |

**Gate:** live farm smoke from the plugin UI: Still, StillTiled, Frames (results downloaded), Video, ExportBlend; cancel mid-render actually cancels.

### Milestone 2 — Make it robust
| ID | Item | Size | Where |
|---|---|---|---|
| 2.1 | Status-model hardening: surface `ProcessingJobStatus` enum (no substring matching); distinguish refresh-error vs job-failure; poll timeout/backoff | S/M | Plugin |
| 2.2 | Job re-attach: persist last job id; "resume tracking" on dialog reopen | M | Plugin |
| 2.3 | Sim/deformation diagnostics: Warning on dropped deformation (topology change) and skipped non-mesh objects; document the "bake happens in the exporter" contract (no BakeAndRender for Max) | M | Plugin |
| 2.4 | Make Settings real: feed tiles/overlap/codec/CRF/fps/samples/denoise/format into `Create*Options`; remove dead fields | S | Plugin |
| 2.5 | Actually log: Serilog calls at submission/refresh/upload/auth boundaries; honor LogLevel | S | Plugin |
| 2.6 | Fix fire-and-forget VM init (+ `SignOut` async-void catch) | S | Plugin |
| 2.7 | Attachment coverage: collect IES for photometric lights; warn on procedural maps; explicit XRef/point-cache policy diagnostics | M | Plugin |
| 2.8 | Upload/submit progress: blob-upload progress → `Uploading(%)` phase; evaluate blob-backed scene payload for big scenes (with DCC-H2) | M | Plugin |
| 2.9 | DCC-M1: explicit `[MemoryPackOrder]` across Dcc.Model + layout regression test (wire-identical, mirrors Render.Model 1.6.1) | M | Controllers |
| 2.10 | DCC-M2: ADR-001 — Dcc.Scripts bracket `OutWit.Controller.Render [1.18.0, 2.0.0)`; module/csproj min Render ≥ 1.18 | S | Controllers |
| 2.11 | DCC-M5: AxisSystem policy — enforce Z-up/right-handed at validation (reject others) or implement conversion; plugin already exports Z-up | S | Controllers |
| 2.12 | DCC sweep (L2..L6): dedupe `ToPythonStringLiteral`, `InnerClone`/`Is` consistency, cancellation in `MaterializeAttachmentsAsync`, build-Blender watchdog, sanitizer-collision test | S | Controllers |

**Gate:** all unit suites green; 3dsmaxbatch smokes green; kill-a-node-mid-render chaos check (Grid reassignment observed from the plugin); replayable logs for a failed job.

### Milestone 3 — Make it match the design (@Design sections)
Order matters: 3.1 first — it fixes most of the perceived mismatch everywhere at once.
| ID | Item | Size | Nature |
|---|---|---|---|
| 3.1 | `MaxControls.xaml` style dictionary: Button (default/primary/danger/disabled), ComboBox, TextBox, CheckBox, RadioButton, ProgressBar, toggle, segmented control — keyed to existing palette brushes; merge into all dialogs | L | XAML |
| 3.2 | Render dialog state swap (4.1.3): swappable work-area templates for all 7 states; VM emits `Uploading/Finalizing/Blocked`; Open folder / New render / Copy log / Retry | L | XAML+VM |
| 3.3 | Render dialog Output quick settings (4.1.2): Format, Tiles ×/overlap, Codec, FPS, Range (binds settings from 2.4); remove duplicate in-window header; spec scene line | M | XAML+VM |
| 3.4 | Menu completion (1.1/1.2): account header (rebuilt on auth signal), separators, About → `ShowSettings(About)`, dynamic Sign in…↔Sign out, label fixes, retire legacy OutWit Export macro | M | MaxScript+C# |
| 3.5 | Diagnostics dialog (4.1.5): 544×520 window over existing `DiagnosticsVm`/`SummaryVm`; Validate/Preflight/Close | M | XAML |
| 3.6 | Toolbar (section 2): macro `icon:` bitmaps + default "OmnibusCloud" toolbar registration; Render icon accent lime | M | MaxScript+assets |
| 3.7 | Settings polish (4.3): sidebar nav w/ icons + version footer; account card + Sign out; Image-format row; friendly display names; theme row read-only or actually consume `ThemeMode` | M | XAML+VM |
| 3.8 | Export dialog polish (4.2): scene line; Copy log + Retry on Failed; percent/phase while exporting; Completed meta; hide Cancel in Ready | S | XAML+VM |
| 3.9 | Status bar fidelity (3): terminal wording, progress bar, Uploading/Finalizing phases (falls out of 3.2) | S/M | behavior |
| 3.10 | Owner/modality (MX-4): Max HWND owner via `WindowInteropHelper`; Show vs ShowDialog decision | S | behavior |
| 3.11 | Cosmetics: circular sign-in spinner + footer band, presence dot + "Close to keep working" hint, light Navy token | S | XAML |

**Gate:** side-by-side review of every dialog/surface vs the `@Design` mockups in both dark and light Max themes.

### Milestone 4 — Ship
| ID | Item | Size |
|---|---|---|
| 4.1 | Packaging: retire `ExportWindow` from PackageContents (or unify its session), Authenticode-sign assemblies (reuse the Blender-bridge signing rig), bump MessagePack (NU1903), clean CS8618, versioned dist + Series range strategy, installer story | M/L |
| 4.2 | Docs: Dcc README rewrite (real supported subset, scripts table, pack-all note); **Render controller README actualization** (delegated-bake section — long-standing debt); plugin README/user guide | M |
| 4.3 | Publish + deploy: Dcc.Model 1.4.1 + Dcc 1.5.0 + Dcc.Scripts 1.1.1 via publish.yml; WitCloud pins Dcc `1.4.*`→`1.5.*` (bump in ALL csprojs that reference it — NU1605 lesson) + image tag + user deploys | S/M |
| 4.4 | Final QA matrix: live e2e per mode × engine on real scenes (incl. an animated/deformation scene, HDRI scene, photometric lights); plugin version 1.0.0-beta | M |

Deferred / demand-driven: DCC-H2 large-scene ref path (blob-backed `DccSceneRef` + binary geometry sidecar); varying-topology sim export (exporter/contract limitation); Max theme live-change subscription.

---

## Versioning & release choreography
- Controllers: Dcc.Model **1.4.1** (MemoryPackOrder, wire-identical), Dcc **1.5.0** (validator behavior changes: fail-fast, axis policy), Dcc.Scripts **1.1.1** (ADR-001 bracket). Bump `<Version>` on main as soon as work starts (collision lesson).
- WitCloud: Dcc pin `1.4.*` → `1.5.*` in every referencing csproj; Dcc.Scripts `1.1.*` covers 1.1.1; new `v*` tag → docker image → user deploys + worker supervisor restart.
- Plugin: independent versioning; target **1.0.0-beta** at Milestone 4 (mirrors the Blender addon release pattern).
