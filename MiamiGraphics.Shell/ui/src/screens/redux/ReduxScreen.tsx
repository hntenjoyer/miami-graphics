import { useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useReduxStore } from '@/store/reduxStore';
import { useSessionStore } from '@/store/sessionStore';
import { ReduxBrowse } from './ReduxBrowse';
import { ReduxDetail } from './ReduxDetail';
import { EASE_DEPTH } from '@/design';

export function ReduxScreen() {
  const selectedId = useReduxStore(s => s.selectedId);
  const load = useReduxStore(s => s.load);
  const syncFavoritesFromCloud = useReduxStore(s => s.syncFavoritesFromCloud);
  const resetFavoritesToGuest = useReduxStore(s => s.resetFavoritesToGuest);
  const auth = useSessionStore(s => s.auth);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
    if (userId) {
      void syncFavoritesFromCloud(userId);
    } else {
      resetFavoritesToGuest();
    }
  }, [auth, syncFavoritesFromCloud, resetFavoritesToGuest]);

  return (
    <AnimatePresence mode="wait" initial={false}>
      {selectedId ? (
        <motion.div
          key={`detail:${selectedId}`}

          initial={{ opacity: 0, scale: 0.99 }}
          animate={{ opacity: 1, scale: 1 }}
          exit={{ opacity: 0, scale: 0.99 }}
          transition={{ duration: 0.28, ease: EASE_DEPTH }}
          className="h-full"
        >
          <ReduxDetail />
        </motion.div>
      ) : (
        <motion.div
          key="browse"

          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.22, ease: EASE_DEPTH }}
          className="h-full"
        >
          <ReduxBrowse />
        </motion.div>
      )}
    </AnimatePresence>
  );
}
