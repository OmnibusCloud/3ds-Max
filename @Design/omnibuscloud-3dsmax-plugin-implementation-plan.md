# OmnibusCloud — 3ds Max плагин · план внедрения

> **SUPERSEDED (2026-07-11): актуальный план UI/UX-волны — `omnibuscloud-3dsmax-ui-ship-plan.md`**
> (waves 1–7: контрол-стили, Diagnostics-диалог, lifecycle, брендинг артефактов, WiX,
> подпись, логи). Ниже — исходный план и сверка с реальностью; разделы «Текущее
> состояние» и «Сверка решений» остаются справочными.

> **СТАТУС (2026-07-11): это АКТУАЛЬНЫЙ план текущей волны (UI/UX).** Функциональная часть
> закрыта как production-ready (плагин 0.7.47 + WitCloud v1.6.55, полный корпус зелёный);
> «сантехника» из перечисленного ниже готова (OIDC-восстановление сессии, submit,
> download-гейты, preflight с проверкой сессии ДО тяжёлого захвата, фазовая модель в VM).
> Первый UI-приоритет — Details-диалог диагностик (DSN-H4): предупреждения об аппроксимациях
> уже собираются с именами объектов и SuggestedAction, их осталось показать. Новые
> поверхности сверх плана: чекбокс бейка сканов в Render/Export-диалогах уже реализован;
> галка «Pack textures & assets» в Export-диалоге декоративна (сервер пакует всегда) —
> убрать или реализовать при полировке.

**Scope:** превратить текущий dev-харнесс (одно окно `ExportWindow` с ~17 командами) в продуктовый плагин-инициатор для 3ds Max 2027: многоповерхностная регистрация (меню/тулбар/quad/шорткаты/статусбар), нативные фикс. модальные диалоги, один Render-экшен + фазовая модель, экспорт в две цели (Blender / DCC JSON), гейт входа над готовым OIDC, follow-Max-theme, персистентность и свои логи.

**Визуальный референс (8 разделов, все в `/mnt/user-data/outputs/`):**
`3dsmax-section-1-menu.html` · `-2-toolbar` · `-3-statusbar` · `-4-1-render-dialog` · `-4-2-export` · `-4-3-settings` · `-4-4-signin` · `-5-color-decisions` (палитра + MX-1…MX-20 + принципы).

---

## Текущее состояние (сверено с репозиторием)

**Проекты:**
- `OutWit.Render.3dsMax.Plugin` — host/entry, `net10.0-windows`, ref `Autodesk.Max` (SDK 2027), RootNamespace `OutWit.Render.ThreeDsMax.Plugin`.
- `OutWit.Render.3dsMax.Plugin.UI` — WPF + MVVM (`OutWit.Common.MVVM`).
- `OutWit.Render.3dsMax.Plugin.Export` — экспорт + auth-сервисы.

