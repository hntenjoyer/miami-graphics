import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { useManualSettingsStore } from '@/store/manualSettingsStore';
import type {
  GtaDisplaySettings, GtaQualitySettings, GtaAntiAliasingSettings,
  GtaWorldSettings, GtaAdvancedSettings,
} from '@/bridge/types';
import { Field, Toggle, OptionPills, Select, Slider, NumberInput } from './ManualControls';

const QUALITY_BASIC = [
  { value: 0, label: 'Normal',    key: 'settings.gfx.normal' },
  { value: 1, label: 'High',      key: 'settings.gfx.high' },
  { value: 2, label: 'Very High', key: 'settings.gfx.veryHigh' },
] as const;

const SHADOW_QUALITY = [
  { value: 0, label: 'Off',       key: 'settings.gfx.off' },
  { value: 1, label: 'Low',       key: 'settings.gfx.low' },
  { value: 2, label: 'Normal',    key: 'settings.gfx.normal' },
  { value: 3, label: 'High',      key: 'settings.gfx.high' },
  { value: 4, label: 'Very High', key: 'settings.gfx.veryHigh' },
] as const;

const POSTFX = [
  { value: 0, label: 'Off',    key: 'settings.gfx.off' },
  { value: 1, label: 'Normal', key: 'settings.gfx.normal' },
  { value: 2, label: 'High',   key: 'settings.gfx.high' },
  { value: 3, label: 'Ultra',  key: 'settings.gfx.ultra' },
] as const;

const ASPECT = [
  { value: 0, label: 'Auto', key: 'settings.gfx.auto' },
  { value: 1, label: '3:2' },
  { value: 2, label: '4:3' },
  { value: 3, label: '5:4' },
  { value: 4, label: '16:10' },
  { value: 5, label: '16:9' },
  { value: 6, label: '17:9' },
  { value: 7, label: '21:9' },
  { value: 8, label: '32:9' },
  { value: 9, label: '48:9' },
] as const;

const WINDOWED = [
  { value: 0, label: 'Полный экран', key: 'settings.gfx.fullscreen' },
  { value: 1, label: 'Без рамки',    key: 'settings.gfx.borderless' },
  { value: 2, label: 'Окно',         key: 'settings.gfx.windowed' },
] as const;

const REFRESH_RATES = [60, 75, 100, 120, 144, 165, 180, 240, 300, 360].map(n => ({ value: n, label: `${n} Hz` }));

const RESOLUTIONS = [
  { value: '1280x720',  label: '1280 × 720' },
  { value: '1440x1080', label: '1440 × 1080 (CS-style)' },
  { value: '1600x900',  label: '1600 × 900' },
  { value: '1728x1080', label: '1728 × 1080' },
  { value: '1920x1080', label: '1920 × 1080 (FHD)' },
  { value: '2560x1080', label: '2560 × 1080 (UWFHD)' },
  { value: '2560x1440', label: '2560 × 1440 (QHD)' },
  { value: '3440x1440', label: '3440 × 1440 (UWQHD)' },
  { value: '3840x2160', label: '3840 × 2160 (4K)' },
];

const MSAA = [
  { value: 0, label: 'Off', key: 'settings.gfx.off' },
  { value: 2, label: '2×' },
  { value: 4, label: '4×' },
  { value: 8, label: '8×' },
] as const;

const ANISO = [
  { value: 0,  label: 'Off', key: 'settings.gfx.off' },
  { value: 2,  label: '2×' },
  { value: 4,  label: '4×' },
  { value: 8,  label: '8×' },
  { value: 16, label: '16×' },
] as const;

const TESSELLATION = [
  { value: 0, label: 'Off',    key: 'settings.gfx.off' },
  { value: 1, label: 'Low',    key: 'settings.gfx.low' },
  { value: 2, label: 'Normal', key: 'settings.gfx.normal' },
  { value: 3, label: 'High',   key: 'settings.gfx.high' },
] as const;

const SSAO = [
  { value: 0, label: 'Off',    key: 'settings.gfx.off' },
  { value: 1, label: 'Normal', key: 'settings.gfx.normal' },
  { value: 2, label: 'High',   key: 'settings.gfx.high' },
] as const;

const REFLECTION = [
  { value: 0, label: 'Off',       key: 'settings.gfx.off' },
  { value: 1, label: 'Low',       key: 'settings.gfx.low' },
  { value: 2, label: 'Normal',    key: 'settings.gfx.normal' },
  { value: 3, label: 'High',      key: 'settings.gfx.high' },
  { value: 4, label: 'Very High', key: 'settings.gfx.veryHigh' },
] as const;

