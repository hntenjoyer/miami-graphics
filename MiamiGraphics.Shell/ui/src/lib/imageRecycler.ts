const PARK_MARGIN = '1200px';

const MIN_PIXELS = 100_000;

type Entry = {
  url: string;
  w: number;
  h: number;
  parked: boolean;
  near: boolean;
};

const state = new WeakMap<HTMLImageElement, Entry>();

const placeholder = (w: number, h: number) =>
  `data:image/svg+xml;charset=utf-8,${encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}"></svg>`,
  )}`;

const isRemote = (src: string) =>
  src.startsWith('http://') || src.startsWith('https://');

function park(img: HTMLImageElement, e: Entry) {
  if (e.parked) return;
  e.parked = true;
  img.dataset.mgParked = '1';
  img.src = placeholder(e.w, e.h);
}

function unpark(img: HTMLImageElement, e: Entry) {
  if (!e.parked) return;
  e.parked = false;
  delete img.dataset.mgParked;
  img.src = e.url;
}

export function installImageRecycler(): void {
  if (typeof IntersectionObserver === 'undefined') return;

  const io = new IntersectionObserver(
    entries => {
      for (const entry of entries) {
        const img = entry.target as HTMLImageElement;
        const e = state.get(img);
        if (!e) continue;
        e.near = entry.isIntersecting;
        if (entry.isIntersecting) unpark(img, e);
        else park(img, e);
      }
    },
    { rootMargin: PARK_MARGIN },
  );

  function track(img: HTMLImageElement) {
    const src = img.getAttribute('src') ?? '';
    if (!src || !isRemote(src)) return;

    const prev = state.get(img);
    if (prev) {
      if (prev.parked) {
        if (!prev.near) { prev.url = src; prev.parked = false; park(img, prev); }
        return;
      }
      prev.url = src;
      return;
    }

    const w = img.naturalWidth, h = img.naturalHeight;
    if (w * h < MIN_PIXELS) {
      if (w || h) return;
      img.addEventListener('load', () => track(img), { once: true });
      return;
    }

    state.set(img, { url: src, w, h, parked: false, near: true });
    io.observe(img);
  }

  const scan = (root: ParentNode) => {
    if (root instanceof HTMLImageElement) { track(root); return; }
    root.querySelectorAll?.('img').forEach(img => track(img as HTMLImageElement));
  };

  scan(document);

  new MutationObserver(records => {
    for (const r of records) {
      if (r.type === 'attributes' && r.target instanceof HTMLImageElement) {
        track(r.target);
        continue;
      }
      r.addedNodes.forEach(n => { if (n instanceof Element) scan(n); });
    }
  }).observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ['src'],
  });
}
