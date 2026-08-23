import { useEffect, useRef, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { Droplet, Target, Share2, Download, Copy, Check, Loader2 } from 'lucide-react';
import { useReticleBuilderStore } from '@/store/reticleBuilderStore';
import { useSessionStore } from '@/store/sessionStore';
import { bridge } from '@/bridge';
import type { ReticleSpec, ReticleWeaponGroup, ReticleWeaponOverride } from '@/bridge/types';

type GeomPatch = Partial<Omit<ReticleWeaponOverride, 'weapon'>>;
import { Toast } from '@/components/Toast';

const GEOM_ROWS: [keyof ReticleSpec, string, string, number, number, number][] = [
  ['dotSize',      'Размер точки',         'px (у ванили 4)', 0, 10, 0.5],
  ['gap',          'Расстояние от центра', 'зазор, px',     0,  40, 1],
  ['length',       'Длина линий',          'px',            0,  50, 1],
  ['thickness',    'Толщина линий',        'px',            1,  14, 0.5],
  ['tilt',         'Наклон',               'градусы',       0,  45, 1],
  ['outlineWidth', 'Толщина обводки',      'px',            0,   6, 0.5],
];

const GLOBAL_ROWS: [keyof ReticleSpec, string, string, number, number, number][] = [
  ['opacity',      'Прозрачность',         '%',             0, 100, 1],
  ['scale',        'Масштаб',              '% (dotScaler)', 50, 200, 5],
];

const WEAPON_TABS: ['all' | ReticleWeaponGroup, string][] = [
  ['all',     'Все'],
  ['pistol',  'Пистолеты'],
  ['smg',     'ПП'],
  ['rifle',   'Автоматы'],
  ['shotgun', 'Дробовики'],
];

function crosshairSvg(s: ReticleSpec, zoom: number): string {
  const cx = 500, cy = 281.5, u = zoom * (1000 / 1280) * (s.scale / 100);
  const col = s.colorMain;
  const gap = s.gap * u, len = s.length * u, th = s.thickness * u;
  const ow = s.outline ? s.outlineWidth * u : 0, ds = s.dotSize * u, op = s.opacity / 100;
  const arms = (len > 0 && th > 0) ? [
    [cx - th / 2, cy - gap - len, th, len],
    [cx - th / 2, cy + gap, th, len],
    [cx - gap - len, cy - th / 2, len, th],
    [cx + gap, cy - th / 2, len, th],
  ] : [];
  const rx = Math.min(2, th / 3);
  let out = '', fill = '';
  for (const a of arms) {
    if (ow > 0) out += `<rect x="${a[0] - ow}" y="${a[1] - ow}" width="${a[2] + ow * 2}" height="${a[3] + ow * 2}" rx="${rx}" fill="#000"/>`;
    fill += `<rect x="${a[0]}" y="${a[1]}" width="${a[2]}" height="${a[3]}" rx="${rx}" fill="${col}"/>`;
  }
  let dot = '';
  if (s.dot && ds > 0) {
    if (ow > 0) dot += `<circle cx="${cx}" cy="${cy}" r="${ds / 2 + ow}" fill="#000"/>`;
    dot += `<circle cx="${cx}" cy="${cy}" r="${ds / 2}" fill="${col}"/>`;
  }
  let ring = '';
  if (s.ring && s.ringRadius > 0 && s.ringThickness > 0) {
    const rr = s.ringRadius * u, rw = s.ringThickness * u;
    if (ow > 0) ring += `<circle cx="${cx}" cy="${cy}" r="${rr}" fill="none" stroke="#000" stroke-width="${rw + ow * 2}"/>`;
    ring += `<circle cx="${cx}" cy="${cy}" r="${rr}" fill="none" stroke="${col}" stroke-width="${rw}"/>`;
  }
  return `<g transform="rotate(${s.tilt} ${cx} ${cy})" opacity="${op}">${ring}${out}${fill}${dot}</g>`;
}

const cardCls = 'rounded-2xl border border-white/[0.08] bg-white/[0.03] backdrop-blur-xl';
const capCls = 'text-[10px] font-bold uppercase tracking-[0.2em] text-text-muted';

export function ReticleConstructor() {
  const { t } = useTranslation();
  const spec  = useReticleBuilderStore(s => s.spec);
  const set   = useReticleBuilderStore(s => s.set);
  const reset = useReticleBuilderStore(s => s.reset);
  const applying   = useReticleBuilderStore(s => s.applying);
  const apply      = useReticleBuilderStore(s => s.apply);
  const loadSpec   = useReticleBuilderStore(s => s.loadSpec);
  const loadFromLegacyCode = useReticleBuilderStore(s => s.loadFromLegacyCode);
  const scene    = useReticleBuilderStore(s => s.scene);
  const setScene = useReticleBuilderStore(s => s.setScene);
  const patchOverride  = useReticleBuilderStore(s => s.patchOverride);
  const removeOverride = useReticleBuilderStore(s => s.removeOverride);

  const [gunTab, setGunTab] = useState<'all' | ReticleWeaponGroup>('all');
  const weaponTabLabel = (key: 'all' | ReticleWeaponGroup) =>
    t(`reticles.weaponTab.${key}`, WEAPON_TABS.find(w => w[0] === key)?.[1] ?? '');
  const override = gunTab === 'all' ? null : (spec.weaponOverrides ?? []).find(o => o.weapon === gunTab) ?? null;
  const effective: ReticleSpec = override
    ? { ...spec, ...override, colorMain: override.colorMain || spec.colorMain }
    : spec;
  const setGeom = <K extends keyof ReticleSpec>(key: K, value: ReticleSpec[K]) => {
    if (gunTab === 'all' || !override) set(key, value);
    else patchOverride(gunTab, { [key]: value } as GeomPatch);
  };

  const auth = useSessionStore(s => s.auth);
  const shareUserId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

  const [codeInput, setCodeInput] = useState('');
  const [sharing, setSharing] = useState(false);
  const [loadingCode, setLoadingCode] = useState(false);
  const stageRef = useRef<HTMLDivElement>(null);
  const [gameZoom, setGameZoom] = useState(3);
  useEffect(() => {
    const el = stageRef.current;
    if (!el) return;
    const recalc = () => {
      const boxPx = el.getBoundingClientRect().width;
      if (boxPx > 0) setGameZoom(Math.min(8, Math.max(1, window.screen.width / boxPx)));
    };
    recalc();
    const ro = new ResizeObserver(recalc);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);
  const [zoomMul, setZoomMul] = useState(1);
  const zoom = gameZoom * zoomMul;
  const [toast, setToast] = useState<{ tone: 'success' | 'error' | 'info'; message: string } | null>(null);

  const share = async () => {
    if (spec.code) {
      try { await navigator.clipboard.writeText(spec.code); } catch {  }
      setCodeInput(spec.code);
      setToast({ tone: 'success', message: t('reticles.knkCopied', { defaultValue: 'KNK-код скопирован: {{code}}', code: spec.code }) });
      return;
    }
    if (!shareUserId) {
      setToast({ tone: 'info', message: t('reticles.loginToShare', 'Войди в аккаунт, чтобы поделиться KNK-кодом') });
      return;
    }
    setSharing(true);
    try {
      const code = await bridge.knkShare(shareUserId, spec);
      set('code', code);
      try { await navigator.clipboard.writeText(code); } catch {  }
      setCodeInput(code);
      setToast({ tone: 'success', message: t('reticles.knkCopied', { defaultValue: 'KNK-код скопирован: {{code}}', code }) });
    } catch (e) {
      setToast({ tone: 'error', message: t('reticles.shareCreateFail', { defaultValue: 'Не удалось создать код: {{error}}', error: (e as Error).message }) });
    } finally {
      setSharing(false);
    }
  };

  const importCode = async () => {
    const v = codeInput.trim();
    if (!v) return;
    setLoadingCode(true);
    try {
      const fetched = await bridge.knkFetch(v);
      loadSpec(fetched);
      setToast({ tone: 'success', message: t('reticles.codeAccepted', 'KNK-код принят - параметры загружены') });
    } catch (e) {
      if (loadFromLegacyCode(v)) {
        setToast({ tone: 'success', message: t('reticles.codeAcceptedLegacy', 'Код принят (старый формат) - параметры загружены') });
      } else {
        const m = (e as Error).message ?? '';
        setToast({
          tone: 'error',
          message: m.includes('NOT_FOUND') ? t('reticles.codeNotFound', 'Код не найден')
            : m.includes('EXPIRED') ? t('reticles.codeExpired', 'Код истёк (не использовался 30 дней)')
            : m.includes('WRONG_KIND') ? t('reticles.codeWrongKind', 'Это HNT-код сборки, а не прицела')
            : t('reticles.codeLoadFail', 'Не удалось загрузить код'),
        });
      }
    } finally {
      setLoadingCode(false);
    }
  };
  const doApply = async () => {
    const r = await apply();
    setToast(r.success
      ? { tone: 'success', message: t('reticles.applied', 'Прицел вшит в update.rpf ✓') }
      : { tone: 'error', message: r.error ?? t('reticles.applyFail', 'Не удалось применить') });
  };

  const sceneBg = scene === 'day'
    ? 'linear-gradient(180deg,#9fc7e8 0%,#bcd8ec 34%,#d7e4ea 46%,#c9c3b4 52%,#8f8a7c 66%,#5f5b50 100%)'
    : 'radial-gradient(120% 90% at 50% 120%,rgba(255,61,139,.22),transparent 60%),linear-gradient(180deg,#0a0f1e 0%,#12152b 42%,#1a1330 60%,#0d0a17 100%)';

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 items-start">
      <div className="flex flex-col gap-4">
        <div className={cardCls}>
          <div className="flex items-center gap-2.5 px-4 h-12 border-b border-white/[0.05]">
            <Droplet size={15} className="text-accent" />
            <h3 className="text-[12.5px] font-bold uppercase tracking-[0.13em] text-text-primary">{t('reticles.colorTitle', 'Цвет прицела')}</h3>
          </div>
          <div className="p-4 grid grid-cols-2 gap-2.5">
            <ColorField
              label={gunTab === 'all'
                ? t('reticles.colorTitle', 'Цвет прицела')
                : t('reticles.colorForGroup', { defaultValue: 'Цвет · {{group}}', group: weaponTabLabel(gunTab) })}
              value={effective.colorMain}
              onChange={v => (gunTab === 'all' || !override) ? set('colorMain', v) : patchOverride(gunTab, { colorMain: v })} />
            <ColorField label={t('reticles.colorAds', 'При наведении на врага')} value={spec.colorAds} onChange={v => set('colorAds', v)} />
          </div>
        </div>

        <div className={cardCls}>
          <div className="flex items-center gap-2.5 px-4 h-12 border-b border-white/[0.05]">
            <Target size={15} className="text-accent" />
            <h3 className="text-[12.5px] font-bold uppercase tracking-[0.13em] text-text-primary">{t('reticles.shapeTitle', 'Форма')}</h3>
            <span className="ml-auto text-[10.5px] text-text-muted">{t('reticles.realtime', 'реальное время')}</span>
          </div>

          <div className="px-4 pt-3 flex flex-wrap gap-1.5">
            {WEAPON_TABS.map(([key, label]) => {
              const hasOwn = key !== 'all' && (spec.weaponOverrides ?? []).some(o => o.weapon === key);
              return (
                <button key={key} onClick={() => setGunTab(key)}
                  className={'text-[10.5px] font-bold uppercase tracking-wider rounded-lg px-2.5 py-1.5 transition-colors ' +
                    (gunTab === key
                      ? 'text-accent bg-accent/15'
                      : 'text-text-secondary bg-bg-elevated hover:text-text-primary')}>
                  {t(`reticles.weaponTab.${key}`, label)}{hasOwn && <span className="ml-1 text-accent">●</span>}
                </button>
              );
            })}
          </div>

          {gunTab !== 'all' && (
            <div className="mx-4 mt-2 px-3 py-2 rounded-lg bg-accent/[0.07]
                            text-[10.5px] leading-snug text-text-secondary">
              <Trans
                i18nKey="reticles.complexModeNote"
                defaults="<b>Важно:</b> отдельные прицелы по группам оружия видны в игре только при включённом «<hl>сложном прицеле</hl>» в настройках Majestic. В простом режиме на всех стволах показывается общий прицел (вкладка «Все»)."
                components={{
                  b: <span className="font-bold text-accent uppercase tracking-wider" />,
                  hl: <span className="text-text-primary font-semibold" />,
                }}
              />
            </div>
          )}

          <div className="px-4 pb-2 pt-1">
            {gunTab !== 'all' && (
              <ToggleRow label={t('reticles.groupOverride', 'Отдельный прицел для группы')}
                hint={override
                  ? t('reticles.groupOverrideOnHint', 'своя форма; цвет и масштаб общие')
                  : t('reticles.groupOverrideOffHint', 'выкл = как общий прицел')}
                on={!!override}
                onClick={() => override ? removeOverride(gunTab) : patchOverride(gunTab, {})} />
            )}
            <div style={gunTab !== 'all' && !override
              ? { opacity: 0.4, pointerEvents: 'none' } : undefined}>
              <ToggleRow label={t('reticles.centerDot', 'Центральная точка')} hint="reticleCenterMC"
                on={effective.dot} onClick={() => setGeom('dot', !effective.dot)} />
              <ToggleRow label={t('reticles.outline', 'Обводка')}
                hint={t('reticles.outlineHint', 'чёрный контур для контраста')}
                on={effective.outline} onClick={() => setGeom('outline', !effective.outline)} />
              {gunTab === 'all' && (
                <ToggleRow label={t('reticles.hideReticle', 'Убирать прицел')}
                  hint={t('reticles.hideReticleHint', 'прятать вне прицеливания, как в ванильной GTA')}
                  on={!spec.permanent} onClick={() => set('permanent', !spec.permanent)} />
              )}
              <ToggleRow label={t('reticles.ring', 'Кольцо')}
                hint={t('reticles.ringHint', 'круг вокруг центра (стиль TIFFANY CIRCLE)')}
                on={effective.ring} onClick={() => setGeom('ring', !effective.ring)} />
              <div style={{ opacity: effective.ring ? 1 : 0.4, pointerEvents: effective.ring ? 'auto' : 'none' }}>
                <SliderRow label={t('reticles.ringRadius', 'Радиус кольца')} hint={t('reticles.unitPx', 'px')}
                  min={3} max={40} step={1}
                  value={effective.ringRadius} onChange={v => setGeom('ringRadius', v)} />
                <SliderRow label={t('reticles.ringThickness', 'Толщина кольца')} hint={t('reticles.unitPx', 'px')}
                  min={0.5} max={8} step={0.5}
                  value={effective.ringThickness} onChange={v => setGeom('ringThickness', v)} />
              </div>
              {GEOM_ROWS.map(([key, label, hint, min, max, step]) => (
                <SliderRow key={key}
                  label={t(`reticles.geom.${key}.label`, label)}
                  hint={t(`reticles.geom.${key}.hint`, hint)}
                  min={min} max={max} step={step}
                  value={effective[key] as number} onChange={v => setGeom(key, v as never)} />
              ))}
              {gunTab === 'all' && GLOBAL_ROWS.map(([key, label, hint, min, max, step]) => (
                <SliderRow key={key}
                  label={t(`reticles.geom.${key}.label`, label)}
                  hint={t(`reticles.geom.${key}.hint`, hint)}
                  min={min} max={max} step={step}
                  value={spec[key] as number} onChange={v => set(key, v as never)} />
              ))}
            </div>
          </div>
        </div>

      </div>

      <div className="lg:sticky lg:top-3 flex flex-col gap-4">
        <div className={cardCls + ' overflow-hidden'}>
          <div ref={stageRef} className="relative aspect-video" style={{ background: sceneBg }}>
            <div className="absolute inset-0 pointer-events-none" style={{ boxShadow: 'inset 0 0 120px rgba(0,0,0,.55)' }} />
            <svg viewBox="0 0 1000 563" preserveAspectRatio="xMidYMid meet" className="absolute inset-0 w-full h-full"
              dangerouslySetInnerHTML={{ __html: crosshairSvg(effective, zoom) }} />
            <div className="absolute top-2.5 left-2.5 right-2.5 flex items-center gap-2">
              <span className="text-[10px] font-bold uppercase tracking-[0.14em] text-accent bg-black/50 backdrop-blur-sm rounded-lg px-2 py-1">
                ● {t('reticles.livePreview', 'Live preview')}{gunTab !== 'all' ? ` · ${weaponTabLabel(gunTab)}` : ''}
                {zoomMul === 1
                  ? ` · ${t('reticles.sizeAsInGame', 'размер как в игре')}`
                  : ` · ${t('reticles.zoomLens', { mul: zoomMul, defaultValue: 'лупа ×{{mul}} от игрового' })}`}
              </span>
              <div className="ml-auto flex gap-1.5">
                {([1, 2, 3] as const).map(m => (
                  <StageBtn key={m} active={zoomMul === m} onClick={() => setZoomMul(m)}>
                    {m === 1 ? t('reticles.zoomInGame', 'В игре') : `×${m}`}
                  </StageBtn>
                ))}
                <StageBtn active={false} onClick={() => setScene(scene === 'day' ? 'night' : 'day')}>
                  {scene === 'day' ? t('reticles.sceneDay', 'День') : t('reticles.sceneNight', 'Ночь')}
                </StageBtn>
              </div>
            </div>
          </div>
        </div>

        <div className={cardCls}>
          <div className="flex items-center gap-2.5 px-4 h-12 border-b border-white/[0.05]">
            <Share2 size={15} className="text-accent" />
            <h3 className="text-[12.5px] font-bold uppercase tracking-[0.13em] text-text-primary">{t('reticles.applyTitle', 'Применить настройки')}</h3>
          </div>
          <div className="p-4">
            <div className={capCls + ' mb-2'}>{t('reticles.knkCaption', 'KNK-код прицела · отдельно от HNT')}</div>
            <div className="flex gap-2">
              <input
                value={codeInput}
                onChange={e => setCodeInput(e.target.value)}
                placeholder={t('reticles.knkPlaceholder', 'Введите KNK-код · KNK-XXXX-XXXX')}
                className="flex-1 font-mono text-[13px] tracking-wider text-text-primary bg-bg-elevated border border-white/[0.08] rounded-xl px-3 py-2.5 outline-none focus:border-[color:var(--accent)]"
              />
              <button onClick={() => void importCode()} disabled={loadingCode}
                className="inline-flex items-center gap-2 text-[12px] font-bold uppercase tracking-wider text-text-primary bg-bg-elevated border border-white/[0.08] rounded-xl px-3.5 hover:border-white/[0.18] transition-colors disabled:opacity-50">
                {loadingCode ? <Loader2 size={14} className="animate-spin" /> : <Download size={14} />}{t('reticles.loadCode', 'Загрузить')}
              </button>
            </div>
            <div className="flex gap-2 mt-2.5">
              <button onClick={() => void share()} disabled={sharing}
                className="flex-1 inline-flex items-center justify-center gap-2 text-[12px] font-bold uppercase tracking-wider text-text-primary bg-bg-elevated border border-white/[0.08] rounded-xl py-2.5 hover:border-white/[0.18] transition-colors disabled:opacity-50">
                {sharing ? <Loader2 size={14} className="animate-spin" /> : <Copy size={14} />}{t('reticles.shareCode', 'Поделиться')}
              </button>
              <button onClick={doApply} disabled={applying}
                className="flex-1 inline-flex items-center justify-center gap-2 text-[12px] font-bold uppercase tracking-wider text-text-primary bg-bg-elevated border border-white/[0.08] rounded-xl py-2.5 hover:border-white/[0.18] transition-colors disabled:opacity-50">
                {applying ? <Loader2 size={14} className="animate-spin" /> : <Check size={14} />}{t('reticles.buildAction', 'Собрать')}
              </button>
            </div>
            <button onClick={doApply} disabled={applying}
              className="w-full mt-3 inline-flex items-center justify-center gap-2 h-11 rounded-xl
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/75 hover:border-white/[0.18]
                         disabled:opacity-50 disabled:cursor-wait transition-colors
                         text-sm font-bold uppercase tracking-wider"
              style={{ outline: 'none' }}>
              {applying ? <Loader2 size={15} className="animate-spin" /> : <Check size={15} strokeWidth={2.6} />}
              {applying ? t('reticles.applying', 'Собираю…') : t('reticles.applyToGame', 'Применить на игру')}
            </button>
            <button onClick={reset}
              className="w-full mt-1.5 text-[11px] uppercase tracking-[0.16em] text-text-muted hover:text-text-secondary transition-colors py-2">
              {t('reticles.resetSettings', 'Сбросить настройки')}
            </button>
          </div>
        </div>
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 6000 : 2800}
      />
    </div>
  );
}

