import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { motion } from 'framer-motion';
import { Save, Download, ShieldCheck, Layers } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';
import { bridge } from '@/bridge';
import type { ReduxItem } from '@/bridge/types';
import { useReduxStore } from '@/store/reduxStore';
import { useReduxVersionsStore } from '@/store/reduxVersionsStore';
import {
  useCustomizeStore,
  type CustomizeComponentName,
} from '@/store/customizeStore';
import { GlassPanel, EASE_DEPTH, Glow3DButton, AccentLoader } from '@/design';
import { SlideIntro } from './SlideIntro';

interface Props {
  forComponent: CustomizeComponentName;
  donorReduxId: string;
}

const COMPONENT_LABEL_RU: Record<CustomizeComponentName, string> = {
  minimap:   'минимапы',
  crosshair: 'прицела',
  tracers:   'трейсеров',
  bloodfx:   'эффектов',
  timecycle: 'таймциклов',
  armor:     'броника',
  arena:     'арены',
};

export function ImportReduxPreview({ forComponent, donorReduxId }: Props) {
  const { t } = useTranslation();
  const componentGenitive = t(
    `customize.import.componentGenitive.${forComponent}`,
    COMPONENT_LABEL_RU[forComponent],
  );
  const items                = useReduxStore(s => s.items);
  const openImportPicker     = useCustomizeStore(s => s.openImportPicker);
  const openComponent        = useCustomizeStore(s => s.openComponent);
  const setGeneric           = useCustomizeStore(s => s.setGeneric);
  const setMinimap           = useCustomizeStore(s => s.setMinimap);
  const setTracer            = useCustomizeStore(s => s.setTracer);

  const storeItem = items.find(x => x.id === donorReduxId) ?? null;

  const [fetchedItem, setFetchedItem] = useState<ReduxItem | null>(null);
  const [fetchTried, setFetchTried] = useState(false);
  useEffect(() => {
    if (storeItem) return;
    let cancelled = false;
    setFetchedItem(null);
    setFetchTried(false);
    bridge.reduxList()
      .then(list => { if (!cancelled) setFetchedItem(list.find(x => x.id === donorReduxId) ?? null); })
      .catch(() => { })
      .finally(() => { if (!cancelled) setFetchTried(true); });
    return () => { cancelled = true; };
  }, [storeItem, donorReduxId]);

  const item = storeItem ?? fetchedItem;
  const versionsByRedux = useReduxVersionsStore(s => s.byRedux);
  const loadVersions    = useReduxVersionsStore(s => s.load);

  useEffect(() => {
    if (donorReduxId) void loadVersions(donorReduxId);
  }, [donorReduxId, loadVersions]);

  const versions = useMemo(() => versionsByRedux[donorReduxId] ?? [], [versionsByRedux, donorReduxId]);

  const eligibleVersions = useMemo(() => {
    if (versions.length === 0) return versions;
    return versions.filter(v => v.components?.[forComponent]?.isFound);
  }, [versions, forComponent]);

  const isLegacyDonor = versions.length === 0
    && !!item?.components?.[forComponent]?.isFound;
  const canImport = eligibleVersions.length > 0 || isLegacyDonor;

  const [selectedVersionId, setSelectedVersionId] = useState<string | null>(null);
  useEffect(() => {
    if (selectedVersionId && eligibleVersions.some(v => v.id === selectedVersionId)) return;
    setSelectedVersionId(eligibleVersions.length > 0 ? eligibleVersions[0].id : null);
  }, [eligibleVersions, selectedVersionId]);

  const selectedVersion = useMemo(
    () => eligibleVersions.find(v => v.id === selectedVersionId) ?? null,
    [eligibleVersions, selectedVersionId]);

  if (!item) {
    if (!fetchTried) {
      return (
        <div className="h-full flex items-center justify-center gap-3 text-sm text-text-muted">
          <AccentLoader size={22} />
          <span>{t('customize.import.loading')}</span>
        </div>
      );
    }
    openImportPicker(forComponent);
    return null;
  }

  const showsMajestic = item.supportedServers.includes('majestic');
  const showsGta5rp   = item.supportedServers.includes('gta5rp');
  const sizeLabel     = formatSize(selectedVersion?.patchSizeBytes ?? item.patchSizeBytes, t);

  const componentInfo = selectedVersion?.components?.[forComponent]
                     ?? item.components?.[forComponent]
                     ?? null;

  const onSave = () => {
    const ref = { reduxId: item.id, reduxName: item.name || item.id };
    const versionId    = selectedVersion?.id ?? null;
    const versionLabel = selectedVersion?.label ?? null;
    if (forComponent === 'minimap') {
      setMinimap({ importedFrom: { ...ref, versionId, versionLabel } });
    } else if (forComponent === 'tracers') {
      setTracer({ source: {
        kind: 'import',
        donorReduxId: item.id,
        donorReduxName: ref.reduxName,
        donorVersionId: versionId,
        donorVersionLabel: versionLabel,
      } });
    } else {
      setGeneric(forComponent, {
        kind: 'import',
        donorReduxId: item.id,
        donorReduxName: ref.reduxName,
        donorVersionId: versionId,
        donorVersionLabel: versionLabel,
      });
    }
    openComponent(forComponent);
  };

  return (
    <SlideIntro
      resetKey={`import-preview:${donorReduxId}:${forComponent}`}
      title={t('customize.import.confirmHeading', 'Вы уверены, что хотите экспортировать именно отсюда?')}
      subtitle={t('customize.import.confirmHint', 'Перенесём только {{component}} - остальные компоненты не будут затронуты.', {
        component: componentGenitive,
      })}
    >
      <div className="relative h-full flex flex-col">
        <header className="px-8 pt-6 pb-2 flex items-center gap-4 shrink-0">
          <BackButton onClick={() => openImportPicker(forComponent)} label={t('customize.import.backToPicker')} />
          <div className="flex-1" />
        </header>

        {}
        <div className="flex-1 overflow-y-auto px-8 pb-32 flex items-center justify-center">
          <div className="max-w-[860px] w-full flex flex-col items-center py-6">

            {}
            <motion.div
              initial={{ opacity: 0, y: 12, scale: 0.99 }}
              animate={{ opacity: 1, y: 0,  scale: 1   }}
              transition={{ duration: 0.42, delay: 0.04, ease: EASE_DEPTH }}
              className="w-full"
            >
              <GlassPanel
                depth="z3" tint="ultra" rounded="3xl" highlight edge
                className="overflow-hidden relative border border-white/[0.08] group"
              >
                <span
                  aria-hidden
                  className="absolute top-0 inset-x-0 h-px pointer-events-none z-20
                             bg-gradient-to-r from-transparent via-white/40 to-transparent"
                />
                <span
                  aria-hidden
                  className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl
                             opacity-60 group-hover:opacity-100 transition-opacity duration-500 z-10"
                  style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
                />
                {}
                <div className="relative w-full aspect-[16/8] bg-bg-elevated">
                  {(() => {
                    const componentShot = item.componentScreenshots?.[forComponent];
                    const fallback      = item.galleryUrls?.[0] ?? item.previewUrl;
                    const src = componentShot || fallback;
                    if (!src) return null;
                    return (
                      <img
                        src={src}
                        alt={item.name || item.id}
                        draggable={false}
                        className="absolute inset-0 w-full h-full object-cover object-center select-none"
                      />
                    );
                  })()}
                  <div style={{ height: '32%' }} className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/40 to-transparent pointer-events-none" />

                  {}
                  <div className="absolute top-4 right-4 z-10 flex items-center gap-2">
                    {showsGta5rp && <Badge logo={gta5rpLogo} alt="GTA5RP" />}
                    {showsMajestic && <Badge logo={majesticLogo} alt="Majestic" />}
                  </div>

                  {}
                  <div className="absolute bottom-0 left-0 right-0 p-6 z-10">
                    <h2 className="font-display font-bold text-3xl uppercase tracking-wide text-white leading-tight
                                   drop-shadow-[0_2px_10px_rgba(0,0,0,0.7)]">
                      {item.name || item.id}
                    </h2>
                    <div className="mt-1.5 flex items-center gap-2 text-sm text-white/80 flex-wrap">
                      <span className="truncate">
                        <strong className="text-white">{item.author || '-'}</strong>
                      </span>
                      <span className="text-white/40">·</span>
                      <span className="text-accent font-bold tabular-nums">{sizeLabel}</span>
                      {item.isVerified && (
                        <span className="inline-flex items-center gap-1 text-status-success"
                              title={t('customize.import.verified')}>
                          <ShieldCheck size={13} />
                        </span>
                      )}
                    </div>
                  </div>
                </div>

                {eligibleVersions.length > 1 && (
                  <div className="px-6 py-3 border-t border-glass-border flex items-center gap-3 flex-wrap">
                    <div className="inline-flex items-center gap-1.5 text-[10px] uppercase tracking-[0.22em] text-text-muted">
                      <Layers size={11} />
                      {t('customize.import.versionLabel', 'Версия')}
                    </div>
                    <div className="inline-flex items-center gap-1.5 flex-wrap">
                      {eligibleVersions.map(v => (
                        <button
                          key={v.id}
                          type="button"
                          onClick={() => setSelectedVersionId(v.id)}
                          className={
                            'px-2.5 py-1 rounded-md text-[11px] font-bold uppercase tracking-wider transition-colors ' +
                            (selectedVersionId === v.id
                              ? 'bg-accent-soft text-accent border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]'
                              : 'bg-bg-elevated-soft text-text-secondary border border-transparent hover:text-text-primary hover:border-border-strong')
                          }
                        >
                          {v.label || `v${v.slot}`}
                        </button>
                      ))}
                    </div>
                  </div>
                )}

                {}
                <div className="px-6 py-4 border-t border-glass-border
                                bg-gradient-to-r from-accent-soft via-transparent to-accent-soft/40
                                flex items-center gap-3">
                  <div className="w-10 h-10 rounded-xl bg-accent-soft flex items-center justify-center
                                  text-accent shrink-0">
                    <Download size={16} />
                  </div>
                  <div className="flex flex-col min-w-0 flex-1">
                    <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted">
                      {t('customize.import.willImport')}
                      {selectedVersion?.label && eligibleVersions.length > 1 && (
                        <span className="ml-1 text-accent">· {selectedVersion.label}</span>
                      )}
                    </span>
                    <span className="text-sm font-bold text-text-primary uppercase tracking-wide truncate">
                      {t(`customize.componentNames.${forComponent}`)}
                    </span>
                  </div>
                  {componentInfo?.internalPaths && componentInfo.internalPaths.length > 0 && (
                    <span className="text-[10px] uppercase tracking-wider text-text-muted tabular-nums">
                      {t('customize.import.filesCount', {
                        count: componentInfo.internalPaths.length,
                        defaultValue:      '{{count}} файлов',
                        defaultValue_one:  '{{count}} файл',
                        defaultValue_few:  '{{count}} файла',
                        defaultValue_many: '{{count}} файлов',
                      })}
                    </span>
                  )}
                </div>
              </GlassPanel>
            </motion.div>
          </div>
        </div>

        {}
        <div className="absolute left-0 right-0 bottom-0 px-8 pb-5 pt-4
                        bg-gradient-to-t from-bg-base via-bg-base/95 to-transparent">
          <div className="max-w-[860px] mx-auto">
            <GlassPanel
              depth="z2" tint="ultra" rounded="2xl" highlight edge
              className="relative overflow-hidden border border-white/[0.08] group
                         flex items-center gap-3 p-2"
            >
              <span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none z-20
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
              />
              <span
                aria-hidden
                className="absolute -top-16 -right-12 w-48 h-48 pointer-events-none blur-3xl
                           opacity-60 group-hover:opacity-100 transition-opacity duration-500 z-0"
                style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
              />
              <button
                type="button"
                onClick={() => openImportPicker(forComponent)}
                className="px-6 h-12 rounded-xl text-sm font-semibold text-text-secondary
                           hover:text-text-primary hover:bg-glass
                           transition-colors duration-200 ease-depth"
              >
                {t('customize.import.cancel')}
              </button>
              <Glow3DButton
                variant="secondary"
                size="lg"
                onClick={onSave}
                disabled={!canImport}
                title={!canImport
                  ? t('customize.import.noComponentTitle',
                      'Ни в одной версии этого донора нет компонента «{{component}}»',
                      { component: componentGenitive })
                  : undefined}
                leading={<Save size={16} strokeWidth={2.4} />}
                className="flex-1"
              >
                {canImport
                  ? t('customize.import.save')
                  : t('customize.import.noComponentCta', 'Нет {{component}} в версиях донора',
                      { component: componentGenitive })}
              </Glow3DButton>
            </GlassPanel>
          </div>
        </div>
      </div>
    </SlideIntro>
  );
}

function Badge({ logo, alt }: { logo: string; alt: string }) {
  return (
    <div className="px-1.5 py-1 rounded-md bg-bg-elevated border border-border-subtle flex items-center" title={alt}>
      <img src={logo} alt={alt} className="w-3.5 h-3.5 object-contain" />
    </div>
  );
}

function formatSize(bytes: number, t: TFunction): string {
  if (bytes <= 0) return t('common.unitMb', '{{value}} MB', { value: '0' });
  const mb = bytes / (1024 * 1024);
  if (mb < 1024) return t('common.unitMb', '{{value}} MB', { value: Math.round(mb) });
  return t('common.unitGb', '{{value}} GB', { value: (mb / 1024).toFixed(1) });
}
