---
hide:
  - navigation
  - toc
---

<div class="mg-intro">
  <div class="mg-intro__eyebrow">Документация · v1.0.2</div>
  <h1 class="mg-intro__title">Miami Graphics</h1>
  <p class="mg-intro__lede">Лаунчер для управления Redux-модами в GTA V. Эта документация описывает, что лаунчер делает внутри: как открывается RPF, как считается diff против чистого baseline, как пересобирается архив и пересчитываются хеши, как работает обход блокировок и админ-панель.</p>
</div>

<div class="mg-meta">
  <div class="mg-meta__item"><span class="mg-meta__k">Строк C#</span><span class="mg-meta__v">76 939</span></div>
  <div class="mg-meta__item"><span class="mg-meta__k">Строк UI</span><span class="mg-meta__v">55 857</span></div>
  <div class="mg-meta__item"><span class="mg-meta__k">RageLib.GTA5</span><span class="mg-meta__v">35 574</span></div>
  <div class="mg-meta__item"><span class="mg-meta__k">CodeWalker</span><span class="mg-meta__v">177 922</span></div>
  <div class="mg-meta__item"><span class="mg-meta__k">Install pipeline</span><span class="mg-meta__v">≈ 6 сек</span></div>
  <div class="mg-meta__item"><span class="mg-meta__k">DPI bypass</span><span class="mg-meta__v">92 % RU</span></div>
</div>

![Главный экран лаунчера](assets/screenshots/screen-010.png)

## Содержание