const GRASS = [
  { value: 0, label: 'Normal',    key: 'settings.gfx.normal' },
  { value: 1, label: 'High',      key: 'settings.gfx.high' },
  { value: 2, label: 'Very High', key: 'settings.gfx.veryHigh' },
] as const;

const SHADOW_SOFT = [
  { value: 0, label: 'Sharp',   key: 'settings.gfx.sharp' },
  { value: 1, label: 'Soft',    key: 'settings.gfx.soft' },
  { value: 2, label: 'Softer',  key: 'settings.gfx.softer' },
  { value: 3, label: 'Softest', key: 'settings.gfx.softest' },
  { value: 4, label: 'AMD' },
] as const;

const DX_VERSION = [
  { value: 2, label: 'DX 10' },
  { value: 3, label: 'DX 11' },
] as const;

const QUALITY_WITH_BELOW = [
  { value: -1, label: 'Минимум', key: 'settings.gfx.minimum' },
  ...QUALITY_BASIC,
] as const;

function trOpts<T extends string | number>(
  t: TFunction,
  options: ReadonlyArray<{ readonly value: T; readonly label: string; readonly key?: string }>,
): Array<{ value: T; label: string }> {
  return options.map(o => ({ value: o.value, label: o.key ? t(o.key, o.label) : o.label }));
}

function SectionBody({ title, hint, children }: {
  title: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <header className="mb-2">
        <h2 className="text-[22px] font-semibold tracking-tight text-text-primary">
          {title}
        </h2>
        {hint && (
          <p className="mt-1 text-[13px] text-text-muted leading-relaxed max-w-lg">
            {hint}
          </p>
        )}
      </header>
      <div className="mt-4">{children}</div>
    </div>
  );
}

export function DisplaySection() {
  const { t } = useTranslation();
  const draft = useManualSettingsStore(s => s.draft?.display);
  const patch = useManualSettingsStore(s => s.patch);
  if (!draft) return null;
  const set = (p: Partial<GtaDisplaySettings>) => patch('display', p);

  const resolutionKey = `${draft.screenWidth}x${draft.screenHeight}`;
  const isCustomResolution = !RESOLUTIONS.some(r => r.value === resolutionKey);

  return (
    <SectionBody
      title={t('settings.manual.display.title', 'Display')}
      hint={t('settings.manual.display.hint', 'Разрешение, частота кадров, режим окна. На FPS-прирост влияют только разрешение (если ниже 1920×1080) и VSync - refresh/aspect/window-mode идут в settings.xml для удобства, но «Прирост»-бар не сдвигают.')}
    >
      <Field
        label={t('settings.manual.resolution.label', 'Разрешение')}
        hint={t('settings.manual.resolution.hint', 'Выбери из стандартных или введи custom width/height ниже. Меньше пикселей - больше FPS на GPU-bound; на CPU-bound сервере эффект умеренный.')}
        category="display" fieldKey="screenWidth"
      >
        <div className="w-full space-y-2">
          <Select
            options={isCustomResolution
              ? [...RESOLUTIONS, {
                  value: resolutionKey,
                  label: t('settings.manual.customResolutionOption', {
                    defaultValue: '{{width}} × {{height}} (custom)',
                    width: draft.screenWidth,
                    height: draft.screenHeight,
                  }),
                }]
              : RESOLUTIONS}
            value={resolutionKey}
            onChange={(next) => {
              const [w, h] = next.split('x').map(Number);
              if (Number.isFinite(w) && Number.isFinite(h)) set({ screenWidth: w, screenHeight: h });
            }}
          />
          {}
          <div className="grid grid-cols-2 gap-2">
            <NumberInput value={draft.screenWidth}  onChange={n => set({ screenWidth: n })}  min={640} suffix={t('settings.manual.widthShort', 'W')} />
            <NumberInput value={draft.screenHeight} onChange={n => set({ screenHeight: n })} min={480} suffix={t('settings.manual.heightShort', 'H')} />
          </div>
        </div>
      </Field>
      <Field label={t('settings.manual.refreshRate.label', 'Refresh Rate')} category="display" fieldKey="refreshRate">
        <Select
          options={[
            ...REFRESH_RATES,
            ...(REFRESH_RATES.some(r => r.value === draft.refreshRate)
              ? []
              : [{
                  value: draft.refreshRate,
                  label: t('settings.manual.customRefreshOption', {
                    defaultValue: '{{rate}} Hz (custom)',
                    rate: draft.refreshRate,
                  }),
                }]),
          ]}
          value={draft.refreshRate}
          onChange={n => set({ refreshRate: n })}
          type="number"
        />
      </Field>
      <Field label={t('settings.manual.aspectRatio.label', 'Aspect Ratio')} category="display" fieldKey="aspectRatio">
        <Select options={trOpts(t, ASPECT)} value={draft.aspectRatio} onChange={n => set({ aspectRatio: n })} type="number" />
      </Field>
      <Field label={t('settings.manual.windowMode.label', 'Window Mode')} category="display" fieldKey="windowed">
        <Select options={trOpts(t, WINDOWED)} value={draft.windowed} onChange={n => set({ windowed: n })} type="number" />
      </Field>
      <Field
        label="VSync"
        hint={t('settings.manual.vsync.hint', 'Выкл = больше FPS и ниже инпут-лаг. Вкл может срезать тиринг, но на CPU-bound сервере не нужен.')}
        category="display" fieldKey="vSync"
      >
        <Toggle checked={draft.vSync} onChange={v => set({ vSync: v })} ariaLabel="VSync" />
      </Field>
    </SectionBody>
  );
}

