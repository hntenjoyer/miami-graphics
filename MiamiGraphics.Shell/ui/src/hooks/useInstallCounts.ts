import { useEffect, useMemo, useState } from 'react';
import { bridge } from '@/bridge';

export function useInstallCounts(eventType: string): Record<string, number> {
  const [counts, setCounts] = useState<Record<string, number>>({});

  useEffect(() => {
    let alive = true;
    bridge.gtaInstallCounts(eventType)
      .then(rec => {
        if (!alive) return;
        const low: Record<string, number> = {};
        for (const [k, v] of Object.entries(rec ?? {})) low[k.toLowerCase()] = v;
        setCounts(low);
      })
      .catch(() => {});
    return () => { alive = false; };
  }, [eventType]);

  return counts;
}

export function useDuplicateNames(names: string[]): Set<string> {
  const key = names.join('|');
  return useMemo(() => {
    const seen = new Set<string>();
    const dup  = new Set<string>();
    for (const n of names) {
      const k = n.toLowerCase();
      if (seen.has(k)) dup.add(k); else seen.add(k);
    }
    return dup;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
}

export function installCountFor(
  counts: Record<string, number>,
  dupNames: Set<string>,
  item: { id: string; name: string },
): number {
  const nameKey = item.name.toLowerCase();
  return dupNames.has(nameKey)
    ? (counts[item.id.toLowerCase()] ?? 0)
    : (counts[nameKey] ?? 0);
}
