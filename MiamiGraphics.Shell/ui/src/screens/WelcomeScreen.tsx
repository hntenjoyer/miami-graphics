import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation, Trans } from 'react-i18next';
import { useState, useEffect } from 'react';
import type { ChangeEvent, ReactNode } from 'react';
import { User, Lock, Mail, Eye, EyeOff, LogIn, UserPlus, ShieldCheck, AlertCircle, CheckCircle2, Check, Ticket, type LucideIcon } from 'lucide-react';
import { useSessionStore } from '@/store/sessionStore';
import { bridge } from '@/bridge';
import { GlassPanel, Glow3DButton, ArrowButton, DepthCheckbox, CodeInput, EASE_DEPTH } from '@/design';
import { TermsModal } from '@/components/TermsModal';
import { PrivacyModal } from '@/components/PrivacyModal';

type WelcomeMode = 'login' | 'register' | 'reset';

const flipVariants = {
  initial: { opacity: 0, rotateY: -14, scale: 0.97, y:  6, transformPerspective: 1200 },
  animate: { opacity: 1, rotateY:   0, scale: 1,    y:  0, transformPerspective: 1200 },
  exit:    { opacity: 0, rotateY:  14, scale: 0.97, y: -4, transformPerspective: 1200 },
  transition: { duration: 0.55, ease: EASE_DEPTH },
};

