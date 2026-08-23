import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { motion } from 'framer-motion';
import { useTranslation, Trans } from 'react-i18next';
import type { TFunction } from 'i18next';
import {
  Shield, Search, FolderOpen, Package, ArrowLeft, RefreshCw, ShieldCheck,
  ArrowRight, FileSignature, Download, ScanSearch, CheckCircle2, RotateCcw,
  Waves, Target, Crosshair, Flame, Eye,
} from 'lucide-react';
import { useSecurityStore } from '@/store/securityStore';
import { useReduxStore } from '@/store/reduxStore';
import { useSessionStore } from '@/store/sessionStore';
import { bridge } from '@/bridge';
import { GlassPanel } from '@/design/primitives/GlassPanel';
import { AccentLoader } from '@/design/primitives/AccentLoader';
import { EASE_DEPTH } from '@/design';
import type { LegitCheckProgress } from '@/bridge/types';
import { LegitReportView } from './LegitReportView';
import { Toast, type ToastTone } from '@/components/Toast';
import { LazyImage } from '@/components/LazyImage';

function Island({ className = '', children }: { className?: string; children: ReactNode }) {
  return (
    <GlassPanel
      depth="z2" tint="ultra" rounded="2xl" highlight edge
      className={'relative overflow-hidden border border-white/[0.08] ' + className}
    >
      <span aria-hidden className="absolute inset-0 pointer-events-none bg-bg-elevated/55" />
      <span
        aria-hidden
        className="absolute -top-16 -right-10 w-48 h-48 pointer-events-none blur-3xl"
        style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 16%, transparent) 0%, transparent 70%)' }}
      />
      <div className="relative">{children}</div>
    </GlassPanel>
  );
}

type Mode = 'home' | 'pick-redux' | 'running' | 'report';

