import { useEffect, useMemo, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Loader2, Cpu, Activity, ShieldCheck, FileX, Copy, Check, Code2, Zap, Download, ThumbsUp, ThumbsDown,
} from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import type { GtaPreset, PresetReactions } from '@/bridge/types';
import { bridge } from '@/bridge';
import { HoverTip } from '@/components/HoverTip';
import { useSessionStore } from '@/store/sessionStore';

interface Props {
  preset:    GtaPreset;
  applying:  boolean;
  installed: boolean;
  onApply:   () => void;
}

export function PresetDetail({ preset, applying, installed, onApply }: Props) {
  const { t, i18n } = useTranslation();
  const [showXml, setShowXml] = useState(false);

  const { rows, loading, error } = useParsedSettings(preset.xmlUrl);

  const auth = useSessionStore(s => s.auth);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const [reactions, setReactions] = useState<PresetReactions>({ likes: 0, dislikes: 0, myReaction: 0 });
  const [reactBusy, setReactBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    setReactions({ likes: 0, dislikes: 0, myReaction: 0 });
    bridge.gtaPresetReactionsGet(preset.id, userId ?? '')
      .then(r => { if (alive) setReactions(r); })
      .catch(() => {  });
    return () => { alive = false; };
  }, [preset.id, userId]);

  const react = async (value: 1 | -1) => {
    if (!userId || reactBusy) return;
    setReactBusy(true);
    try {
      const r = await bridge.gtaPresetReactionSet(preset.id, value);
      setReactions(r);
    } catch {  }
    finally { setReactBusy(false); }
  };

  return (
    <div className="h-full flex flex-col overflow-hidden">
      <div className="flex-1 overflow-y-auto">
        <div className="px-10 pt-9 pb-6 flex flex-col gap-8">

          {}
          <header className="flex items-start justify-between gap-4 flex-wrap">
            <div className="min-w-0 flex-1">
              <span className="text-[10px] font-bold uppercase tracking-[0.28em] text-accent">
                {preset.isTournament
                  ? t('gtaSettings.preset.kindTournament', 'Турнирный конфиг')
                  : t('gtaSettings.preset.kindGraphics', 'Графический пресет')}
              </span>
              <h1 className="font-display text-[36px] font-bold text-text-primary tracking-tight leading-[1.05] mt-1.5 break-words">
                {preset.name}
              </h1>
              <div className="mt-2.5 text-[12px] text-text-muted flex items-center gap-2.5 flex-wrap">
                {preset.author && <span>{preset.author}</span>}
                <span>{formatDate(preset.uploadedAt, i18n.language)}</span>
                <BiasInline bias={preset.cpuBias} />
                <span className="inline-flex items-center gap-1.5 tabular-nums"
                      title={t('gtaSettings.preset.installsCount', { count: preset.downloadCount, defaultValue: '{{count}} установок' })}>
                  <Download size={11} strokeWidth={2.2} />
                  {t('gtaSettings.preset.installsCount', { count: preset.downloadCount, defaultValue: '{{count}} установок' })}
                </span>
                {installed && (
                  <span className="inline-flex items-center gap-1.5 text-status-success">
                    <span className="w-1.5 h-1.5 rounded-full bg-status-success" />
                    {t('redux.installedPill', 'Установлено')}
                  </span>
                )}
              </div>
            </div>
            <div className="shrink-0 flex items-center gap-2 flex-wrap justify-end">
              <div className="flex items-center gap-1.5">
                <HoverTip label={userId
                  ? t('gtaSettings.preset.likeTip', 'Нравится')
                  : t('gtaSettings.preset.loginToRate', 'Войдите в аккаунт, чтобы оценить')}>
                <button
                  type="button"
                  onClick={() => react(1)}
                  disabled={reactBusy || !userId}
                  style={{ outline: 'none' }}
                  className={
                    'inline-flex items-center gap-1.5 h-10 px-3 rounded-xl border transition-colors duration-200 ' +
                    'text-[13px] font-bold tabular-nums disabled:cursor-not-allowed ' +
                    (reactions.myReaction === 1
                      ? 'bg-status-success/15 border-transparent text-status-success'
                      : 'bg-white/[0.04] border-white/[0.10] text-text-secondary hover:bg-white/[0.08] ' +
                        'hover:border-white/[0.18] hover:text-text-primary disabled:opacity-50')
                  }
                >
                  {reactBusy ? <Loader2 size={14} className="animate-spin" /> : <ThumbsUp size={14} strokeWidth={2.2} />}
                  {reactions.likes}
                </button>
                </HoverTip>
                <HoverTip label={userId
                  ? t('gtaSettings.preset.dislikeTip', 'Не нравится')
                  : t('gtaSettings.preset.loginToRate', 'Войдите в аккаунт, чтобы оценить')}>
                <button
                  type="button"
                  onClick={() => react(-1)}
                  disabled={reactBusy || !userId}
                  style={{ outline: 'none' }}
                  className={
                    'inline-flex items-center gap-1.5 h-10 px-3 rounded-xl border transition-colors duration-200 ' +
                    'text-[13px] font-bold tabular-nums disabled:cursor-not-allowed ' +
                    (reactions.myReaction === -1
                      ? 'bg-status-error/15 border-transparent text-status-error'
                      : 'bg-white/[0.04] border-white/[0.10] text-text-secondary hover:bg-white/[0.08] ' +
                        'hover:border-white/[0.18] hover:text-text-primary disabled:opacity-50')
                  }
                >
                  {reactBusy ? <Loader2 size={14} className="animate-spin" /> : <ThumbsDown size={14} strokeWidth={2.2} />}
                  {reactions.dislikes}
                </button>
                </HoverTip>
              </div>
              <HoverTip label={t('gtaSettings.preset.showXmlTip', 'Показать settings.xml')}>
              <button
                type="button"
                onClick={() => setShowXml(s => !s)}
                style={{ outline: 'none' }}
                className={
                  'inline-flex items-center gap-2 h-10 px-3.5 rounded-xl ' +
                  'border transition-colors duration-200 ' +
                  (showXml
                    ? 'bg-white/[0.10] border-white/[0.30] text-text-primary'
                    : 'bg-white/[0.04] border-white/[0.10] text-text-secondary ' +
                      'hover:bg-white/[0.08] hover:border-white/[0.18] hover:text-text-primary')
                }
              >
                <Code2 size={14} strokeWidth={2.2} />
                <span className="text-[11px] uppercase tracking-[0.18em] font-bold">{t('gtaSettings.preset.xmlCodeBtn', 'XML Код')}</span>
              </button>
              </HoverTip>
            </div>
          </header>

          {}
          {preset.description && (
            <p className="text-[13px] text-text-secondary leading-[1.65] whitespace-pre-line -mt-3">
              {preset.description}
            </p>
          )}

          {}
          <span aria-hidden className="block h-px bg-gradient-to-r from-transparent via-white/12 to-transparent" />

          {}
          <section className="flex flex-col gap-2">
            <span className="inline-flex items-center gap-2 text-[10px] font-bold uppercase tracking-[0.28em] text-text-muted">
              <Zap size={11} strokeWidth={2.4} className="text-status-success" />
              {t('gtaSettings.preset.expectedFps', 'Ожидаемый FPS')}
            </span>
            {preset.expectedFpsLow != null && preset.expectedFpsHigh != null ? (
              <div className="flex items-baseline gap-2 flex-wrap">
                <span className="text-[28px] font-bold tabular-nums leading-none text-status-success">
                  {preset.expectedFpsLow}
                  <span className="text-text-muted mx-1.5 font-semibold">–</span>
                  {preset.expectedFpsHigh}
                </span>
                <span className="text-[12px] text-text-muted font-semibold">FPS</span>
                {preset.baselineHwLabel && (
                  <span className="text-[11px] text-text-muted ml-1">· {preset.baselineHwLabel}</span>
                )}
              </div>
            ) : (
              <div className="text-[12px] text-text-muted">
                {t('gtaSettings.preset.expectedFpsMissing', 'Автор не указал ожидаемый FPS для этого пресета.')}
              </div>
            )}
          </section>

          {}
          <span aria-hidden className="block h-px bg-gradient-to-r from-transparent via-white/12 to-transparent" />

          {}
          <section className="flex flex-col gap-4">
            <h2 className="text-[10px] font-bold uppercase tracking-[0.28em] text-text-muted">
              {t('gtaSettings.preset.detailedParams', 'Детальные параметры')}
            </h2>
            {loading ? (
              <div className="flex items-center gap-2 text-[12px] text-text-muted py-4">
                <Loader2 size={12} className="animate-spin" />
                {t('gtaSettings.preset.xmlLoading', 'Читаем XML с R2…')}
              </div>
            ) : error ? (
              <div data-launcher-demo-hide-xml-error className="flex items-start gap-2 text-[12px] text-status-error">
                <FileX size={13} className="shrink-0 mt-0.5" />
                <div>
                  {t('gtaSettings.preset.xmlParseError', { error, defaultValue: 'Не удалось разобрать XML: {{error}}.' })}
                  <div className="mt-0.5 text-text-muted">
                    {t('gtaSettings.preset.xmlParseErrorHint', 'На саму установку не влияет - нажми «Применить настройки».')}
                  </div>
                </div>
              </div>
            ) : rows.length === 0 ? (
              <div className="text-[12px] text-text-muted">
                {t('gtaSettings.preset.xmlNoFields', 'XML не содержит распознанных полей.')}
              </div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-x-10 gap-y-0">
                {rows.map(row => (
                  <ParamLine key={row.label} label={row.label} value={row.value} />
                ))}
              </div>
            )}
          </section>

        </div>
      </div>

      {}
      <AnimatePresence initial={false}>
        {showXml && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit   ={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
            style={{ overflow: 'hidden' }}
            className="shrink-0 border-t border-border-subtle bg-bg-elevated-soft"
          >
            <div className="px-10 py-4">
              <XmlPreview key={preset.id} xmlUrl={preset.xmlUrl} />
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {}
      <footer className="shrink-0 px-10 pb-7 pt-2">
        <button
          type="button"
          onClick={onApply}
          disabled={applying}
          style={{ outline: 'none' }}
          className="w-full inline-flex items-center justify-center gap-2.5 h-14 rounded-2xl
                     bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                     hover:bg-bg-elevated/75 hover:border-white/[0.18]
                     text-[13px] font-bold uppercase tracking-[0.22em]
                     transition-colors duration-200
                     disabled:opacity-60 disabled:cursor-wait"
        >
          {applying && <Loader2 size={16} className="animate-spin" />}
          <span>
            {applying
              ? t('gtaSettings.preset.applying', 'Применяем…')
              : (installed
                  ? t('gtaSettings.preset.reapplyCta', 'Переустановить настройки')
                  : t('gtaSettings.preset.applyCta', 'Применить настройки'))}
          </span>
        </button>
      </footer>
    </div>
  );
}

type ParsedSettings = Record<string, string>;

interface ParamRow {
  label: string;
  value: string;
}

interface LabelKey { key: string; def: string }

const QUALITY_LABEL: Record<string, LabelKey> = {
  '0': { key: 'gtaSettings.value.normal',   def: 'normal' },
  '1': { key: 'gtaSettings.value.high',     def: 'high' },
  '2': { key: 'gtaSettings.value.veryHigh', def: 'very high' },
  '3': { key: 'gtaSettings.value.ultra',    def: 'ultra' },
};

const ASPECT_LABEL: Record<string, string> = {
  '1': '3:2',
  '2': '4:3',
  '3': '5:4',
  '4': '16:10',
  '5': '16:9',
  '6': '17:9',
  '7': '21:9',
  '8': '32:9',
  '9': '48:9',
};

const WINDOW_MODE_LABEL: Record<string, LabelKey> = {
  '0': { key: 'gtaSettings.value.fullscreen', def: 'fullscreen' },
  '1': { key: 'gtaSettings.value.windowed',   def: 'windowed' },
  '2': { key: 'gtaSettings.value.borderless', def: 'borderless' },
};

function pickValue(s: ParsedSettings, ...names: string[]): string | undefined {
  for (const n of names) {
    const v = s[n];
    if (v !== undefined && v !== null && v !== '') return v;
  }
  return undefined;
}

function formatValue(key: string, raw: string, t: TFunction): string {
  switch (key) {
    case 'AspectRatio':
      return raw === '0' ? t('gtaSettings.value.auto', 'auto') : (ASPECT_LABEL[raw] ?? raw);
    case 'Windowed': {
      const w = WINDOW_MODE_LABEL[raw];
      return w ? t(w.key, w.def) : raw;
    }
    case 'PauseOnFocusLoss':
    case 'VSync':
    case 'FXAA_Enabled':
    case 'TXAA_Enabled':
    case 'Lighting_FogVolumes':
    case 'Shader_SSA':
    case 'DoF':
    case 'HdStreamingInFlight':
    case 'Lighting_DynamicShadows':
    case 'UltraShadows_Enabled':
    case 'Shadow_LongShadows':
    case 'Shadow_ParticleShadows':
    case 'Reflection_MipBlur':
      return raw === '1' || raw === 'true'
        ? t('gtaSettings.value.on', 'true')
        : t('gtaSettings.value.offBool', 'false');
    case 'ShadowQuality':
    case 'Shadow_Quality':
    case 'ReflectionQuality':
    case 'Reflection_Quality':
    case 'GrassQuality':
    case 'Grass_Quality':
    case 'PostFX':
    case 'MSAA':
    case 'ReflectionMSAA': {
      if (raw === '0') return t('gtaSettings.value.off', 'off');
      const q = QUALITY_LABEL[raw];
      return q ? t(q.key, q.def) : (key === 'MSAA' || key === 'ReflectionMSAA' ? `${raw}x` : raw);
    }
    case 'TextureQuality':
    case 'Texture_Quality':
    case 'ShaderQuality':
    case 'Shader_Quality':
    case 'ParticleQuality':
    case 'Particles_Quality':
    case 'WaterQuality':
    case 'Water_Quality':
    case 'AnisotropicFiltering':
    case 'Tessellation':
    case 'SSAO':
    case 'DX_Version': {
      const q = QUALITY_LABEL[raw];
      return q ? t(q.key, q.def) : raw;
    }
    default:
      return raw;
  }
}

function buildParamRows(s: ParsedSettings, t: TFunction): ParamRow[] {
  const rows: ParamRow[] = [];
  const push = (label: string, value: string | undefined) => {
    if (value !== undefined && value !== null && value !== '') {
      rows.push({ label, value });
    }
  };

  if (s.ScreenWidth && s.ScreenHeight) {
    rows.push({ label: 'Resolution', value: `${s.ScreenWidth}x${s.ScreenHeight}` });
  }
  const refresh = pickValue(s, 'RefreshRate');
  push('Refresh Rate', refresh ? `${refresh} Hz` : undefined);
  const aspect = pickValue(s, 'AspectRatio');
  push('Aspect Ratio', aspect ? formatValue('AspectRatio', aspect, t) : undefined);
  const windowed = pickValue(s, 'Windowed');
  push('Window Mode', windowed ? formatValue('Windowed', windowed, t) : undefined);
  const vsync = pickValue(s, 'VSync');
  push('VSync', vsync ? formatValue('VSync', vsync, t) : undefined);

  const tex = pickValue(s, 'TextureQuality', 'Texture_Quality');
  push('Texture Quality', tex ? formatValue('TextureQuality', tex, t) : undefined);
  const shader = pickValue(s, 'ShaderQuality', 'Shader_Quality');
  push('Shader Quality', shader ? formatValue('ShaderQuality', shader, t) : undefined);
  const shadow = pickValue(s, 'ShadowQuality', 'Shadow_Quality');
  push('Shadow Quality', shadow ? formatValue('ShadowQuality', shadow, t) : undefined);
  const refl = pickValue(s, 'ReflectionQuality', 'Reflection_Quality');
  push('Reflection Quality', refl ? formatValue('ReflectionQuality', refl, t) : undefined);
  const grass = pickValue(s, 'GrassQuality', 'Grass_Quality');
  push('Grass Quality', grass ? formatValue('GrassQuality', grass, t) : undefined);
  const water = pickValue(s, 'WaterQuality', 'Water_Quality');
  push('Water Quality', water ? formatValue('WaterQuality', water, t) : undefined);
  const particle = pickValue(s, 'ParticleQuality', 'Particles_Quality');
  push('Particle Quality', particle ? formatValue('ParticleQuality', particle, t) : undefined);

  push('Post FX', s.PostFX ? formatValue('PostFX', s.PostFX, t) : undefined);
  push('MSAA', s.MSAA ? formatValue('MSAA', s.MSAA, t) : undefined);
  push('Reflection MSAA', s.ReflectionMSAA ? formatValue('ReflectionMSAA', s.ReflectionMSAA, t) : undefined);
  push('FXAA', s.FXAA_Enabled ? formatValue('FXAA_Enabled', s.FXAA_Enabled, t) : undefined);
  push('TXAA', s.TXAA_Enabled ? formatValue('TXAA_Enabled', s.TXAA_Enabled, t) : undefined);
  push('Anisotropic', s.AnisotropicFiltering ? formatValue('AnisotropicFiltering', s.AnisotropicFiltering, t) : undefined);

  push('Population Density', s.CityDensity);
  push('Ped Variety',        s.PedVarietyMultiplier);
  push('Vehicle Variety',    s.VehicleVarietyMultiplier);
  push('LOD Scale',          s.LodScale);
  push('Ped LOD Bias',       s.PedLodBias);
  push('Vehicle LOD Bias',   s.VehicleLodBias);

  push('Tessellation',  s.Tessellation ? formatValue('Tessellation', s.Tessellation, t) : undefined);
  push('SSAO',          s.SSAO ? formatValue('SSAO', s.SSAO, t) : undefined);
  push('Shadow Distance',     s.Shadow_Distance);
  push('Shadow Soft Shadows', s.Shadow_SoftShadows);
  push('Shadow Long Shadows', s.Shadow_LongShadows ? formatValue('Shadow_LongShadows', s.Shadow_LongShadows, t) : undefined);
  push('Shadow Particle Shadows', s.Shadow_ParticleShadows ? formatValue('Shadow_ParticleShadows', s.Shadow_ParticleShadows, t) : undefined);
  push('Ultra Shadows',       s.UltraShadows_Enabled ? formatValue('UltraShadows_Enabled', s.UltraShadows_Enabled, t) : undefined);
  push('Reflection Mip Blur', s.Reflection_MipBlur ? formatValue('Reflection_MipBlur', s.Reflection_MipBlur, t) : undefined);
  push('Fog Volumes',         s.Lighting_FogVolumes ? formatValue('Lighting_FogVolumes', s.Lighting_FogVolumes, t) : undefined);
  push('DoF',                 s.DoF ? formatValue('DoF', s.DoF, t) : undefined);
  push('HD Streaming',        s.HdStreamingInFlight ? formatValue('HdStreamingInFlight', s.HdStreamingInFlight, t) : undefined);
  push('Motion Blur',         s.MotionBlurStrength);
  push('DX Version',          s.DX_Version);

  return rows;
}

async function fetchTextWithRetry(
  url: string, attempts = 4, isAlive: () => boolean = () => true,
): Promise<string> {
  let lastErr: unknown;
  for (let i = 0; i < attempts; i++) {
    if (!isAlive()) throw new Error('aborted');
    try {
      const r = await fetch(url, { cache: 'no-cache' });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      return await r.text();
    } catch (e) {
      lastErr = e;
      if (i < attempts - 1) {
        await new Promise(res => setTimeout(res, 250 * (i + 1)));
      }
    }
  }
  throw lastErr instanceof Error ? lastErr : new Error(String(lastErr));
}

function useParsedSettings(xmlUrl: string) {
  const { t } = useTranslation();
  const [text, setText] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    setText(null);
    setError(null);
    setLoading(true);
    fetchTextWithRetry(xmlUrl, 4, () => alive)
      .then(t => { if (alive) { setText(t); setLoading(false); } })
      .catch(e => {
        if (alive) {
          setError(e instanceof Error ? e.message : String(e));
          setLoading(false);
        }
      });
    return () => { alive = false; };
  }, [xmlUrl]);

  const rows = useMemo<ParamRow[]>(() => {
    if (!text) return [];
    try {
      const doc = new DOMParser().parseFromString(text, 'application/xml');
      if (doc.querySelector('parsererror')) return [];

      const dict: ParsedSettings = {};
      const walk = (node: Element) => {
        const value = node.getAttribute('value');
        if (value !== null) dict[node.tagName] = value;
        for (const child of Array.from(node.children)) walk(child);
      };
      if (doc.documentElement) walk(doc.documentElement);

      return buildParamRows(dict, t);
    } catch {
      return [];
    }
  }, [text, t]);

  return { rows, loading, error };
}

