# OmnibusCloud 3ds Max Plugin — UI/UX & Ship Plan (Waves)

> **STATUS (2026-07-11): ACTIVE plan for the UI/UX + ship milestone.** Supersedes the phase
> list in `omnibuscloud-3dsmax-plugin-implementation-plan.md` (its "current state" and MX
> reconciliation notes remain valid reference). Functional core is production-ready
> (plugin 0.7.47 + WitCloud v1.6.55, 21/21 farm corpus green).
>
> Inputs: production-plan **Milestone 3** (3.1–3.11) + mockups `3dsmax-section-*.html`
> (palette & MX-1…MX-20 in section 5) + session goals 2026-07-11: visual parity with the
> design, overlap fixes, explicit UX flow, WiX installer, versioning/About, pipeline
> signing, WitCloud-client-style logs, public OmnibusCloud branding of artifacts.

## Verified current state (code inventory, 2026-07-11)

**Already in place (do not rebuild):**
- Dialogs exist: `RenderDialog`, `ExportDialog`, `SettingsDialog`, `SignInDialog` (all
  `ResizeMode=NoResize`, code-behind-free MVVM). Legacy `ExportWindow` still ships as a
  dev harness (blue `#FF2F6FED`, "OutWit Export" title).
- Theming: `MaxPalette.xaml` / `MaxPaletteLight.xaml` (brush keys `Max.Brush.*`, accent
  `#5BBF4A` / on-accent `#1A2B4C`), `MaxThemeService` reads Max color manager, applied
  per-dialog in `MaxPluginBootstrap`.
- Lifecycle model complete in code: `MaxRenderPhase` (14 phases incl. Uploading/
  Finalizing/Cancelling), `MaxRenderStatus` snapshot, 2s poll loop. **Render/Cancel
  enablement already correct**: `CanRender = signed-in && !IsActiveJob`,
  `CanCancel = IsActiveJob && !cancelRequested` (`RenderDialogViewModel.cs:290-291`).
- Logging already mirrors the WitCloud client: Serilog daily-rolling
  `%APPDATA%\OmnibusCloud\Logs\3dsmax-plugin-.log` (`MaxPluginLogging.cs`),
  `MaxDiagnosticsLauncher` (open folder / latest log) wired to Settings ▸ Diagnostics.
- UI brand strings mostly migrated: window titles, menu, 5 of 6 macroscripts already say
  "OmnibusCloud".

**Gaps this plan closes:**
- **No control styles at all** — no `MaxControls.xaml`; every Button/ComboBox/TextBox/
  CheckBox renders stock WPF (Aero) chrome, only tinted. Root of DSN-H1.
- **No Diagnostics/Details dialog** — `DiagnosticsVm` (severity/message/context/
  SuggestedAction) is populated by `ShowDetails()` (`RenderDialogViewModel.cs:250`) but
  binds only to the legacy ExportWindow. The honesty-wave warnings are invisible.
- **Overlap hazards**: RenderDialog footer is a plain horizontal `StackPanel` with up to
  6 buttons in a 452px window (Completed state collides with the account block);
  RenderDialog uses `SizeToContent=Height` instead of the design's fixed 452×432 with a
  swapped work area; ExportDialog is pinned at 452×320 and clips when the V-Ray bake
  block is visible.
- **No per-phase work-area templates** — progress is a 4px bar; the 7 lifecycle cards of
  section 4.1.3 are not implemented.
- **Upload progress never reported**: `Uploading` phase exists but is never emitted; the
  session transport has no `IProgress<>`; `ProgressPercent` hardcoded to 5% until the
  server responds.
- **Version is fiction**: no `<Version>` in any csproj → assemblies are 1.0.0.0; About
  shows assembly version ("1.0.0" always); only PackageContents.xml gets stamped from
  the `plugin-v*` tag by the build script.
- **Signing**: build script has optional PFX signing (`Invoke-OptionalSigning`), secrets
  not configured; exemplar `client-release.yml` signs via SSL.com eSigner
  (installer-only, quota ~240/yr, tag-gated: skips `-dev/-test`, signs `-beta`/stable).
