import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Languages, Sparkles, Boxes, ZapOff, ChevronRight, ChevronLeft, Check,
} from 'lucide-react';
import { useUiStore } from '@/store/uiStore';
import { useLanguage } from '@/contexts/SettingsContext';
import { GlassPanel, EASE_DEPTH } from '@/design';
import type { Language, Background } from '@/bridge/types';

const ONBOARDING_LOCAL_KEY = 'hntgraph.onboardingDone';

export function isOnboardingDone(): boolean {
  try { return window.localStorage.getItem(ONBOARDING_LOCAL_KEY) === '1'; }
  catch { return true; }
}
export function markOnboardingDone(): void {
  try { window.localStorage.setItem(ONBOARDING_LOCAL_KEY, '1'); } catch {  }
}
export function resetOnboarding(): void {
  try { window.localStorage.removeItem(ONBOARDING_LOCAL_KEY); } catch {  }
}

interface Props {
  onContinue: () => void;
}

type Step = 0 | 1;
const TOTAL_STEPS = 2;

const fadeIn = {
  initial: { opacity: 0, y: 16, scale: 0.97 },
  animate: { opacity: 1, y: 0, scale: 1 },
  exit:    { opacity: 0, y: -8, scale: 0.97 },
  transition: { duration: 0.42, ease: EASE_DEPTH },
};

export function OnboardingScreen({ onContinue }: Props) {
  const { t } = useTranslation();

  const { setLanguage } = useLanguage();
  const setBackground = useUiStore(s => s.setBackground);
  const currentLang   = useUiStore(s => s.settings.language);

  const [step, setStep] = useState<Step>(0);
  const [lang, setLang] = useState<Language>(currentLang);

  const [bg, setBg] = useState<Background>('cubes');

  useEffect(() => { void setLanguage(lang); }, [lang, setLanguage]);
  useEffect(() => { void setBackground(bg); }, [bg, setBackground]);

  const isLast = step === TOTAL_STEPS - 1;
  const next = () => {
    if (!isLast) setStep((step + 1) as Step);
    else {
      markOnboardingDone();
      onContinue();
    }
  };
  const back = () => { if (step > 0) setStep((step - 1) as Step); };

  return (

    <motion.div
      className="relative w-full h-full flex flex-col items-center justify-center px-8"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 1.8, ease: 'easeOut', delay: 0.45 }}
    >
      <AnimatePresence mode="wait">
        <motion.div
          key={step}
          {...fadeIn}
          className="w-full max-w-[760px] flex flex-col gap-8"
        >
          {}
          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-3">
              <span className="text-[10px] uppercase tracking-[0.28em] text-text-muted font-semibold">
                {t('onboarding.stepOf', { current: step + 1, total: TOTAL_STEPS })}
              </span>
              <div className="flex items-center gap-1.5">
                {Array.from({ length: TOTAL_STEPS }).map((_, i) => (
                  <StepDot key={i} active={step >= i} done={step > i} />
                ))}
              </div>
            </div>
            <h1 className="font-display font-extrabold text-3xl md:text-4xl text-text-primary tracking-tight">
              {step === 0 ? t('onboarding.languageTitle') : t('onboarding.bgTitle')}
            </h1>
            <p className="text-sm text-text-muted max-w-[520px] leading-relaxed">
              {step === 0 ? t('onboarding.languageSubtitle') : t('onboarding.bgSubtitle')}
            </p>
          </div>

          {}
          <div>
            {step === 0 && <LanguageGrid value={lang} onChange={setLang} />}
            {step === 1 && <BackgroundGrid value={bg} onChange={setBg} />}
          </div>

          {}
          <div className="flex items-center justify-between gap-3 pt-2">
            <button
              type="button"
              onClick={back}
              disabled={step === 0}
              className={
                'inline-flex items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold transition-colors ' +
                (step === 0
                  ? 'opacity-30 cursor-not-allowed text-text-muted'
                  : 'text-text-secondary hover:text-text-primary hover:bg-glass')
              }
              style={{ outline: 'none' }}
            >
              <ChevronLeft size={14} />
              <span>{t('onboarding.back')}</span>
            </button>

            <NextButton
              label={isLast ? t('onboarding.finish') : t('onboarding.next')}
              isFinish={isLast}
              onClick={next}
            />
          </div>
        </motion.div>
      </AnimatePresence>
    </motion.div>
  );
}

function StepDot({ active, done }: { active: boolean; done: boolean }) {
  return (
    <span className={
      'block rounded-full transition-all duration-300 ' +
      (done
        ? 'w-2 h-2 bg-accent'
        : active
          ? 'w-6 h-2 bg-accent'
          : 'w-2 h-2 bg-glass-strong')
    } />
  );
}

function LanguageGrid({
  value, onChange,
}: { value: Language; onChange: (l: Language) => void }) {
  const { t } = useTranslation();
  const opts: { code: Language; native: string; sub: string }[] = [
    { code: 'ru', native: 'Русский',  sub: t('onboarding.langRu') },
    { code: 'en', native: 'English',  sub: t('onboarding.langEn') },
    { code: 'pl', native: 'Polski',   sub: t('onboarding.langPl') },
  ];
  return (
    <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
      {opts.map(o => (
        <TileButton
          key={o.code}
          active={value === o.code}
          onClick={() => onChange(o.code)}
          icon={<Languages size={20} />}
          title={o.native}
          subtitle={o.sub}
        />
      ))}
    </div>
  );
}

