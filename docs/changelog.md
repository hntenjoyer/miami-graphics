# Changelog

Хронология значимых версий Miami Graphics. Подробности в `git log` и Supabase `app_versions`.

## 1.0.13 - 2026-05-25

Тема релиза: фикс рендера у админов, добавление версий редукса без полной перезаливки, починка картинок в РФ.

- **New**: кнопка `+` в «Версиях редукса» в админ-редакторе. Клик ведёт на форму, где выбираешь новый `update.rpf`, парсер его читает, версия добавляется к существующему редуксу (V2, V3, ... до V5). Каталог-row остаётся как есть, только мерджатся `components`. Полная заливка нового редукса не нужна.
- **New**: переключатель проекта (Majestic / 5RP / Both) в окне «Редактирование редукса». Раньше для смены проекта приходилось перезаливать весь патч. Теперь это просто чекбокс, сохраняется в `redux_items.supported_servers`.
- **New**: автозеркалирование внешних картинок при сохранении редукса. Когда админ вставляет URL картинки с imgur / postimg / ibb / любого левого домена в `preview_url` / `galleryUrls` / `componentScreenshots`, при Save лаунчер качает её через `FragmentingHttpHandler`, грузит в наш R2 как `redux/{id}/mirror/{slot}-{sha1}.{ext}` и заменяет URL в БД. После этого картинка идёт через [WebView2 bypass interceptor](network/webview2-bypass.md) и грузится в РФ.
- **New**: автоматическая установка Chromium на машину админа при первом запуске. После скачивания `Renderer.zip` бутстрапер запускает `node node_modules/puppeteer/install.mjs` с `PUPPETEER_CACHE_DIR=Renderer/.cache/puppeteer`. Puppeteer качает `chrome-headless-shell` (~120 MB) с googleapis. Дальше `render.js` использует bundled Chromium 138, а не системный Edge. Это устраняет проблему «у админа работает тестовый рендер, а пакетный падает» из-за залоченных профилей Edge и блокировок Defender.
- **Fix**: дубликат `ProbeRendererState` в `AdminGunpackQueueService` удалён. Теперь и тестовый, и боевой пути используют один `RendererBootstrapper.Probe()`.
- **Fix**: `GlbToPngRenderer.RenderAsync` переписан с наивного `string.Format` quoting на `ProcessStartInfo.ArgumentList` (Win32-safe).
- **Fix**: версии в публичной карточке редукса (V1 / V2 / ...) перенесены из блока описания в карточку установки, под кнопку «Установить». Помещаются до пяти штук в один ряд, длинные label'ы truncate'ятся.

## 1.0.2 - 2026-05-13

**Тема релиза**: bug fixes в кастомизации мульти-версионных redux'ов + проверка settings.xml.

- **Fix**: кастомизация redux'а с 2+ версиями - теперь UI правильно показывает превью компонентов для **выбранной** версии. Раньше превью брались всегда из v1 даже если юзер выбрал v2.
- **Fix**: «Какая версия для customize» - добавлен явный version-picker в UI. Раньше всегда брался первый match по reduxId, что приводило к багам когда у мода больше одной версии.
- **Fix**: «BypassTester сломался - нет файла для теста». Test-file URL мигрировал на новый CDN-путь, теперь BypassTester использует `MiamiGraphicsRenderer_1.0.0.zip` который точно есть в R2.

## 1.0.1 - 2026-05-12

**Тема**: hotfix render error #130.

- **Fix**: React `Error #130` при открытии preview для некоторых redux'ов - react рендерил `undefined` как child из-за async race condition в `useGLTF`. Добавлен loading state.
- **Fix**: minor UI bugs в админ-панели (несохраняющиеся checkboxes).

## 1.0.0 - 2026-05-08

**Тема**: первый production release под брендом Miami Graphics.

- **New**: бренд переименован с «Hunter Graphics» на «Miami Graphics» (codebase остался MiamiGraphics из исторических причин).
- **New**: Customize-flow с multi-version support (`baseVersionId`, `donorVersionId`).
- **New**: HNT-коды для p2p обмена сборками.
- **New**: PRO Players секция в каталоге.
- **New**: GTA Presets - apply settings.xml одной кнопкой.
- **New**: Auto-update через Supabase `app_versions` + Inno installer chain.
- **Infra**: переезд с предыдущего R2 bucket'а на `huntergraphics` с CDN.
- **Network**: финальная версия [FragmentingHttpHandler](network/fragmenting-http-handler.md) (3 фрагмента, no delay) - 92% pass rate в РФ.

## Pre-release (2025)

Не релизилось публично, был beta-период:

- Переход с CodeWalker на RageLib (см. [история](history/codewalker.md)).
- Оптимизация RageLib v1 → v2 (4 мин → 6 сек на инжект, см. [v2](history/ragelib-v2.md)).
- Финальная архитектура shaders для гангов (см. [3D render](history/3d-render.md)).
- WebView2 bypass interceptor.
- Zapret integration.

## Принципы версионирования

- **Patch** (1.0.X) - bugfixes, мелкие UI improvements.
- **Minor** (1.X.0) - новые features (новый component type, новая админ-секция).
- **Major** (X.0.0) - breaking changes в data model. Пока не было.

См. [release-pipeline](build/release-pipeline.md) для процесса.

## Где смотреть детали

- **git log** на [github.com/hntenjoyer/miami-graphics](https://github.com/hntenjoyer/miami-graphics) - все коммиты с описаниями.
- **Supabase `app_versions`** - таблица с `version`, `release_notes`, `released_at`, `is_required`.
- **R2 releases path** - `cdn.miamigraphicsstorage.uk/releases/` - все когда-либо собранные installer'ы.