export function SecurityScreen() {
  const { t } = useTranslation();
  const {
    running, progress, report, error,
    preselect, setPreselect, sharedCode, sharing,
    checkRedux, checkOwnRpf, share, reset,
  } = useSecurityStore();

  const items = useReduxStore(s => s.items);
  const loadRedux = useReduxStore(s => s.load);
  const auth = useSessionStore(s => s.auth);
  const shareUserId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

  const [mode, setMode] = useState<Mode>('home');
  const [query, setQuery] = useState('');
  const [toast, setToast] = useState<{ open: boolean; tone: ToastTone; message: string }>(
    { open: false, tone: 'info', message: '' });

  useEffect(() => { void loadRedux(); }, [loadRedux]);

  useEffect(() => {
    if (preselect) {
      const { reduxId } = preselect;
      setPreselect(null);
      setMode('running');
      void checkRedux(reduxId);
    }
  }, [preselect, setPreselect, checkRedux]);

  useEffect(() => {
    if (running) setMode('running');
    else if (report) setMode('report');
  }, [running, report]);

  useEffect(() => {
    if (error) {
      setToast({ open: true, tone: 'error', message: error });
      if (!running && !report) setMode('home');
    }
  }, [error]); // eslint-disable-line react-hooks/exhaustive-deps

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    const list = items.filter(i => i.status !== 'hidden');
    if (!q) return list;
    return list.filter(i =>
      i.name.toLowerCase().includes(q) || (i.author ?? '').toLowerCase().includes(q));
  }, [items, query]);

  const backHome = () => { reset(); setMode('home'); setQuery(''); };

  const onPickRedux = (id: string) => {
    setMode('running');
    void checkRedux(id);
  };

  const onCheckOwn = async () => {
    const path = await bridge.openFileDialog('update.rpf', '*.rpf').catch(() => null);
    if (!path) return;
    setMode('running');
    void checkOwnRpf(path);
  };

  const onCheckInstalled = () => {
    setMode('running');
    void checkOwnRpf(null);
  };

  const onShare = async () => {
    if (!shareUserId) {
      setToast({ open: true, tone: 'info', message: t('security.toast.loginToShare', 'Войди в аккаунт, чтобы отправить отчёт администрации.') });
      return;
    }
    if (sharedCode) {
      void navigator.clipboard?.writeText(sharedCode).catch(() => {});
      setToast({ open: true, tone: 'success', message: t('security.toast.codeCopied', { defaultValue: 'Код отчёта скопирован: {{code}}', code: sharedCode }) });
      return;
    }
    const code = await share(shareUserId);
    if (code) {
      void navigator.clipboard?.writeText(code).catch(() => {});
      setToast({ open: true, tone: 'success', message: t('security.toast.codeCopied', { defaultValue: 'Код отчёта скопирован: {{code}}', code }) });
    }
  };

  return (
   <div className="h-full overflow-y-auto">
    <div className="flex flex-col max-w-[1280px] 2xl:max-w-[1600px] mx-auto px-8 py-6 pb-16">
      <div className="flex items-center gap-3 mb-1.5">
        {mode !== 'home' && (
          <button type="button" onClick={backHome}
                  className="w-9 h-9 rounded-xl flex items-center justify-center text-text-muted hover:text-accent
                             hover:bg-glass border border-glass-border bg-glass transition-colors">
            <ArrowLeft size={16} />
          </button>
        )}
        <div className="w-10 h-10 rounded-xl flex items-center justify-center shrink-0"
             style={{ background: 'var(--glass-bg)', boxShadow: '0 0 0 1px var(--glass-border)' }}>
          <Shield size={20} className="text-accent" />
        </div>
        <div>
          <h1 className="font-display font-bold text-2xl uppercase tracking-wide text-text-primary leading-tight">
            {t('security.title', 'Безопасность')}
          </h1>
          <p className="text-[13px] text-text-secondary">{t('security.subtitle', 'Проверка сборок и модов на честность')}</p>
        </div>
      </div>

      {mode === 'home' && (
        <HomeCards
          onCheckMod={() => setMode('pick-redux')}
          onCheckOwn={onCheckOwn}
          onCheckInstalled={onCheckInstalled}
          onFetchByCode={(c) => useSecurityStore.getState().fetchByCode(c)}
        />
      )}

      {mode === 'pick-redux' && (
        <ReduxPicker
          items={filtered}
          query={query}
          setQuery={setQuery}
          onPick={onPickRedux}
        />
      )}

      {mode === 'running' && (
        <RunningCard progress={progress} />
      )}

      {mode === 'report' && report && (
        <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="flex flex-col gap-4">
          <LegitReportView
            report={report}
            onShare={onShare}
            sharedCode={sharedCode}
            sharing={sharing}
          />
          <CheckAnotherCta
            onCheckMod={() => { reset(); setMode('pick-redux'); }}
            onCheckOwn={onCheckOwn}
            onCheckInstalled={onCheckInstalled}
          />
          <WhatWeCheckPanel />
        </motion.div>
      )}

      <Toast open={toast.open} tone={toast.tone} message={toast.message}
             onClose={() => setToast(t => ({ ...t, open: false }))} />
    </div>
   </div>
  );
}

function CheckAnotherCta({ onCheckMod, onCheckOwn, onCheckInstalled }: {
  onCheckMod: () => void; onCheckOwn: () => void; onCheckInstalled: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Island className="p-4">
      <div className="flex items-center gap-2.5 mb-3">
        <div className="w-8 h-8 rounded-lg flex items-center justify-center shrink-0"
             style={{ background: 'var(--glass-bg)', boxShadow: '0 0 0 1px var(--glass-border)' }}>
          <RotateCcw size={15} className="text-text-muted" />
        </div>
        <p className="text-[13.5px] font-semibold text-text-primary">{t('security.again.title', 'Проверить ещё один мод или файл')}</p>
      </div>
      <div className="flex flex-wrap gap-2.5">
        <CtaChip Icon={Package}     label={t('security.again.mod', 'Мод из каталога')}            onClick={onCheckMod} />
        <CtaChip Icon={FolderOpen}  label={t('security.again.ownRpf', 'Свой update.rpf')}         onClick={onCheckOwn} />
        <CtaChip Icon={ShieldCheck} label={t('security.again.installed', 'Установленный сейчас')} onClick={onCheckInstalled} />
      </div>
    </Island>
  );
}

