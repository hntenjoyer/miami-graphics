import { useEffect, useState, type ReactNode } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { AlertTriangle, X, ArrowRight, ImageOff } from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';

interface Props {
  open:    boolean;
  title:   string;
  message: string;
  confirmLabel: string;
  cancelLabel:  string;
  destructive?: boolean;
  imageUrl?: string | null;
  imageContain?: boolean;
  hideConfirmArrow?: boolean;
  details?: ReactNode;
  onConfirm: () => void;
  onCancel:  () => void;
}

export function ConfirmModal({
  open, title, message, confirmLabel, cancelLabel, destructive, imageUrl, imageContain, hideConfirmArrow, details, onConfirm, onCancel,
}: Props) {

  const hasImage = imageUrl !== undefined;
  const [imgErr, setImgErr] = useState(false);
  useEffect(() => { setImgErr(false); }, [imageUrl]);

  const trimmed = message.trim();
  const blankSplit = trimmed.indexOf('\n\n');
  const subject = blankSplit > -1 ? trimmed.slice(0, blankSplit).trim() : null;
  const body    = blankSplit > -1 ? trimmed.slice(blankSplit + 2).trim() : trimmed;

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[100] flex items-center justify-center p-6"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.24, ease: EASE_DEPTH }}
          onClick={onCancel}
          onKeyDown={(e) => { if (e.key === 'Escape') onCancel(); }}
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
            exit   ={{ opacity: 0, scale: 0.96, y: 8 }}
            transition={{ duration: 0.34, ease: EASE_DEPTH }}
            onClick={e => e.stopPropagation()}
            className="w-full max-w-[520px]"
            style={{
              filter: destructive
                ? 'drop-shadow(0 22px 50px color-mix(in srgb, var(--status-error) 20%, transparent)) drop-shadow(0 6px 18px rgba(0,0,0,0.55))'
                : 'drop-shadow(0 22px 50px rgba(255,255,255,0.10)) drop-shadow(0 6px 18px rgba(0,0,0,0.55))',
            }}
          >
            <GlassPanel
              depth="z3"
              tint="ultra"
              rounded="3xl"
              className="relative overflow-hidden p-7 flex flex-col gap-5 shadow-glass-inner
                         border border-white/[0.08]"
            >
              {}
              <span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
              />

              <div className={'flex gap-4 ' + (subject ? 'items-start' : 'items-center')}>
                {}
                <div className="relative shrink-0 w-12 h-12 flex items-center justify-center">
                  <span
                    aria-hidden
                    className="absolute -inset-2 rounded-3xl"
                    style={{
                      background: destructive
                        ? 'radial-gradient(ellipse at 50% 50%, color-mix(in srgb, var(--status-error) 30%, transparent), transparent 70%)'
                        : 'radial-gradient(ellipse at 50% 50%, rgba(255,255,255,0.18), transparent 70%)',
                      filter: 'blur(8px)',
                    }}
                  />
                  <span
                    aria-hidden
                    className="absolute inset-0 rounded-2xl bg-white/[0.08] border border-white/[0.10]"
                  />
                  <AlertTriangle
                    size={20}
                    className={destructive ? 'relative text-status-error' : 'relative text-white'}
                    strokeWidth={1.8}
                  />
                </div>

                <div className="flex-1 min-w-0">
                  <h2 className="text-[15px] font-display font-bold text-text-primary uppercase tracking-[0.08em] leading-tight">
                    {title}
                  </h2>
                  {subject && (
                    <div
                      className="mt-2 inline-flex items-center px-2.5 py-1 rounded-md
                                 text-[11px] font-bold uppercase tracking-[0.14em]
                                 bg-white/[0.06] border border-white/[0.10]
                                 text-text-primary"
                    >
                      {subject}
                    </div>
                  )}
                </div>

                <button
                  type="button"
                  onClick={onCancel}
                  aria-label={cancelLabel}
                  className="shrink-0 w-8 h-8 -mt-1 -mr-1 rounded-lg flex items-center justify-center
                             text-text-muted hover:text-text-primary hover:bg-white/[0.08]
                             transition-colors"
                  style={{ outline: 'none' }}
                >
                  <X size={14} />
                </button>
              </div>

              {hasImage && (
                <div className="relative rounded-2xl overflow-hidden border border-white/[0.10]
                                bg-black/40 aspect-[16/7]">
                  {imageUrl && !imgErr ? (
                    <img
                      src={imageUrl}
                      alt=""
                      onError={() => setImgErr(true)}
                      className={'absolute inset-0 w-full h-full ' + (imageContain ? 'object-contain' : 'object-cover')}
                    />
                  ) : (
                    <div className="absolute inset-0 flex items-center justify-center text-text-muted">
                      <ImageOff size={26} strokeWidth={1.6} />
                    </div>
                  )}
                  <span aria-hidden className="absolute inset-0 ring-1 ring-inset ring-white/10 rounded-2xl pointer-events-none" />
                  <span aria-hidden className="absolute inset-x-0 bottom-0 h-14 bg-gradient-to-t from-black/70 to-transparent pointer-events-none" />
                </div>
              )}

              <p className="text-[13.5px] text-text-secondary leading-relaxed whitespace-pre-line pl-1">
                {body}
              </p>

              {details && (
                <div className="rounded-2xl border border-white/[0.09] bg-white/[0.03] overflow-hidden">
                  {details}
                </div>
              )}

              {}
              <div
                className="h-px bg-gradient-to-r from-transparent via-white/12 to-transparent mx-1"
                aria-hidden="true"
              />

              <div className="flex items-center justify-end gap-2">
                <button
                  type="button"
                  onClick={onCancel}
                  className="px-4 h-10 rounded-xl text-[13px] font-semibold
                             text-text-secondary border border-white/[0.06] bg-white/[0.04]
                             hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                             transition-colors"
                  style={{ outline: 'none' }}
                >
                  {cancelLabel}
                </button>
                <motion.button
                  type="button"
                  onClick={onConfirm}
                  autoFocus
                  whileHover={{ y: -1 }}
                  whileTap={{ scale: 0.97 }}
                  transition={{ duration: 0.15, ease: EASE_DEPTH }}
                  className={
                    'group relative inline-flex items-center gap-2.5 h-10 rounded-2xl ' +
                    (hideConfirmArrow ? 'px-5 ' : 'pl-5 pr-3 ') +
                    'text-[13px] font-bold uppercase tracking-[0.08em] ' +
                    'transition-colors duration-200 ' +
                    (destructive
                      ? 'bg-status-error text-white border border-status-error ' +
                        'shadow-[0_10px_28px_-10px_color-mix(in_srgb,var(--status-error)_55%,transparent)]'
                      : 'bg-white text-black border border-white ' +
                        'shadow-[0_10px_28px_-10px_rgba(255,255,255,0.55),inset_0_1px_0_rgba(255,255,255,0.85)]')
                  }
                  style={{ outline: 'none' }}
                >
                  <span>{confirmLabel}</span>
                  {!hideConfirmArrow && (
                    <span
                      className={
                        'inline-flex items-center justify-center w-7 h-7 rounded-xl ' +
                        'transition-transform duration-200 ease-depth ' +
                        'group-hover:translate-x-0.5 ' +
                        (destructive
                          ? 'bg-white/20 text-white'
                          : 'bg-black text-white')
                      }
                    >
                      <ArrowRight size={13} strokeWidth={2.5} />
                    </span>
                  )}
                </motion.button>
              </div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