export function QualitySection() {
  const { t } = useTranslation();
  const draft = useManualSettingsStore(s => s.draft?.quality);
  const patch = useManualSettingsStore(s => s.patch);
  if (!draft) return null;
  const set = (p: Partial<GtaQualitySettings>) => patch('quality', p);

  return (
    <SectionBody
      title={t('settings.manual.quality.title', 'Quality')}
      hint={t('settings.manual.quality.hint', 'Качество текстур, шейдеров, воды и эффектов. Тени отдельным контролом - самый заметный CPU-impact.')}
    >
      <Field
        label={t('settings.manual.textureQuality.label', 'Texture Quality')}
        hint={t('settings.manual.textureQuality.hint', 'Влияет на VRAM. На современных видяхах оставляй High - стоит 0.5% FPS.')}
        category="quality" fieldKey="textureQuality"
      >
        <OptionPills options={trOpts(t, QUALITY_BASIC)} value={draft.textureQuality} onChange={n => set({ textureQuality: n })} />
      </Field>
      <Field label={t('settings.manual.shaderQuality.label', 'Shader Quality')} category="quality" fieldKey="shaderQuality">
        <OptionPills options={trOpts(t, QUALITY_BASIC)} value={draft.shaderQuality} onChange={n => set({ shaderQuality: n })} />
      </Field>
      <Field
        label={t('settings.manual.waterQuality.label', 'Water Quality')}
        hint={t('settings.manual.waterQuality.hint', '«Минимум» = -1 в XML (ниже UI-минимума, бесплатные FPS).')}
        category="quality" fieldKey="waterQuality"
      >
        <OptionPills options={trOpts(t, QUALITY_WITH_BELOW)} value={draft.waterQuality} onChange={n => set({ waterQuality: n })} />
      </Field>
      <Field
        label={t('settings.manual.particleQuality.label', 'Particle Quality')}
        hint={t('settings.manual.particleQuality.hint', '«Минимум» = -1. Снижает дым/искры/взрывы.')}
        category="quality" fieldKey="particleQuality"
      >
        <OptionPills options={trOpts(t, QUALITY_WITH_BELOW)} value={draft.particleQuality} onChange={n => set({ particleQuality: n })} />
      </Field>
      <Field
        label={t('settings.manual.postFx.label', 'Post FX')}
        hint={t('settings.manual.postFx.hint', 'Bloom, exposure, tone-mapping. На сервере не критично.')}
        category="quality" fieldKey="postFx"
      >
        <OptionPills options={trOpts(t, POSTFX)} value={draft.postFx} onChange={n => set({ postFx: n })} />
      </Field>
      <Field
        label={t('settings.manual.shadowQuality.label', 'Shadow Quality')}
        hint={t('settings.manual.shadowQuality.hint', 'Главный shadow-knob. Off действительно убирает тени (при условии Shadow_*_Enabled = false в Advanced).')}
        category="quality" fieldKey="shadowQuality"
      >
        <OptionPills options={trOpts(t, SHADOW_QUALITY)} value={draft.shadowQuality} onChange={n => set({ shadowQuality: n })} />
      </Field>
    </SectionBody>
  );
}