<div class="mg-toc">

  <div class="mg-toc__item">
    <div class="mg-toc__num">01</div>
    <a href="00-overview/"><span class="mg-toc__title">Что это вообще</span></a>
    <p class="mg-toc__desc">Архитектура: Shell (WPF), Bridge (JSON-RPC), Core (RPF-инжектор), UI (React).</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">02</div>
    <a href="00-why-not-trivial/"><span class="mg-toc__title">Почему это не «скачать и заменить»</span></a>
    <p class="mg-toc__desc">Пять сценариев, которые ломают наивный install. Что мы делаем вместо этого.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">03</div>
    <a href="rpf/"><span class="mg-toc__title">Формат update.rpf</span></a>
    <p class="mg-toc__desc">RPF8: header, TOC, AES + NG ключи, вложенные архивы, RageLib.GTA5 fork, ArchiveFix.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">04</div>
    <a href="parser/what-we-extract/"><span class="mg-toc__title">Парсер донора</span></a>
    <p class="mg-toc__desc">Что вытаскиваем из мода: minimap.gfx, hud_reticle.gfx, core.ypt, bloodfx.dat, armor, timecycle.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">05</div>
    <a href="diff/algorithm/"><span class="mg-toc__title">Diff и patch.zip</span></a>
    <p class="mg-toc__desc">SHA-256 сравнение, рекурсивное в nested RPF, генерация Replace/Import/Delete actions.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">06</div>
    <a href="inject/pipeline/"><span class="mg-toc__title">Инжект в GTA</span></a>
    <p class="mg-toc__desc">Pipeline в 8 шагов, preflight CLEAN/LAST/DIRTY, Smart Rebuild с .bak rollback, UpdateRpfMutex.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">07</div>
    <a href="backup/pipeline/"><span class="mg-toc__title">Backup</span></a>
    <p class="mg-toc__desc">Snapshot vs Clean, manifest, atomic temp+rename+FlushToDisk, FileLockDetector через Restart Manager.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">08</div>
    <a href="3d/glb-viewer/"><span class="mg-toc__title">3D рендер</span></a>
    <p class="mg-toc__desc">GLB-viewer в WebView2 (R3F), headless рендер на сервере через Puppeteer + SwiftShader.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">09</div>
    <a href="paths/gta-detection/"><span class="mg-toc__title">Пути и детекция</span></a>
    <p class="mg-toc__desc">Registry, Steam, Epic scan, FileVersionInfo, AppData layout, gta_versions whitelist.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">10</div>
    <a href="customize/data-model/"><span class="mg-toc__title">Кастомизация</span></a>
    <p class="mg-toc__desc">Multi-version модель, donor cache, история редактирования .gfx через swfmill + jpexs.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">11</div>
    <a href="network/fragmenting-http-handler/"><span class="mg-toc__title">Сеть и обход блокировок</span></a>
    <p class="mg-toc__desc">TLS ClientHello фрагментация, WebView2 перехват запросов, DoH, AssetCache, Zapret integration.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">12</div>
    <a href="update/inno-supabase/"><span class="mg-toc__title">Auto-update</span></a>
    <p class="mg-toc__desc">Inno installer + Supabase app_versions, installed_version.txt для single-file publish.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">13</div>
    <a href="history/codewalker/"><span class="mg-toc__title">История попыток</span></a>
    <p class="mg-toc__desc">CodeWalker провал, RageLib v1 → v2 (4 минуты → 6 секунд), tracers сага, конфликты ганов, шейдеры.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">14</div>
    <a href="protection/guards/"><span class="mg-toc__title">Защита и проверки</span></a>
    <p class="mg-toc__desc">Семь гардов перед инжектом, что физически происходит на каждой точке провала.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">15</div>
    <a href="admin/"><span class="mg-toc__title">Админ-панель</span></a>
    <p class="mg-toc__desc">12 секций: redux, gunpack whitelist, армор, DLC import, library, presets, PRO players, popularity.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">16</div>
    <a href="hnt/"><span class="mg-toc__title">HNT-коды</span></a>
    <p class="mg-toc__desc">Короткие коды HNT-XXXXXX для обмена сборками. Selective export, customize-aware apply.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">17</div>
    <a href="infra/supabase-schema/"><span class="mg-toc__title">Инфра</span></a>
    <p class="mg-toc__desc">Supabase schema (15 таблиц + RLS + RPC), R2 layout, Swiss VPS, CloudFlare Worker.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">18</div>
    <a href="build/publish/"><span class="mg-toc__title">Сборка и релиз</span></a>
    <p class="mg-toc__desc">dotnet publish single-file, Inno installer, release pipeline, rollback.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">19</div>
    <a href="gallery/"><span class="mg-toc__title">Скриншоты</span></a>
    <p class="mg-toc__desc">59 скринов всех экранов лаунчера и админки.</p>
  </div>

  <div class="mg-toc__item">
    <div class="mg-toc__num">20</div>
    <a href="changelog/"><span class="mg-toc__title">Changelog</span></a>
    <p class="mg-toc__desc">Хронология версий и значимых изменений.</p>
  </div>

</div>

## Стек

| Слой | Технология |
|---|---|
| Shell | WPF, .NET 8, single-file publish |
| UI | React 18, TypeScript, Vite, Tailwind, Framer Motion |
| Bridge | JSON-RPC поверх `window.chrome.webview.postMessage` |
| RPF parser | RageLib.GTA5 (fork), ArchiveFix.exe |
| 3D в UI | React Three Fiber, Three.js |
| Headless рендер | Node, Puppeteer, headless Chrome, SwiftShader |
| База | Supabase (PostgreSQL + REST + RLS) |
| Хранилище | Cloudflare R2 (S3-compatible) |
| CDN | CloudFlare Worker с aggressive cache |
| DPI bypass | TLS ClientHello фрагментация (custom HttpClient handler) |
| VPN fallback | AmneziaVPN на Swiss VPS (`84.234.18.182`) |
| Installer | Inno Setup 6 |

## С чего начать

- Если **впервые** смотришь - раздел [02. Почему это не «скачать и заменить»](00-why-not-trivial.md). 5 минут, обзор.
- Если хочешь понять **ядро** - [03. Формат RPF](rpf/index.md) → [06. Инжект](inject/pipeline.md) → [Smart Rebuild](inject/smart-rebuild.md).
- Если интересны **провалы** - [13. История попыток](history/codewalker.md). Все шесть итераций по порядку с цифрами.
- Если ищешь **конкретный экран** - [19. Скриншоты](gallery.md).