const CHECK_CATEGORIES = [
  { Icon: Waves,      labelKey: 'security.categories.recoil',    labelDef: 'Отдача',    descKey: 'security.categories.recoilDesc',    descDef: 'Поля, гасящие подброс ствола' },
  { Icon: Target,     labelKey: 'security.categories.spread',    labelDef: 'Разброс',   descKey: 'security.categories.spreadDesc',    descDef: 'Насколько кучно летят пули' },
  { Icon: Crosshair,  labelKey: 'security.categories.aim',       labelDef: 'Аим',       descKey: 'security.categories.aimDesc',       descDef: 'Допуски хедшотов, LockOnRange' },
  { Icon: Flame,      labelKey: 'security.categories.damage',    labelDef: 'Урон',      descKey: 'security.categories.damageDesc',    descDef: 'Damage, модификаторы, скорострельность' },
  { Icon: Eye,        labelKey: 'security.categories.viewmodel', labelDef: 'Вьюмодель', descKey: 'security.categories.viewmodelDesc', descDef: 'Позиция оружия и поле зрения' },
] as const;

function WhatWeCheckPanel() {
  const { t } = useTranslation();
  return (
    <Island className="p-4">
      <p className="text-[13.5px] font-semibold text-text-primary mb-1">{t('security.categories.title', 'По каким категориям сверяем')}</p>
      <p className="text-[11.5px] text-text-secondary mb-3 leading-relaxed">
        {t('security.categories.hint', 'В «красный список» попадают только поля, дающие игровое преимущество. Графика, текстуры и звук его не трогают.')}
      </p>
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-2.5">
        {CHECK_CATEGORIES.map(c => (
          <div key={c.labelKey} className="flex items-center gap-2.5 px-3 py-2.5 rounded-xl bg-glass border border-glass-border">
            <div className="w-8 h-8 rounded-lg flex items-center justify-center shrink-0"
                 style={{ background: 'color-mix(in srgb, var(--accent) 14%, transparent)' }}>
              <c.Icon size={15} className="text-accent" />
            </div>
            <div className="min-w-0">
              <p className="text-[12.5px] font-medium text-text-primary leading-tight">{t(c.labelKey, c.labelDef)}</p>
              <p className="text-[10.5px] text-text-muted leading-snug">{t(c.descKey, c.descDef)}</p>
            </div>
          </div>
        ))}
      </div>
    </Island>
  );
}

function CtaChip({ Icon, label, onClick }: { Icon: typeof Package; label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="group inline-flex items-center gap-2 pl-3 pr-3.5 py-2 rounded-full
                 bg-[color-mix(in_srgb,var(--accent-soft)_45%,transparent)]
                 border border-[color-mix(in_srgb,var(--accent)_28%,transparent)]
                 hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                 transition-colors duration-200"
    >
      <Icon size={14} className="text-accent shrink-0" />
      <span className="text-[12.5px] font-medium text-text-primary">{label}</span>
      <ArrowRight size={12} className="text-accent shrink-0 -ml-0.5 transition-transform duration-200 group-hover:translate-x-0.5" />
    </button>
  );
}

