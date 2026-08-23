const CF_HOSTS = new Set([
  'miamigraphicsstorage.uk',
  'hnt.miamigraphicsstorage.uk',
  'eu.miamigraphicsstorage.uk',
  'cdn.miamigraphicsstorage.uk',
]);
const FALLBACK_HOST = 'ru.miamigraphicsstorage.uk';

export function installImageFallback(): void {
  window.addEventListener(
    'error',
    e => {
      const el = e.target;
      if (!(el instanceof HTMLImageElement) && !(el instanceof HTMLVideoElement)) return;
      if (el.dataset.mgRuFallback === '1') return;

      let url: URL;
      try { url = new URL(el.currentSrc || el.src, window.location.href); } catch { return; }
      if (!CF_HOSTS.has(url.hostname)) return;

      el.dataset.mgRuFallback = '1';
      url.hostname = FALLBACK_HOST;
      el.src = url.toString();
      if (el instanceof HTMLVideoElement) el.load();
    },
    true,
  );
}
