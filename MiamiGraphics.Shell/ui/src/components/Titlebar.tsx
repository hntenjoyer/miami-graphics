import { ChevronRight, Copy, HelpCircle, Minus, Square, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { bridge } from '@/bridge';
import { useNavStore } from '@/store/navStore';
import {
  MAIN_NAV, REDUX_CUSTOMIZE_CHILD, MODIFICATION_TABS, SECURITY_NAV_ITEM,
} from '@/data/navigation';

export function Titlebar() {
  const { t } = useTranslation();
  const activeId = useNavStore(s => s.activeId);
  const requestNavigate = useNavStore(s => s.requestNavigate);

  const sectionLabel = resolveSectionLabel(activeId, t);
  const onHome = activeId === 'home' || !activeId;
  const brand = t('titlebar.brand');
  const sectionHint = useNavStore(s => s.sectionHint);

  const [maximized, setMaximized] = useState(false);
  useEffect(() => {
    const cb = (d: { maximized: boolean }) => setMaximized(d.maximized);
    bridge.events?.on('window:state', cb);
    return () => bridge.events?.off('window:state', cb);
  }, []);

  return (
    <div
      className="relative h-9 flex items-center select-none
                 bg-glass-strong backdrop-blur-glass-heavy backdrop-saturate-liquid
                 z-30"
    >
      {}
      <span
        aria-hidden
        className="pointer-events-none absolute inset-0
                   bg-[radial-gradient(ellipse_at_50%_-30%,rgba(255,255,255,0.08),transparent_60%)]"
      />

      {}
      <div
        className="flex items-center gap-2 pl-4 pr-3 h-full shrink-0"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <button
          type="button"
          onClick={() => { if (!onHome) requestNavigate('home'); }}
          aria-label={onHome ? brand : t('titlebar.brandToHome')}
          title={onHome ? brand : t('titlebar.brandToHome')}
          disabled={onHome}
          className={
            'inline-flex items-center text-[11px] font-bold uppercase tracking-[0.22em] ' +
            'transition-colors duration-200 ease-depth ' +
            (onHome
              ? 'text-text-primary cursor-default'
              : 'text-text-primary/85 hover:text-white cursor-pointer')
          }
          style={{ outline: 'none' }}
        >
          {brand}
        </button>
        {!onHome && sectionLabel && (
          <>
            <ChevronRight
              size={12}
              className="text-text-muted/60 shrink-0"
              strokeWidth={2.4}
            />
            <span className="text-[11px] font-semibold uppercase tracking-[0.18em] text-text-secondary">
              {sectionLabel}
            </span>
            {sectionHint && sectionHint.subtitle && (
              <SectionHintGlyph
                title={sectionHint.title}
                subtitle={sectionHint.subtitle}
              />
            )}
          </>
        )}
      </div>

      {}
      <div
        className="flex-1 h-full"
        onMouseDown={(e) => { if (e.button === 0) void bridge.windowStartDrag(); }}
        onDoubleClick={() => void bridge.windowMaximize()}
      />

      <div className="flex items-stretch" onMouseDown={(e) => e.stopPropagation()}>
        <button
          type="button"
          onClick={() => void bridge.windowMinimize()}
          aria-label={t('titlebar.minimize')}
          title={t('titlebar.minimize')}
          className="w-10 h-9 flex items-center justify-center
                     text-text-muted hover:bg-white/5 hover:text-text-primary
                     transition-colors duration-200 ease-depth"
        >
          <Minus size={13} />
        </button>
        <button
          type="button"
          onClick={() => void bridge.windowMaximize()}
          aria-label={maximized ? 'Восстановить' : 'Развернуть'}
          title={maximized ? 'Восстановить' : 'Развернуть'}
          className="w-10 h-9 flex items-center justify-center
                     text-text-muted hover:bg-white/5 hover:text-text-primary
                     transition-colors duration-200 ease-depth"
        >
          {maximized ? <Copy size={12} /> : <Square size={11} />}
        </button>
        <button
          type="button"
          onClick={() => void bridge.windowClose()}
          aria-label={t('titlebar.close')}
          title={t('titlebar.close')}
          className="titlebar-btn--close w-10 h-9 flex items-center justify-center
                     text-text-muted hover:bg-status-error hover:text-white
                     transition-colors duration-200 ease-depth"
        >
          <X size={13} />
        </button>
      </div>
    </div>
  );
}

function resolveSectionLabel(
  activeId: string | null,
  t: (key: string) => string,
): string | null {
  if (!activeId) return null;

  const standalone: Record<string, string> = {
    'home':           'nav.home',
    'downloads':      'nav.downloads',
    'app-settings':   'sidebar.appSettings',
    'profile':        'nav.profile',
    'faq':            'nav.faq',
    'installed':      'nav.installed',
    'redux-customize': REDUX_CUSTOMIZE_CHILD.labelKey,
    [SECURITY_NAV_ITEM.id]: SECURITY_NAV_ITEM.labelKey,
    'environment':    'nav.environment',
    'timecycles':     'environment.timecycles',
    'env-roads':      'environment.roads',
    'env-trees':      'environment.trees',
  };
  if (activeId in standalone) return t(standalone[activeId]);
  for (const item of MAIN_NAV) {
    if (item.id === activeId) return t(item.labelKey);
    if (item.children) {
      const child = item.children.find(c => c.id === activeId);
      if (child) return t(child.labelKey);
    }
  }

  const mod = MODIFICATION_TABS.find(c => c.id === activeId);
  if (mod) return t(mod.labelKey);
  return null;
}

function SectionHintGlyph({ title, subtitle }: { title: string; subtitle: string }) {
  const [open, setOpen] = useState(false);
  return (
    <span
      className="relative inline-flex items-center"
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
      onFocus={() => setOpen(true)}
      onBlur={() => setOpen(false)}
    >
      <button
        type="button"
        aria-label={title}
        title={title}
        className="ml-1.5 inline-flex items-center justify-center w-[18px] h-[18px] rounded-full
                   text-text-muted/70 hover:text-text-primary
                   transition-colors duration-200 ease-depth focus-visible:outline-none"
      >
        <HelpCircle size={13} strokeWidth={2} />
      </button>
      <AnimatePresence>
        {open && (
          <motion.div
            key="hint"
            initial={{ opacity: 0, y: 4, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit   ={{ opacity: 0, y: 4, scale: 0.98 }}
            transition={{ duration: 0.18, ease: [0.22, 1, 0.36, 1] }}
            className="absolute left-0 top-full mt-2 z-50 w-[340px] max-w-[80vw] pointer-events-none"
            style={{ filter: 'drop-shadow(0 24px 48px rgba(0,0,0,0.55)) drop-shadow(0 4px 12px rgba(0,0,0,0.45))' }}
          >
            <div
              className="relative rounded-2xl px-5 py-4 overflow-hidden"
              style={{
                background: 'linear-gradient(180deg,#16161d 0%,#0e0e14 100%)',
                border: '1px solid rgba(255,255,255,0.08)',
                boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.06)',
              }}
            >
              <span
                aria-hidden
                className="absolute inset-x-4 top-0 h-px
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
              />
              <p className="text-[10px] font-bold uppercase tracking-[0.28em] text-text-muted">
                {title}
              </p>
              <p className="text-[12.5px] leading-relaxed text-text-secondary mt-2">
                {subtitle}
              </p>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </span>
  );
}