export function AntiAliasingSection() {
  const { t } = useTranslation();
  const draft = useManualSettingsStore(s => s.draft?.antiAliasing);
  const patch = useManualSettingsStore(s => s.patch);
  if (!draft) return null;
  const set = (p: Partial<GtaAntiAliasingSettings>) => patch('antiAliasing', p);

  return (
    <SectionBody
      title={t('settings.manual.antiAliasing.title', 'Anti-Aliasing')}
      hint={t('settings.manual.antiAliasing.hint', 'MSAA - самое дорогое; на CPU-bound сервере проще держать в Off + FXAA для лестниц.')}
    >
      <Field
        label="FXAA"
        hint={t('settings.manual.fxaa.hint', 'Дешёвый постобработочный AA. ~0.3% FPS, лестницы становятся мягче.')}
        category="antiAliasing" fieldKey="fxaa"
      >
        <Toggle checked={draft.fxaa} onChange={v => set({ fxaa: v })} ariaLabel="FXAA" />
      </Field>
      <Field
        label="TXAA"
        hint={t('settings.manual.txaa.hint', 'Temporal AA. Дороже FXAA, картинка чуть размыта.')}
        category="antiAliasing" fieldKey="txaa"
      >
        <Toggle checked={draft.txaa} onChange={v => set({ txaa: v })} ariaLabel="TXAA" />
      </Field>
      <Field
        label="MSAA"
        hint={t('settings.manual.msaa.hint', 'Multi-Sample AA. Off на турнирных конфигах.')}
        category="antiAliasing" fieldKey="msaa"
      >
        <OptionPills options={trOpts(t, MSAA)} value={draft.msaa} onChange={n => set({ msaa: n })} />
      </Field>
      <Field
        label={t('settings.manual.reflectionMsaa.label', 'Reflection MSAA')}
        hint={t('settings.manual.reflectionMsaa.hint', 'MSAA внутри отражений. Хочешь FPS - Off.')}
        category="antiAliasing" fieldKey="reflectionMsaa"
      >
        <OptionPills options={trOpts(t, MSAA)} value={draft.reflectionMsaa} onChange={n => set({ reflectionMsaa: n })} />
      </Field>
    </SectionBody>
  );
}

