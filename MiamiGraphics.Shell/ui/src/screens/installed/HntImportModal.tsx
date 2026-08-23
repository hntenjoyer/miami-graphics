import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Loader2, AlertTriangle, Layers, Crosshair, Download, Check, Sliders, Palette, Search, Ticket } from 'lucide-react';
import { Modal } from '@/design';
import { bridge } from '@/bridge';
import { useNavStore } from '@/store/navStore';
import { startHntImport, finishHntImport } from '@/store/installProgressStore';
import { useHntInstallStore, type HntComponentSnap } from '@/store/hntInstallStore';
import { ensureBackupOrGate } from '@/store/installGate';
import { useDirtyConfirmStore } from '@/store/dirtyConfirmStore';
import { useReduxStore } from '@/store/reduxStore';
import { getCachedLibrary } from '@/store/libraryCache';
import { getArmorLibraryCache } from '@/store/armorLibraryCache';
import type { HntCode, HntImportResult, CustomizationDraftBridge, GenericSettingBridge } from '@/bridge/types';

interface Props {
  onAppliedRefresh: () => void;
  onClose: () => void;
}

export function HntImportModal({ onAppliedRefresh, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const [stage, setStage] = useState<'input' | 'preview' | 'applying' | 'done'>('input');
  const [code, setCode] = useState('');
  const [previewData, setPreviewData] = useState<HntCode | null>(null);
  const [result, setResult] = useState<HntImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const requestNavigate = useNavStore(s => s.requestNavigate);

  const onFetchPreview = async () => {
    if (!code.trim()) return;
    setError(null);
    setStage('preview');
    try {
      const r = await bridge.hntCodePreview(code.trim().toUpperCase());
      setPreviewData(r);
    } catch (e) {
      const msg = (e as Error).message;

      if (msg.includes('HNT_CODE_NOT_FOUND')) setError(t('hntImport.errorNotFound'));
      else if (msg.includes('HNT_CODE_EXPIRED')) setError(t('hntImport.errorExpired'));
      else setError(msg);
      setStage('input');
    }
  };

  const onApply = () => {
    if (!previewData) return;

    if (previewData.payload.reduxId) {
      if (!ensureBackupOrGate()) return;
    }

    onClose();

    requestNavigate('downloads');

    const p = previewData.payload;
    const hntCode = previewData.code;
    const totalSteps =
      (p.reduxId ? 1 : 0)
      + (p.reduxId && p.extras?.customizeDraft ? 1 : 0)
      + (p.gunpackId ? 1 : 0)
      + (p.selectedGuns?.length ?? 0)
      + (p.armor?.id ? 1 : 0)
      + (p.minimap?.id ? 1 : 0)
      + (p.reticle?.id ? 1 : 0)
      + (p.sounds?.id ? 1 : 0)
      + (p.bigMap?.id ? 1 : 0);
    startHntImport(hntCode, totalSteps);

    void (async () => {
      try {
        const r = await bridge.hntCodeApply(previewData.payload);

        if (!r.success && r.errorMessage === 'DIRTY_FILES_NEED_CONFIRM') {
          finishHntImport(hntCode, 'error', t(
            'hntImport.dirtyCard',
            'update.rpf изменён вне лаунчера - нужно подтверждение сброса к чистой GTA.',
          ));
          useDirtyConfirmStore.getState().open({
            title: t('hntImport.dirtyTitle', 'Файлы GTA модифицированы'),
            message: t(
              'hntImport.dirtyMessage',
              'В update.rpf уже есть сторонние моды (не наши). Перед установкой по HNT-коду мы восстановим чистый update.rpf из бекапа лаунчера - твои текущие моды в нём пропадут.\n\nПродолжить?',
            ),
            cancelLabel: t('settings.cache.cancel', 'Отмена'),
            actions: [{
              label: t('hntImport.dirtyAction', 'Восстановить и установить'),
              kind: 'danger',
              run: async () => {
                const ok = await bridge.backupRestoreClean();
                if (!ok) {
                  finishHntImport(hntCode, 'error', t(
                    'hntImport.cleanMissing',
                    'Чистый update.rpf не найден в локальном бекапе. Запусти бекап со стартового экрана.',
                  ));
                  return;
                }
                startHntImport(hntCode, totalSteps);
                const r2 = await bridge.hntCodeApply(p);
                finishHntImport(hntCode, r2.success ? 'done' : 'error', r2.errorMessage);
                if (r2.success) onAppliedRefresh();
              },
            }],
          });
          return;
        }

        setResult(r);
        finishHntImport(hntCode, r.success ? 'done' : 'error', r.errorMessage);

        if (r.success) {
          const components: HntComponentSnap[] = [];
          if (p.armor?.id)   components.push({ key: 'armor',   id: p.armor.id,   name: p.armor.name   ?? p.armor.id });
          if (p.minimap?.id) components.push({ key: 'minimap', id: p.minimap.id, name: p.minimap.name ?? p.minimap.id });
          if (p.reticle?.id) components.push({ key: 'reticle', id: p.reticle.id, name: p.reticle.name ?? p.reticle.id });
          if (p.sounds?.id)  components.push({ key: 'sounds',  id: p.sounds.id,  name: p.sounds.name  ?? p.sounds.id });
          if (p.bigMap?.id)  components.push({ key: 'bigMap',  id: p.bigMap.id,  name: p.bigMap.name  ?? p.bigMap.id });
          useHntInstallStore.getState().set({
            code:           hntCode,
            reduxId:        p.reduxId,
            reduxName:      p.reduxName,
            gunpackId:      p.gunpackId,
            gunpackName:    p.gunpackName,
            selgunsCount:   p.selectedGuns?.length ?? 0,
            customizeCount: p.extras?.customizeDraft ? 1 : 0,
            components,
            installedAt:    Date.now(),
          });
        }

        onAppliedRefresh();
      } catch (e) {
        const msg = (e as Error).message ?? t('common.unknownError', 'неизвестная ошибка');
        finishHntImport(hntCode, 'error', msg);
      }
    })();
  };

  const payload = previewData?.payload ?? null;
  const hasRedux   = !!payload?.reduxId;
  const hasGunpack = !!payload?.gunpackId;
  const gunCount   = payload?.selectedGuns.length ?? 0;

  const reduxItems = useReduxStore(s => s.items);
  const reduxNameOf = (id: string) =>
    reduxItems.find(r => r.id === id)?.name
      ?? id.replace(/[_-]+/g, ' ').trim().toUpperCase();

  const componentRows = useMemo(() => {
    if (!payload) return [] as ReceiptLineData[];
    const srcLabel = (source: string | undefined) =>
      source === 'redux' ? t('userBuilds.badgeFromRedux', 'из редукса')
        : source === 'library' ? t('hntImport.srcFromCatalog', 'из каталога')
        : null;
    const row = (label: string, ref: { source: string; id: string; name: string | null } | null | undefined): ReceiptLineData | null =>
      ref?.id ? { label, value: `«${ref.name ?? ref.id}»`, note: srcLabel(ref.source) } : null;
    return [
      row(t('userBuilds.component_armor', 'Броня'),  payload.armor),
      row(t('downloads.kind.minimap', 'Миникарта'),  payload.minimap),
      row(t('downloads.kind.reticle', 'Прицел'),     payload.reticle),
      row(t('downloads.kind.sounds', 'Звуки'),       payload.sounds),
      row(t('nav.bigmap', 'Большая карта'),          payload.bigMap),
    ].filter(Boolean) as ReceiptLineData[];
  }, [payload, t]);
  const hasComponents = componentRows.length > 0;

  const hasAnything = hasRedux || hasGunpack || gunCount > 0 || hasComponents;

  const customizeRows = useMemo(() => {
    const draft: CustomizationDraftBridge | undefined = payload?.extras?.customizeDraft;
    if (!draft) return [] as ReceiptLineData[];

    type LibKind = 'minimap' | 'crosshair' | 'tracers' | 'bloodfx' | 'timecycle' | 'armor' | 'arena';
    const libName = (kind: LibKind, id: string) =>
      getCachedLibrary(kind)?.find(x => x.id === id)?.name ?? null;
    const armorLibName = (id: string) =>
      getArmorLibraryCache().find(a => a.id === id)?.name ?? null;

    const sourceOf = (g: GenericSettingBridge, kind: LibKind): string | null => {
      if (g.kind === 'import' && g.donorReduxId)
        return t('hntImport.srcFromReduxNamed', 'из редукса «{{name}}»', { name: reduxNameOf(g.donorReduxId) });
      if (g.kind === 'library' && g.libraryItemId) {
        const n = libName(kind, g.libraryItemId);
        return n
          ? t('hntImport.srcFromLibraryNamed', '«{{name}}» · из библиотеки', { name: n })
          : t('downloads.custo.fromLibrary', 'из библиотеки');
      }
      if (g.kind === 'armorLibrary' && g.armorLibraryId) {
        const n = armorLibName(g.armorLibraryId);
        return n
          ? t('hntImport.srcFromArmorCatalogNamed', '«{{name}}» · из каталога брони', { name: n })
          : t('hntImport.srcFromArmorCatalog', 'из каталога брони');
      }
      if (g.kind === 'clear') return t('downloads.custo.cleared', 'убрано');
      return null;
    };

    const rows: ReceiptLineData[] = [];
    const push = (label: string, g: GenericSettingBridge, kind: LibKind) => {
      const src = sourceOf(g, kind);
      if (src) rows.push({ label, value: src, note: null });
    };
    push(t('hntImport.customBloodfx',   'Кровь'),      draft.bloodfx,   'bloodfx');
    push(t('hntImport.customCrosshair', 'Прицел'),     draft.crosshair, 'crosshair');
    push(t('hntImport.customTimecycle', 'Тайм-цикл'),  draft.timecycle, 'timecycle');
    push(t('hntImport.customArmor',     'Броня'),      draft.armor,     'armor');
    push(t('hntImport.customArena',     'Арена'),      draft.arena,     'arena');

    const m = draft.minimap;
    const mmParts: string[] = [];
    if (m.libraryItemId) {
      const n = libName('minimap', m.libraryItemId);
      mmParts.push(n
        ? t('hntImport.srcFromLibraryNamed', '«{{name}}» · из библиотеки', { name: n })
        : t('downloads.custo.fromLibrary', 'из библиотеки'));
    } else if (m.importedFromReduxId) {
      mmParts.push(t('hntImport.srcFromReduxNamed', 'из редукса «{{name}}»', { name: reduxNameOf(m.importedFromReduxId) }));
    }
    if (m.pngOverlayPath) mmParts.push(t('downloads.custo.ownPng', 'свой PNG'));
    if (m.enabled) {
      const colorsCustom =
        m.hpColor?.toUpperCase()    !== '#34D399' ||
        m.armorColor?.toUpperCase() !== '#60A5FA';
      if (colorsCustom) mmParts.push(t('hntImport.customColors', 'цвета HP/брони ({{hp}} / {{armor}})', { hp: m.hpColor, armor: m.armorColor }));
    }
    if (mmParts.length > 0) {
      rows.push({ label: t('hntImport.customMinimap', 'Миникарта'), value: mmParts.join(' · '), note: null });
    }

    const tr = draft.tracers;
    if (tr.sourceKind !== 'default') {
      const trParts: string[] = [];
      if (tr.sourceKind === 'model' && tr.modelFolderName) trParts.push(t('hntImport.tracerModel', 'модель «{{name}}»', { name: tr.modelFolderName }));
      if (tr.sourceKind === 'import' && tr.donorReduxId)   trParts.push(t('hntImport.srcFromReduxNamed', 'из редукса «{{name}}»', { name: reduxNameOf(tr.donorReduxId) }));
      if (tr.sourceKind === 'model' || tr.overrideColor)   trParts.push(t('hntImport.tracerColor', 'цвет RGB {{r}}, {{g}}, {{b}}', { r: tr.r, g: tr.g, b: tr.b }));
      rows.push({
        label: t('hntImport.customTracers', 'Трассеры'),
        value: trParts.length > 0 ? trParts.join(' · ') : t('hntImport.tracersConfigured', 'настроены'),
        note: null,
      });
    }
    return rows;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [payload, reduxItems, t]);

  const totalPositions =
    (hasRedux ? 1 : 0) + (hasGunpack ? 1 : 0) + gunCount
    + customizeRows.length + componentRows.length;

  return (
    <Modal.Root
      onClose={onClose}
      closeLabel={t('hntImport.close')}
      maxWidthClassName={stage === 'preview' && previewData ? 'max-w-[680px]' : 'max-w-[520px]'}
    >
      <Modal.Header icon={Ticket}>
        <Modal.Title>{t('hntImport.title')}</Modal.Title>
        <Modal.Subtitle>{t('hntImport.subtitle')}</Modal.Subtitle>
      </Modal.Header>

      <Modal.Body>
        {}
        {stage === 'input' && (
          <>
            <div className="flex flex-col gap-2">
              <input
                type="text"
                value={code}
                onChange={e => setCode(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') void onFetchPreview(); }}
                placeholder="HNT-XXXX-XXXX"
                spellCheck={false}
                autoFocus
                className="px-4 py-3 rounded-xl bg-glass border border-glass-border
                           font-mono text-base text-text-primary tabular-nums
                           tracking-[0.12em] uppercase
                           placeholder:text-text-muted/50 placeholder:tracking-[0.12em]
                           focus:outline-none focus:border-accent/60 focus:bg-glass-strong"
              />
              <span className="text-[10px] text-text-muted px-1">
                {t('hntImport.formatHint')}
              </span>
            </div>
            {error && (
              <div className="flex items-start gap-2 text-status-error text-sm">
                <AlertTriangle size={16} className="shrink-0 mt-0.5" />
                <span>{error}</span>
              </div>
            )}
            <button
              type="button"
              onClick={() => void onFetchPreview()}
              disabled={!code.trim()}
              className="self-end inline-flex items-center justify-center gap-2 h-12 px-6 rounded-xl
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/75 hover:border-white/[0.18]
                         disabled:opacity-40 disabled:cursor-not-allowed
                         transition-colors text-sm font-bold uppercase tracking-wider"
              style={{ outline: 'none' }}
            >
              <Search size={16} />
              <span>{t('hntImport.fetchButton')}</span>
            </button>
          </>
        )}

        {}
        {stage === 'preview' && !previewData && (
          <div className="py-10 flex flex-col items-center gap-3 text-text-muted">
            <Loader2 size={24} className="animate-spin" />
            <p className="text-sm">{t('hntImport.fetching')}</p>
          </div>
        )}
        {stage === 'preview' && previewData && payload && (
          <>
            <div className="flex items-center justify-between text-xs text-text-muted">
              <span>
                {t('hntImport.previewMeta', {
                  count: previewData.downloadsCount,
                  date: new Date(previewData.createdAt).toLocaleDateString(i18n.language),
                })}
              </span>
              <span className="font-mono tabular-nums tracking-[0.12em] text-text-secondary">
                {previewData.code}
              </span>
            </div>

            {!hasAnything && (
              <div className="py-6 text-center text-text-muted text-sm">
                {t('hntImport.previewEmpty')}
              </div>
            )}

            {hasAnything && (
              <div className="flex flex-col rounded-xl bg-glass border border-glass-border
                              max-h-[58vh] overflow-y-auto px-5 py-4">
                {hasRedux && (
                  <ReceiptSection icon={<Layers size={12} />} title={t('hntImport.previewRedux', 'Редукс')}>
                    <ReceiptLine
                      label={payload.reduxName ?? payload.reduxId!}
                      value={payload.reduxAuthor
                        ? t('hntImport.authorLine', { author: payload.reduxAuthor, defaultValue: 'автор: {{author}}' })
                        : null}
                      bold
                    />
                  </ReceiptSection>
                )}
                {hasGunpack && (
                  <ReceiptSection icon={<Crosshair size={12} />} title={t('hntImport.previewGunpack', 'Ган-пак')}>
                    <ReceiptLine label={payload.gunpackName ?? payload.gunpackId!} value={null} bold />
                  </ReceiptSection>
                )}
                {gunCount > 0 && (
                  <ReceiptSection
                    icon={<Crosshair size={12} />}
                    title={`${t('hntImport.previewGuns', 'Отдельные ганы')} · ${gunCount}`}
                  >
                    {payload.selectedGuns.map(g => (
                      <ReceiptLine
                        key={`${g.gunpackId}:${g.internalName}`}
                        label={g.displayName}
                        value={g.gunpackName
                          ? t('hntImport.fromPack', { name: g.gunpackName, defaultValue: 'из пака «{{name}}»' })
                          : null}
                      />
                    ))}
                  </ReceiptSection>
                )}
                {customizeRows.length > 0 && (
                  <ReceiptSection
                    icon={<Sliders size={12} />}
                    title={`${t('hntImport.previewCustomize', 'Кастомизация редукса')} · ${customizeRows.length}`}
                  >
                    {customizeRows.map(r => (
                      <ReceiptLine key={r.label + r.value} label={r.label} value={r.value} note={r.note} />
                    ))}
                  </ReceiptSection>
                )}
                {hasComponents && (
                  <ReceiptSection
                    icon={<Palette size={12} />}
                    title={`${t('hntImport.previewComponents', 'Оформление')} · ${componentRows.length}`}
                  >
                    {componentRows.map(r => (
                      <ReceiptLine key={r.label + r.value} label={r.label} value={r.value} note={r.note} />
                    ))}
                  </ReceiptSection>
                )}

                <div className="mt-1.5 pt-2.5 border-t border-dashed border-white/[0.14]
                                flex items-baseline justify-between">
                  <span className="text-[11px] uppercase tracking-[0.18em] text-text-muted font-bold">
                    {t('hntImport.total', 'Итого')}
                  </span>
                  <span className="text-base font-semibold text-text-primary tabular-nums">
                    {t('hntImport.positionsCount', {
                      count: totalPositions,
                      defaultValue_one: '{{count}} позиция',
                      defaultValue_few: '{{count}} позиции',
                      defaultValue_many: '{{count}} позиций',
                      defaultValue: '{{count}} позиций',
                    })}
                  </span>
                </div>
              </div>
            )}

            {error && (
              <div className="flex items-start gap-2 text-status-error text-sm">
                <AlertTriangle size={16} className="shrink-0 mt-0.5" />
                <span>{error}</span>
              </div>
            )}

            <div className="flex items-center justify-between gap-2 pt-1">
              <button
                type="button"
                onClick={() => { setPreviewData(null); setStage('input'); }}
                className="btn-glow btn-glow--ghost btn-glow--sm"
              >
                {t('hntImport.back')}
              </button>
              <button
                type="button"
                onClick={() => void onApply()}
                disabled={!hasAnything}
                className="inline-flex items-center justify-center gap-2 h-12 px-6 rounded-xl
                           bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                           hover:bg-bg-elevated/75 hover:border-white/[0.18]
                           disabled:opacity-40 disabled:cursor-not-allowed
                           transition-colors text-sm font-bold uppercase tracking-wider"
                style={{ outline: 'none' }}
              >
                <Download size={16} />
                <span>{t('hntImport.applyButton')}</span>
              </button>
            </div>
          </>
        )}

        {}
        {stage === 'applying' && (
          <div className="py-10 flex flex-col items-center gap-3 text-text-muted">
            <Loader2 size={24} className="animate-spin" />
            <p className="text-sm">{t('hntImport.applying')}</p>
            <p className="text-xs text-text-muted opacity-60 max-w-[300px] text-center">
              {t('hntImport.applyingHint')}
            </p>
          </div>
        )}

        {}
        {stage === 'done' && result && (
          <>
            <div className="flex items-center gap-2 text-status-success">
              <Check size={18} />
              <p className="text-sm font-semibold">
                {result.success ? t('hntImport.donePerfect') : t('hntImport.donePartial')}
              </p>
            </div>
            <div className="flex flex-col gap-1.5 text-xs">
              <ResultRow label={t('hntImport.previewRedux')}   step={result.reduxStep} />
              <ResultRow label={t('hntImport.previewGunpack')} step={result.gunpackStep} />
              <ResultRow label={t('hntImport.previewGuns')}    step={result.selectedGunsStep} />
              <ResultRow label={t('hntImport.previewComponents', 'Оформление')} step={result.componentsStep ?? null} />
            </div>
            <button
              type="button"
              onClick={onClose}
              className="btn-glow btn-glow--filled self-end"
            >
              {t('hntImport.doneClose')}
            </button>
          </>
        )}
      </Modal.Body>
    </Modal.Root>
  );
}

interface ReceiptLineData {
  label: string;
  value: string | null;
  note?: string | null;
}

function ReceiptSection({
  icon, title, children,
}: {
  icon: React.ReactNode;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div className="py-2.5 border-b border-dashed border-white/[0.10] last:border-b-0">
      <div className="flex items-center gap-2 mb-1.5 text-accent">
        <span className="shrink-0">{icon}</span>
        <span className="text-[11px] uppercase tracking-[0.2em] font-bold">{title}</span>
      </div>
      <div className="flex flex-col gap-1">{children}</div>
    </div>
  );
}

function ReceiptLine({
  label, value, note, bold,
}: ReceiptLineData & { bold?: boolean }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <span className={
        'min-w-0 break-words text-[13.5px] leading-snug ' +
        (bold ? 'font-display font-semibold text-base text-text-primary' : 'text-text-secondary')
      }>
        {label}
      </span>
      {(value || note) && (
        <span className="shrink-0 max-w-[55%] text-right text-[11.5px] text-text-muted leading-snug break-words">
          {value}{value && note ? ' · ' : ''}{note}
        </span>
      )}
    </div>
  );
}

function ResultRow({
  label, step,
}: {
  label: string;
  step: { skipped: boolean; success: boolean; errorMessage: string | null } | null;
}) {
  if (!step || step.skipped) return null;
  return (
    <div className={
      'flex items-center gap-2 px-3 py-1.5 rounded-lg ' +
      (step.success ? 'bg-green-500/10 text-green-300' : 'bg-red-500/10 text-red-300')
    }>
      {step.success ? <Check size={12} /> : <AlertTriangle size={12} />}
      <span className="font-semibold uppercase tracking-wider">{label}</span>
      {step.errorMessage && (
        <span className="text-text-muted truncate flex-1 text-right">{step.errorMessage}</span>
      )}
    </div>
  );
}
