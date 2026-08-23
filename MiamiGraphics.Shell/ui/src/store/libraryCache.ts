import type { LibraryComponent } from '@/bridge/types';

type LibraryKind = 'minimap' | 'crosshair' | 'tracers' | 'bloodfx' | 'timecycle' | 'armor' | 'arena' | 'sounds';

const cache: Partial<Record<LibraryKind, LibraryComponent[]>> = {};

export function getCachedLibrary(kind: LibraryKind): LibraryComponent[] | null {
  return cache[kind] ?? null;
}

export function setCachedLibrary(kind: LibraryKind, rows: LibraryComponent[]): void {
  cache[kind] = rows;
}
