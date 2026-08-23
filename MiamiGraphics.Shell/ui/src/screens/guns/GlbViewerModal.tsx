import { Suspense, lazy } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { Loader2 } from 'lucide-react';

interface Props {
  glbUrl: string | null;
  title:  string;
  onClose: () => void;
  subjectKind?: 'gun' | 'armor';
}

const InnerModal = lazy(() =>
  import('./GlbViewerModalCanvas').then(m => ({ default: m.GlbViewerModal }))
);

export function GlbViewerModal(props: Props) {
  return createPortal(
    <Suspense fallback={<ChunkLoadingFallback onClose={props.onClose} />}>
      <InnerModal {...props} />
    </Suspense>,
    document.body,
  );
}

function ChunkLoadingFallback({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  return (
    <motion.div
      className="fixed inset-0 z-[100] bg-black/65 backdrop-blur-glass-ultra backdrop-saturate-liquid
                 flex items-center justify-center p-6"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.18 }}
      onClick={onClose}
    >
      <div className="flex flex-col items-center gap-3 text-text-primary">
        <Loader2 size={28} className="animate-spin text-accent" />
        <span className="text-xs uppercase tracking-wider text-text-muted">{t('guns.viewer.chunkLoading', 'Загружаю 3D-просмотр')}</span>
      </div>
    </motion.div>
  );
}