export function WorldSection() {
  const { t } = useTranslation();
  const draft = useManualSettingsStore(s => s.draft?.world);
  const patch = useManualSettingsStore(s => s.patch);
  if (!draft) return null;
  const set = (p: Partial<GtaWorldSettings>) => patch('world', p);

  return (
    <SectionBody
      title={t('settings.manual.world.title', 'World')}
      hint={t('settings.manual.world.hint', 'CPU-критичные настройки - самый большой прирост. Population Density можно загнать в 0 - UI этого не даёт, а XML принимает.')}
    >
      <Field
        label={t('settings.manual.populationDensity.label', 'Population Density')}
        hint={t('settings.manual.populationDensity.hint', '0.0 = пустой город (макс CPU-выигрыш). 1.0 = ванильная толпа.')}
        category="world" fieldKey="cityDensity"
      >
        <Slider value={draft.cityDensity} min={0} max={1} step={0.05}
                onChange={n => set({ cityDensity: n })}
                ticks={[{ value: 0, label: '0' }, { value: 0.5, label: '0.5' }, { value: 1, label: '1.0' }]} />
      </Field>
      <Field
        label={t('settings.manual.pedVariety.label', 'Ped Variety')}
        hint={t('settings.manual.pedVariety.hint', 'Разнообразие моделей пешеходов. 0.0 экономит CPU + VRAM.')}
        category="world" fieldKey="pedVariety"
      >
        <Slider value={draft.pedVariety} min={0} max={1} step={0.05}
                onChange={n => set({ pedVariety: n })} />
      </Field>
      <Field
        label={t('settings.manual.vehicleVariety.label', 'Vehicle Variety')}
        hint={t('settings.manual.vehicleVariety.hint', 'Разнообразие машин. 0.0 = только базовые.')}
        category="world" fieldKey="vehicleVariety"
      >
        <Slider value={draft.vehicleVariety} min={0} max={1} step={0.05}
                onChange={n => set({ vehicleVariety: n })} />
      </Field>
      <Field
        label={t('settings.manual.lodScale.label', 'LOD Scale')}
        hint={t('settings.manual.lodScale.hint', 'Дальность отрисовки объектов. 0.0 = только ближайшее.')}
        category="world" fieldKey="lodScale"
      >
        <Slider value={draft.lodScale} min={0} max={1} step={0.05}
                onChange={n => set({ lodScale: n })} />
      </Field>
      <Field
        label={t('settings.manual.vehicleLodBias.label', 'Vehicle LOD Bias')}
        hint={t('settings.manual.vehicleLodBias.hint', '-0.5 - бесплатные FPS на pop-in машинах. Ванильный = 1.0.')}
        category="world" fieldKey="vehicleLodBias"
      >
        <Slider value={draft.vehicleLodBias} min={-0.5} max={1} step={0.05}
                onChange={n => set({ vehicleLodBias: n })}
                ticks={[{ value: -0.5, label: '-0.5' }, { value: 0, label: '0' }, { value: 1, label: '1' }]} />
      </Field>
      <Field label={t('settings.manual.pedLodBias.label', 'Ped LOD Bias')} category="world" fieldKey="pedLodBias">
        <Slider value={draft.pedLodBias} min={0} max={1} step={0.05}
                onChange={n => set({ pedLodBias: n })} />
      </Field>
      <Field label={t('settings.manual.grassQuality.label', 'Grass Quality')} category="world" fieldKey="grassQuality">
        <OptionPills options={trOpts(t, GRASS)} value={draft.grassQuality} onChange={n => set({ grassQuality: n })} />
      </Field>
      <Field
        label={t('settings.manual.reflectionQuality.label', 'Reflection Quality')}
        hint={t('settings.manual.reflectionQuality.hint', 'Качество отражений в окнах/воде. Дорогая на GPU.')}
        category="world" fieldKey="reflectionQuality"
      >
        <OptionPills options={trOpts(t, REFLECTION)} value={draft.reflectionQuality} onChange={n => set({ reflectionQuality: n })} />
      </Field>
      <Field
        label={t('settings.manual.shadowDistance.label', 'Shadow Distance')}
        hint={t('settings.manual.shadowDistance.hint', '0.0 - отрубает каскадные тени дальних объектов.')}
        category="world" fieldKey="shadowDistance"
      >
        <Slider value={draft.shadowDistance} min={0} max={1} step={0.05}
                onChange={n => set({ shadowDistance: n })} />
      </Field>
      <Field
        label={t('settings.manual.maxLodScale.label', 'Max LOD Scale')}
        hint={t('settings.manual.maxLodScale.hint', 'Ваниль держит 0.0. Поднимать - тянуть дальние модели, FPS от этого только падает.')}
        category="world" fieldKey="maxLodScale"
      >
        <Slider value={draft.maxLodScale} min={0} max={1} step={0.05}
                onChange={n => set({ maxLodScale: n })} />
      </Field>
    </SectionBody>
  );
}

