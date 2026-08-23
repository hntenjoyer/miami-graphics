import { useCallback, useEffect, useState } from 'react';
import { bridge } from '@/bridge';
import { useSessionStore } from '@/store/sessionStore';

export function useItemFavorites(itemType: string) {
  const auth = useSessionStore(s => s.auth);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const [favorites, setFavorites] = useState<Set<string>>(new Set());

  useEffect(() => {
    if (!userId) { setFavorites(new Set()); return; }
    let alive = true;
    bridge.itemFavoritesList(userId, itemType)
      .then(ids => { if (alive) setFavorites(new Set(ids)); })
      .catch(() => {});
    return () => { alive = false; };
  }, [userId, itemType]);

  const toggle = useCallback((itemId: string) => {
    if (!userId || !itemId) return;
    setFavorites(prev => {
      const next = new Set(prev);
      if (next.has(itemId)) {
        next.delete(itemId);
        void bridge.itemFavoriteRemove(userId, itemType, itemId);
      } else {
        next.add(itemId);
        void bridge.itemFavoriteAdd(userId, itemType, itemId);
      }
      return next;
    });
  }, [userId, itemType]);

  return { favorites, toggle, canFavorite: !!userId };
}