function BackgroundGrid({
  value, onChange,
}: { value: Background; onChange: (bg: Background) => void }) {
  const { t } = useTranslation();
  const opts: { id: Background; icon: React.ReactNode; title: string; sub: string }[] = [
    { id: 'cubes',  icon: <Boxes size={20} />,    title: t('firstRun.bg.cubes'),  sub: t('onboarding.bgCubesHint') },
    { id: 'aurora', icon: <Sparkles size={20} />, title: t('firstRun.bg.aurora'), sub: t('onboarding.bgAuroraHint') },
    { id: 'off',    icon: <ZapOff size={20} />,   title: t('firstRun.bg.off'),    sub: t('onboarding.bgOffHint') },
  ];
  return (
    <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
      {opts.map(o => (
        <TileButton
          key={o.id}
          active={value === o.id}
          onClick={() => onChange(o.id)}
          icon={o.icon}
          title={o.title}
          subtitle={o.sub}
        />
      ))}
    </div>
  );
}

function TileButton({
  active, onClick, icon, title, subtitle,
}: {
  active: boolean;
  onClick: () => void;
  icon: React.ReactNode;
  title: string;
  subtitle: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      style={{ outline: 'none' }}
      className={
        'relative flex flex-col items-start gap-2 p-4 rounded-2xl border text-left transition-[background-color,border-color,box-shadow,transform] duration-200 ease-out ' +
        (active
          ? 'bg-accent-soft/50 border-accent shadow-[0_8px_24px_var(--accent-soft)] -translate-y-0.5'
          : 'bg-bg-surface border-glass-border hover:border-text-secondary/40 hover:-translate-y-0.5')
      }
    >
      <div className={
        'w-10 h-10 rounded-lg flex items-center justify-center transition-colors ' +
        (active ? 'bg-accent text-text-on-accent' : 'bg-bg-elevated text-text-muted')
      }>
        {icon}
      </div>
      <span className="text-sm font-display font-bold uppercase tracking-wide text-text-primary">
        {title}
      </span>
      <span className="text-xs text-text-muted leading-snug">
        {subtitle}
      </span>
      {active && (
        <span className="absolute top-3 right-3 w-6 h-6 rounded-full bg-accent text-text-on-accent
                         flex items-center justify-center shadow-z1">
          <Check size={13} strokeWidth={3} />
        </span>
      )}
    </button>
  );
}

function NextButton({
  label, isFinish, onClick,
}: {
  label: string;
  isFinish: boolean;
  onClick: () => void;
}) {
  return (
    <motion.button
      type="button"
      onClick={onClick}
      initial={false}
      whileHover={{ scale: 1.02 }}
      whileTap={{ scale: 0.985 }}
      transition={{ type: 'spring', stiffness: 380, damping: 24 }}
      style={{ outline: 'none' }}
      className={
        'group relative inline-flex items-center gap-3 ' +
        'pl-7 pr-5 py-3 rounded-2xl ' +
        'bg-[color-mix(in_srgb,var(--accent-soft)_55%,transparent)] ' +
        'backdrop-blur-glass ' +
        'border border-[color-mix(in_srgb,var(--accent)_35%,transparent)] ' +
        'hover:border-[color-mix(in_srgb,var(--accent)_70%,transparent)] ' +
        'transition-[border-color,box-shadow] duration-300 ease-smooth ' +
        'shadow-[0_8px_32px_-12px_color-mix(in_srgb,var(--accent)_40%,transparent)] ' +
        'hover:shadow-[0_12px_44px_-12px_color-mix(in_srgb,var(--accent)_70%,transparent)]'
      }
    >
      {}
      <span
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 rounded-2xl
                   bg-[radial-gradient(circle_at_50%_50%,color-mix(in_srgb,var(--accent)_25%,transparent),transparent_70%)]
                   opacity-60 group-hover:opacity-90 transition-opacity duration-500"
      />

      <span className="relative font-display font-bold text-[15px] uppercase tracking-[0.08em] text-text-primary">
        {label}
      </span>

      {}
      <span
        className={
          'relative w-9 h-9 rounded-xl flex items-center justify-center shrink-0 ' +
          'bg-[color-mix(in_srgb,var(--accent)_18%,transparent)] ' +
          'border border-[color-mix(in_srgb,var(--accent)_25%,transparent)] ' +
          'transition-transform duration-300 ease-smooth ' +
          (isFinish ? 'group-hover:scale-105' : 'group-hover:translate-x-1.5')
        }
      >
        {isFinish
          ? <Check size={16} strokeWidth={2.6} className="text-accent" />
          : <ChevronRight size={18} strokeWidth={2.4} className="text-accent" />}
      </span>
    </motion.button>
  );
}

export const _GlassPanel = GlassPanel;
