import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FolderCog, FolderOpen, RotateCcw, Loader2, CheckCircle2, AlertTriangle } from 'lucide-react';
import { SettingsSection } from './SettingsSection';
import { SettingsRow } from './SettingsRow';
import { bridge } from '@/bridge';
import type { GtaPathInfo } from '@/bridge/types';

export function GtaPathSection({
  onToast,
}: {
  onToast?: (t: { tone: 'success' | 'error'; message: string }) => void;
}) {
  const { t } = useTranslation();
  const [info, setInfo] = useState<GtaPathInfo | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = async () => {
    try { setInfo(await bridge.getGtaPathInfo()); }
    catch (e) { console.warn('[settings.gtaPath] info fail', e); }
  };
  useEffect(() => { void reload(); }, []);

  const pick = async () => {
    if (busy) return;
    setBusy(true);
    try {
      const p = await bridge.openFolderDialog();
      if (!p) return;
      const ok = await bridge.setGtaPathOverride(p);
      if (ok) {
        await reload();
        onToast?.({ tone: 'success', message: t('settings.gtaPath.saved', 'Папка GTA изменена - установки пойдут сюда.') });
      } else {
        onToast?.({ tone: 'error', message: t('settings.gtaPath.invalid', 'В выбранной папке нет GTA5.exe. Укажи корень GTA 5.') });
      }
    } catch (e) {
      onToast?.({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusy(false);
    }
  };

  const reset = async () => {
    if (busy) return;
    setBusy(true);
    try {
      await bridge.clearGtaPathOverride();
      await reload();
      onToast?.({ tone: 'success', message: t('settings.gtaPath.resetDone', 'Сброшено - снова определяется автоматически.') });
    } catch (e) {
      onToast?.({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally {
      setBusy(false);
    }
  };

  const resolved = info?.resolvedPath || '';

  return (
    <SettingsSection
      icon={FolderCog}
      title={t('settings.gtaPath.title', 'Папка GTA 5')}
      description={t('settings.gtaPath.description', 'Куда лаунчер ставит моды. Если у тебя несколько копий (Steam / Epic) - выбери нужную вручную.')}
    >
      <SettingsRow
        label={
          info?.overrideActive
            ? t('settings.gtaPath.currentManual', 'Текущая папка (выбрана вручную)')
            : t('settings.gtaPath.currentAuto', 'Текущая папка (определена автоматически)')
        }
        description={resolved || t('settings.gtaPath.notFound', 'GTA не найдена - укажи папку вручную.')}
        control={
          <div className="flex items-center gap-2.5">
            {info && (info.valid
              ? <CheckCircle2 size={15} className="text-status-success shrink-0" />
              : <AlertTriangle size={15} className="text-status-error shrink-0" />)}
            <button
              type="button"
              onClick={pick}
              disabled={busy}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                         bg-white/[0.04] text-text-primary border border-white/[0.08]
                         hover:bg-white/[0.08] hover:border-white/[0.18]
                         disabled:opacity-50 disabled:cursor-wait
                         transition-colors text-sm font-bold uppercase tracking-wider"
              style={{ outline: 'none' }}
            >
              {busy ? <Loader2 size={14} className="animate-spin" /> : <FolderOpen size={14} />}
              <span>{t('settings.gtaPath.pick', 'Выбрать')}</span>
            </button>
          </div>
        }
      />
      {info?.overrideActive && (
        <SettingsRow
          label={t('settings.gtaPath.resetLabel', 'Авто-определение')}
          description={t('settings.gtaPath.autoHint', 'Без ручного выбора нашлось бы: {{path}}', { path: info.autoDetectedPath || '-' })}
          control={
            <button
              type="button"
              onClick={reset}
              disabled={busy}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                         bg-white/[0.04] text-text-secondary border border-white/[0.08]
                         hover:bg-white/[0.08] hover:text-text-primary hover:border-white/[0.18]
                         disabled:opacity-50 disabled:cursor-wait
                         transition-colors text-sm font-bold uppercase tracking-wider"
              style={{ outline: 'none' }}
            >
              <RotateCcw size={14} />
              <span>{t('settings.gtaPath.reset', 'Сбросить на авто')}</span>
            </button>
          }
        />
      )}
    </SettingsSection>
  );
}