function HomeCards({ onCheckMod, onCheckOwn, onCheckInstalled, onFetchByCode }: {
  onCheckMod: () => void;
  onCheckOwn: () => void;
  onCheckInstalled: () => void;
  onFetchByCode: (code: string) => void;
}) {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  return (
    <div className="flex flex-col gap-4 mt-3">
      <Island className="p-4">
        <div className="flex gap-3 items-start">
          <div className="w-9 h-9 rounded-lg flex items-center justify-center shrink-0"
               style={{ background: 'color-mix(in srgb, var(--accent) 16%, transparent)', boxShadow: '0 0 0 1px color-mix(in srgb, var(--accent) 25%, transparent)' }}>
            <ShieldCheck size={17} className="text-accent" />
          </div>
          <p className="text-[13px] text-text-secondary leading-relaxed">
            <Trans
              i18nKey="security.home.disclaimer"
              defaults="Мы отвечаем за целостность файлов, которые передаём от модмейкера - мы<strong> не вносим в них изменений</strong>. Здесь можно вручную проверить любой мод из каталога или свой <code>update.rpf</code>: проверка сравнит его с чистой GTA и покажет всё, что связано с отдачей, разбросом, аимом и уроном."
              components={{ strong: <strong className="text-text-primary" />, code: <code /> }}
            />
          </p>
        </div>
      </Island>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <BigChoice
          Icon={Package}
          title={t('security.home.modTitle', 'Проверить мод из каталога')}
          desc={t('security.home.modDesc', 'Выбери редукс из списка - проверим по манифесту, опасные файлы сверим по значениям.')}
          cta={t('security.home.modCta', 'Выбрать мод')}
          onClick={onCheckMod}
        />
        <BigChoice
          Icon={FolderOpen}
          title={t('security.home.ownTitle', 'Проверить свой update.rpf')}
          desc={t('security.home.ownDesc', 'Выбери файл или проверь установленный - полное сравнение с чистой копией.')}
          cta={t('security.home.ownCta', 'Выбрать файл')}
          onClick={onCheckOwn}
          secondary={{ label: t('security.home.checkInstalled', 'Проверить установленный'), onClick: onCheckInstalled }}
        />
      </div>

      <Island className="p-4">
        <p className="text-[13px] font-medium text-text-primary mb-2.5">{t('security.home.byCodeTitle', 'Открыть отчёт по коду')}</p>
        <div className="flex items-center gap-2">
          <input
            value={code}
            onChange={e => setCode(e.target.value.toUpperCase())}
            placeholder="LGT-XXXX-XXXX"
            className="flex-1 h-10 px-3 rounded-xl bg-glass border border-glass-border text-[13px]
                       text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
          />
          <button type="button" onClick={() => code.trim() && onFetchByCode(code.trim())}
                  disabled={!code.trim()}
                  className="inline-flex items-center gap-2 h-10 px-4 rounded-xl text-[12.5px] font-bold uppercase tracking-wider
                             bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                             hover:bg-bg-elevated/75 hover:border-white/[0.18] transition-colors
                             disabled:opacity-40 disabled:cursor-not-allowed">
            {t('common.open', 'Открыть')} <ArrowRight size={14} />
          </button>
        </div>
      </Island>
    </div>
  );
}

function BigChoice({ Icon, title, desc, cta, onClick, secondary }: {
  Icon: typeof Package; title: string; desc: string; cta: string; onClick: () => void;
  secondary?: { label: string; onClick: () => void };
}) {
  return (
    <Island className="p-5 flex flex-col gap-3 h-full">
      <button type="button" onClick={onClick} className="group text-left flex flex-col gap-3 flex-1 w-full">
        <div className="w-10 h-10 rounded-xl flex items-center justify-center"
             style={{ background: 'color-mix(in srgb, var(--accent) 16%, transparent)', boxShadow: '0 0 0 1px color-mix(in srgb, var(--accent) 25%, transparent)' }}>
          <Icon size={19} className="text-accent" />
        </div>
        <div>
          <p className="text-[15px] font-semibold text-text-primary">{title}</p>
          <p className="mt-1 text-[12.5px] text-text-secondary leading-relaxed">{desc}</p>
        </div>

        <span
          className="mt-auto inline-flex items-center gap-2 self-start pl-3.5 pr-2.5 py-1.5 rounded-full
                     bg-[color-mix(in_srgb,var(--accent-soft)_55%,transparent)]
                     border border-[color-mix(in_srgb,var(--accent)_35%,transparent)]
                     group-hover:border-[color-mix(in_srgb,var(--accent)_65%,transparent)]
                     transition-colors duration-200"
        >
          <span className="text-[11.5px] font-bold uppercase tracking-wider text-text-primary">{cta}</span>
          <span className="w-5 h-5 rounded-full flex items-center justify-center shrink-0
                            bg-[color-mix(in_srgb,var(--accent)_20%,transparent)]
                            transition-transform duration-200 group-hover:translate-x-0.5">
            <ArrowRight size={12} strokeWidth={2.6} className="text-accent" />
          </span>
        </span>
      </button>
      {secondary && (
        <button type="button" onClick={secondary.onClick}
                className="self-start text-[12px] text-text-muted hover:text-accent transition-colors">
          {secondary.label}
        </button>
      )}
    </Island>
  );
}

