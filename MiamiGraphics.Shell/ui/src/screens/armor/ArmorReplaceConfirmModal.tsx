import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { CheckCircle2, Sparkles, ArrowRight, Check, X, Loader2, Shield } from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';

interface ArmorTile {
  name:   string;
  screenshot: string | null;
  kindLabel?: string;
}

type Selection = 'current' | 'incoming';

interface Props {
  open: boolean;
  current: ArmorTile;
  incoming: ArmorTile;
  busy: boolean;
  onKeepCurrent: () => void;
  onInstallNew:  () => void;
  onCancel:      () => void;
}

export function ArmorReplaceConfirmModal({
  open, current, incoming, busy, onKeepCurrent, onInstallNew, onCancel,
}: Props) {
  const { t } = useTranslation();

  const [picked, setPicked] = useState<Selection>('incoming');
  useEffect(() => { if (open) setPicked('incoming'); }, [open]);

  const handleContinue = () => {
    if (busy) return;
    if (picked === 'current') onKeepCurrent();
    else                       onInstallNew();
  };

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[100] flex items-center justify-center p-6"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.24, ease: EASE_DEPTH }}
          onClick={() => { if (!busy) onCancel(); }}
          onKeyDown={(e) => { if (e.key === 'Escape' && !busy) onCancel(); }}
          style={{
            background:
              'radial-gradient(ellipse at center, rgba(20,20,28,0.78) 0%, rgba(0,0,0,0.86) 75%)',
            backdropFilter: 'blur(14px) saturate(140%)',
            WebkitBackdropFilter: 'blur(14px) saturate(140%)',
          }}
        >
          <motion.div
            initial={{ opacity: 0, scale: 0.94, y: 14 }}
            animate={{ opacity: 1, scale: 1,    y: 0 }}
            exit={{ opacity: 0, scale: 0.96, y: 8 }}
            transition={{ duration: 0.34, ease: EASE_DEPTH }}
            onClick={e => e.stopPropagation()}
            className="w-full max-w-[880px]"
            style={{
              filter:
                'drop-shadow(0 26px 60px rgba(255,255,255,0.10)) drop-shadow(0 6px 18px rgba(0,0,0,0.62))',
            }}
          >
            <GlassPanel
              depth="z3"
              tint="ultra"
              rounded="3xl"
              className="relative overflow-hidden p-8 flex flex-col gap-7 shadow-glass-inner
                         border border-white/[0.08]"
            >
              {}
              <span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
              />
              <span
                aria-hidden
                className="pointer-events-none absolute -top-24 -left-24 w-72 h-72 rounded-full opacity-30 blur-3xl"
                style={{ background: 'radial-gradient(circle, rgba(255,255,255,0.5), transparent 70%)' }}
              />
              <span
                aria-hidden
                className="pointer-events-none absolute -bottom-32 -right-20 w-80 h-80 rounded-full opacity-20 blur-3xl"
                style={{ background: 'radial-gradient(circle, rgba(255,255,255,0.4), transparent 70%)' }}
              />

              <button
                type="button"
                onClick={() => { if (!busy) onCancel(); }}
                disabled={busy}
                aria-label={t('armor.replaceCancel')}
                className="absolute top-4 right-4 w-9 h-9 rounded-xl flex items-center justify-center
                           bg-white/[0.04] border border-white/[0.06] text-text-secondary
                           hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                           transition-colors
                           disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <X size={14} />
              </button>

              <header className="flex flex-col gap-2 text-center max-w-2xl mx-auto">
                <span className="text-[11px] uppercase tracking-[0.28em] text-text-muted font-semibold">
                  {t('armor.replaceEyebrow')}
                </span>
                <h2 className="font-display text-2xl font-bold uppercase tracking-wide text-text-primary leading-tight">
                  {t('armor.replaceTitle')}
                </h2>
                <p className="text-sm text-text-secondary leading-relaxed">
                  {t('armor.replacePickHint')}
                </p>
              </header>

              <div className="grid items-stretch gap-4
                              grid-cols-1
                              md:grid-cols-[1fr_auto_1fr]">
                <PreviewTile
                  variant="current"
                  selected={picked === 'current'}
                  disabled={busy}
                  label={t('armor.replaceCurrent')}
                  badgeIcon={<CheckCircle2 size={11} />}
                  badgeText={t('armor.installedPill')}
                  tile={current}
                  onPick={() => setPicked('current')}
                />
                <ArrowDivider />
                <PreviewTile
                  variant="incoming"
                  selected={picked === 'incoming'}
                  disabled={busy}
                  label={t('armor.replaceIncoming')}
                  badgeIcon={<Sparkles size={11} />}
                  badgeText={t('armor.replaceIncomingBadge')}
                  tile={incoming}
                  onPick={() => setPicked('incoming')}
                />
              </div>

              {}
              <div className="flex flex-col gap-2.5 items-center">
                <motion.button
                  type="button"
                  onClick={handleContinue}
                  disabled={busy}
                  whileHover={!busy ? { y: -1 } : undefined}
                  whileTap={!busy ? { scale: 0.98 } : undefined}
                  transition={{ duration: 0.15, ease: EASE_DEPTH }}
                  className="h-12 w-full max-w-[420px] rounded-2xl overflow-hidden
                             bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                             hover:bg-bg-elevated/80 hover:border-white/[0.20]
                             text-sm font-bold uppercase tracking-[0.16em]
                             transition-colors disabled:opacity-50 disabled:cursor-not-allowed
                             flex items-center justify-center gap-2"
                  style={{ outline: 'none' }}
                >
                  {busy
                    ? <Loader2 size={14} className="animate-spin" />
                    : null}
                  {busy
                    ? t('armor.replaceBusy')
                    : (picked === 'current'
                        ? t('armor.replaceContinueKeep')
                        : t('armor.replaceContinueInstall'))}
                </motion.button>
                <button
                  type="button"
                  onClick={() => { if (!busy) onCancel(); }}
                  disabled={busy}
                  className="text-[11px] uppercase tracking-[0.22em] text-text-muted
                             hover:text-text-primary transition-colors
                             disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  {t('armor.replaceCancel')}
                </button>
              </div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function PreviewTile({
  variant, selected, disabled, label, badgeIcon, badgeText, tile, onPick,
}: {
  variant: 'current' | 'incoming';
  selected: boolean;
  disabled: boolean;
  label: string;
  badgeIcon: React.ReactNode;
  badgeText: string;
  tile:  ArmorTile;
  onPick: () => void;
}) {
  const isIncoming = variant === 'incoming';
  return (
    <button
      type="button"
      onClick={onPick}
      disabled={disabled}
      className="group flex flex-col gap-2.5 text-left p-0 rounded-2xl
                 transition disabled:opacity-50 disabled:cursor-not-allowed"
    >
      <div className="flex items-center justify-between px-1">
        <span className="text-[10px] uppercase tracking-[0.24em] text-text-muted font-semibold">
          {label}
        </span>
        <span
          className={
            'inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] uppercase tracking-wider font-semibold ' +
            (isIncoming
              ? 'bg-white/[0.10] text-text-primary border border-white/[0.18]'
              : 'bg-status-success/20 text-status-success')
          }
        >
          {badgeIcon}
          {badgeText}
        </span>
      </div>

      {}
      <div
        className={
          'relative aspect-square rounded-2xl overflow-hidden transition ' +
          (selected
            ? 'ring-2 ring-white ring-offset-0'
            : 'ring-1 ring-white/[0.08] group-hover:ring-white/[0.25]')
        }
        style={{
          background: 'linear-gradient(155deg, rgba(255,255,255,0.03), rgba(255,255,255,0))',
          boxShadow: selected
            ? 'inset 0 0 60px rgba(255,255,255,0.10), 0 10px 28px rgba(255,255,255,0.18)'
            : 'inset 0 0 50px rgba(0,0,0,0.4)',
        }}
      >
        <div className="absolute inset-0 bg-glass-strong" />
        {tile.screenshot ? (
          <>
            <div
              aria-hidden
              className="absolute inset-0"
              style={{
                background: `url(${tile.screenshot}) center / cover no-repeat`,
                filter: 'blur(28px) brightness(0.55) saturate(1.2)',
                transform: 'scale(1.18)',
              }}
            />
            <div className="absolute inset-0 flex items-center justify-center p-4">
              <img
                src={tile.screenshot}
                alt=""
                draggable={false}
                className="max-w-full max-h-full w-auto h-auto object-contain rounded-md"
              />
            </div>
          </>
        ) : (
          <div
            aria-hidden
            className="absolute inset-0 flex items-center justify-center text-text-muted"
            style={{ background: 'linear-gradient(135deg, rgba(255,255,255,0.08), rgba(20,20,28,1))' }}
          >
            <Shield size={36} className="opacity-40" />
          </div>
        )}
        {}
        {selected && (
          <span
            className="absolute top-3 right-3 w-7 h-7 rounded-full flex items-center justify-center
                       bg-white text-black border border-white
                       shadow-[0_6px_18px_rgba(255,255,255,0.45),inset_0_1px_0_rgba(255,255,255,0.85)]"
          >
            <Check size={14} strokeWidth={3} />
          </span>
        )}
      </div>

      <div className="flex flex-col gap-0.5 pt-1">
        <h3
          className={
            'text-base font-bold truncate uppercase tracking-wide text-center transition-colors ' +
            (selected ? 'text-text-primary' : 'text-text-secondary group-hover:text-text-primary')
          }
        >
          {tile.name}
        </h3>
        {tile.kindLabel && (
          <span className="text-[10px] uppercase tracking-[0.18em] text-text-muted text-center">
            {tile.kindLabel}
          </span>
        )}
      </div>
    </button>
  );
}

function ArrowDivider() {
  return (
    <div className="hidden md:flex items-center justify-center px-2">
      <div
        className="w-10 h-10 rounded-full flex items-center justify-center
                   bg-white/[0.04] border border-white/[0.10] text-text-secondary"
        style={{ boxShadow: 'inset 0 1px 0 rgba(255,255,255,0.10)' }}
      >
        <ArrowRight size={16} />
      </div>
    </div>
  );
}
