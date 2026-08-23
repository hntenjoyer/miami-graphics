import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Copy, Check, Loader2, AlertTriangle, Layers, Crosshair, Box, Palette, Ticket } from 'lucide-react';
import { Modal } from '@/design';
import { bridge } from '@/bridge';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import type { HntCode } from '@/bridge/types';

interface Props {
  userId: string;
  onClose: () => void;
  autoAll?: boolean;
}

export function HntExportModal({ userId, onClose, autoAll }: Props) {
  const { t } = useTranslation();
  const [code, setCode] = useState<HntCode | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [generating, setGenerating] = useState(false);

  const installedReduxId       = useReduxStore(s => s.installedReduxId);
  const installedGunpack       = useGunpackStore(s => s.installedGunpack);
  const installedSelectedGuns  = useGunpackStore(s => s.installedSelectedGuns);
  const hasRedux   = !!installedReduxId;
  const hasGunpack = !!installedGunpack.activeGunpackId;
  const hasGuns    = installedSelectedGuns.length > 0;

  const [includeRedux,        setIncludeRedux]        = useState(hasRedux);
  const [includeGunpack,      setIncludeGunpack]      = useState(hasGunpack);
  const [includeSelectedGuns, setIncludeSelectedGuns] = useState(hasGuns);
  const [includeComponents,   setIncludeComponents]   = useState(true);

  const [components, setComponents] = useState<string[] | null>(null);
  useEffect(() => {
    let alive = true;
    (async () => {
      const [mm, rt, snd, arm, bm] = await Promise.all([
        bridge.getCurrentMinimapInfo().catch(() => null),
        bridge.getCurrentReticleInfo().catch(() => null),
        bridge.getCurrentSoundPackInfo().catch(() => null),
        bridge.getCurrentArmorInfo().catch(() => null),
        bridge.bigMapGetState().catch(() => null),
      ]);
      const names: string[] = [];
      if (mm)  names.push(t('hntExport.comp.minimap', { name: mm.name || mm.id, defaultValue: 'миникарта «{{name}}»' }));
      if (rt)  names.push(t('hntExport.comp.reticle', { name: rt.name || rt.id, defaultValue: 'прицел «{{name}}»' }));
      if (snd) names.push(t('hntExport.comp.sounds',  { name: snd.name || snd.id, defaultValue: 'звуки «{{name}}»' }));
      if (arm && arm.kind !== 'none' && !(arm.kind === 'redux' && arm.id === installedReduxId))
        names.push(t('hntExport.comp.armor', { name: arm.name || arm.id, defaultValue: 'броня «{{name}}»' }));
      if (bm?.enabled && bm.id) names.push(t('hntExport.comp.bigmap', { name: bm.name ?? bm.id, defaultValue: 'большая карта «{{name}}»' }));
      if (alive) setComponents(names);
    })();
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  const componentsLoaded = components !== null;
  const hasComponents    = (components?.length ?? 0) > 0;

  const anySelected = includeRedux || includeGunpack || includeSelectedGuns
    || (includeComponents && hasComponents);

  const canExport = hasRedux;

  const mountedRef = useRef(true);
  useEffect(() => { mountedRef.current = true; return () => { mountedRef.current = false; }; }, []);

  const onGenerate = async () => {
    if (generating || code || !anySelected || !canExport) return;
    setGenerating(true);
    try {
      const r = await bridge.hntCodeExport(userId, {
        includeRedux:        includeRedux        && hasRedux,
        includeGunpack:      includeGunpack      && hasGunpack,
        includeSelectedGuns: includeSelectedGuns && hasGuns,
        includeComponents:   includeComponents   && hasComponents,
      });
      if (mountedRef.current) setCode(r);
    } catch (e) {
      if (mountedRef.current) setError((e as Error).message);
    } finally {
      if (mountedRef.current) setGenerating(false);
    }
  };

  const onCopy = async () => {
    if (!code) return;
    try {
      await navigator.clipboard.writeText(code.code);
      setCopied(true);
    } catch {

      const ta = document.createElement('textarea');
      ta.value = code.code;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand('copy'); setCopied(true); }
      finally { document.body.removeChild(ta); }
    }
  };

  useEffect(() => {
    if (!autoAll || !componentsLoaded || code || error || generating) return;
    if (canExport && anySelected) void onGenerate();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoAll, componentsLoaded]);

  useEffect(() => {
    if (code) void onCopy();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [code]);

  return (
    <Modal.Root onClose={onClose} closeLabel={t('hntExport.close')}>
      <Modal.Header icon={Ticket}>
        <Modal.Title>{t('hntExport.title')}</Modal.Title>
        {code && <Modal.Subtitle>{t('hntExport.subtitle')}</Modal.Subtitle>}
      </Modal.Header>

      <Modal.Body>
        {!code && !error && !canExport && (
          <div className="py-6 flex flex-col gap-4">
            <div className="flex items-start gap-2.5 text-text-secondary">
              <AlertTriangle size={18} className="shrink-0 mt-0.5 text-status-warning" />
              <p className="text-sm leading-relaxed">
                {t('hntExport.noReduxHint', 'HNT-код можно создать только когда установлен редукс. Установи любой редукс из каталога - и сможешь поделиться всей сборкой (редукс, ган-пак, ганы, оформление) одним кодом.')}
              </p>
            </div>
            <button
              type="button"
              onClick={onClose}
              className="self-end inline-flex items-center justify-center gap-2 h-10 px-5 rounded-xl
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/75 hover:border-white/[0.18]
                         transition-colors text-[12px] font-bold uppercase tracking-[0.08em]"
              style={{ outline: 'none' }}
            >
              {t('hntExport.gotIt', 'Понятно')}
            </button>
          </div>
        )}

        {!code && !error && canExport && autoAll && (!componentsLoaded || anySelected) && (
          <div className="py-8 flex flex-col items-center gap-3">
            <Loader2 size={22} className="animate-spin text-accent" />
            <p className="text-sm text-text-secondary">{t('hntExport.generatingAll', 'Генерирую HNT-код со всей сборкой…')}</p>
          </div>
        )}

        {!code && !error && canExport && autoAll && componentsLoaded && !anySelected && (
          <div className="py-6 flex flex-col gap-4">
            <div className="flex items-start gap-2.5 text-text-secondary">
              <AlertTriangle size={18} className="shrink-0 mt-0.5 text-status-warning" />
              <p className="text-sm leading-relaxed">
                {t('hntExport.nothingToShare', 'Сейчас нечем делиться: ничего не установлено. HNT-код включает редукс, ган-пак, отдельные ганы, броню, миникарту, прицел, звуки и большую карту.')}
              </p>
            </div>
            <button
              type="button"
              onClick={onClose}
              className="self-end inline-flex items-center justify-center gap-2 h-10 px-5 rounded-xl
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/75 hover:border-white/[0.18]
                         transition-colors text-[12px] font-bold uppercase tracking-[0.08em]"
              style={{ outline: 'none' }}
            >
              {t('hntExport.gotIt', 'Понятно')}
            </button>
          </div>
        )}

        {!code && !error && canExport && !autoAll && (
          <div className="flex flex-col gap-4">
            <p className="text-sm text-text-secondary leading-relaxed">
              {t('hntExport.pickHint', 'Выберите что попадёт в HNT-код. Получатель сможет установить только отмеченные компоненты одной кнопкой.')}
            </p>
            <div className="flex flex-col gap-2">
              <ExportRow
                icon={<Layers size={16} />}
                label={t('hntExport.row.redux', 'Редукс')}
                sublabel={hasRedux ? null : t('hntExport.row.notInstalled', 'Не установлен')}
                disabled={!hasRedux || generating}
                checked={includeRedux && hasRedux}
                onChange={(v) => setIncludeRedux(v)}
              />
              <ExportRow
                icon={<Crosshair size={16} />}
                label={t('hntExport.row.gunpack', 'Ган-пак')}
                sublabel={hasGunpack
                  ? installedGunpack.activeGunpackName ?? null
                  : t('hntExport.row.notInstalled', 'Не установлен')}
                disabled={!hasGunpack || generating}
                checked={includeGunpack && hasGunpack}
                onChange={(v) => setIncludeGunpack(v)}
              />
              <ExportRow
                icon={<Box size={16} />}
                label={t('hntExport.row.guns', 'Отдельные ганы')}
                sublabel={hasGuns
                  ? t('hntExport.row.gunsCount', { count: installedSelectedGuns.length, defaultValue: '{{count}} шт.' })
                  : t('hntExport.row.noneSelected', 'Нет выбранных')}
                disabled={!hasGuns || generating}
                checked={includeSelectedGuns && hasGuns}
                onChange={(v) => setIncludeSelectedGuns(v)}
              />
              <ExportRow
                icon={<Palette size={16} />}
                label={t('hntExport.row.components', 'Оформление')}
                sublabel={!componentsLoaded
                  ? t('hntExport.row.checking', 'Проверяю…')
                  : hasComponents
                    ? components!.join(', ')
                    : t('hntExport.row.nothingInstalled', 'Ничего не установлено')}
                disabled={!hasComponents || generating}
                checked={includeComponents && hasComponents}
                onChange={(v) => setIncludeComponents(v)}
              />
            </div>
            <button
              type="button"
              onClick={() => void onGenerate()}
              disabled={!anySelected || generating}
              className="self-end inline-flex items-center gap-2 px-4 h-10 rounded-xl
                         bg-accent text-text-on-accent font-semibold text-sm
                         hover:bg-accent-hover shadow-glow-accent
                         disabled:opacity-40 disabled:cursor-not-allowed
                         transition-colors"
              style={{ outline: 'none' }}
            >
              {generating
                ? (<><Loader2 size={14} className="animate-spin" /> {t('hntExport.generatingBtn', 'Генерируем…')}</>)
                : t('hntExport.generateBtn', 'Сгенерировать код')}
            </button>
          </div>
        )}

        {error && (
          <div className="py-6 flex flex-col gap-3">
            <div className="flex items-start gap-2 text-status-error">
              <AlertTriangle size={18} className="shrink-0 mt-0.5" />
              <p className="text-sm">{t('hntExport.failedHint')}</p>
            </div>
            <code className="text-xs px-3 py-2 rounded-lg bg-glass border border-glass-border
                             text-text-secondary font-mono break-all">
              {error}
            </code>
          </div>
        )}

        {code && (
          <>
            <div
              className="flex flex-col items-center gap-2 px-5 py-7 rounded-2xl border border-transparent
                         bg-accent/5 shadow-glow-accent text-center"
            >
              <span className="w-10 h-10 rounded-full flex items-center justify-center
                               bg-accent-soft border border-white/[0.08]">
                {copied ? <Check size={18} className="text-accent" /> : <Copy size={18} className="text-accent" />}
              </span>
              <p className="text-sm font-semibold text-text-primary">
                {copied
                  ? t('hntExport.inClipboard', 'Код уже в вашем буфере обмена')
                  : t('hntExport.copyingToClipboard', 'Копирую код в буфер обмена…')}
              </p>
              <button
                type="button"
                onClick={() => void onCopy()}
                style={{ outline: 'none' }}
                className="mt-1 text-[11px] uppercase tracking-wider text-text-muted
                           hover:text-accent transition-colors"
              >
                {t('hntExport.copy')}
              </button>
            </div>

            <p className="text-xs text-text-muted text-center">{t('hntExport.ttlHint')}</p>

            <button
              type="button"
              onClick={onClose}
              className="self-end inline-flex items-center justify-center gap-2 h-12 px-6 rounded-xl
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/75 hover:border-white/[0.18]
                         transition-colors text-sm font-bold uppercase tracking-wider"
              style={{ outline: 'none' }}
            >
              <span>{t('hntExport.done')}</span>
            </button>
          </>
        )}
      </Modal.Body>
    </Modal.Root>
  );
}

function ExportRow({
  icon, label, sublabel, checked, disabled, onChange,
}: {
  icon: React.ReactNode;
  label: string;
  sublabel?: string | null;
  checked: boolean;
  disabled?: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <button
      type="button"
      onClick={() => { if (!disabled) onChange(!checked); }}
      disabled={disabled}
      className={
        'w-full flex items-center gap-3 px-3.5 h-12 rounded-xl border transition-colors ' +
        (disabled
          ? 'bg-white/[0.02] border-white/[0.04] text-text-muted cursor-not-allowed'
          : checked
            ? 'bg-accent-soft border-accent/40 text-text-primary hover:bg-accent/15'
            : 'bg-white/[0.04] border-white/[0.08] text-text-secondary hover:bg-white/[0.07] hover:border-white/[0.15]')
      }
      style={{ outline: 'none' }}
    >
      <span className={
        'shrink-0 w-5 h-5 rounded-md border flex items-center justify-center transition-colors ' +
        (checked
          ? 'bg-accent border-accent text-text-on-accent'
          : 'bg-transparent border-white/[0.18]')
      }>
        {checked && <Check size={12} strokeWidth={3} />}
      </span>
      <span className="shrink-0">{icon}</span>
      <span className="flex-1 min-w-0 flex flex-col items-start gap-0 text-left">
        <span className="text-sm font-semibold leading-tight truncate w-full">{label}</span>
        {sublabel && (
          <span className="text-[11px] text-text-muted leading-tight truncate w-full">{sublabel}</span>
        )}
      </span>
    </button>
  );
}
