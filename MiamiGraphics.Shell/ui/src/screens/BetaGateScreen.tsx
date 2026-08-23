import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { KeyRound, ShieldAlert, Loader2, LogOut } from 'lucide-react';
import { useSessionStore } from '@/store/sessionStore';
import { GlassPanel, EASE_DEPTH } from '@/design';

export function BetaGateScreen() {
  const { t } = useTranslation();
  const beta       = useSessionStore(s => s.beta);
  const betaError  = useSessionStore(s => s.betaError);
  const checkBeta  = useSessionStore(s => s.checkBeta);
  const redeemBeta = useSessionStore(s => s.redeemBeta);
  const logout     = useSessionStore(s => s.logout);

  const [code, setCode]   = useState('');
  const [busy, setBusy]   = useState(false);
  const [err,  setErr]    = useState<string | null>(null);

  useEffect(() => { if (beta === 'unknown') void checkBeta(); }, [beta, checkBeta]);

  const submit = async () => {
    const c = code.trim();
    if (!c || busy) return;
    setBusy(true); setErr(null);
    const ok = await redeemBeta(c);
    if (!ok) {
      const e = useSessionStore.getState().betaError;
      setErr(
        e === 'invalid_code' ? t('betaGate.errorInvalid', 'Неверный код доступа.')
        : e === 'already_used' ? t('betaGate.errorUsed', 'Этот код уже использован.')
        : e === 'revoked'      ? t('betaGate.errorRevoked', 'Код отозван.')
        : e === 'hwid_mismatch'? t('betaGate.errorHwid', 'Код привязан к другому устройству.')
        : t('betaGate.errorGeneric', 'Не удалось активировать код. Попробуйте ещё раз.'),
      );
    }
    setBusy(false);
  };

  const checking = beta === 'checking' || beta === 'unknown';
  const denied   = beta === 'denied';

  const deniedMsg =
    betaError === 'hwid_mismatch' ? t('betaGate.deniedHwid', 'Этот аккаунт привязан к другому устройству. Обратитесь к администратору для сброса привязки.')
    : betaError === 'revoked'     ? t('betaGate.deniedRevoked', 'Доступ к закрытому бета-тесту отозван.')
    : betaError === 'network'     ? t('betaGate.deniedNetwork', 'Нет связи с сервером. Проверьте интернет и попробуйте снова.')
    : t('betaGate.deniedDefault', 'Нет доступа к закрытому бета-тесту.');

  return (
    <div className="h-full w-full flex items-center justify-center p-8 overflow-y-auto">
      <motion.div
        initial={{ opacity: 0, y: 14 }} animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.34, ease: EASE_DEPTH }}
        className="w-full max-w-md"
      >
        <GlassPanel depth="z3" tint="ultra" highlight edge rounded="3xl"
          className="relative overflow-hidden border border-white/[0.08] p-8 flex flex-col gap-5">
          <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none
                                       bg-gradient-to-r from-transparent via-white/40 to-transparent" />
          <span aria-hidden className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
            style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }} />

          <div className="relative flex flex-col items-center text-center gap-3">
            <span className="inline-flex items-center justify-center w-14 h-14 rounded-2xl text-accent"
              style={{
                background: 'color-mix(in srgb, var(--accent) 14%, transparent)',
                boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 32%, transparent)',
              }}>
              {denied ? <ShieldAlert size={26} /> : <KeyRound size={26} />}
            </span>
            <span className="text-[10px] uppercase tracking-[0.28em] text-accent font-bold">{t('betaGate.eyebrow', 'Закрытый бета-тест')}</span>
            <h1 className="text-xl font-bold text-text-primary tracking-tight">
              {checking ? t('betaGate.titleChecking', 'Проверяем доступ…') : denied ? t('betaGate.titleDenied', 'Доступ закрыт') : t('betaGate.titleCode', 'Код доступа')}
            </h1>
            <p className="text-[13px] text-text-secondary leading-relaxed">
              {checking ? t('betaGate.descChecking', 'Секунду, проверяем твой доступ к ЗБТ.')
                : denied ? deniedMsg
                : t('betaGate.descCode', 'Введи инвайт-код, чтобы получить доступ. Код одноразовый и привязывается к этому устройству.')}
            </p>
          </div>

          {checking && (
            <div className="relative flex justify-center py-2">
              <Loader2 size={22} className="animate-spin text-text-muted" />
            </div>
          )}

          {beta === 'need_code' && (
            <form onSubmit={(e) => { e.preventDefault(); void submit(); }} className="relative flex flex-col gap-3">
              <div className="relative overflow-hidden rounded-xl border border-white/[0.10]
                              bg-bg-elevated/55 backdrop-blur-xl focus-within:border-accent transition-colors">
                <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                                             bg-gradient-to-r from-transparent via-white/40 to-transparent" />
                <input
                  value={code}
                  onChange={(e) => { setCode(e.target.value); setErr(null); }}
                  placeholder="ZBT-XXXXXX"
                  autoFocus spellCheck={false}
                  style={{ outline: 'none' }}
                  className="relative z-10 w-full h-12 px-4 bg-transparent text-center text-[15px] font-mono tracking-[0.18em]
                             text-text-primary placeholder:text-text-muted uppercase"
                />
              </div>
              {err && <span className="text-[12px] text-status-error text-center">{err}</span>}
              <button type="submit" disabled={busy || code.trim().length < 3} style={{ outline: 'none' }}
                className="inline-flex items-center justify-center gap-2 h-12 rounded-xl
                           bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                           hover:bg-bg-elevated/80 hover:border-white/[0.20]
                           disabled:opacity-40 disabled:cursor-not-allowed
                           transition-colors text-sm font-bold uppercase tracking-wider">
                {busy && <Loader2 size={15} className="animate-spin" />}
                {t('betaGate.activate', 'Активировать')}
              </button>
            </form>
          )}

          {denied && betaError === 'network' && (
            <button type="button" onClick={() => void checkBeta()} style={{ outline: 'none' }}
              className="relative inline-flex items-center justify-center gap-2 h-11 rounded-xl
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/80 hover:border-white/[0.20]
                         transition-colors text-sm font-bold uppercase tracking-wider">
              {t('betaGate.retry', 'Повторить')}
            </button>
          )}

          <button type="button" onClick={logout} style={{ outline: 'none' }}
            className="relative inline-flex items-center justify-center gap-1.5 text-[12px]
                       text-text-muted hover:text-text-primary transition-colors">
            <LogOut size={13} /> {t('betaGate.logout', 'Выйти из аккаунта')}
          </button>
        </GlassPanel>
      </motion.div>
    </div>
  );
}
