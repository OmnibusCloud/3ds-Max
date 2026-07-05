# OmnibusCloud — 3ds Max плагин · дизайн-пакет

Канонический набор дизайн-разделов + план внедрения. Публичный бренд — **OmnibusCloud** (плагин = «OmnibusCloud 3ds Max»). Рабочие имена (OutWit/WitEngine/WitCloud) — только в коде.

## Разделы (по порядку)
1. `3dsmax-section-1-menu.html` — Меню: состав, гейт (Render/Export только при входе), шапка аккаунта
2. `3dsmax-section-2-toolbar.html` — Тулбар: 3 кнопки, floating/docked
3. `3dsmax-section-3-statusbar.html` — Статус-бар: prompt + progress, фазы джобы
4.1 `3dsmax-section-4-1-render-dialog.html` — Render-диалог: анатомия · Output (Still/Tiled/Frames/Video) · цикл · Target · Diagnostics
4.2 `3dsmax-section-4-2-export.html` — Export: Blender `.blend` (дефолт, через сервер) + DCC JSON (локально)
4.3 `3dsmax-section-4-3-settings.html` — Settings: sidebar (General / Connection / Output / Diagnostics / About)
4.4 `3dsmax-section-4-4-signin.html` — Sign in: OIDC / PKCE через системный браузер
5. `3dsmax-section-5-color-decisions.html` — Цвет, решения MX-1…MX-20, принципы

## План
`omnibuscloud-3dsmax-plugin-implementation-plan.md` — 7 фаз внедрения, привязка к классам репозитория.
`omnibuscloud-3dsmax-production-plan.md` — план доведения до production (по аудитам 2026-07-04: контроллер Render.Dcc, функциональность плагина, соответствие дизайну).

## Эндпоинты
Identity (вход) — `auth.omnibuscloud.com` · Cloud (ферма) — `engine.omnibuscloud.com`