- **No installer** — zip + manual `Install-*.ps1` (copies to
  `%ProgramData%\Autodesk\ApplicationPlugins\OutWit.Render.3dsMax.Plugin`).
- **Artifact names still OutWit**: DLLs `OutWit.Render.3dsMax.Plugin*.dll`, plugin folder,
  zip name, `PackageContents.xml` Author/CompanyDetails="OutWit", legacy
  `Macro_OutWit_Export.mcr` + `ExportWindow`.
- **Dead UI**: Export "Pack textures & assets" checkbox binds `PackAssets` which is never
  read (server always packs). `LogLevel` setting persisted but not applied to the Serilog
  sink.

---

## Waves

Each wave ends releasable (`plugin-v0.7.x` tag + manual promote), user verifies visually
in his Max. Order: visual foundation → content → surfaces → identity → installer →
signing → final gate. Waves 4–6 are Milestone-4 items pulled forward by session goals.

### Wave 1 — Visual foundation: control styles + layout integrity
*(M3.1, M3.10 · session goals 1, 2 · fixes DSN-H1)* — **CODE DONE 2026-07-11, awaiting
visual check in Max.** Delivered: `Themes/MaxControls.xaml` (Button + Primary/Danger,
TextBox, ComboBox+items, CheckBox, RadioButton + Segment/Sidebar shapes, GroupBox with
floating legend, ProgressBar w/ indeterminate pulse, slim ScrollBar, ToolTip, focus
visuals), state brushes in both palettes, segmented Output axes + GroupBox cards in
Render/Export, per-state footer sets (`ShowConfigActions` replaces `ShowRenderButton`),
dead Pack checkbox removed (D2), Max HWND owner for all 4 dialogs.
**Deviation:** dialogs keep `SizeToContent=Height` + `MinHeight` for now — the Ready
work area (V-Ray bake block) is taller than the mockup's 432 until Wave 2/3 restructure
it; the hard-fixed 452×432 lands with the Wave-2 state templates. Content never clips,
which beats a fixed height that does. Known environmental: 3 `SmokeValidate…3dsMaxBatch`
tests fail with exit −130 on this machine — reproduced on a clean baseline without
Wave-1 changes (license/batch issue, not a regression); 252 unit tests green.

1. **`Themes/MaxControls.xaml`** — style dictionary for every control the dialogs use,
   keyed to existing `Max.Brush.*` brushes, matching section-5 specs (32px inputs, 5px
   radius, group boxes with legend, segmented control, toggle switch):
   - Button: default / primary (accent fill, navy text, 32px) / danger (coral) /
     disabled (45% opacity); ComboBox (full template — sunken input + chevron + themed
     popup); TextBox (sunken `#2a2a2a`); CheckBox (15px, accent fill when checked);
     RadioButton; ProgressBar (8px rounded track + accent, indeterminate variant);
     ToggleSwitch (32×18) and segmented control as reusable styles/templates.
   - Merge into all 4 dialogs + implicit styles scoped per dialog root so stock chrome
     can never leak.
2. **Fixed-size dialogs, swapped work area (MX-4)** — RenderDialog: drop
   `SizeToContent`, fix at 452×432 sized to the heaviest (Blocker) state; ExportDialog:
   re-measure fixed height so the tallest Ready state (bake block visible) fits.
3. **Footer integrity** — per-state button sets via ContentTemplate swap (never >2
   buttons + account line at once, per mockups); replace the 6-button `StackPanel`
   pile-up; account line truncates with ellipsis, never under buttons.
4. **Owner/modality (M3.10)** — Max HWND owner via `WindowInteropHelper` for all
   dialogs, so z-order/minimize behave (currently ownerless).

**Gate:** 249 unit tests green; all four dialogs visually clean in dark & light, no
stock Aero visible, no overlaps in any lifecycle state (forced via debug states).