export function AdvancedSection() {
  const { t } = useTranslation();
  const draft = useManualSettingsStore(s => s.draft?.advanced);
  const patch = useManualSettingsStore(s => s.patch);
  if (!draft) return null;
  const set = (p: Partial<GtaAdvancedSettings>) => patch('advanced', p);

  return (
    <SectionBody
      title={t('settings.manual.advanced.title', 'Advanced')}
      hint={t('settings.manual.advanced.hint', 'Тонкие крутилки. Большинство - bool-флаги связанные с тенями: при Shadow Quality = Off обязательно отключи и эти, иначе тени остаются.')}
    >
      <Field label={t('settings.manual.tessellation.label', 'Tessellation')} category="advanced" fieldKey="tessellation">
        <OptionPills options={trOpts(t, TESSELLATION)} value={draft.tessellation} onChange={n => set({ tessellation: n })} />
      </Field>
      <Field
        label={t('settings.manual.anisotropicFiltering.label', 'Anisotropic Filtering')}
        hint={t('settings.manual.anisotropicFiltering.hint', 'Чёткость текстур под углом. Off экономит ~0.5% FPS.')}
        category="advanced" fieldKey="anisotropicFiltering"
      >
        <OptionPills options={trOpts(t, ANISO)} value={draft.anisotropicFiltering} onChange={n => set({ anisotropicFiltering: n })} />
      </Field>
      <Field
        label="SSAO"
        hint={t('settings.manual.ssao.hint', 'Ambient occlusion в углах.')}
        category="advanced" fieldKey="ssao"
      >
        <OptionPills options={trOpts(t, SSAO)} value={draft.ssao} onChange={n => set({ ssao: n })} />
      </Field>
      <Field label={t('settings.manual.softShadows.label', 'Soft Shadows')} category="advanced" fieldKey="shadowSoftShadows">
        <OptionPills options={trOpts(t, SHADOW_SOFT)} value={draft.shadowSoftShadows} onChange={n => set({ shadowSoftShadows: n })} />
      </Field>
      <Field
        label={t('settings.manual.shadowSplitZStart.label', 'Shadow Split Z Start')}
        hint={t('settings.manual.shadowSplitZStart.hint', 'Граница ближнего каскада теней. Ваниль 0.93.')}
        category="advanced" fieldKey="shadowSplitZStart"
      >
        <Slider value={draft.shadowSplitZStart} min={0} max={1} step={0.01}
                onChange={n => set({ shadowSplitZStart: n })} />
      </Field>
      <Field
        label={t('settings.manual.shadowSplitZEnd.label', 'Shadow Split Z End')}
        hint={t('settings.manual.shadowSplitZEnd.hint', 'Граница дальнего каскада. Ваниль 0.89. Обе в 0 вместе с Shadow Distance = 0 - это и есть полностью выключенные тени.')}
        category="advanced" fieldKey="shadowSplitZEnd"
      >
        <Slider value={draft.shadowSplitZEnd} min={0} max={1} step={0.01}
                onChange={n => set({ shadowSplitZEnd: n })} />
      </Field>
      <Field
        label={t('settings.manual.ultraShadows.label', 'Ultra Shadows')}
        hint={t('settings.manual.ultraShadows.hint', 'При Shadow Quality = Off этот флаг тоже должен быть Off.')}
        category="advanced" fieldKey="ultraShadows"
      >
        <Toggle checked={draft.ultraShadows} onChange={v => set({ ultraShadows: v })} />
      </Field>
      <Field label={t('settings.manual.shadowParticles.label', 'Shadow Particles')} category="advanced" fieldKey="shadowParticles">
        <Toggle checked={draft.shadowParticles} onChange={v => set({ shadowParticles: v })} />
      </Field>
      <Field label={t('settings.manual.longShadows.label', 'Long Shadows')} category="advanced" fieldKey="shadowLongShadows">
        <Toggle checked={draft.shadowLongShadows} onChange={v => set({ shadowLongShadows: v })} />
      </Field>
      <Field label={t('settings.manual.reflectionMipBlur.label', 'Reflection Mip Blur')} category="advanced" fieldKey="reflectionMipBlur">
        <Toggle checked={draft.reflectionMipBlur} onChange={v => set({ reflectionMipBlur: v })} />
      </Field>
      <Field
        label={t('settings.manual.dxVersion.label', 'DirectX Version')}
        hint={t('settings.manual.dxVersion.hint', 'DX 10 чуть быстрее на слабых CPU, но потеряешь часть эффектов.')}
        category="advanced" fieldKey="dxVersion"
      >
        <Select options={DX_VERSION} value={draft.dxVersion} onChange={n => set({ dxVersion: n })} type="number" />
      </Field>
      <Field label={t('settings.manual.dof.label', 'Depth of Field')} category="advanced" fieldKey="dof">
        <Toggle checked={draft.dof} onChange={v => set({ dof: v })} />
      </Field>
      <Field label={t('settings.manual.hdStreaming.label', 'HD Streaming In Flight')} category="advanced" fieldKey="hdStreaming">
        <Toggle checked={draft.hdStreaming} onChange={v => set({ hdStreaming: v })} />
      </Field>
      <Field
        label={t('settings.manual.motionBlur.label', 'Motion Blur')}
        hint={t('settings.manual.motionBlur.hint', '0.0 = отключено. Большинство играющих держит в нуле.')}
        category="advanced" fieldKey="motionBlur"
      >
        <Slider value={draft.motionBlur} min={0} max={1} step={0.05}
                onChange={n => set({ motionBlur: n })} />
      </Field>
      <Field label={t('settings.manual.fogVolumes.label', 'Fog Volumes')} category="advanced" fieldKey="fogVolumes">
        <Toggle checked={draft.fogVolumes} onChange={v => set({ fogVolumes: v })} />
      </Field>
    </SectionBody>
  );
}
