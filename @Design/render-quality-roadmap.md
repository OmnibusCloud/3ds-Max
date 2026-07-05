# OmnibusCloud 3ds Max — Render-Quality Roadmap

**Date:** 2026-07-05. Goal: turn the plugin into a **working tool** — a user opens a typical 3ds Max scene, signs in, hits Render, and gets back an image that faithfully represents that scene. This roadmap is the plan we execute against; it sits under Milestone 2 of `omnibuscloud-3dsmax-production-plan.md` and drives the render-fidelity items (2.13, 2.16–2.18) plus the robustness gaps found on 2026-07-05.

**Why a dedicated roadmap:** the 2026-07-05 live renders (hardwood_hdri, Maxine) showed the render path has several *compounding* collector/mapper gaps — overexposure, aborts on empty meshes, wrong camera framing, under-lit HDRI. "Make scenes look normal" is therefore not a single tweak but a measured workstream with a quality bar and a regression corpus. Everything here is **client-side** (the plugin's collector + `MaxSceneDccSceneMapper`), so fixes ship in a plugin release with **no server deploy**, and are validated on this dev box via `3dsmaxbatch` + the live farm.

---

## 1. Quality bar — what "faithful" means

A scene render is **acceptable** when all five hold (visual review against the Max viewport/expected look):

1. **Renders** — no validation/build abort; an image comes back.
2. **Framing** — the intended camera view (recognizable composition), not a wrong/empty/too-close angle.
3. **Exposure** — sanely lit: not blown to white, not black; highlights tone-mapped.
4. **Materials** — base colours and material character are recognizable (not all-white/all-grey).
5. **Textures & environment** — referenced bitmaps and the HDRI/background are present.

**Scope (what the tool targets).** *Supported* (must meet the bar): standard + photometric lights; standard/physical (Principled-mappable) materials; bitmap textures; HDRI/environment maps; standard + target cameras; static and keyframed/skinned geometry (baked to per-frame vertices). *Best-effort / documented limitation* (may differ, must not crash): Arnold/OSL node-graph shaders, procedural maps, render-element/AOV setups, exotic light types, crowd/particle systems beyond the baked-deformation path. The tool is judged on the **Supported** set; Best-effort scenes must degrade gracefully (render *something*, with a Warning), never abort or mislead.

---

## 2. Regression corpus (fixed test set)

Ten `@Data` scenes, chosen for feature coverage. Tier A = Supported (must meet the bar); Tier B = advanced/best-effort (must render + be exposure-sane, framing/materials may be imperfect). Rerun the whole corpus after every wave.

| # | Scene | Tier | Exercises |
|---|---|---|---|
| 1 | `Characters/Complete/Maxine.max` | A | Skinned character, textures, photometric lights, single camera |
| 2 | `Characters/Complete/Ape.max` | A | Character, explicit lights (near-zero attenuation) |
| 3 | `Raytrace/AdvancedExamples/A08trans.max` | A | Materials, transparency, single camera, standard lights |
| 4 | `Flex/Flex-TeaPotBounce.max` | A | Baked deformation animation |
| 5 | `Displacement/Displacement-MoonRock.max` | A | Displacement + texture |
| 6 | `Lighting/Lighting-Vertex.max` | A | Vertex colours |
| 7 | `CameraEffects/MotionBlur-DragonFlying_Scanline.max` | A | Motion blur, animation |
| 8 | `ViewportRendering/troll_cleric29_max2018.max` | A | Dense textured mesh |
| 9 | `Rendering/hardwood.mat.lab/hardwood_hdri_Arnold.max` | B | HDRI-dominant interior, 4 cameras, Arnold materials |
| 10 | `Crowd/MotionClips/FishTank.max` | B | Crowd animation (memory stress) |

Full paths are under `C:\Workspace\OmnibusCloud\3ds-Max\@Data\3ds_max\Scenes\`.

---

## 3. Validation method

- **Harness:** a repeatable script that, per corpus scene, runs `3dsmaxbatch` with the plugin's collector to export the DCC scene, submits it to the live farm (`RenderDccSceneStill`, a mid-scene frame for animated ones), downloads the PNG, and writes all results + a status table to `@Output/render-quality/<wave>/`.
- **Review:** each render is eyeballed against the five acceptance criteria; the status table records per-scene pass/fail per criterion (Renders / Framing / Exposure / Materials / Textures).
- **Gate:** a wave is done when its target criterion passes for all in-scope tiers (see waves). We never regress a previously-passing criterion.
- **Cost control:** low samples (32–48) + small resolution for iteration; the corpus is 10 scenes so a full sweep is affordable.

---

## 4. Waves

Each wave: implement → build plugin → run corpus sweep → visual review → iterate on numbers → commit. Ship a plugin release at the end of Wave 2 and Wave 4.

### Wave 0 — Baseline & harness  *(no code changes)*
Build the sweep harness; run it once to capture the **current** state of all 10 scenes across the five criteria. This is the scoreboard we improve. Deliverable: `@Output/render-quality/baseline/` + a filled status table.
**Gate:** baseline table exists; we know exactly which scene fails which criterion.

### Wave 1 — Robustness: every Tier-A scene *renders* (criterion 1)
Remove the hard aborts and crashes so a picture always comes back.
- **2.16c** Skip empty/degenerate meshes (no positions) at collection, with a Warning. *(Maxine abort.)*
- **2.13** Skip zero-intensity lights instead of exporting them (validator blocks `Intensity ≤ 0`), with a Warning. *(Viewport-Dragon.)*
- Skip other non-convertible objects the validator would reject (degenerate cameras/lights), logging each drop.
- **2.14** Bound collector memory for deformation sampling so crowd scenes don't OOM (cap/stream per-frame capture; diagnostic if truncated). *(FishTank — Tier B, but the crash is fixed here.)*
**Gate:** all 10 scenes produce an image (Tier B included); no aborts/OOM.

### Wave 2 — Exposure: no Tier-A scene blown out or black (criterion 3)
The #1 fidelity bug. Fix systematic overexposure.
- **2.16b** Overhaul light-power calibration: calibrate each light against its **actual** distance to the lit geometry, not the scene radius; add a hard **max output-wattage clamp**; keep the low-end boost so nothing renders black.
- **2.16** Normalize photometric (`ILightscapeLight`) intensity to a multiplier-equivalent before calibration.
- **2.18a** Set a sensible default `ViewTransform` (AgX) + exposure so residual highlights tone-map instead of clipping.
- Unit-test the mapper clamp/calibration (pure logic, no Max needed).
**Gate:** every Tier-A scene is sanely exposed; Tier-B not blown to white. **→ Ship plugin release.**

### Wave 3 — Framing: correct camera (criterion 2)
- **2.17** Resolve the **active render camera** (the render/active-viewport camera), not "first camera"; mark it so the generator renders through it (reorder so it's first, or add `RenderSettings.ActiveCameraId` + a small generator change).
- Verify target-camera orientation/look-at is captured correctly (hardwood's cameras all framed wrong — investigate coordinate/target handling).
- Fallback framing: when no camera is authored, frame the synthesized camera to the scene bounds.
**Gate:** every Tier-A scene shows the intended composition.

### Wave 4 — Materials & environment fidelity (criteria 4 & 5)
- **2.18b** Carry the Max environment-map intensity into `World.Strength` so HDRI scenes are correctly lit (not under-lit).
- Material coverage: audit which Max material types map to Principled correctly; read Arnold/physical material base params where feasible; **Warn** on unmapped procedural maps instead of silent loss (M7).
- Texture coverage: confirm all referenced bitmaps resolve/upload (already working for the common case); Warn on unresolved.
**Gate:** every Tier-A scene has recognizable materials/colours + present textures/HDRI. **→ Ship plugin release (release-candidate quality).**

### Wave 5 — Sign-off & docs
- Final corpus sweep; all Tier-A scenes meet all five criteria; Tier-B render + exposure-sane with documented caveats.
- Document supported vs best-effort scene features (user-facing) + the diagnostics users will see for dropped content.
- Fold results back into the production plan; bump plugin to a stable version.
**Gate:** corpus sign-off table green for Tier A.

---

## 5. Status tracker

Updated after each wave (✅ pass / ⚠️ partial / ❌ fail / — not yet run).

| Scene | Renders | Framing | Exposure | Materials | Textures |
|---|---|---|---|---|---|
| Maxine | ❌ (empty mesh) | — | ❌ (579k W) | — | — |
| Ape | — | — | — | — | — |
| A08trans | — | — | — | — | — |
| Flex-TeaPotBounce | — | — | — | — | — |
| Displacement-MoonRock | — | — | — | — | — |
| Lighting-Vertex | — | — | — | — | — |
| MotionBlur-DragonFlying | — | — | — | — | — |
| troll_cleric29 | — | — | — | — | — |
| hardwood_hdri_Arnold (B) | ⚠️ (renders) | ❌ (wrong cam) | ❌ (4.7M W) | ⚠️ | ✅ |
| FishTank (B) | ❌ (OOM) | — | — | — | — |

Known data points from 2026-07-05 pre-work renders are pre-filled; Wave 0 fills the rest.

---

## 6. Parallel track (not this roadmap, tracked in the main plan)

The app-robustness M2 items (status enum, job re-attach, real settings, logging, MemoryPackOrder, capacity preflight 2.15) and the M3 design-compliance work run separately. This roadmap is specifically the **render-quality** path to a trustworthy image; the two converge at the Milestone-4 release.