### Wave 2 — Content: Diagnostics dialog + render lifecycle + upload progress
*(M3.5, M3.2, M3.9, tail 2.8 · session goal 3 · fixes DSN-H4, DSN-H3)*
**Partially delivered by the 0.7.50 feedback wave (2026-07-11):** DiagnosticsDialog 544×520
shipped (M3.5 done — validation grid/scene/about + Validate/Preflight, opened from
Details…); Output quick settings (M3.3 pulled forward: Format/Codec/Tiles/Range/Size, all
honest — the request now carries ImageFormat/Tiles*/VideoPreset/VideoCrf to the wire);
session restore at Max startup (WarmUpSession from Initialize.ms — the menu gate reflects
the persisted session without a manual sign-in); menu Sign in/Sign out split into two
gated items; whole-network checkbox no longer defaults visible; live theme preview;
Settings friendly labels + ImageFormat default + About links; first real log lines
(session service). **Wave 2 COMPLETE (plugin-v0.7.51-beta, 2026-07-11):** lifecycle work-area views (config /
active-phase card with icon+title+counter+bar+sub-line / Completed with duration + image
thumbnail / Failed with Copy log + Retry), real upload progress (request-carried callback →
transport reports per-attachment → VM emits Uploading, UI-thread marshalled), Finalizing
mapped from the server's ~100% plateau, footer "Close to keep working" hint. Height note:
the swap container has MinHeight=340 and the window keeps SizeToContent — a hard-fixed
height would clip the V-Ray-scan config state; jump between states is now minimal.
**Awaiting user visual check of a live render walk.**

1. **Diagnostics/Details dialog (M3.5, first content priority)** — new
   `Views/DiagnosticsDialog.xaml` 544×520 per section 4.1.5: *Validation* grid
   (Severity icon+color / Message / Suggested action) bound to existing
   `DiagnosticsVm.Items`; *Scene* read-only summary from `SummaryVm`; *About* block
   (plugin version, Max/SDK, last error); footer Validate / Preflight / Close re-running
   the existing `ShowDetails()` collection. Honesty-wave warnings (named approximations
   + SuggestedAction) become visible — this is the payoff of the whole honesty wave.
2. **Seven lifecycle work-area states (M3.2)** — `DataTemplate` per phase group in
   RenderDialog (Ready/Blocked · Submitting/Uploading · Running · Finalizing ·
   Completed · Failed · Cancelling) per section 4.1.3: centered phase icon + progress +
   sub-line; footer per state (Cancel-danger while active, "Close to keep working —
   render continues" hint, Completed → Open folder / New render, Failed → Copy log /
   Retry, Cancelling → disabled Cancel + indeterminate bar).
3. **Upload progress (tail 2.8)** — thread `IProgress<double>` through
   `MaxConnectedRenderSubmissionTransportOmnibusCloudSession.SubmitAsync` (blob/asset
   upload loop), emit `MaxRenderStatus.Uploading(fraction)`; `MapJobToStatus` stops
   hardcoding 5%.
4. **Status bar fidelity (M3.9)** — terminal wording + Uploading/Finalizing phases in
   the existing status-bar service (falls out of the phase templates).

**UX-flow acceptance (session goal 3):** Render disabled + Cancel enabled during the
whole active job (already in VM — verify visually); close ≠ cancel; Failed offers Retry;
Completed offers New render.

**Gate:** live render from user's Max walks all states visibly; upload of a heavy scene
shows a moving percentage before the server responds.