function ParamLine({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-3 py-3
                    border-b border-white/[0.06]
                    last:border-b-0">
      <span className="text-[12.5px] text-text-secondary truncate">{label}</span>
      <span className="text-[12.5px] font-semibold text-text-primary tabular-nums truncate text-right">
        {value}
      </span>
    </div>
  );
}

function BiasInline({ bias }: { bias: GtaPreset['cpuBias'] }) {
  const cfg = bias === 'cpu'
    ? { Icon: Cpu,         label: 'CPU-friendly', color: '#a78bfa' }
    : bias === 'gpu'
      ? { Icon: ShieldCheck, label: 'GPU-friendly', color: '#5eead4' }
      : { Icon: Activity,    label: 'Balanced',     color: '#818cf8' };
  const { Icon } = cfg;
  return (
    <span className="inline-flex items-center gap-1">
      <Icon size={11} style={{ color: cfg.color }} />
      <span>{cfg.label}</span>
    </span>
  );
}

function formatDate(iso: string, locale: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleDateString(locale, { year: 'numeric', month: 'long', day: 'numeric' });
  } catch {
    return iso;
  }
}

function XmlPreview({ xmlUrl }: { xmlUrl: string }) {
  const { t } = useTranslation();
  const [text, setText]       = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError]     = useState<string | null>(null);
  const [copied, setCopied]   = useState(false);

  useEffect(() => {
    if (text !== null || loading || error) return;
    let alive = true;
    setLoading(true);
    fetchTextWithRetry(xmlUrl, 4, () => alive)
      .then(t => { if (alive) { setText(t); setLoading(false); } })
      .catch(e => {
        if (alive) {
          setError(e instanceof Error ? e.message : String(e));
          setLoading(false);
        }
      });
    return () => { alive = false; };

  }, [xmlUrl]);

  const onCopy = async () => {
    if (!text) return;
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {  }
  };

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-[12px] text-text-muted py-2">
        <Loader2 size={12} className="animate-spin" />
        {t('gtaSettings.preset.xmlLoading', 'Читаем XML с R2…')}
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-start gap-2 text-[12px] text-status-error py-2">
        <FileX size={13} className="shrink-0 mt-0.5" />
        <div>
          {t('gtaSettings.preset.xmlLoadError', { error, defaultValue: 'Не удалось загрузить XML: {{error}}.' })}
          <div className="mt-0.5 text-text-muted">
            {t('gtaSettings.preset.xmlLoadErrorHint', 'На саму установку не влияет.')}
          </div>
        </div>
      </div>
    );
  }

  if (text === null) return null;

  return (
    <div className="relative">
      <button
        type="button"
        onClick={onCopy}
        title={copied
          ? t('gtaSettings.preset.xmlCopied', 'Скопировано')
          : t('gtaSettings.preset.xmlCopy', 'Скопировать')}
        style={{ outline: 'none' }}
        className="absolute top-1 right-1 z-10 inline-flex items-center gap-1 px-2 h-7 rounded-md
                   text-[10px] uppercase tracking-[0.14em] text-text-muted
                   hover:text-text-primary hover:bg-glass
                   transition-colors"
      >
        {copied ? <Check size={11} /> : <Copy size={11} />}
        {copied ? 'OK' : 'copy'}
      </button>
      <pre className="text-[11px] leading-[1.55] font-mono text-text-secondary overflow-auto max-h-[280px] pr-12">
        <code>{text}</code>
      </pre>
    </div>
  );
}