function ReduxPicker({ items, query, setQuery, onPick }: {
  items: ReturnType<typeof useReduxStore.getState>['items'];
  query: string; setQuery: (s: string) => void; onPick: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="mt-3 flex flex-col gap-4">
      <Island className="p-3">
        <div className="relative">
          <Search size={15} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-text-muted pointer-events-none" />
          <input
            autoFocus
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder={t('security.picker.searchPlaceholder', 'Найти редукс по названию или автору…')}
            className="w-full h-11 pl-10 pr-3 rounded-xl bg-glass border border-glass-border text-[13.5px]
                       text-text-primary placeholder:text-text-muted outline-none focus:border-accent transition-colors"
          />
        </div>
      </Island>

      <div className="flex items-center justify-between px-1">
        <p className="text-[12px] text-text-muted">
          {items.length > 0
            ? t('security.picker.found', { defaultValue: 'Найдено: {{n}}', n: items.length })
            : t('security.picker.pickHint', 'Выбери мод, который хочешь проверить')}
        </p>
      </div>

      {items.length === 0 ? (
        <Island className="p-8 flex flex-col items-center gap-2 text-center">
          <Search size={22} className="text-text-muted" />
          <p className="text-[13px] text-text-secondary">{t('security.picker.noResults', { defaultValue: 'Ничего не найдено по запросу «{{query}}».', query })}</p>
        </Island>
      ) : (
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
          {items.map(it => (
            <button
              key={it.id}
              type="button"
              onClick={() => onPick(it.id)}
              className="group relative aspect-[4/3] w-full rounded-2xl overflow-hidden text-left
                         bg-bg-elevated border border-white/[0.06] hover:border-accent/60
                         shadow-z1 hover:shadow-glow-accent transition-[transform,box-shadow,border-color]
                         duration-300 ease-smooth hover:-translate-y-0.5"
            >
              {it.previewUrl
                ? <LazyImage src={it.previewUrl} alt="" className="absolute inset-0 w-full h-full object-cover
                                   transform-gpu transition-transform duration-[900ms] ease-smooth group-hover:scale-[1.05]" />
                : <div className="absolute inset-0 bg-gradient-to-br from-bg-elevated to-bg-base" />}

              <div className="absolute inset-x-0 bottom-0 h-2/3 pointer-events-none
                              bg-gradient-to-t from-black/85 via-black/35 to-transparent" />

              <div
                className="absolute top-2 right-2 w-7 h-7 rounded-lg flex items-center justify-center shrink-0
                           bg-black/55 backdrop-blur-md border border-white/10 text-white/80
                           group-hover:text-accent group-hover:border-accent/40 transition-colors"
                title={t('security.picker.check', 'Проверить')}
              >
                <Shield size={13} />
              </div>

              <div className="absolute bottom-0 left-0 right-0 px-3 pb-2.5">
                <p className="text-[13px] font-semibold text-white truncate leading-tight
                              [text-shadow:0_2px_4px_rgba(0,0,0,0.9)]">
                  {it.name || it.id}
                </p>
                <p className="text-[11px] text-white/65 truncate mt-0.5">{it.author || '-'}</p>
              </div>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

const STAGE_ORDER: LegitCheckProgress['stage'][] = ['manifest', 'download', 'scan', 'done'];
const STAGE_ICONS: Record<LegitCheckProgress['stage'], typeof FileSignature> = {
  manifest: FileSignature, download: Download, scan: ScanSearch, done: CheckCircle2,
};

const stageLabel = (stage: LegitCheckProgress['stage'], t: TFunction): string => ({
  manifest: t('security.stage.manifest', 'Читаем манифест мода'),
  download: t('security.stage.download', 'Скачиваем файлы мода'),
  scan:     t('security.stage.scan', 'Сверяем с чистой GTA'),
  done:     t('security.stage.done', 'Готово'),
}[stage]);

function RunningCard({ progress }: { progress: ReturnType<typeof useSecurityStore.getState>['progress'] }) {
  const { t } = useTranslation();
  const pct = progress?.percent ?? 0;
  const stage = progress?.stage ?? 'manifest';
  const currentLabel = progress
    ? stageLabel(progress.stage, t)
    : t('security.running.preparing', 'Готовимся к проверке');
  const stageIdx = STAGE_ORDER.indexOf(stage);

  return (
    <Island className="mt-3 p-6">
      <div className="flex items-center gap-4">
        <div className="relative w-14 h-14 shrink-0 flex items-center justify-center">
          <AccentLoader size={40} />
          <span className="absolute text-[10px] font-bold tabular-nums text-text-primary">{pct}%</span>
        </div>
        <div className="min-w-0 flex-1">
          <h3 className="text-[15px] font-semibold text-text-primary">{t('security.running.title', 'Идёт проверка')}</h3>
          <p className="text-[12.5px] text-text-secondary truncate">{currentLabel}…</p>
        </div>
        <span className="hidden sm:block text-4xl font-display font-bold tabular-nums text-text-primary shrink-0">
          {pct}<span className="text-lg text-text-muted">%</span>
        </span>
      </div>

      <div className="mt-4 h-2.5 rounded-full bg-track overflow-hidden relative">
        <motion.div
          className="h-full rounded-full bg-accent relative"
          animate={{ width: `${pct}%` }}
          transition={{ duration: 0.3, ease: EASE_DEPTH }}
          style={{ boxShadow: '0 0 12px 1px color-mix(in srgb, var(--accent) 70%, transparent)' }}
        />
      </div>

      <div className="mt-4 flex items-center gap-1.5">
        {STAGE_ORDER.map((s, i) => {
          const Icon = STAGE_ICONS[s];
          const isDone = i < stageIdx || stage === 'done';
          const isCurrent = i === stageIdx && stage !== 'done';
          return (
            <div key={s} className="flex items-center gap-1.5 flex-1 min-w-0">
              <div
                className="w-7 h-7 rounded-full flex items-center justify-center shrink-0 transition-colors duration-300"
                style={
                  isDone
                    ? { background: 'color-mix(in srgb, var(--accent) 22%, transparent)' }
                    : isCurrent
                      ? { background: 'color-mix(in srgb, var(--accent) 16%, transparent)', boxShadow: '0 0 0 2px color-mix(in srgb, var(--accent) 45%, transparent)' }
                      : { background: 'var(--glass-bg)', boxShadow: '0 0 0 1px var(--glass-border)' }
                }
              >
                {isDone
                  ? <CheckCircle2 size={13} className="text-accent" />
                  : <Icon size={12} className={isCurrent ? 'text-accent' : 'text-text-muted'} />}
              </div>
              <span className={`hidden md:block text-[11px] truncate ${isCurrent ? 'text-text-primary font-medium' : 'text-text-muted'}`}>
                {stageLabel(s, t)}
              </span>
              {i < STAGE_ORDER.length - 1 && (
                <div className="flex-1 h-px min-w-[8px]"
                     style={{ background: isDone ? 'color-mix(in srgb, var(--accent) 35%, transparent)' : 'var(--glass-border)' }} />
              )}
            </div>
          );
        })}
      </div>

      {progress?.currentFile && (
        <div className="mt-4 flex items-center gap-2.5 px-3 py-2.5 rounded-xl bg-glass border border-glass-border">
          <ScanSearch size={13} className="text-text-muted shrink-0" />
          <code className="text-[11.5px] text-text-secondary truncate">{progress.currentFile}</code>
        </div>
      )}

      <p className="mt-4 text-[11.5px] text-text-muted flex items-center gap-1.5">
        <RefreshCw size={12} className="animate-spin shrink-0" /> {t('security.running.dontClose', 'Не закрывай окно до конца проверки')}
      </p>
    </Island>
  );
}