### Wave 3 — Surfaces & polish: Output, menu, toolbar, Settings/Export
*(M3.3, M3.4, M3.6, M3.7, M3.8, M3.11 · session goal 1)*
**Delivered (plugin-v0.7.52-beta, 2026-07-11):** menu per canon 1.1 (separator groups:
scene | account/config | external; About item → Settings▸About via ShowAbout; Sign in/out
split landed in 0.7.50); legacy retired (ExportWindow view + Macro_OutWit_Export deleted,
bootstrap/command-service entry points removed; ExportMainViewModel kept as sub-VM root);
macroscript icons — PNGs generated from the mockup SVG geometries into
Contents/Icons/OmnibusCloud (Render = lime bolt), wired via iconName:.
Export polish (M3.8): upload % + server-conversion % in the status line, Failed card with
Copy log + Retry. M3.3/M3.7 landed earlier (0.7.50).
**Needs live-Max verification:** (a) menu separators (`CreateSeparator` guarded by try —
harmless if absent), (b) whether the package's Contents/Icons dir is on the icon search
path (fallback: text-only buttons), (c) About switching an already-open Settings.
**Deferred:** dynamic account header in the menu (new menu system may not support runtime
label rebuild — needs live experimentation); auto-registered default toolbar (users can
place the now-iconed macros via Customize UI meanwhile).

1. **Output quick settings (M3.3)** — Format / Tiles ×+overlap / Codec / FPS / Range
   rows per section 4.1.2, bound to existing persisted settings (TilesX/Y, TileOverlap,
   VideoContainer/Codec); scene line "file — WxH · N frames · Camera". *(Any NEW
   `[Setting]` ⇒ key in `Resources/plugin-settings.json` — hard rule.)*
2. **Menu completion (M3.4)** — account header item (rebuilt on auth signal), About →
   `ShowSettings(About)`, dynamic Sign in…↔Sign out label, separators per section 1;
   retire `Macro_OutWit_Export.mcr` + drop legacy ExportWindow from PackageContents.
3. **Toolbar (M3.6)** — `icon:` bitmaps for the macroscripts + default "OmnibusCloud"
   toolbar registration; Render icon accent lime.
4. **Settings polish (M3.7)** — sidebar icons + version footer ("OmnibusCloud vX.Y.Z"),
   account card with Sign out, image-format row, friendly display names; `ThemeMode`
   actually consumed (FollowMax/Dark/Light) or the row made read-only.
5. **Export polish (M3.8) + dead checkbox** — scene line, Copy log + Retry on Failed,
   percent/phase while exporting, Completed meta; **resolve "Pack textures & assets"**
   (default: remove — server always packs; see Decisions).
6. **Cosmetics (M3.11)** — circular sign-in spinner + footer band, presence dot,
   "Close to keep working" hint, light-navy token.

**Gate (Milestone-3 gate):** side-by-side of every dialog/surface vs `@Design` mockups
in both Max themes; user sign-off.

### Wave 4 — Identity: branding rename + real versioning + log placement
*(session goals 5, 7, 8 · prerequisite for Waves 5–6)*
**DONE (plugin-v1.0.0-beta, 2026-07-11).** AssemblyName ×3 → `OmnibusCloud.3dsMax.Plugin(.UI/.Export)`
(RootNamespace stays OutWit; InternalsVisibleTo + XAML pack URIs + MaxThemeResources URI +
DiagnosticsDialog assembly= updated); template folder + Initialize.ms renamed; PackageContents
Name/Author/Company/AppNameSpace → OmnibusCloud; zip → `OmnibusCloud.3dsMax.Plugin-<v>.zip`
(portal regex already accepts it); logs follow the INSTALL SCOPE (ProgramData for all-users
installs with a writability probe → APPDATA fallback; Settings ▸ Diagnostics shows the resolved
path); the LogLevel setting drives a live Serilog LoggingLevelSwitch. Versioning landed earlier
(0.7.49). MSI wipes the legacy `OutWit.Render.3dsMax.Plugin` folder on install (verified locally:
planted legacy folder removed, new folder installed, clean uninstall).

