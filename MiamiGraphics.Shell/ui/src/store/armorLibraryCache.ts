import type { ArmorLibraryItem } from '@/bridge/types';

let cache: ArmorLibraryItem[] = [];

export function getArmorLibraryCache(): ArmorLibraryItem[] {
  return cache;
}

export function setArmorLibraryCache(rows: ArmorLibraryItem[]): void {
  cache = rows;
}
