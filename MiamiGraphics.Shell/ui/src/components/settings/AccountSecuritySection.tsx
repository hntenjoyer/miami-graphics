import { useState, useEffect, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { ShieldCheck, Eye, EyeOff, Check, X, AlertCircle, KeyRound, Mail, Loader2, type LucideIcon } from 'lucide-react';
import { useSessionStore } from '@/store/sessionStore';
import { bridge } from '@/bridge';
import { SettingsSection } from './SettingsSection';
import { CodeInput, EASE_DEPTH, GlassPanel } from '@/design';

export function AccountSecuritySection() {
  const { t } = useTranslation();
  const auth = useSessionStore(s => s.auth);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

  const [openModal, setOpenModal] = useState<'none' | 'password' | 'email'>('none');
  const close = () => setOpenModal('none');

  if (!userId) return null;

  return (
    <>
      <SettingsSection
        icon={ShieldCheck}
        title={t('settings.security.title')}
        description={t('settings.security.description')}
      >
        <SecurityRow
          icon={KeyRound}
          label={t('settings.security.passwordRowLabel')}
          description={t('settings.security.passwordRowDescription')}
          actionLabel={t('settings.security.passwordRowAction')}
          onOpen={() => setOpenModal('password')}
        />
        <SecurityRow
          icon={Mail}
          label={t('settings.security.emailRowLabel')}
          description={t('settings.security.emailRowDescription')}
          actionLabel={t('settings.security.emailRowAction')}
          onOpen={() => setOpenModal('email')}
        />
      </SettingsSection>

      <SecurityModal
        open={openModal === 'password'}
        icon={KeyRound}
        title={t('settings.security.passwordRowAction')}
        description={t('settings.security.passwordRowDescription')}
        onClose={close}
      >
        <PasswordChangeForm userId={userId} onDone={close} />
      </SecurityModal>

      <SecurityModal
        open={openModal === 'email'}
        icon={Mail}
        title={t('settings.security.emailRowAction')}
        description={t('settings.security.emailRowDescription')}
        onClose={close}
      >
        <EmailChangeForm userId={userId} onDone={close} />
      </SecurityModal>
    </>
  );
}

interface SecurityRowProps {
  icon:        LucideIcon;
  label:       string;
  description: string;
  actionLabel: string;
  onOpen:      () => void;
}

function SecurityRow({ icon: Icon, label, description, actionLabel, onOpen }: SecurityRowProps) {
  return (
    <div className="py-3 flex items-center justify-between gap-6">
      <div className="min-w-0">
        <div className="text-sm font-medium text-text-primary">{label}</div>
        <div className="text-xs text-text-muted mt-0.5">{description}</div>
      </div>
      <button
        type="button"
        onClick={onOpen}
        style={{ outline: 'none' }}
        className="shrink-0 inline-flex items-center justify-center gap-2 h-11 px-5 rounded-xl
                   bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                   hover:bg-bg-elevated/75 hover:border-white/[0.18]
                   transition-colors text-sm font-bold uppercase tracking-wider"
      >
        <Icon size={16} />
        <span>{actionLabel}</span>
      </button>
    </div>
  );
}

interface SecurityModalProps {
  open:        boolean;
  icon:        LucideIcon;
  title:       string;
  description: string;
  onClose:     () => void;
  children:    ReactNode;
}

function SecurityModal({ open, icon: Icon, title, description, onClose, children }: SecurityModalProps) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[100] flex items-center justify-center p-6"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.24, ease: EASE_DEPTH }}
          onClick={onClose}
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
            style={{ filter: 'drop-shadow(0 22px 50px rgba(255,255,255,0.10)) drop-shadow(0 6px 18px rgba(0,0,0,0.55))' }}
          >
            <GlassPanel
              depth="z3" tint="ultra" rounded="3xl" highlight edge
              className="relative overflow-hidden p-7 flex flex-col gap-5 border border-white/[0.08]"
            >
              <span aria-hidden className="absolute inset-0 pointer-events-none bg-bg-elevated/55" />
              <span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
              />
              <span
                aria-hidden
                className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
                style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
              />

              <div className="relative flex items-center gap-4">
                <div className="relative shrink-0 w-12 h-12 flex items-center justify-center">
                  <span
                    aria-hidden
                    className="absolute -inset-2 rounded-3xl"
                    style={{ background: 'radial-gradient(ellipse at 50% 50%, rgba(255,255,255,0.18), transparent 70%)', filter: 'blur(8px)' }}
                  />
                  <span aria-hidden className="absolute inset-0 rounded-2xl bg-white/[0.08] border border-white/[0.10]" />
                  <Icon size={20} className="relative text-white" strokeWidth={1.8} />
                </div>
                <div className="flex-1 min-w-0">
                  <h2 className="text-[15px] font-display font-bold text-text-primary uppercase tracking-[0.08em] leading-tight">
                    {title}
                  </h2>
                  <p className="text-xs text-text-muted mt-1 leading-snug">{description}</p>
                </div>
                <button
                  type="button"
                  onClick={onClose}
                  aria-label={title}
                  className="shrink-0 w-8 h-8 -mt-1 -mr-1 rounded-lg flex items-center justify-center
                             text-text-muted hover:text-text-primary hover:bg-white/[0.08] transition-colors"
                  style={{ outline: 'none' }}
                >
                  <X size={14} />
                </button>
              </div>

              <div
                className="relative h-px bg-gradient-to-r from-transparent via-white/12 to-transparent mx-1"
                aria-hidden="true"
              />

              <div className="relative">{children}</div>
            </GlassPanel>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

const stepTransition = {
  initial:    { opacity: 0, y: 8 },
  animate:    { opacity: 1, y: 0 },
  exit:       { opacity: 0, y: -4 },
  transition: { duration: 0.32, ease: EASE_DEPTH },
} as const;

const fieldVariants = {
  hidden:  { opacity: 0, y: 6 },
  visible: (i: number) => ({
    opacity: 1, y: 0,
    transition: { delay: 0.05 + i * 0.05, duration: 0.32, ease: EASE_DEPTH },
  }),
};

function StaggerField({ index, children }: { index: number; children: ReactNode }) {
  return (
    <motion.div custom={index} variants={fieldVariants} initial="hidden" animate="visible">
      {children}
    </motion.div>
  );
}

function SubmitButton({ busy, disabled, icon: Icon, children }: {
  busy?: boolean; disabled?: boolean; icon?: LucideIcon; children: ReactNode;
}) {
  const off = disabled || busy;
  return (
    <motion.button
      type="submit"
      disabled={off}
      whileTap={off ? undefined : { scale: 0.98 }}
      transition={{ duration: 0.15, ease: EASE_DEPTH }}
      style={{ outline: 'none' }}
      className="w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                 bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                 hover:bg-bg-elevated/75 hover:border-white/[0.18]
                 transition-colors text-sm font-bold uppercase tracking-wider
                 disabled:opacity-40 disabled:pointer-events-none"
    >
      {busy ? <Loader2 size={16} className="animate-spin" /> : (Icon ? <Icon size={16} /> : null)}
      <span>{children}</span>
    </motion.button>
  );
}

function PasswordChangeForm({ userId, onDone }: { userId: string; onDone: () => void }) {
  const { t } = useTranslation();
  const [step, setStep] = useState<'request' | 'confirm' | 'done'>('request');

  const [oldPw, setOldPw]       = useState('');
  const [newPw, setNewPw]       = useState('');
  const [repeatPw, setRepeatPw] = useState('');
  const [showOld, setShowOld]   = useState(false);
  const [showNew, setShowNew]   = useState(false);
  const [code, setCode]         = useState('');
  const [busy, setBusy]         = useState(false);
  const [error, setError]       = useState<string | null>(null);

  const tooShort = newPw.length > 0 && newPw.length < 8;
  const mismatch = repeatPw.length > 0 && repeatPw !== newPw;
  const canRequest =
    oldPw.length > 0 &&
    newPw.length >= 8 &&
    newPw === repeatPw &&
    !busy;

  const onRequest = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canRequest) return;
    setError(null);
    setBusy(true);
    try {
      await bridge.changePasswordRequest(userId, oldPw, newPw);
      try {
        await bridge.changePasswordConfirm(userId, '000000');
        setStep('done');
        setTimeout(() => onDone(), 1300);
      } catch {
        setStep('confirm');
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : t('settings.security.passwordRowAction'));
    } finally {
      setBusy(false);
    }
  };

  const onConfirm = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy || code.length !== 6) return;
    setError(null);
    setBusy(true);
    try {
      await bridge.changePasswordConfirm(userId, code);
      setStep('done');

      setTimeout(() => onDone(), 1300);
    } catch (e) {
      setError(e instanceof Error ? e.message : t('settings.security.passwordRowAction'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AnimatePresence mode="popLayout" initial={false}>

      {step === 'request' && (
        <motion.form
          key="pw-request"
          onSubmit={onRequest}
          className="flex flex-col gap-4"
          {...stepTransition}
        >
          <div className="flex flex-col gap-3 max-w-md">
            <StaggerField index={0}>
              <FieldShell
                label={t('settings.security.currentPassword')}
                type={showOld ? 'text' : 'password'}
                value={oldPw}
                onChange={setOldPw}
                autoComplete="current-password"
                rightSlot={<EyeToggle on={showOld} onClick={() => setShowOld(s => !s)} />}
              />
            </StaggerField>
            <StaggerField index={1}>
              <FieldShell
                label={t('settings.security.newPassword')}
                type={showNew ? 'text' : 'password'}
                value={newPw}
                onChange={setNewPw}
                autoComplete="new-password"
                rightSlot={<EyeToggle on={showNew} onClick={() => setShowNew(s => !s)} />}
              />
            </StaggerField>
            {tooShort && <FieldHint>{t('settings.security.errorPasswordTooShort')}</FieldHint>}
            <StaggerField index={2}>
              <FieldShell
                label={t('settings.security.newPasswordRepeat')}
                type={showNew ? 'text' : 'password'}
                value={repeatPw}
                onChange={setRepeatPw}
                autoComplete="new-password"
              />
            </StaggerField>
            {mismatch && <FieldHint>{t('settings.security.errorPasswordsDontMatch')}</FieldHint>}

            {error && <ErrorBanner>{error}</ErrorBanner>}
          </div>

          <SubmitButton busy={busy} disabled={!canRequest} icon={Check}>
            {t('settings.security.passwordRowSendCode')}
          </SubmitButton>
        </motion.form>
      )}

      {step === 'confirm' && (
        <motion.form
          key="pw-confirm"
          onSubmit={onConfirm}
          className="flex flex-col gap-4"
          {...stepTransition}
        >
          <div className="flex flex-col gap-3 max-w-md">
            <StaggerField index={0}>
              <p className="text-xs text-text-muted leading-snug">
                {t('settings.security.passwordCodeHint')}
              </p>
            </StaggerField>
            <StaggerField index={1}>
              {}
              <div className="w-full max-w-[22rem]">
                <CodeInput value={code} onChange={setCode} autoFocus />
              </div>
            </StaggerField>

            {error && <ErrorBanner>{error}</ErrorBanner>}
          </div>

          {}
          <div className="flex flex-col gap-2">
            <SubmitButton busy={busy} disabled={code.length !== 6 || busy} icon={Check}>
              {t('settings.security.passwordRowSubmit')}
            </SubmitButton>
            <button
              type="button"
              onClick={() => { setStep('request'); setCode(''); setError(null); }}
              className="self-center text-xs text-text-muted hover:text-accent transition-colors"
            >
              {t('settings.security.passwordRowResend')}
            </button>
          </div>
        </motion.form>
      )}

      {step === 'done' && (
        <motion.div key="pw-done" {...stepTransition}>
          <SuccessBanner text={t('settings.security.passwordChanged')} />
        </motion.div>
      )}

    </AnimatePresence>
  );
}

function EmailChangeForm({ userId, onDone }: { userId: string; onDone: () => void }) {
  const { t } = useTranslation();
  const [step, setStep] = useState<'request' | 'confirm' | 'done'>('request');

  const [currentPw, setCurrentPw] = useState('');
  const [newEmail, setNewEmail]   = useState('');
  const [showPw, setShowPw]       = useState(false);
  const [code, setCode]           = useState('');
  const [busy, setBusy]           = useState(false);
  const [error, setError]         = useState<string | null>(null);
  const setProfile                = useSessionStore.setState;

  const onRequest = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    if (!newEmail.trim()) {
      setError(t('settings.security.errorEmailRequired'));
      return;
    }
    setError(null);
    setBusy(true);
    try {
      await bridge.changeEmailRequest(userId, currentPw, newEmail.trim());
      try {
        const updated = await bridge.changeEmailConfirm(userId, '000000');
        setProfile(prev => ({ ...prev, profile: updated }));
        setStep('done');
        setTimeout(() => onDone(), 1300);
      } catch {
        setStep('confirm');
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : t('settings.security.emailRowAction'));
    } finally {
      setBusy(false);
    }
  };

  const onConfirm = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy || code.length !== 6) return;
    setError(null);
    setBusy(true);
    try {
      const updated = await bridge.changeEmailConfirm(userId, code);
      setProfile(prev => ({ ...prev, profile: updated }));
      setStep('done');

      setTimeout(() => onDone(), 1300);
    } catch (e) {
      setError(e instanceof Error ? e.message : t('settings.security.emailRowAction'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AnimatePresence mode="popLayout" initial={false}>

      {step === 'request' && (
        <motion.form
          key="email-request"
          onSubmit={onRequest}
          className="flex flex-col gap-4"
          {...stepTransition}
        >
          <div className="flex flex-col gap-3 max-w-md">
            <StaggerField index={0}>
              <FieldShell
                label={t('settings.security.currentPassword')}
                type={showPw ? 'text' : 'password'}
                value={currentPw}
                onChange={setCurrentPw}
                autoComplete="current-password"
                rightSlot={<EyeToggle on={showPw} onClick={() => setShowPw(s => !s)} />}
              />
            </StaggerField>
            <StaggerField index={1}>
              <FieldShell
                label={t('settings.security.newEmail')}
                type="email"
                value={newEmail}
                onChange={setNewEmail}
                autoComplete="email"
              />
            </StaggerField>

            {error && <ErrorBanner>{error}</ErrorBanner>}
          </div>

          <SubmitButton busy={busy} disabled={!currentPw || !newEmail.trim() || busy} icon={Check}>
            {t('settings.security.emailRowSendCode')}
          </SubmitButton>
        </motion.form>
      )}

      {step === 'confirm' && (
        <motion.form
          key="email-confirm"
          onSubmit={onConfirm}
          className="flex flex-col gap-4"
          {...stepTransition}
        >
          <div className="flex flex-col gap-3 max-w-md">
            <StaggerField index={0}>
              <p className="text-xs text-text-muted leading-snug">
                {t('settings.security.emailCodeHint', { email: newEmail.trim() })}
              </p>
            </StaggerField>
            <StaggerField index={1}>
              {}
              <div className="w-full max-w-[22rem]">
                <CodeInput value={code} onChange={setCode} autoFocus />
              </div>
            </StaggerField>

            {error && <ErrorBanner>{error}</ErrorBanner>}
          </div>

          <div className="flex flex-col gap-2">
            <SubmitButton busy={busy} disabled={code.length !== 6 || busy} icon={Check}>
              {t('settings.security.emailRowConfirm')}
            </SubmitButton>
            <button
              type="button"
              onClick={() => { setStep('request'); setCode(''); setError(null); }}
              className="self-center text-xs text-text-muted hover:text-accent transition-colors"
            >
              {t('settings.security.emailRowResend')}
            </button>
          </div>
        </motion.form>
      )}

      {step === 'done' && (
        <motion.div key="email-done" {...stepTransition}>
          <SuccessBanner text={t('settings.security.emailChanged')} />
        </motion.div>
      )}

    </AnimatePresence>
  );
}

interface FieldShellProps {
  label: string;
  type: string;
  value: string;
  onChange: (v: string) => void;
  autoComplete?: string;
  rightSlot?: ReactNode;
}

function FieldShell({ label, type, value, onChange, autoComplete, rightSlot }: FieldShellProps) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
      <div className="relative">
        <input
          type={type}
          value={value}
          onChange={e => onChange(e.target.value)}
          autoComplete={autoComplete}
          className="w-full px-4 py-3 pr-11
                     bg-glass-strong backdrop-blur-glass
                     border border-glass-border rounded-2xl
                     text-text-primary placeholder:text-text-muted
                     outline-none transition-all duration-200 ease-depth
                     focus:border-accent focus:shadow-[0_0_0_4px_var(--accent-soft)]"
        />
        {rightSlot && (
          <div className="absolute right-3 top-1/2 -translate-y-1/2">{rightSlot}</div>
        )}
      </div>
    </label>
  );
}