function ColorField({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div className="flex items-center gap-2.5 bg-bg-elevated rounded-xl p-2.5">
      <label className="relative w-10 h-10 rounded-lg border border-white/[0.08] overflow-hidden cursor-pointer shrink-0" style={{ background: value }}>
        <input type="color" value={value} onChange={e => onChange(e.target.value)}
          className="absolute -inset-2 w-[200%] h-[200%] opacity-0 cursor-pointer" />
      </label>
      <div className="min-w-0">
        <div className="text-[10px] uppercase tracking-[0.14em] text-text-muted font-bold">{label}</div>
        <div className="font-mono text-[12px] text-text-secondary mt-0.5">{value.toUpperCase()}</div>
      </div>
    </div>
  );
}

function ToggleRow({ label, hint, on, onClick }: { label: string; hint: string; on: boolean; onClick: () => void }) {
  return (
    <div className="flex items-center gap-3 py-2.5 border-b border-white/[0.05] last:border-0">
      <div className="flex-1">
        <div className="text-[12.5px] font-semibold text-text-secondary">{label}</div>
        <div className="text-[10.5px] text-text-muted mt-0.5">{hint}</div>
      </div>
      <button onClick={onClick}
        className={'relative w-10 h-[23px] rounded-full transition-colors shrink-0 ' +
          (on ? 'bg-accent/25' : 'bg-bg-elevated')}>
        <span className={'absolute top-0.5 w-[17px] h-[17px] rounded-full transition-all ' +
          (on ? 'left-[19px] bg-accent' : 'left-0.5 bg-text-muted')} />
      </button>
    </div>
  );
}

