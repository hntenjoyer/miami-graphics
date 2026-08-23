import { useMemo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { AnimatePresence, motion } from 'framer-motion';
import { X, Trophy } from 'lucide-react';
import {
  MODIFICATION_TABS, type NavItem, type NavItemId,
} from '@/data/navigation';
import { EASE_DEPTH } from '@/design/tokens';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useBigMapStore } from '@/store/bigMapStore';
import { useSubmitDraftStore } from '@/store/submitDraftStore';
import { useNavStore } from '@/store/navStore';

interface ModificationsHubProps {
  activeSubId: NavItemId;
  onSubTabChange: (id: NavItemId) => void;
  children: ReactNode;
}

export function ModificationsHub({ activeSubId, onSubTabChange, children }: ModificationsHubProps) {
  const { t } = useTranslation();
  const tabs = useMemo(() => MODIFICATION_TABS, []);

  const reduxDetailRaw = useReduxStore(s => s.selectedId !== null);
  const gunpackDetailRaw = useGunpackStore(
    s => s.selectedId !== null || s.selectedWhitelistName !== null,
  );
  const bigMapDetailRaw = useBigMapStore(s => s.selectedId !== null);
  const reduxDetailOpen   = reduxDetailRaw   && activeSubId === 'redux';
  const gunpackDetailOpen = gunpackDetailRaw && activeSubId === 'guns';
  const bigMapDetailOpen  = bigMapDetailRaw  && activeSubId === 'bigmap';
  const stripVisible = !(reduxDetailOpen || gunpackDetailOpen || bigMapDetailOpen);

  const pickingFor   = useSubmitDraftStore(s => s.pickingFor);
  const cancelPick   = useSubmitDraftStore(s => s.cancelPick);
  const requestNavigate = useNavStore(s => s.requestNavigate);
  const onCancelPickAndReturn = () => {
    cancelPick();
    requestNavigate('players');
  };

  return (
    <div className="h-full flex flex-col">
      {pickingFor && (
        <div
          className="shrink-0 px-5 py-3 flex items-center gap-3 border-b border-accent/30"
          style={{ background: 'color-mix(in srgb, var(--accent) 14%, transparent)' }}
        >
          <span className="inline-flex items-center justify-center w-8 h-8 rounded-lg
                           bg-accent text-text-on-accent">
            <Trophy size={14} />
          </span>
          <div className="flex-1 min-w-0">
            <div className="text-[11px] font-bold uppercase tracking-[0.18em] text-accent">
              {pickingFor === 'redux'
                ? t('players.pick.reduxTitle', 'Выбор редукса для сборки')
                : t('players.pick.gunpackTitle', 'Выбор ган-пака для сборки')}
            </div>
            <div className="text-[12px] text-text-secondary">
              {t('players.pick.hint', 'Откройте карточку и нажмите «Использовать в сборке».')}
              {pickingFor === 'redux' && ' ' + t('players.pick.hintRedux', 'Можно зайти в «Кастомизировать» - выбор запомнится.')}
            </div>
          </div>
          <button
            type="button"
            onClick={onCancelPickAndReturn}
            className="inline-flex items-center gap-1.5 px-3 h-8 rounded-lg
                       bg-bg-elevated/60 border border-border-subtle
                       text-[12px] text-text-secondary hover:text-text-primary
                       hover:bg-bg-elevated transition-colors"
            style={{ outline: 'none' }}
          >
            <X size={12} /> {t('players.pick.cancel', 'Отменить')}
          </button>
        </div>
      )}
      {}
      <AnimatePresence initial={false}>
        {stripVisible && (
          <motion.div
            key="hub-strip"
            initial={{ opacity: 0, height: 0, y: -10 }}
            animate={{ opacity: 1, height: 'auto', y: 0 }}
            exit   ={{ opacity: 0, height: 0, y: -10 }}
            transition={{
              opacity: { duration: 0.32, ease: EASE_DEPTH },
              height:  { duration: 0.42, ease: EASE_DEPTH },
              y:       { duration: 0.36, ease: EASE_DEPTH },
            }}
            className="shrink-0 relative overflow-hidden"
          >
            <div className="px-4 pt-3 pb-3">
              <span
                aria-hidden
                className="pointer-events-none absolute inset-x-0 -top-px h-px
                           bg-gradient-to-r from-transparent via-white/12 to-transparent"
              />
              <div className="flex flex-wrap items-stretch gap-2">
                {tabs.map((tab, idx) => (
                  <ModificationChip
                    key={tab.id}
                    tab={tab}
                    index={idx}
                    active={tab.id === activeSubId}
                    label={t(tab.labelKey)}
                    onClick={() => onSubTabChange(tab.id)}
                  />
                ))}
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {}
      <div className="flex-1 min-h-0 relative">
        <AnimatePresence mode="wait" initial={false}>
          <motion.div
            key={activeSubId}
            className="absolute inset-0"
            initial={{ opacity: 0, y: 12, scale: 0.992 }}
            animate={{ opacity: 1, y:  0, scale: 1 }}
            exit   ={{ opacity: 0, y: -8, scale: 0.996 }}
            transition={{
              opacity: { duration: 0.36, ease: EASE_DEPTH },
              y:       { duration: 0.42, ease: EASE_DEPTH },
              scale:   { duration: 0.42, ease: EASE_DEPTH },
            }}
          >
            {children}
          </motion.div>
        </AnimatePresence>
      </div>
    </div>
  );
}

function ModificationChip({
  tab, active, label, index, onClick,
}: {
  tab: NavItem;
  active: boolean;
  label: string;
  index: number;
  onClick: () => void;
}) {
  const Icon = tab.icon;
  return (
    <motion.button
      type="button"
      onClick={onClick}
      title={label}
      aria-label={label}

      initial={{ opacity: 0, y: -10, scale: 0.96 }}
      animate={{ opacity: 1, y:  0, scale: 1 }}
      transition={{
        duration: 0.42,
        ease: EASE_DEPTH,
        delay: 0.12 + index * 0.05,
      }}
      whileHover={{ y: -2, transition: { duration: 0.18, ease: EASE_DEPTH, delay: 0 } }}
      whileTap={{ scale: 0.97, transition: { duration: 0.12, ease: EASE_DEPTH } }}
      className={
        'group relative flex flex-1 min-w-fit items-center justify-center gap-2 ' +
        'h-[46px] px-3.5 rounded-2xl whitespace-nowrap ' +
        'text-[10.5px] font-bold uppercase tracking-[0.14em] ' +
        'transition-[background-color,color,border-color,box-shadow] duration-300 ease-smooth ' +
        'border focus-visible:outline-none ' +
        (active
          ? 'bg-bg-elevated text-text-primary border-white/[0.16] ' +
            'shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_10px_26px_-12px_rgba(0,0,0,0.65)]'
          : 'bg-white/[0.03] text-text-secondary border-white/[0.07] ' +
            'hover:bg-white/[0.07] hover:text-text-primary hover:border-white/[0.16]')
      }
    >
      {active && (
        <span
          aria-hidden
          className="absolute top-0 inset-x-3 h-px pointer-events-none
                     bg-gradient-to-r from-transparent via-white/35 to-transparent"
        />
      )}
      <Icon size={14} strokeWidth={2} className="relative shrink-0" />
      <span className="relative leading-none">{label}</span>
    </motion.button>
  );
}
