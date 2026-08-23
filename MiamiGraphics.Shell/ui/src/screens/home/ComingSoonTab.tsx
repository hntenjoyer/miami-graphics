import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { GlassPanel, EASE_DEPTH } from '@/design';

interface Props {
  sectionId: string;
}

export function ComingSoonTab({ sectionId }: Props) {
  const { t } = useTranslation();
  const sectionLabel = t(`nav.${sectionId}`, { defaultValue: '' });

  return (
    <div className="flex flex-col items-center justify-center h-full px-6">
      <motion.div
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, ease: EASE_DEPTH }}
      >
        <GlassPanel depth="z2" tint="soft" rounded="3xl" className="px-10 py-8 text-center max-w-md">
          <h2 className="font-display text-2xl font-bold text-text-primary mb-2 tracking-tight">
            {sectionLabel || t('home.comingSoon')}
          </h2>
          <p className="text-text-secondary text-sm">
            {t('home.workInProgress')}
          </p>
        </GlassPanel>
      </motion.div>
    </div>
  );
}
