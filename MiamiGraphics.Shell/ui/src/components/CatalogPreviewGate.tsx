import { type ReactNode } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { AccentLoader, EASE_DEPTH } from '@/design';

interface CatalogPreviewGateProps {
  urls: readonly (string | null | undefined)[];
  ready: boolean;
  children: ReactNode;
  label?: string;
}

export function CatalogPreviewGate({ ready, children, label }: CatalogPreviewGateProps) {
  const { t } = useTranslation();
  return (
    <div className="relative">
      <div
        style={{
          filter: ready ? 'none' : 'blur(14px) saturate(120%)',
          transition: 'filter 0.45s cubic-bezier(0.22, 1, 0.36, 1)',
          pointerEvents: ready ? 'auto' : 'none',
        }}
      >
        {children}
      </div>
      <AnimatePresence>
        {!ready && (
          <motion.div
            key="catalog-loader"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit   ={{ opacity: 0 }}
            transition={{ duration: 0.3, ease: EASE_DEPTH }}
            className="absolute inset-0 z-10 flex flex-col items-center justify-start pt-24 gap-3 pointer-events-none"
          >
            <AccentLoader size={28} />
            <span className="text-[11px] uppercase tracking-[0.22em] text-text-muted font-semibold">
              {label ?? t('catalog.loadingPreviews', 'Загружаем превью…')}
            </span>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
