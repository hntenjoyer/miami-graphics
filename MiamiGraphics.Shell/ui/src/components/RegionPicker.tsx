import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Zap, Globe2, Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { GlassPanel, Glow3DButton, EASE_DEPTH } from '@/design';
import { bridge } from '@/bridge';
import type { ServerRegionPing } from '@/bridge/IAppBridge';

interface Props {
  showCancel?: boolean;
  initialRegion?: 'eu' | 'ru' | '';
  onChosen: (region: 'eu' | 'ru') => void;
  onCancel?: () => void;
}

export function RegionPicker({ showCancel, initialRegion, onChosen, onCancel }: Props) {
  const { t } = useTranslation();
  const [selected, setSelected] = useState<'eu' | 'ru' | null>(
    initialRegion === 'eu' || initialRegion === 'ru' ? initialRegion : null,
  );
  const [ping,     setPing]     = useState<ServerRegionPing | null>(null);
  const [pinging,  setPinging]  = useState(true);
  const [saving,   setSaving]   = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const result = await bridge.serverRegionPing();
        if (!cancelled) setPing(result);
      } catch (err) {
        console.warn('[RegionPicker] ping failed', err);
        if (!cancelled) setPing({ euMs: null, ruMs: null });
      } finally {
        if (!cancelled) setPinging(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (selected || !ping) return;
    if (ping.ruMs !== null && ping.euMs !== null) {
      setSelected(ping.ruMs < ping.euMs ? 'ru' : 'eu');
    } else if (ping.ruMs !== null) {
      setSelected('ru');
    } else if (ping.euMs !== null) {
      setSelected('eu');
    }
  }, [ping, selected]);

  const handleConfirm = async () => {
    if (!selected || saving) return;
    setSaving(true);
    try {
      await bridge.serverRegionSet(selected);
      onChosen(selected);
    } catch (err) {
      console.error('[RegionPicker] save failed', err);
      setSaving(false);
    }
  };

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 z-[110] flex items-center justify-center p-6"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        transition={{ duration: 0.28, ease: EASE_DEPTH }}
        style={
          showCancel
            ? {
                background:
                  'radial-gradient(ellipse at center, rgba(20,20,28,0.82) 0%, rgba(0,0,0,0.92) 75%)',
                backdropFilter: 'blur(14px) saturate(140%)',
                WebkitBackdropFilter: 'blur(14px) saturate(140%)',
              }
            : {
                background:
                  'radial-gradient(ellipse at center, transparent 38%, rgba(8,8,12,0.45) 100%)',
              }
        }

        onClick={showCancel ? onCancel : undefined}
      >
        <motion.div
          initial={{ opacity: 0, scale: 0.94, y: 14 }}
          animate={{ opacity: 1, scale: 1,    y: 0  }}
          exit   ={{ opacity: 0, scale: 0.96, y: 8  }}
          transition={{ duration: 0.38, ease: EASE_DEPTH }}
          onClick={e => e.stopPropagation()}
          className="w-full max-w-[640px]"
        >
          <GlassPanel
            depth="z3"
            tint="ultra"
            rounded="3xl"
            className="relative overflow-hidden p-8 flex flex-col gap-6 shadow-glass-inner
                       border border-white/[0.08]"
          >
            {}
            <span
              aria-hidden
              className="absolute top-0 inset-x-0 h-px pointer-events-none
                         bg-gradient-to-r from-transparent via-white/40 to-transparent"
            />

            <header>
              <div className="flex items-center gap-2 mb-2">
                <Globe2 size={16} className="text-text-secondary" />
                <span className="text-[10px] font-mono font-bold tracking-[0.14em] uppercase text-text-muted">
                  {t('regionPicker.tag')}
                </span>
              </div>
              <h2 className="text-[22px] font-display font-bold text-text-primary
                             leading-tight tracking-[-0.02em]">
                {t('regionPicker.title')}
              </h2>
              <p className="mt-2 text-[13px] text-text-secondary leading-relaxed">
                {t('regionPicker.subtitle')}
              </p>
            </header>

            <div className="grid grid-cols-2 gap-3">
              <RegionCard
                code="eu"
                title={t('regionPicker.eu.title')}
                subtitle={t('regionPicker.eu.subtitle')}
                hint={t('regionPicker.eu.hint')}
                latencyMs={ping?.euMs ?? null}
                pinging={pinging}
                selected={selected === 'eu'}
                onSelect={() => setSelected('eu')}
              />
              <RegionCard
                code="ru"
                title={t('regionPicker.ru.title')}
                subtitle={t('regionPicker.ru.subtitle')}
                hint={t('regionPicker.ru.hint')}
                latencyMs={ping?.ruMs ?? null}
                pinging={pinging}
                selected={selected === 'ru'}
                onSelect={() => setSelected('ru')}
              />
            </div>

            <p className="text-[11px] text-text-muted leading-relaxed pl-1">
              {t('regionPicker.footnote')}
            </p>

            <div className="flex items-center justify-end gap-2 pt-1">
              {showCancel && (
                <button
                  type="button"
                  onClick={onCancel}
                  className="px-4 h-10 rounded-xl text-[13px] font-semibold
                             text-text-secondary border border-white/[0.06] bg-white/[0.04]
                             hover:bg-white/[0.10] hover:text-text-primary
                             hover:border-white/[0.18] transition-colors"
                  style={{ outline: 'none' }}
                >
                  {t('common.cancel')}
                </button>
              )}
              <Glow3DButton
                type="button"
                variant="secondary"
                size="md"
                onClick={handleConfirm}
                disabled={!selected || saving}
                busy={saving}
              >
                {saving ? t('regionPicker.saving') : t('regionPicker.confirm')}
              </Glow3DButton>
            </div>
          </GlassPanel>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}

