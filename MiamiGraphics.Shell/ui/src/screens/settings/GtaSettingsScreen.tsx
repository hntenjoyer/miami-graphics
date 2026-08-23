import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { EASE_DEPTH } from '@/design';
import { PresetsTab } from './PresetsTab';
import { BuilderTab } from './BuilderTab';
import { PcDiagBridgePill } from '@/screens/pcdiag/PcDiagBridgePill';
import { ScreenHero } from '@/screens/ScreenHero';

type Tab = 'builder' | 'pro';

export function GtaSettingsScreen() {
  const { t } = useTranslation();
  const [tab, setTab] = useState<Tab>('builder');

  const tabs: ReadonlyArray<{ id: Tab; label: string }> = [
    { id: 'builder', label: 'Конструктор' },
    { id: 'pro',     label: 'PRO Сеттинги' },
  ];

  const tabStrip = (floating: boolean) => (
    <>
      {tabs.map(tabItem => {
        const isActive = tabItem.id === tab;
        return (
          <button
            key={tabItem.id}
            type="button"
            onClick={() => setTab(tabItem.id)}
            style={{ outline: 'none' }}
            className={
              'inline-flex items-center h-9 px-3.5 rounded-xl border ' +
              'text-[11.5px] font-bold uppercase tracking-[0.12em] ' +
              'transition-all duration-300 ease-smooth ' +
              (floating ? 'backdrop-blur-md ' : '') +
              (isActive
                ? 'bg-white text-black border-white shadow-pill-active'
                : floating
                  ? 'bg-black/55 border-white/[0.14] text-white/75 ' +
                    'hover:bg-black/70 hover:text-white hover:border-white/30'
                  : 'bg-white/[0.04] border-white/[0.12] text-white/70 ' +
                    'hover:bg-white/[0.08] hover:text-white hover:border-white/25')
            }
          >
            {tabItem.label}
          </button>
        );
      })}
      <PcDiagBridgePill floating={floating} />
    </>
  );

  const floatingTabs = tab === 'builder';

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <header className="shrink-0 px-8">
        <ScreenHero
          title={t('gtaSettings.heroTitle')}
          subtitle={t('gtaSettings.heroSubtitle')}
        />
      </header>

      {tabs.length > 1 && !floatingTabs && (
        <nav className="shrink-0 flex items-center gap-2 px-8 pt-3 pb-1">
          {tabStrip(false)}
        </nav>
      )}

      <div className="relative flex-1 min-h-0 overflow-hidden">
        {tabs.length > 1 && floatingTabs && (
          <nav className="absolute top-4 left-4 z-30 flex items-center gap-2">
            {tabStrip(true)}
          </nav>
        )}
        <AnimatePresence mode="wait" initial={false}>
          <motion.div
            key={tab}
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: 1, y: 0 }}
            exit   ={{ opacity: 0, y: -6 }}
            transition={{ duration: 0.22, ease: EASE_DEPTH }}
            className="h-full"
          >
            {tab === 'builder' && <BuilderTab />}
            {tab === 'pro'     && <PresetsTab />}
          </motion.div>
        </AnimatePresence>
      </div>
    </div>
  );
}