function SliderRow({ label, hint, min, max, step, value, onChange }:
  { label: string; hint: string; min: number; max: number; step: number; value: number; onChange: (v: number) => void }) {
  const clamp = (v: number) => Math.min(max, Math.max(min, Number.isFinite(v) ? v : 0));
  return (
    <div className="grid grid-cols-[130px_1fr_58px] items-center gap-3 py-2.5 border-b border-white/[0.05] last:border-0">
      <div className="text-[12.5px] font-semibold text-text-secondary">{label}
        <span className="block text-[10px] text-text-muted font-normal mt-0.5">{hint}</span>
      </div>
      <input type="range" min={min} max={max} step={step} value={value}
        onChange={e => onChange(clamp(parseFloat(e.target.value)))}
        style={{ accentColor: 'var(--accent)' }} className="w-full cursor-pointer" />
      <input type="text" inputMode="decimal" value={value}
        onChange={e => onChange(clamp(parseFloat(e.target.value)))}
        className="font-mono text-[13px] font-semibold text-text-primary bg-bg-elevated border border-transparent rounded-lg px-2 py-1.5 w-full text-center outline-none focus:border-[color:var(--accent)]" />
    </div>
  );
}

function StageBtn({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button onClick={onClick}
      className={'text-[10.5px] font-bold uppercase tracking-wider backdrop-blur-sm rounded-lg px-2.5 py-1.5 transition-colors ' +
        (active ? 'text-accent bg-accent/15' : 'text-text-secondary bg-black/50 hover:text-text-primary')}>
      {children}
    </button>
  );
}