export function WelcomeScreen() {
  const [mode, setMode] = useState<WelcomeMode>('login');

  return (
    <div className="relative w-full h-full overflow-hidden">
      <div
        className="absolute inset-0 flex items-center justify-center px-6"
        style={{ perspective: 1400 }}
      >
        <AnimatePresence mode="popLayout" initial={false}>
          {mode === 'login' && (
            <motion.div key="login" {...flipVariants} style={{ transformStyle: 'preserve-3d' }}>
              <LoginForm
                goRegister={() => setMode('register')}
                goReset={() => setMode('reset')}
              />
            </motion.div>
          )}
          {mode === 'register' && (
            <motion.div key="register" {...flipVariants} style={{ transformStyle: 'preserve-3d' }}>
              <AuthCard>
                <RegisterForm goLogin={() => setMode('login')} />
              </AuthCard>
            </motion.div>
          )}
          {mode === 'reset' && (
            <motion.div key="reset" {...flipVariants} style={{ transformStyle: 'preserve-3d' }}>
              <AuthCard>
                <ResetForm goLogin={() => setMode('login')} />
              </AuthCard>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}

function AuthCard({ children }: { children: ReactNode }) {
  return (
    <GlassPanel
      depth="z3" tint="ultra" rounded="3xl" highlight edge
      className="relative w-[480px] max-w-[92vw] overflow-hidden border border-white/[0.08] p-9"
    >
      <span
        aria-hidden="true"
        className="absolute top-0 inset-x-0 h-px pointer-events-none
                   bg-gradient-to-r from-transparent via-white/45 to-transparent"
      />
      <span
        aria-hidden="true"
        className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl"
        style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
      />
      <div className="relative">{children}</div>
    </GlassPanel>
  );
}

function AuthHeader({ icon: Icon, title, subtitle }: { icon: LucideIcon; title: string; subtitle?: string }) {
  return (
    <div className="flex flex-col items-center text-center mb-5">
      <div className="relative mb-5 w-14 h-14">
        <motion.span
          aria-hidden="true"
          className="absolute inset-0 rounded-2xl blur-md"
          style={{ background: 'radial-gradient(circle, var(--accent) 0%, transparent 70%)' }}
          initial={{ opacity: 0.3, scale: 0.85 }}
          animate={{ opacity: [0.3, 0.55, 0.3], scale: [0.85, 1.1, 0.85] }}
          transition={{ duration: 2.8, ease: 'easeInOut', repeat: Infinity }}
        />
        <div className="relative w-14 h-14 rounded-2xl bg-accent-soft border border-white/[0.08]
                        flex items-center justify-center">
          <Icon size={26} className="text-accent" strokeWidth={2} />
        </div>
      </div>
      <h1 className="text-[23px] font-bold text-text-primary tracking-tight">{title}</h1>
      {subtitle && (
        <p className="mt-2 text-[13.5px] leading-relaxed text-text-secondary px-2">{subtitle}</p>
      )}
    </div>
  );
}

function LoginForm({ goRegister, goReset }: { goRegister: () => void; goReset: () => void }) {
  const { t } = useTranslation();
  const loginAsGuest = useSessionStore(s => s.loginAsGuest);
  const loginWithCredentials = useSessionStore(s => s.loginWithCredentials);
  const zbtEnabled = useSessionStore(s => s.zbtEnabled);

  const [login, setLogin] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [remember, setRemember] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setError(null);
    setBusy(true);
    try {
      await loginWithCredentials(login, password, remember);
    } catch (e) {
      const msg = e instanceof Error ? e.message : '';
      setError(msg && !/invalid login or password/i.test(msg) ? msg : t('welcome.invalidCredentials'));
    } finally {
      setBusy(false);
    }
  };

  const onGuest = async () => {
    if (busy) return;
    setBusy(true);
    try { await loginAsGuest(); }
    finally { setBusy(false); }
  };

  const inputCls =
    'w-full h-[52px] pr-3 rounded-xl bg-glass-strong backdrop-blur-glass ' +
    'border border-glass-border text-[15px] text-text-primary placeholder:text-text-muted ' +
    'outline-none transition-all duration-200 focus:border-accent ' +
    'focus:shadow-[0_0_0_3px_var(--accent-soft)]';

  return (
    <div className="flex flex-col items-center gap-4">
      <GlassPanel
        depth="z3" tint="ultra" rounded="3xl" highlight edge
        className="relative w-[460px] max-w-[92vw] overflow-hidden border border-white/[0.08] p-9"
      >
        <span
          aria-hidden="true"
          className="absolute top-0 inset-x-0 h-px pointer-events-none
                     bg-gradient-to-r from-transparent via-white/45 to-transparent"
        />
        <span
          aria-hidden="true"
          className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl"
          style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
        />

        <form onSubmit={onSubmit} className="relative">
          <div className="relative mx-auto mb-5 w-14 h-14">
            <motion.span
              aria-hidden="true"
              className="absolute inset-0 rounded-2xl blur-md"
              style={{ background: 'radial-gradient(circle, var(--accent) 0%, transparent 70%)' }}
              initial={{ opacity: 0.3, scale: 0.85 }}
              animate={{ opacity: [0.3, 0.55, 0.3], scale: [0.85, 1.1, 0.85] }}
              transition={{ duration: 2.8, ease: 'easeInOut', repeat: Infinity }}
            />
            <div className="relative w-14 h-14 rounded-2xl bg-accent-soft border border-white/[0.08]
                            flex items-center justify-center">
              <LogIn size={26} className="text-accent" strokeWidth={2} />
            </div>
          </div>

          <h1 className="text-center text-[23px] font-bold text-text-primary tracking-tight">
            {t('welcome.cardTitle')}
          </h1>
          <p className="mt-2 text-center text-[13.5px] leading-relaxed text-text-secondary px-2">
            {t('welcome.cardSubtitle')}
          </p>

          <div className="mt-6 flex flex-col gap-3">
            <div className="relative group">
              <Mail size={17} strokeWidth={2.25} className="absolute z-10 left-3.5 top-1/2 -translate-y-1/2 text-white
                                         group-focus-within:text-accent transition-colors pointer-events-none" />
              <input
                type="text"
                autoComplete="username"
                placeholder={t('welcome.loginPlaceholder')}
                value={login}
                onChange={e => setLogin(e.target.value)}
                className={inputCls + ' pl-11'}
              />
            </div>
            <div className="relative group">
              <Lock size={17} strokeWidth={2.25} className="absolute z-10 left-3.5 top-1/2 -translate-y-1/2 text-white
                                         group-focus-within:text-accent transition-colors pointer-events-none" />
              <input
                type={showPassword ? 'text' : 'password'}
                autoComplete="current-password"
                placeholder={t('welcome.passwordPlaceholder')}
                value={password}
                onChange={e => setPassword(e.target.value)}
                className={inputCls + ' pl-11 pr-10'}
              />
              <button
                type="button"
                onMouseDown={e => e.preventDefault()}
                onClick={() => setShowPassword(s => !s)}
                tabIndex={-1}
                aria-label={showPassword ? t('welcome.hidePassword', 'Скрыть пароль') : t('welcome.showPassword', 'Показать пароль')}
                className="btn-no-press absolute right-2.5 top-1/2 -translate-y-1/2 w-7 h-7
                           flex items-center justify-center shrink-0
                           text-text-secondary hover:text-text-primary transition-colors"
                style={{ outline: 'none', lineHeight: 0 }}
              >
                {showPassword ? <Eye size={16} className="block" /> : <EyeOff size={16} className="block" />}
              </button>
            </div>
          </div>

          <div className="mt-2.5 flex items-center justify-between">
            <button
              type="button"
              onClick={() => setRemember(r => !r)}
              className="group flex items-center gap-2 text-[12px] font-semibold
                         text-text-secondary hover:text-text-primary transition-colors"
              style={{ outline: 'none' }}
            >
              <span
                className={
                  'w-[18px] h-[18px] rounded-md border flex items-center justify-center shrink-0 transition-all ' +
                  (remember
                    ? 'bg-accent-soft border-[color-mix(in_srgb,var(--accent)_60%,transparent)]'
                    : 'bg-glass-strong border-glass-border group-hover:border-white/25')
                }
              >
                {remember && <Check size={13} className="text-accent" strokeWidth={3} />}
              </span>
              {t('welcome.rememberMe')}
            </button>
            <button
              type="button"
              onClick={goReset}
              className="text-[12px] font-semibold text-text-secondary hover:text-accent transition-colors"
              style={{ outline: 'none' }}
            >
              {t('welcome.forgotPassword')}
            </button>
          </div>

          {error && (
            <p className="mt-2.5 text-center text-[12px]" style={{ color: 'var(--status-error)' }}>{error}</p>
          )}

          <div className="mt-4">
            <ArrowButton
              type="submit"
              busy={busy}
              fullWidth
              label={t('welcome.signInButton')}
            />
          </div>
        </form>
      </GlassPanel>

      <div className="flex items-center gap-3 text-[12px]">
        <button
          type="button"
          onClick={goRegister}
          className="font-semibold text-text-secondary hover:text-text-primary transition-colors"
        >
          {t('welcome.signUpLink')}
        </button>
        {!zbtEnabled && (
          <>
            <span className="text-text-muted">·</span>
            <button
              type="button"
              onClick={onGuest}
              disabled={busy}
              className="font-semibold text-text-secondary hover:text-text-primary transition-colors disabled:opacity-50"
            >
              {t('welcome.guestButton')}
            </button>
          </>
        )}
      </div>
    </div>
  );
}

function RegisterForm({ goLogin }: { goLogin: () => void }) {
  const { t } = useTranslation();
  const registerRequest = useSessionStore(s => s.registerRequest);
  const registerConfirm = useSessionStore(s => s.registerConfirm);
  const redeemBeta = useSessionStore(s => s.redeemBeta);
  const zbtEnabled = useSessionStore(s => s.zbtEnabled);

  const [step, setStep] = useState<'precode' | 'form'>(zbtEnabled ? 'precode' : 'form');
  const [betaCode, setBetaCode] = useState('');
  const [betaBusy, setBetaBusy] = useState(false);
  const [betaErr, setBetaErr]   = useState<string | null>(null);
  const [email, setEmail] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [passwordRepeat, setPasswordRepeat] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [showRepeat, setShowRepeat] = useState(false);
  const [agreed, setAgreed] = useState(false);
  const [agreedPrivacy, setAgreedPrivacy] = useState(false);
  const [termsOpen, setTermsOpen] = useState(false);
  const [privacyOpen, setPrivacyOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [promo, setPromo] = useState('');
  const [promoOwner, setPromoOwner] = useState<string | null>(null);
  const [promoChecked, setPromoChecked] = useState(false);

  useEffect(() => {
    let alive = true;
    bridge.installerPromo()
      .then(c => { if (alive && c) setPromo(c); })
      .catch(() => {});
    return () => { alive = false; };
  }, []);

  useEffect(() => {
    const c = promo.trim();
    setPromoOwner(null);
    setPromoChecked(false);
    if (c.length < 2) return;
    const id = setTimeout(() => {
      bridge.checkPromo(c)
        .then(r => { setPromoOwner(r.ok ? (r.display || c) : null); setPromoChecked(true); })
        .catch(() => setPromoChecked(false));
    }, 350);
    return () => clearTimeout(id);
  }, [promo]);

  const attachPromo = async () => {
    try { await bridge.attachReferral(promo.trim()); } catch {  }
  };

  const usernameValid = /^[A-Za-z0-9_]{3,32}$/.test(username);
  const usernameInvalid = username.length > 0 && !usernameValid;
  const tooShort = password.length > 0 && password.length < 8;
  const mismatch = passwordRepeat.length > 0 && passwordRepeat !== password;
  const canSubmit =
    agreed &&
    agreedPrivacy &&
    email.length > 0 &&
    usernameValid &&
    password.length >= 8 &&
    password === passwordRepeat &&
    !busy;

  const onPrecode = async (e: React.FormEvent) => {
    e.preventDefault();
    const c = betaCode.trim();
    if (!c || betaBusy) return;
    setBetaErr(null); setBetaBusy(true);
    try {
      const r = await bridge.betaCodeCheck(c);
      if (r.ok) setStep('form');
      else setBetaErr(t('welcome.zbtCodeInvalid', 'Неверный или уже использованный код.'));
    } catch { setBetaErr(t('welcome.zbtNoConnection', 'Нет связи с сервером.')); }
    finally { setBetaBusy(false); }
  };

  const onRequest = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    setError(null);
    setBusy(true);
    try {
      const promoCode = promo.trim();
      if (promoCode) {
        const r = await bridge.checkPromo(promoCode).catch(() => ({ ok: false, display: null }));
        if (!r.ok) {
          setError(t('welcome.promoNotFound', { defaultValue: 'Промокода {{code}} не существует. Проверь код или очисти поле.', code: promoCode }));
          setPromoOwner(null); setPromoChecked(true);
          return;
        }
      }
      await registerRequest(email, username, password);
      await registerConfirm(email, '000000', true);
      await attachPromo();
      if (zbtEnabled) await redeemBeta(betaCode);
    } catch (e) {
      setError(e instanceof Error && e.message ? e.message : t('welcome.errorServerNotReady'));
    } finally {
      setBusy(false);
    }
  };

  return (

    <motion.div
      layout
      transition={{ duration: 0.5, ease: EASE_DEPTH }}
      style={{ willChange: 'height' }}
    >
      <TermsModal open={termsOpen} onClose={() => setTermsOpen(false)} />
      <PrivacyModal open={privacyOpen} onClose={() => setPrivacyOpen(false)} />
      <AnimatePresence mode="popLayout" initial={false}>

        {step === 'precode' && (
          <motion.form
            key="register-precode"
            onSubmit={onPrecode}
            className="flex flex-col gap-3"
            {...stepTransition}
          >
            <AuthHeader
              icon={ShieldCheck}
              title={t('welcome.zbtTitle', 'Доступ к ЗБТ')}
              subtitle={t('welcome.zbtSubtitle', 'Введи инвайт-код, чтобы создать аккаунт.')}
            />
            <TextField
              icon={ShieldCheck}
              type="text"
              placeholder="ZBT-XXXXXX"
              value={betaCode}
              onChange={e => { setBetaCode(e.target.value); setBetaErr(null); }}
              autoComplete="off"
            />
            {betaErr && <ErrorBanner>{betaErr}</ErrorBanner>}
            <ArrowButton
              type="submit"
              busy={betaBusy}
              disabled={betaCode.trim().length < 3}
              fullWidth
              label={t('welcome.zbtNext', 'Далее')}
            />
            <p className="text-center text-sm text-text-secondary mt-1">
              {t('welcome.haveAccount')}{' '}
              <button
                type="button"
                onClick={goLogin}
                className="font-semibold text-accent hover:text-accent-hover transition-colors"
              >
                {t('welcome.signInLink')}
              </button>
            </p>
          </motion.form>
        )}

        {}
        {step === 'form' && (
          <motion.form
            key="register-form"
            onSubmit={onRequest}
            className="flex flex-col gap-3"
            {...stepTransition}
          >
            <AuthHeader
              icon={UserPlus}
              title={t('welcome.registerTitle')}
              subtitle={t('welcome.registerSubtitle')}
            />

            <TextField
              icon={Mail}
              type="email"
              placeholder={t('welcome.emailPlaceholder')}
              value={email}
              onChange={e => setEmail(e.target.value)}
              autoComplete="email"
            />
            <FieldHint>{t('welcome.registerNoEmail')}</FieldHint>
            <TextField
              icon={User}
              type="text"
              placeholder={t('welcome.usernamePlaceholder')}
              value={username}
              onChange={e => setUsername(e.target.value)}
              autoComplete="username"
            />
            {usernameInvalid && <FieldHint kind="error">{t('welcome.errorUsernameInvalid')}</FieldHint>}
            <PasswordField
              placeholder={t('welcome.passwordPlaceholder')}
              value={password}
              onChange={e => setPassword(e.target.value)}
              show={showPassword}
              onToggle={() => setShowPassword(s => !s)}
              autoComplete="new-password"
            />
            {tooShort && <FieldHint kind="error">{t('welcome.errorPasswordTooShort')}</FieldHint>}

            <PasswordField
              placeholder={t('welcome.passwordRepeatPlaceholder')}
              value={passwordRepeat}
              onChange={e => setPasswordRepeat(e.target.value)}
              show={showRepeat}
              onToggle={() => setShowRepeat(s => !s)}
              autoComplete="new-password"
            />
            {mismatch && <FieldHint kind="error">{t('welcome.errorPasswordsDontMatch')}</FieldHint>}

            <TextField
              icon={Ticket}
              type="text"
              placeholder={t('welcome.promoPlaceholder', 'Промокод (если есть)')}
              value={promo}
              onChange={e => setPromo(e.target.value.toUpperCase().slice(0, 16))}
              autoComplete="off"
            />
            {promo.trim().length >= 2 && promoChecked && !promoOwner && (
              <FieldHint kind="error">{t('welcome.promoInvalidHint', 'Такого промокода нет')}</FieldHint>
            )}

            <DepthCheckbox
              checked={agreed}
              onChange={setAgreed}
              label={
                <Trans
                  i18nKey="welcome.agreeTermsRich"
                  components={{
                    a: (
                      <button
                        type="button"
                        onClick={e => { e.preventDefault(); e.stopPropagation(); setTermsOpen(true); }}
                        className="font-semibold text-accent hover:text-accent-hover
                                   underline-offset-2 hover:underline transition-colors"
                      />
                    ),
                  }}
                />
              }
              size="md"
              className="mt-1"
            />

            <DepthCheckbox
              checked={agreedPrivacy}
              onChange={setAgreedPrivacy}
              label={
                <Trans
                  i18nKey="welcome.agreePrivacyRich"
                  components={{
                    a: (
                      <button
                        type="button"
                        onClick={e => { e.preventDefault(); e.stopPropagation(); setPrivacyOpen(true); }}
                        className="font-semibold text-accent hover:text-accent-hover
                                   underline-offset-2 hover:underline transition-colors"
                      />
                    ),
                  }}
                />
              }
              size="md"
            />

            {error && <ErrorBanner>{error}</ErrorBanner>}

            <ArrowButton
              type="submit"
              busy={busy}
              disabled={!canSubmit}
              fullWidth
              label={t('welcome.signUpButton')}
            />

            <p className="text-center text-sm text-text-secondary mt-1">
              {t('welcome.haveAccount')}{' '}
              <button
                type="button"
                onClick={goLogin}
                className="font-semibold text-accent hover:text-accent-hover transition-colors"
              >
                {t('welcome.signInLink')}
              </button>
            </p>
          </motion.form>
        )}

      </AnimatePresence>
    </motion.div>
  );
}

const stepTransition = {
  initial: { opacity: 0, y: 8 },
  animate: { opacity: 1, y: 0 },
  exit:    { opacity: 0, y: -4 },
  transition: { duration: 0.38, ease: EASE_DEPTH },
} as const;

const fieldVariants = {
  hidden:  { opacity: 0, y: 10 },
  visible: (i: number) => ({
    opacity: 1, y: 0,
    transition: { delay: 0.08 + i * 0.07, duration: 0.45, ease: EASE_DEPTH },
  }),
};

function ResetForm({ goLogin }: { goLogin: () => void }) {
  const { t } = useTranslation();

  const [step, setStep]   = useState<'email' | 'verify' | 'done'>('email');
  const [email, setEmail] = useState('');
  const [code, setCode]   = useState('');
  const [password, setPassword]   = useState('');
  const [confirm,  setConfirm]    = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy]   = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onRequest = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setError(null);
    setBusy(true);
    try {
      await bridge.requestPasswordReset(email.trim());
      setStep('verify');
    } catch (e) {
      setError(e instanceof Error ? e.message : t('welcome.errorServerNotReady'));
    } finally {
      setBusy(false);
    }
  };

  const onVerify = async (e: React.FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setError(null);
    if (password.length < 8) {
      setError(t('welcome.errorPasswordTooShort'));
      return;
    }
    if (password !== confirm) {
      setError(t('welcome.errorPasswordsDontMatch'));
      return;
    }
    setBusy(true);
    try {
      await bridge.consumePasswordReset(code.trim(), password);
      setStep('done');
    } catch (e) {
      setError(e instanceof Error ? e.message : t('welcome.errorServerNotReady'));
    } finally {
      setBusy(false);
    }
  };

  return (

    <motion.div
      layout
      transition={{ duration: 0.5, ease: EASE_DEPTH }}
      style={{ willChange: 'height' }}
    >
    <AnimatePresence mode="popLayout" initial={false}>

      {}
      {step === 'email' && (
        <motion.form
          key="step-email"
          onSubmit={onRequest}
          className="flex flex-col gap-3"
          {...stepTransition}
        >
          <motion.h2
            custom={0} variants={fieldVariants} initial="hidden" animate="visible"
            className="text-lg font-semibold text-text-primary -mt-1 mb-1"
          >
            {t('welcome.resetTitle')}
          </motion.h2>
          <motion.p
            custom={1} variants={fieldVariants} initial="hidden" animate="visible"
            className="text-xs text-text-muted -mt-1 mb-1 leading-snug"
          >
            {t('welcome.resetEmailHint')}
          </motion.p>

          <motion.div custom={2} variants={fieldVariants} initial="hidden" animate="visible">
            <TextField
              icon={Mail}
              type="email"
              placeholder={t('welcome.emailPlaceholder')}
              value={email}
              onChange={e => setEmail(e.target.value)}
              autoComplete="email"
            />
          </motion.div>

          {error && <ErrorBanner>{error}</ErrorBanner>}

          <motion.div custom={3} variants={fieldVariants} initial="hidden" animate="visible">
            <ArrowButton
              type="submit"
              busy={busy}
              disabled={!email}
              fullWidth
              label={t('welcome.resetSubmit')}
            />
          </motion.div>

          <motion.div custom={4} variants={fieldVariants} initial="hidden" animate="visible">
            <Glow3DButton type="button" variant="ghost" onClick={goLogin} fullWidth>
              {t('welcome.backToLogin')}
            </Glow3DButton>
          </motion.div>
        </motion.form>
      )}

      {}
      {step === 'verify' && (
        <motion.form
          key="step-verify"
          onSubmit={onVerify}
          className="flex flex-col gap-3"
          {...stepTransition}
        >
          <motion.h2
            custom={0} variants={fieldVariants} initial="hidden" animate="visible"
            className="text-lg font-semibold text-text-primary -mt-1 mb-1"
          >
            {t('welcome.resetVerifyTitle')}
          </motion.h2>
          <motion.p
            custom={1} variants={fieldVariants} initial="hidden" animate="visible"
            className="text-xs text-text-muted -mt-1 mb-1 leading-snug"
          >
            {t('welcome.resetVerifyHint', { email })}
          </motion.p>

          <motion.div custom={2} variants={fieldVariants} initial="hidden" animate="visible">
            <CodeInput value={code} onChange={setCode} autoFocus />
          </motion.div>

          <motion.div custom={3} variants={fieldVariants} initial="hidden" animate="visible">
            <PasswordField
              placeholder={t('welcome.passwordPlaceholder')}
              value={password}
              onChange={e => setPassword(e.target.value)}
              show={showPassword}
              onToggle={() => setShowPassword(s => !s)}
              autoComplete="new-password"
            />
          </motion.div>
          <motion.div custom={4} variants={fieldVariants} initial="hidden" animate="visible">
            <PasswordField
              placeholder={t('welcome.passwordRepeatPlaceholder')}
              value={confirm}
              onChange={e => setConfirm(e.target.value)}
              show={showPassword}
              onToggle={() => setShowPassword(s => !s)}
              autoComplete="new-password"
            />
          </motion.div>

          {error && <ErrorBanner>{error}</ErrorBanner>}

          <motion.div custom={5} variants={fieldVariants} initial="hidden" animate="visible">
            <ArrowButton
              type="submit"
              busy={busy}
              disabled={code.length !== 6 || password.length < 8 || password !== confirm}
              fullWidth
              isFinish
              label={t('welcome.resetCommit')}
            />
          </motion.div>

          {}
          <motion.div
            custom={6} variants={fieldVariants} initial="hidden" animate="visible"
            className="flex items-center justify-center gap-3 text-xs text-text-muted py-1"
          >
            <button
              type="button"
              onClick={() => { setStep('email'); setError(null); setCode(''); }}
              className="hover:text-accent transition-colors"
            >
              {t('welcome.resetResend')}
            </button>
            <span className="opacity-40">·</span>
            <button
              type="button"
              onClick={goLogin}
              className="hover:text-accent transition-colors"
            >
              {t('welcome.backToLogin')}
            </button>
          </motion.div>
        </motion.form>
      )}

      {}
      {}
      {step === 'done' && (
        <motion.div
          key="step-done"
          className="flex flex-col items-center justify-center py-6 text-center"
          {...stepTransition}
        >
          <motion.div
            initial={{ scale: 0.7, opacity: 0 }}
            animate={{ scale: 1,   opacity: 1 }}
            transition={{ type: 'spring', stiffness: 240, damping: 22, delay: 0.08 }}
            className="w-14 h-14 rounded-2xl
                       bg-[color-mix(in_srgb,var(--status-success)_18%,transparent)]
                       border border-[color-mix(in_srgb,var(--status-success)_40%,transparent)]
                       flex items-center justify-center mb-4"
          >
            <CheckCircle2 size={28} style={{ color: 'var(--status-success)' }} />
          </motion.div>
          <motion.h3
            initial={{ opacity: 0, y: 4 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.18 }}
            className="font-bold text-text-primary text-lg mb-1"
          >
            {t('welcome.resetDoneTitle')}
          </motion.h3>
          <motion.p
            initial={{ opacity: 0, y: 4 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.24 }}
            className="text-sm text-text-muted mb-5"
          >
            {t('welcome.resetDoneHint')}
          </motion.p>
          <motion.div
            initial={{ opacity: 0, y: 4 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, ease: EASE_DEPTH, delay: 0.30 }}
          >
            <Glow3DButton onClick={goLogin} size="lg">
              {t('welcome.backToLogin')}
            </Glow3DButton>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
    </motion.div>
  );
}

interface TextFieldProps {
  icon: LucideIcon;
  type?: string;
  placeholder: string;
  value: string;
  onChange: (e: ChangeEvent<HTMLInputElement>) => void;
  autoComplete?: string;
  rightSlot?: ReactNode;
}

function TextField({ icon: Icon, type = 'text', placeholder, value, onChange, autoComplete, rightSlot }: TextFieldProps) {
  return (
    <div className="relative group">
      <Icon
        size={17}
        strokeWidth={2.25}
        className="absolute z-10 left-3.5 top-1/2 -translate-y-1/2
                   text-white group-focus-within:text-accent
                   transition-colors duration-200 pointer-events-none"
      />
      <input
        type={type}
        value={value}
        onChange={onChange}
        placeholder={placeholder}
        autoComplete={autoComplete}
        className="w-full h-[52px] pl-11 pr-11
                   bg-glass-strong backdrop-blur-glass
                   border border-glass-border rounded-xl
                   text-[15px] text-text-primary placeholder:text-text-muted
                   outline-none transition-all duration-200 ease-depth
                   focus:border-accent focus:shadow-[0_0_0_3px_var(--accent-soft)]"
      />
      {rightSlot && <div className="absolute right-3 top-1/2 -translate-y-1/2">{rightSlot}</div>}
    </div>
  );
}

interface PasswordFieldProps {
  placeholder: string;
  value: string;
  onChange: (e: ChangeEvent<HTMLInputElement>) => void;
  show: boolean;
  onToggle: () => void;
  autoComplete?: string;
}

function PasswordField(props: PasswordFieldProps) {
  const { t } = useTranslation();
  return (
    <TextField
      icon={Lock}
      type={props.show ? 'text' : 'password'}
      placeholder={props.placeholder}
      value={props.value}
      onChange={props.onChange}
      autoComplete={props.autoComplete}
      rightSlot={
        <button
          type="button"
          onMouseDown={e => e.preventDefault()}
          onClick={props.onToggle}
          tabIndex={-1}
          className="btn-no-press w-7 h-7 flex items-center justify-center shrink-0 rounded-md
                     text-text-secondary hover:text-text-primary transition-colors"
          style={{ outline: 'none', lineHeight: 0 }}
          aria-label={props.show ? t('welcome.hidePassword', 'Скрыть пароль') : t('welcome.showPassword', 'Показать пароль')}
        >
          {props.show ? <EyeOff size={16} className="block" /> : <Eye size={16} className="block" />}
        </button>
      }
    />
  );
}

function ErrorBanner({ children }: { children: ReactNode }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: -6, x: 0 }}
      animate={{
        opacity: 1, y: 0,

        x: [0, -4, 4, -2, 2, 0],
      }}
      transition={{
        opacity: { duration: 0.18, ease: EASE_DEPTH },
        y:       { duration: 0.18, ease: EASE_DEPTH },
        x:       { duration: 0.32, ease: 'easeOut' },
      }}
      className="flex items-center gap-2.5 px-3.5 py-2.5 rounded-xl
                 bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)]
                 border border-[color-mix(in_srgb,var(--status-error)_30%,transparent)]
                 shadow-[0_4px_16px_-6px_color-mix(in_srgb,var(--status-error)_55%,transparent)]"
    >
      <span className="shrink-0" style={{ color: 'var(--status-error)' }}>
        <AlertCircle size={16} />
      </span>
      <span className="text-sm leading-snug text-text-primary">{children}</span>
    </motion.div>
  );
}

function FieldHint({ children, kind }: { children: ReactNode; kind?: 'error' }) {
  const color = kind === 'error' ? 'var(--status-error)' : 'var(--text-muted)';
  return <p className="text-xs -mt-1.5 ml-1" style={{ color }}>{children}</p>;
}