### Wave 6 — Pipeline signing — **DONE (plugin-v1.0.0-beta)**
eSigner (SSL.com) in plugin.yml with the client's tag gate (skip -dev/-test/-internal; sign
-beta/-rc/stable; no-op without secrets): the MAIN plugin DLL is signed once inside the staged
bundle (flows into both zip and MSI harvest), then the MSI — 2 quota operations per release
(user's requirement: minimum). PFX path in Build ps1 stays as offline fallback.

1. **Public artifact names (goal 8)** — follow the client's split (user-visible = brand,
   code identity = OutWit): `AssemblyName` → `OmnibusCloud.3dsMax.Plugin` /
   `.UI` / `.Export` (RootNamespace stays `OutWit.Render.ThreeDsMax.*`); plugin folder →
   `%ProgramData%\Autodesk\ApplicationPlugins\OmnibusCloud.3dsMax.Plugin`; zip →
   `OmnibusCloud.3dsMax.Plugin-<ver>.zip`; `PackageContents.xml` Author/CompanyDetails →
   OmnibusCloud (+ site URL), AppNameSpace → `com.omnibuscloud.3dsmax`. Update
   `Initialize.ms` (assembly path + `dotNetObject` type strings), macroscript module
   paths, Build/Install/Uninstall scripts, `plugin.yml` artifact names.
   **Migration:** installer/scripts must delete the legacy `OutWit.Render.3dsMax.Plugin`
   folder (both ProgramData and per-user APPDATA locations) — two registered packages
   would double the menu.
2. **Real versioning (goal 5)** — pass the tag-derived version into the build
   (`dotnet build -p:Version=<ver>` in `Build-*.ps1`, InformationalVersion incl. suffix);
   About/Settings footer/Diagnostics About read
   `AssemblyInformationalVersionAttribute` (client's `ResolveAppVersion` pattern:
   `v0.8.0` short + full string in diagnostics); dev builds show `0.0.0-dev`, never a
   fake "1.0.0".
