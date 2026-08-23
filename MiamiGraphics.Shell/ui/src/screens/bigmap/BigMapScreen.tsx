import { motion, AnimatePresence } from 'framer-motion';
import { useBigMapStore } from '@/store/bigMapStore';
import { BigMapBrowse } from './BigMapBrowse';
import { BigMapDetail } from './BigMapDetail';
import { EASE_DEPTH } from '@/design';

export function BigMapScreen() {
  const selectedId = useBigMapStore(s => s.selectedId);
  const select     = useBigMapStore(s => s.select);

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
          <BigMapDetail onBack={() => select(null)} />
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
          <BigMapBrowse onOpenMap={(id) => select(id)} />
        </motion.div>
      )}
    </AnimatePresence>
  );
}
