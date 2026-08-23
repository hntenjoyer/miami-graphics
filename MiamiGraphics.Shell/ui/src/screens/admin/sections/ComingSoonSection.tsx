import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { GlassPanel, EASE_DEPTH } from '@/design';

export function ComingSoonSection({ sectionId }: { sectionId: string }) {
  const { t } = useTranslation();
  const label = t(`admin.subnav.${sectionId}`, { defaultValue: '' });

  return (
    <div className="flex flex-col items-center justify-center h-full px-6">
      <motion.div
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, ease: EASE_DEPTH }}
      >
        <GlassPanel depth="z2" tint="soft" rounded="2xl" className="px-8 py-6 text-center max-w-md">
          <h2 className="text-xl font-bold text-text-primary mb-1 tracking-tight">
            {label || t('home.comingSoon')}
          </h2>
          <p className="text-text-secondary text-sm">{t('home.workInProgress')}</p>
        </GlassPanel>
      </motion.div>
    </div>
  );
}