3. **Log placement by install scope (goal 7)** — plugin resolves its own package root at
   runtime: under `%ProgramData%\Autodesk\ApplicationPlugins` (all-users) → logs to
   `%ProgramData%\OmnibusCloud\Logs`; under `%APPDATA%\Autodesk\ApplicationPlugins`
   (per-user) → `%APPDATA%\OmnibusCloud\Logs` (current behavior). Writability probe with
   fallback (client's `EnsureUsable` pattern) — ProgramData may be read-only for
   standard users, then fall back to APPDATA. Apply `LogLevel` setting to the Serilog
   sink via `LoggingLevelSwitch` (today hardcoded Debug). Settings ▸ Diagnostics keeps
   Open logs folder / Open latest log (already wired) — point them at the resolved dir.

**Gate:** fresh install under the new name on user's Max; old folder gone; About shows
the tag version; logs land per install scope.

### Wave 5 — WiX installer
*(session goal 4)*
**Delivered (plugin-v0.7.53-beta, 2026-07-11), built out of order per user request.**
`Setup/` WiX v6 SDK project (not in the solution — built explicitly after bundle staging):
dual-scope MSI (WixUI_Advanced scope page, per-machine default; per-scope folder defaults
steered to the Autodesk auto-discovery roots via SetProperty after
WixSetDefaultPer[User|Machine]Folder), wildcard-harvested payload (`<Files
BundleDir\**>`), MajorUpgrade with AllowDowngrades, RemoveFolderEx wipe of the target on
install+uninstall (kills pre-MSI xcopy remnants and stale DLLs), HKMU version stamp,
UpgradeCode reused from PackageContents.xml. CI: plugin.yml builds the MSI from the same
staged bundle, numeric ProductVersion (suffix stripped), zip+MSI in the artifact and the
release with SHA256SUMS over both.
**Locally verified end-to-end (per-user scope):** silent install → files under
`%APPDATA%\Autodesk\ApplicationPlugins\...` + HKCU stamp; upgrade 0.7.53→0.7.54 wipes a
planted stale DLL and keeps exactly one product; downgrade allowed; uninstall removes the
folder and registry cleanly; the live per-machine xcopy install stayed untouched.
**Not yet verified live:** per-machine scope (needs elevation) and the interactive
WixUI_Advanced dialog flow — user checks with the released MSI.

1. **WiX v4 MSI, dual scope (decided)** (`Setup/` project, heat-less explicit component
   list — the payload is one folder): one MSI offering "for everyone" / "just for me"
   (WixUI_Advanced-style scope page driving `ALLUSERS`/`MSIINSTALLPERUSER`); the
   ApplicationPlugins target is set from the chosen scope via `SetDirectory`:
   per-machine → `%ProgramData%\Autodesk\ApplicationPlugins\OmnibusCloud.3dsMax.Plugin`,
   per-user → `%APPDATA%\Autodesk\ApplicationPlugins\OmnibusCloud.3dsMax.Plugin`
   (both are Autodesk-sanctioned auto-discovery paths — no registry keys needed).
   Wave-4 log-scope detection then follows whichever location the plugin runs from.
2. **Overwrite semantics (explicit session requirement)** — `MajorUpgrade` with
   `AllowSameVersionUpgrades=yes`, stable `UpgradeCode` (reuse the GUID already in
   PackageContents.xml), `Schedule=afterInstallInitialize` → any older/equal version is
   removed first, files always replaced. Plus a cleanup action for the pre-MSI legacy
   `OutWit.Render.3dsMax.Plugin` xcopy folder.
3. **CI integration** — `plugin.yml` gains an MSI job: build → stamp ProductVersion from
   the tag → attach `OmnibusCloud-3dsMax-Plugin-<ver>.msi` to the release next to the
   zip (zip stays for manual/portable installs). MSI ProductVersion needs numeric
   x.y.z — strip the `-beta` suffix for MSI only.

**Gate:** install → Max sees the plugin; install older-over-newer and newer-over-older
both leave exactly one clean copy; uninstall removes the folder; upgrade over a running
legacy xcopy install cleans it.

### Wave 6 — Pipeline signing
*(session goal 6 · exemplar: `WitCloud/.github/workflows/client-release.yml`)*

1. **eSigner (SSL.com) steps in `plugin.yml`** mirroring the client: sign the **MSI** and
   the **3 plugin DLLs** with `sslcom/esigner-codesign@develop`; same secrets
   (`ESIGNER_USERNAME/PASSWORD/CREDENTIAL_ID/TOTP_SECRET`); same quota gate — skip for
   `-dev`/`-test` tags, sign `-beta`/stable; graceful no-op when secrets are absent
   (warning, unsigned artifact — like today's `Invoke-OptionalSigning`).
   Note: the client signs only the installer to save quota; we sign DLLs too because Max
   loads them directly (SmartScreen/AV on .NET assemblies in ProgramData) — 4 files ≈ 4
   quota ops per promoted release, acceptable at our release cadence.
2. Keep the PFX path in `Build-*.ps1` as a local/off-line fallback.
3. `SHA256SUMS` covers zip + MSI (already generated for zip).

**Gate:** promoted release carries a signed MSI + signed DLLs (`signtool verify /pa`);
`-dev` tag skips signing and still releases.

### Wave 7 — Final QA gate — **PASSED (user QA on plugin-v1.0.2-beta, 2026-07-12)**
User installed the signed branded MSI and confirmed the plugin works (incl. a video render
of the Ape corpus scene). 1.0.0-beta shipped 2026-07-12 and was followed by two installer
polish releases the same day: 1.0.1-beta (UAC program name via eSigner program_name; ARP
icon + About/Help links; WixUI banner/dialog bitmaps) and 1.0.2-beta (dialog band replicates
the desktop client's login brand pane: OmnibusCloud-Vertical.png over navy with the bottom
fade). **Remaining exit step: promote `plugin-v1.0.2-beta` to Latest**
(`gh release edit plugin-v1.0.2-beta --prerelease=false --latest`).

1. Full visual side-by-side vs mockups (both themes) — Milestone 3 gate.
2. Smoke: menu gating, OIDC sign-in, render all 4 modes with lifecycle walk, export both
   targets, settings persistence, logs open, About version.
3. 249 unit tests + live farm job; then version **1.0.0-beta** (Milestone 4 target) via
   the now-real version pipeline.

### Post-ship tails (tracked, none block the promote)
- **Dynamic account header — attempted and CLOSED as won't-do (1.0.3-test, 2026-07-12).**
  Approach: passive OutWitAccount macroscript item + ICuiMenuItem.SetTitle from Initialize.ms
  on a bootstrap AccountStateChanged event (Max-UI-thread-marshalled). Live Max 2027 finding:
  menu STRUCTURE manipulation at #cuiRegisterMenus time works reliably (DeleteItem + recreate
  migrated the persisted menu both ways), but runtime SetTitle never changed the visible Qt
  menu — the header stayed static. Reverted; the account identity remains visible in every
  dialog footer and in Settings. Re-open only if a future Max exposes a menu-refresh API.
- **Live-Max experiments (Wave-3 deferred):** default "OmnibusCloud" toolbar
  auto-registration; confirm macroscript icons resolve from the package's Contents/Icons
  (else text-only fallback); About focusing an already-open Settings dialog.
- **Hygiene (M4.1 leftovers):** MessagePack advisory bump (NU1902), CS8618 nullable
  cleanup.
- **Release housekeeping:** optionally delete superseded 1.0.0/1.0.1-beta releases.

---

## Decisions (confirmed by Dmitry, 2026-07-11)

| # | Decision | Resolution |
|---|---|---|
| D1 | New assembly/folder name | **`OmnibusCloud.3dsMax.Plugin`** (+`.UI`/`.Export`); folder/zip same; namespaces stay OutWit |
| D2 | Export "Pack textures & assets" | **Remove** — server always packs (`pack_all`); no server contract for a toggle |
| D3 | Installer scope | **Dual-scope MSI** — one package, "everyone" (ProgramData) / "just for me" (AppData) choice at install time |
| D4 | Signing set | MSI + 3 DLLs, tag-gated like the client (skip `-dev/-test`) — default, revisit if quota bites |
| D5 | UI framework | **Pure XAML styles, no MaterialDesignInXAML** (soft deviation from MX-2's letter; its point was "WPF, not Avalonia"). Rationale: the design language is native-Windows/Max, not Material; hosted WPF has no `Application.Current` for MDIX theming; third-party UI DLLs risk version collisions with other plugins in the Max process; the needed control set is small (ComboBox template is the only heavy piece). Hand-rolled templates must cover all interaction states incl. `FocusVisualStyle`/keyboard focus in both themes |

## Risks
- **WPF ComboBox/segmented full retemplating is the bulk of Wave 1** — scope templates to
  states actually used (no editable combos); test popup theming inside Max's HWND host.
- **Rename collides with the installed dev plugin** (user's machine has the OutWit folder
  in ProgramData — earlier probe-collision lesson): uninstall legacy before first
  renamed install; wave-4 gate covers it.
- **eSigner quota** (~240 ops/yr shared with the client) — tag gate keeps dev iterations
  free; monitor if release cadence grows.
- **MSI vs `-beta` versions** — ProductVersion is numeric; suffix lives only in file
  name/release title.
- **Dual-scope MSI complexity** — scope page + `SetDirectory` + per-scope upgrade
  detection (a per-user install is invisible to a per-machine upgrade search and vice
  versa; document "uninstall the other scope first" or add both `UpgradeVersion`
  searches). Budget extra debug time in Wave 5.
- **Fixed dialog heights vs content growth** — heaviest-state sizing rule (MX-4);
  re-measure whenever a new row is added.

## Working rules (session-invariant)
- Any new `[Setting]` property ⇒ key in `Resources/plugin-settings.json`
  (`EverySettingPropertyHasAnEmbeddedDefaultTest` enforces).
- Always `cd /c/Workspace/OmnibusCloud/3ds-Max` before `dotnet`/`git`.
- Verify builds by DLL timestamp / `grep -cE "error CS"`.
- Tests: `dotnet test OutWit.Render.3dsMax.Plugin.Export.Tests` (249 green baseline).
- Releases: `plugin-v*` tag → CI zip (+MSI from Wave 5); `-beta` promoted manually
  (`gh release edit <tag> --prerelease=false --latest`). User verifies visually per wave.
