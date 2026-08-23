const CACHE_PREFIX = 'hntgraph.cache.';

const CACHE_VERSION = 1;

interface CacheEntry<T> {
  v: number;
  ts: number;
  data: T;
}

export function readCache<T>(key: string, maxAgeMs: number = Infinity): T | null {
  try {
    const raw = window.localStorage.getItem(CACHE_PREFIX + key);
    if (!raw) return null;
    const entry = JSON.parse(raw) as CacheEntry<T>;
    if (entry.v !== CACHE_VERSION) return null;
    if (Date.now() - entry.ts > maxAgeMs) return null;
    return entry.data;
  } catch {
    return null;
  }
}

export function writeCache<T>(key: string, data: T): void {
  try {
    const entry: CacheEntry<T> = { v: CACHE_VERSION, ts: Date.now(), data };
    window.localStorage.setItem(CACHE_PREFIX + key, JSON.stringify(entry));
  } catch {

  }
}

export function clearCache(key: string): void {
  try { window.localStorage.removeItem(CACHE_PREFIX + key); }
  catch {  }
}

export const CacheKeys = {
  reduxItems:    'redux.items',
  gunpackPublic: 'gunpack.public',
} as const;
