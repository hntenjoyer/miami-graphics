const seen = new Set<string>();

export function prefetchGlb(url: string | null | undefined): void {
  if (!url) return;
  if (seen.has(url)) return;
  seen.add(url);
  try {
    void fetch(url, { credentials: 'omit' }).catch(() => {});
  } catch {
  }
}
