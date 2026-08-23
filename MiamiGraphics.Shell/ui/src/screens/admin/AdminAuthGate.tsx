import { useState, type ReactNode } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { ShieldCheck, AlertTriangle, Globe, QrCode, Unlock } from 'lucide-react';
import { bridge } from '@/bridge';
import { GlassPanel, ArrowButton, EASE_DEPTH } from '@/design';
import { useAdminGate } from '@/store/adminGate';

const STEPS: { icon: typeof Globe; text: string }[] = [
  { icon: Globe,  text: 'Откроется страница входа в браузере' },
  { icon: QrCode, text: 'Подтвердите личность через Telegram (отсканируйте QR-код)' },
  { icon: Unlock, text: 'Панель разблокируется здесь автоматически' },
];

export function AdminAuthGate({ children }: { children: ReactNode }) {
  const unlocked = useAdminGate((s) => s.unlocked);
  const setUnlocked = useAdminGate((s) => s.setUnlocked);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (unlocked) return <>{children}</>;

  const authenticate = async () => {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      const r = await bridge.adminWebAuthenticate();
      if (r.success) setUnlocked(true);
      else setError(r.error ?? 'Не удалось пройти аутентификацию.');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка аутентификации.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="h-full overflow-y-auto">
      <div className="min-h-full flex items-center justify-center p-8">
        <motion.div
          initial={{ opacity: 0, scale: 0.95, y: 16, filter: 'blur(8px)' }}
          animate={{ opacity: 1, scale: 1, y: 0, filter: 'blur(0px)' }}
          transition={{ duration: 0.4, ease: EASE_DEPTH }}
          className="w-full max-w-[440px]"
        >
          <GlassPanel
            depth="z3" tint="ultra" rounded="3xl" highlight edge
            className="relative overflow-hidden border border-white/[0.08]"
          >
            <span
              aria-hidden
              className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                         bg-gradient-to-r from-transparent via-white/45 to-transparent"
            />
            <span
              aria-hidden
              className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl"
              style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
            />

            <div className="relative p-8 flex flex-col items-center text-center">
              <div className="relative w-16 h-16 mb-5">
                <motion.span
                  aria-hidden
                  className="absolute inset-0 rounded-2xl blur-md"
                  style={{ background: 'radial-gradient(circle, var(--accent) 0%, transparent 70%)' }}
                  initial={{ opacity: 0.3, scale: 0.85 }}
                  animate={{ opacity: [0.3, 0.55, 0.3], scale: [0.85, 1.12, 0.85] }}
                  transition={{ duration: 2.8, ease: 'easeInOut', repeat: Infinity }}
                />
                <div className="relative w-16 h-16 rounded-2xl bg-accent-soft border border-white/[0.08]
                                flex items-center justify-center">
                  <ShieldCheck size={30} className="text-accent" strokeWidth={2} />
                </div>
              </div>

              <div className="text-[10px] font-mono font-bold uppercase tracking-[0.2em] text-text-muted mb-1.5">
                Безопасный вход
              </div>
              <h1 className="font-display text-[22px] font-bold text-text-primary tracking-tight">
                Доступ к админ-панели
              </h1>
              <p className="mt-2 text-[13px] leading-relaxed text-text-secondary px-2">
                Пройдите вход в браузере через Telegram - после подтверждения
                панель откроется здесь.
              </p>

              <div className="w-full mt-6 flex flex-col gap-3 text-left">
                {STEPS.map((s, i) => (
                  <div key={i} className="flex items-center gap-3">
                    <span className="shrink-0 w-7 h-7 rounded-full bg-accent-soft
                                     border border-white/[0.08] text-accent text-[12px] font-bold
                                     flex items-center justify-center tabular-nums">
                      {i + 1}
                    </span>
                    <s.icon size={15} strokeWidth={2} className="shrink-0 text-text-muted" />
                    <span className="text-[13px] text-text-secondary leading-snug">{s.text}</span>
                  </div>
                ))}
              </div>

              <div className="w-full mt-7">
                <ArrowButton
                  type="button"
                  fullWidth
                  busy={busy}
                  onClick={authenticate}
                  label={busy ? 'Ожидание входа…' : 'Открыть браузер для входа'}
                />
              </div>

              <AnimatePresence initial={false}>
                {busy && (
                  <motion.div
                    key="waiting-hint"
                    initial={{ opacity: 0, height: 0, marginTop: 0 }}
                    animate={{ opacity: 1, height: 'auto', marginTop: 12 }}
                    exit={{ opacity: 0, height: 0, marginTop: 0 }}
                    transition={{ duration: 0.32, ease: EASE_DEPTH }}
                    className="w-full overflow-hidden"
                  >
                    <div className="flex items-center justify-center gap-2.5 px-3.5 py-2.5 rounded-xl
                                    bg-[color-mix(in_srgb,var(--accent)_8%,transparent)]
                                    border border-[color-mix(in_srgb,var(--accent)_22%,transparent)]">
                      <span className="relative flex w-2 h-2 shrink-0">
                        <span className="absolute inline-flex w-full h-full rounded-full bg-accent opacity-60 animate-ping" />
                        <span className="relative inline-flex w-2 h-2 rounded-full bg-accent" />
                      </span>
                      <span className="text-[12px] text-text-secondary leading-snug">
                        Не закрывайте лаунчер - панель откроется автоматически после входа.
                      </span>
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>

              {error && (
                <div className="w-full mt-4 flex items-start gap-2 text-left rounded-xl
                                px-3.5 py-2.5 text-[13px]
                                bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)]
                                border border-[color-mix(in_srgb,var(--status-error)_30%,transparent)]"
                     style={{ color: 'var(--status-error)' }}>
                  <AlertTriangle size={16} className="shrink-0 mt-0.5" />
                  <span>{error}</span>
                </div>
              )}
            </div>
          </GlassPanel>
        </motion.div>
      </div>
    </div>
  );
}