function EyeToggle({ on, onClick }: { on: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      tabIndex={-1}
      className="p-1.5 rounded-md text-text-muted hover:text-text-primary transition-colors"
      aria-label={on ? 'Hide' : 'Show'}
    >
      {on ? <EyeOff size={16} /> : <Eye size={16} />}
    </button>
  );
}

function FieldHint({ children }: { children: ReactNode }) {
  return (
    <p className="text-xs text-status-error pl-1 -mt-1">{children}</p>
  );
}

function ErrorBanner({ children }: { children: ReactNode }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: -4 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.22, ease: EASE_DEPTH }}
      className="flex items-start gap-2 px-3.5 py-2.5 rounded-xl
                 bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)]
                 border border-[color-mix(in_srgb,var(--status-error)_30%,transparent)]
                 text-sm text-text-primary"
    >
      <AlertCircle size={16} className="mt-0.5 shrink-0" style={{ color: 'var(--status-error)' }} />
      <span>{children}</span>
    </motion.div>
  );
}

function SuccessBanner({ text }: { text: string }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 6, scale: 0.98 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ duration: 0.32, ease: EASE_DEPTH }}
      className="flex items-center gap-2 px-3.5 py-2.5 rounded-xl
                 bg-[color-mix(in_srgb,var(--status-success)_10%,transparent)]
                 border border-[color-mix(in_srgb,var(--status-success)_30%,transparent)]
                 text-sm text-text-primary"
    >
      <Check size={16} className="shrink-0" style={{ color: 'var(--status-success)' }} />
      {text}
    </motion.div>
  );
}