interface CardProps {
  code: 'eu' | 'ru';
  title: string;
  subtitle: string;
  hint: string;
  latencyMs: number | null;
  pinging: boolean;
  selected: boolean;
  onSelect: () => void;
}

function RegionCard({ code, title, subtitle, hint, latencyMs, pinging, selected, onSelect }: CardProps) {
  const { t } = useTranslation();
  return (
    <button
      type="button"
      onClick={onSelect}
      className="relative text-left p-5 rounded-2xl transition-all duration-200
                 border bg-white/[0.025]"
      style={{
        borderColor: selected ? 'rgba(255,255,255,0.32)' : 'rgba(255,255,255,0.08)',
        background:  selected ? 'rgba(255,255,255,0.06)'  : 'rgba(255,255,255,0.025)',
        outline: 'none',
        cursor: 'pointer',
      }}
    >
      <div className="flex items-start justify-between mb-2">
        <div className="flex items-center gap-2">
          <span
            className="inline-flex items-center justify-center w-9 h-9 rounded-xl text-[15px]
                       font-mono font-bold tracking-wider"
            style={{
              background: code === 'ru'
                ? 'linear-gradient(135deg, rgba(255,255,255,0.06), rgba(255,255,255,0.02))'
                : 'linear-gradient(135deg, rgba(255,255,255,0.06), rgba(255,255,255,0.02))',
              border: '1px solid rgba(255,255,255,0.12)',
              color: 'rgba(255,255,255,0.92)',
            }}
          >
            {code === 'ru' ? 'RU' : 'EU'}
          </span>
          <div className="leading-tight">
            <div className="text-[14px] font-display font-bold text-text-primary tracking-[-0.01em]">
              {title}
            </div>
            <div className="text-[11px] text-text-muted">{subtitle}</div>
          </div>
        </div>

        <div
          className={`w-5 h-5 rounded-full flex items-center justify-center transition-all
                      ${selected ? 'bg-white' : 'bg-white/[0.06] border border-white/[0.18]'}`}
        >
          {selected && <Check size={12} className="text-black" strokeWidth={3} />}
        </div>
      </div>

      {}
      <div className="flex items-center gap-2 mt-3 pt-3 border-t border-white/[0.06]">
        <Zap size={11} className="text-text-muted" />
        <span className="text-[10px] uppercase tracking-[0.12em] font-bold text-text-muted">
          {t('regionPicker.latency')}
        </span>
        <span className="ml-auto text-[12px] font-mono font-bold"
          style={{
            color: pinging              ? 'rgba(255,255,255,0.4)'
                 : latencyMs === null   ? 'rgba(255,180,180,0.85)'
                 : latencyMs < 80       ? 'rgba(110,231,183,0.95)'
                 : latencyMs < 200      ? 'rgba(255,255,255,0.85)'
                 :                        'rgba(255,210,150,0.95)'
          }}>
          {pinging
            ? '…'
            : latencyMs === null
              ? t('regionPicker.unreachable')
              : `${latencyMs} ms`}
        </span>
      </div>

      <p className="text-[11.5px] text-text-secondary leading-relaxed mt-3">
        {hint}
      </p>
    </button>
  );
}