**Регистрация (МаxScript, не C# ActionTable):**
- `Contents/scripts/OutWit.Render.3dsMax.Plugin.Initialize.ms` — грузит сборку; `OutWit_3dsMax_ShowExportWindow` → `dotNetObject MaxPluginBootstrap` → `ShowExportWindow()`; `OutWit_3dsMax_RegisterMenu` через `callbacks.addScript #cuiRegisterMenus` создаёт submenu «OutWit» с ОДНИМ flat-экшеном «OutWitExport» (вставка перед Help).
- `Contents/macroscripts/Macro_OutWit_Export.mcr` — макроскрипт-экшен.
- `PackageContents.xml` — `ApplicationPackage`, Series 2027–2027, регистрирует Initialize.ms (pre-startup) + макроскрипт.

**UI-харнесс:**
- `ExportWindow` (WPF Window) — DataContext = `ExportMainViewModel`, на `Loaded` зовёт `CloudVm.RestoreSessionAsync()`.
- `ExportMainViewModel` (MainVm) — ~17 RelayCommands: `Validate`, `Export`, `ExportAndOpenFolder`, `LoadExecutionScope`, `Preflight`, `LaunchRender`, `RefreshJob`, `UploadPackage`, `DownloadResult`, `OpenDownloadedResult(+Folder)`, `OpenPrimaryArtifact`, `OpenLaunchPackageFolder`; под-VM `SummaryVm`/`OptionsVm`/`DiagnosticsVm`.
- `ApplicationViewModel` — корень композиции (создаёт все сервисы и VM).
- `RenderLaunchViewModel` (LaunchVm) — `SelectedRenderMode`, `ResolutionX/Y`, `FrameStart/End`, `Samples`, `UseAllClients`, `SelectedGroupName`, `ApplyJobState`, `ApplyExecutionScope`.
- `CloudSessionViewModel` (CloudVm) — `CloudUrl`, `IdentityUrl`, `RestoreSessionAsync`, `ApplyExecutionScope`, sign-in.
- `OptionsVm` — `OutputFolder`, `OutputFormat`, `OpenFolderAfterExport`.

**Готовая сантехника (переиспользуем, не пишем заново):**
- OIDC-вход: `MaxCloudSessionService` (= `MaxSessionStoreDpapi` + `MaxSystemBrowserLauncherShell` + `MaxAuthorizationCallbackListenerLoopback`). Токен в DPAPI-сторе (паттерн `TokenProtector` клиента). Loopback-callback. `RestoreSessionAsync`.
- Submit на ферму: `MaxConnectedRenderService` (`LaunchPreparation` + `Preflight` + `Submission`), транспорт `MaxConnectedRenderSubmissionTransportOmnibusCloudSession` (по сессии/OIDC).
- Экспорт/сцена: `MaxSceneExportService` (`ExportCurrentScene(folder, format)`, `ValidateCurrentScene`), `MaxSceneSummaryService`, host-доступ `MaxHostApplicationService` (через `IGlobal` Autodesk.Max).
- Scope: `MaxConnectedExecutionScopeService` → группы/права (`UseAllClients`/`CanRunOnAllClients`).

---

## Сверка решений с реальностью (важно)

1. **MX-1 — механизм уточнён: макроскрипты + `cuiRegisterMenus`, а не C# `IActionTable`.** В 2027 и в этом коде экшены — это макроскрипты (`.mcr`) в категории «OutWit»; они сами доступны в Customize UI для тулбара/quad/шорткатов, а структуру меню строит `cuiRegisterMenus`. Смысл MX-1 (один источник → все поверхности) сохраняется; «ActionTable» в тексте MX-1 читать как «набор макроскриптов».
2. **API-key путь не выносим в прод-UI.** В коде есть `MaxConnectedRenderArchiveUploaderOmnibusCloudApiKey` (для smoke-автоматизации) рядом с session-транспортом. Прод-флоу — только session/OIDC (MX-11). API-key аплоадер остаётся для batch-тестов, в диалогах его нет.
3. **`Samples` в `LaunchVm` не сёрфим.** Рендер Blender-side (MX-20) — 3ds-Max-сэмплов нет. Поле остаётся в запросе с дефолтом (или удаляется), в UI его нет (как и Arnold/denoise/passes).
4. **`ExportWindow` — это смоук-харнесс, а не продукт.** Его ~17 команд разносим по трём диалогам и схлопываем Render-цепочку в один экшен (MX-8). Сам `ExportWindow` ретайрится/перепрофилируется.
5. **Вход уже плумбится.** Phase 5 — UI-гейт над `MaxCloudSessionService`, не новая auth-механика (как Phase 4 у бриджа).
6. **Брендинг — публичный UI только OmnibusCloud.** В хедере/меню/диалогах/About — бренд **OmnibusCloud** (плагин = **«OmnibusCloud 3ds Max»**); рабочие имена (OutWit/WitEngine/WitCloud/WitIdentity) — только в коде/неймспейсах, не в UI. SSO-страница возврата — «OmnibusCloud Identity». Категория макроскриптов в Customize UI — «OmnibusCloud».

---

## Фазы

### Phase 1 — Многоповерхностная регистрация (макроскрипты + меню) · _не начато_

Цель: каноничное меню (Раздел 1) и доступность экшенов на всех поверхностях; один экшен → один макроскрипт.

- **`MaxPluginBootstrap.cs`** — добавить точки входа поверх существующего `ShowExportWindow()`: `ShowRenderDialog()`, `ShowExportDialog()`, `ShowSettings(tab?)`, `SignIn()`, `SignOut()`, `OpenPortal()`. Все идемпотентны (single-instance окна, как сейчас сделано для `ExportWindow`).
- **`Contents/macroscripts/`** — расщепить на per-command макроскрипты, категория в Customize UI = «OmnibusCloud» (публичный бренд): `Macro_OutWit_Render.mcr`, `Macro_OutWit_Export.mcr`, `Macro_OutWit_Settings.mcr`, `Macro_OutWit_SignInOut.mcr` (тоггл по состоянию) — каждый зовёт соответствующий метод bootstrap.
- **`Initialize.ms` (`cuiRegisterMenus`)** — собрать структуру меню per Раздел 1: пассивная шапка аккаунта → разделитель → Render · Export → разделитель → Sign in/out (тоггл) · Settings → разделитель → Open portal · About. **Гейт (MX-17):** Render/Export `disabled` пока нет сессии — enabled-state пунктов завязать на сессию; на смену auth — ребилд/обновление меню (через menu-manager). **Шапка аккаунта** — динамический disabled-пункт; уточнить механику динамического лейбла в новом menu-system (ребилд по auth-сигналу).
- **`PackageContents.xml`** — зарегистрировать все новые макроскрипты (Series 2027).

_Файлы: `MaxPluginBootstrap.cs`, `Initialize.ms`, новые `.mcr`, `PackageContents.xml`._

### Phase 2 — Сплит `ExportWindow` → Render / Export / Settings диалоги · _не начато_

Цель: три нативных модальных диалога вместо одного окна-харнесса; все фикс. размера (MX-4).

- **`Views/RenderDialog.xaml(.cs)`** + **`RenderDialogViewModel`** — конфиг + submit (Output 2-оси, Target, lifecycle). `ResizeMode=NoResize`, close-only, фикс. размер; рабочая область свапается между состояниями.
- **`Views/ExportDialog.xaml(.cs)`** + **`ExportDialogViewModel`** — две цели экспорта (Phase 4).
- **`Views/SettingsDialog.xaml(.cs)`** + **`SettingsViewModel`** — sidebar (General/Connection/Output/Diagnostics/About), Cancel/Save (MX-7).
- **Декомпозиция `ExportMainViewModel`** — растащить ~17 команд: Render-цепочка → `RenderDialogVm`; Export/Validate → `ExportDialogVm`; Settings-поля → `SettingsVm`. Переиспользовать `LaunchVm`/`CloudVm`/сервисы из `ApplicationViewModel` (композиционный корень сохраняется).
- **`ExportWindow`** — ретайр (или тонкий dev-only харнесс под флагом, вне меню).

_Файлы: новые Views + VM в `.Plugin.UI`; правка `ApplicationViewModel`/`MaxPluginCommandService`/`MaxPluginBootstrap`._

### Phase 3 — Render: один экшен + фазы + статусбар · _не начато_

Цель: схлопнуть цепочку в один Render (MX-8); фоновый прогресс в статусбаре (MX-5/6); цикл (MX-13).

- **Один `Render`** — `Validate → pack → upload → submit` становятся внутренними шагами с прогрессом, не кнопками. В `RenderDialogVm` остаётся одна primary-команда; старые `Upload/Download/Preflight/RefreshJob`-кнопки уходят из UI (логика — внутрь или в Details).
- **Фазовая модель** — портировать из бриджа (`bridge_status.py` → C# `MaxRenderStatus`/`Phase`): `Submitting(Uploading%) · Running(N/M) · Finalizing · Completed · Failed · Cancelling`. Источник истины — серверный статус job (poll), не локальная догадка.
- **`Services/Max…StatusBarService`** — обёртка над `IInterface.PushPrompt/ReplacePrompt` (+ `progressStart/progressUpdate/progressEnd` с Cancel) через `IGlobal`. **Close ≠ cancel (MX-5):** закрытие Render-диалога не трогает job — прогресс уезжает в статусбар.
- **Output 2-оси (MX-9)** — Image/Animation → `RenderStill/StillTiled/Frames/Video` → `LaunchVm.SelectedRenderMode`. Быстрые настройки (форматы/тайлы/кодек/fps) в диалоге; глубоких рендер-настроек нет (MX-20).
- **Target (MX-10)** — комбобокс `AvailableGroupNames`/`SelectedGroupName` + чекбокс `UseAllClients` (виден по `CanRunOnAllClients`; включён → комбобокс disabled). Уже частично в `LaunchVm`.
- **Транспорт** — только `MaxConnectedRenderSubmissionTransportOmnibusCloudSession` (OIDC); API-key аплоадер из флоу убрать.
- **Details/Diagnostics** — отдельный фикс. диалог (валидация экспорта + сводка сцены + версии), за кнопкой «Details» (MX-12).

_Файлы: `RenderDialogVm`, новый `MaxRenderStatus.cs` (порт фаз), `MaxStatusBarService.cs`, правка `MaxConnectedRenderService`-вызовов; `Views/DetailsDialog.xaml`._

### Phase 4 — Export: две цели (Blender / DCC JSON) · _не начато_

Цель: дефолт — Blender .blend через сервер; альтернатива — локальный DCC JSON (MX-18).

- **DCC JSON (локально)** — существующий `MaxSceneExportService.ExportCurrentScene(folder, format)`.
- **Blender .blend (через сервер)** — новый server round-trip: экспорт DCC → upload (session, НЕ API-key) → сервер конвертит DCC→`.blend` → возврат `.blend` как результат (на рендеринг не уходит). Переиспользовать upload-механику submission-транспорта + конверт-эндпоинт. Новый `MaxSceneBlenderExportService` (или метод в connected-сервисе).
- **`ExportDialogVm`** — состояния Ready/Exporting/Completed/Failed; дефолт-таргет Blender; Pack textures & assets; Save to.
- **Гейт (MX-17)** — Export требует сессии; аккаунт в баре всегда.

_Файлы: `ExportDialogVm`, `MaxSceneBlenderExportService.cs` (server-convert), правка export-сервисов._

### Phase 5 — Гейт входа + session-driven enablement · _не начато_

Цель: UI-гейт над готовым OIDC (`MaxCloudSessionService`); состояние сессии рулит меню/тулбаром.

- **Sign in** — макроскрипт → `Bootstrap.SignIn()` → существующий браузерный флоу; показать диалог ожидания (Раздел 4.4: спиннер «Signing in…», Reopen/Cancel); на loopback-callback — сессия активна; успех/таймаут-состояния.
- **Enablement** — сессия включает Render/Export и шапку аккаунта (Phase 1 ребилд меню по auth-сигналу); `RestoreSessionAsync` на старте (как сейчас в `ExportWindow.Loaded`, но на уровне плагина/первого показа).
- **Sign out / Open portal** — `Bootstrap.SignOut()` / `OpenPortal()` (портал — в системном браузере, сессии не требует).

_Файлы: `SignIn/SignOut/OpenPortal` в bootstrap, `Views/SignInDialog.xaml`, auth-сигнал → menu refresh; переиспользование `MaxCloudSessionService`/`CloudVm`._

### Phase 6 — Follow-Max-theme + персистентность + логи · _не начато_

- **Тема следует за Max (MX-14)** — `MaxThemeService` читает color manager (`IColorManager` через `IGlobal`); свап ResourceDictionary WPF: тёмная Max-gray ↔ светлая WitCloud (палитра — Раздел 5.1). Дефолт тёмная. Подписка на смену темы хоста, если доступна.
- **Персистентность (MX-16)** — `OutWit.Common.Settings` (+`.Json`), per-OS-user `%appdata%`, не синкается; `MaxPluginSettings` (`SettingsContainer`): дефолты Output (target/format/preset/folder), `RememberLastRenderSettings` (мастер-тоггл), последние настройки запуска. Паттерн — как Phase 5 бриджа (embedded resource defaults + `UseJsonFile(User)`).
- **Свои логи (MX-19)** — Serilog rolling в `%APPDATA%\OmnibusCloud\Logs`; **`MaxDiagnosticsLauncher`** — зеркало клиентского `DiagnosticsLauncher` (`GetLogsDirectory` → `%APPDATA%/OmnibusCloud/Logs`, `GetLatestLogFile`, `OpenLogsFolder`, `OpenLatestLog`, `OpenPath` кросс-платформенно). Таб Diagnostics: Level + Open logs folder + Open last log.

_Файлы: `MaxThemeService.cs` + Themes/*.xaml, `MaxPluginSettings.cs` + `Resources/plugin-settings.json`, Serilog-bootstrap, `MaxDiagnosticsLauncher.cs`._

### Phase 7 — Packaging + QA · _не начато_

- **`PackageContents.xml`** — все макроскрипты + Initialize.ms; Series 2027–2027; проверка путей сборки.
- **Подпись/нотаризация** .NET-сборок (AV/SmartScreen) — обязательна к релизу.
- **Smoke-чеклист:** меню появляется и гейтится (Render/Export серые до входа); вход через OIDC; Render submit по сессии + прогресс в статусбаре + close≠cancel; Export обе цели (Blender/.blend и DCC JSON); Settings персистит per-user; логи открываются (folder + last). Прогон существующих batch-смоук-сервисов (`MaxPluginCommandService.Create…`).

---

## Риски

- **Динамическая шапка аккаунта в новом menu-system** — лейбл с email; уточнить, поддерживается ли динамический текст пункта, или ребилд меню по auth-сигналу (Phase 1/5).
- **Гейт enabled-state в меню** — синхронизация disabled Render/Export с сессией требует ребилда/refresh меню на auth-смену; продумать сигнал.
- **Server-convert для Blender-таргета (Phase 4)** — нужен конверт-эндпоинт «DCC→.blend как результат» (если ещё не на сервере) + session-upload вместо API-key.
- **WPF в хосте Max** — тема/диалоги поверх Max; модальность и owner-handle (HWND Max) для корректного z-order и блокировки.
- **Theme-read из color manager** — доступность `IColorManager` и событий смены темы в Autodesk.Max 2027 (фолбэк: дефолт тёмная, ручной свитч в Settings при отсутствии события).

---

## Рекомендуемый порядок

**Phase 1 (макроскрипты + меню + bootstrap-точки)** → **Phase 2 (сплит на 3 диалога)** → **Phase 3 (один Render + фазы + статусбар)** → **Phase 5 (гейт входа — плумбинг готов, разблокирует прод-флоу)** → **Phase 4 (Blender-таргет, нужен server-convert)** → **Phase 6 (тема + персистентность + логи)** → **Phase 7 (packaging + QA)**.

Phase 5 поднят перед Phase 4: вход уже плумбится и разблокирует и Render, и Export; Blender-таргет (Phase 4) зависит от server-convert и тяжелее.
