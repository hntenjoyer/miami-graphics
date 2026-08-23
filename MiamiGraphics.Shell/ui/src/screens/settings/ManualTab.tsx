import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Loader2 } from 'lucide-react';
import { EASE_DEPTH } from '@/design';
import { useManualSettingsStore } from '@/store/manualSettingsStore';
import { Toast, type ToastTone } from '@/components/Toast';
import {
  DisplaySection, QualitySection, AntiAliasingSection, WorldSection, AdvancedSection,
} from './ManualSections';

type ManualCategory = 'display' | 'quality' | 'antiAliasing' | 'world' | 'advanced';

const CATEGORIES: ReadonlyArray<{
  id: ManualCategory;
  defaultLabel: string;
}> = [
  { id: 'display',      defaultLabel: 'Display'      },
  { id: 'quality',      defaultLabel: 'Quality'      },
  { id: 'antiAliasing', defaultLabel: 'Anti-Aliasing' },
  { id: 'world',        defaultLabel: 'World'        },
  { id: 'advanced',     defaultLabel: 'Advanced'     },
];

export function ManualTab() {
  const { t } = useTranslation();
  const draft          = useManualSettingsStore(s => s.draft);
  const loading        = useManualSettingsStore(s => s.loading);
  const loadError      = useManualSettingsStore(s => s.loadError);
  const liveGain       = useManualSettingsStore(s => s.liveGainPercent);
  const baselineGain   = useManualSettingsStore(s => s.baselineGainPercent);
  const dirtyCounts    = useManualSettingsStore(s => s.dirtyCountByCategory);
  const dirtyTotal     = useManualSettingsStore(s => s.dirtyTotal);
  const applying       = useManualSettingsStore(s => s.applying);
  const baselineExisted= useManualSettingsStore(s => s.baselineExisted);
  const load           = useManualSettingsStore(s => s.load);
  const reset          = useManualSettingsStore(s => s.reset);
  const apply          = useManualSettingsStore(s => s.apply);

  const [active, setActive] = useState<ManualCategory>('display');
  const [toast, setToast] = useState<{ open: boolean; tone: ToastTone; message: string }>({
    open: false, tone: 'success', message: '',
  });

  useEffect(() => { void load(); }, [load]);

  const isDirty = dirtyTotal > 0;

  const onApply = async () => {
    const result = await apply();
    if (!result.success) {
      setToast({
        open: true, tone: 'error',
        message: result.errorMessage
          ? t('settings.manual.saveFailedWith', {
              defaultValue: 'Не удалось сохранить: {{error}}',
              error: result.errorMessage,
            })
          : t('settings.manual.saveFailed', 'Не удалось сохранить настройки.'),
      });
    } else if (result.gameWasRunning) {
      setToast({
        open: true, tone: 'warning',
        message: t('settings.manual.savedGameRunning',
          'Настройки сохранены, но GTA запущена - закрой игру, иначе она перезапишет файл.'),
      });
    } else {
      setToast({
        open: true, tone: 'success',
        message: t('settings.manual.saved', 'Настройки сохранены в settings.xml.'),
      });
    }
  };

  if (loading && !draft) {
    return (
      <div className="h-full flex items-center justify-center text-text-muted gap-2">
        <Loader2 size={14} className="animate-spin" />
        <span className="text-[12px]">{t('settings.manual.loading', 'Читаем твой settings.xml…')}</span>
      </div>
    );
  }

  if (loadError) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-center px-12 gap-2">
        <div className="text-[13px] text-status-error">{t('settings.manual.loadFailed', 'Не удалось прочитать settings.xml')}</div>
        <div className="text-[12px] text-text-muted max-w-md">{loadError}</div>
      </div>
    );
  }

  if (!draft) return null;

  const headroom = Math.max(1, 43 - baselineGain);
  const gainBarPct = Math.min(100, (liveGain / headroom) * 100);

  return (
    <div className="h-full flex overflow-hidden">

      {}
      <aside className="w-[220px] shrink-0 h-full flex flex-col border-r border-border-subtle px-3 py-5">
        {!baselineExisted && (
          <div className="mb-3 mx-1 px-2.5 py-2 rounded-md bg-status-info/10 border border-status-info/30 text-[11px] text-text-secondary leading-relaxed">
            {t('settings.manual.noBaseline', 'settings.xml ещё нет - заполнено vanilla-defaults.')}
          </div>
        )}
        <div className="px-2 pb-2 text-[11px] text-text-muted">{t('settings.manual.sections', 'Разделы')}</div>
        <div className="space-y-0.5">
          {CATEGORIES.map(c => {
            const isActive = c.id === active;
            const dirtyCount = dirtyCounts[c.id];
            const dirtyTitle = t('settings.manual.dirtyCount', {
              count: dirtyCount,
              defaultValue: '{{count}} изменений',
              defaultValue_one: '{{count}} изменение',
              defaultValue_few: '{{count}} изменения',
              defaultValue_many: '{{count}} изменений',
            });
            return (
              <button
                key={c.id}
                type="button"
                onClick={() => setActive(c.id)}
                style={{ outline: 'none' }}
                className={
                  'w-full flex items-center gap-2 px-2.5 h-8 rounded-md text-left text-[13px] transition-colors duration-150 ' +
                  (isActive
                    ? 'bg-accent-soft text-text-primary'
                    : 'text-text-secondary hover:bg-glass hover:text-text-primary')
                }
              >
                <span className="flex-1 truncate">{t(`settings.manual.${c.id}.title`, c.defaultLabel)}</span>
                {dirtyCount > 0 && (
                  <span
                    className="text-[11px] text-text-muted tabular-nums"
                    aria-label={dirtyTitle}
                    title={dirtyTitle}
                  >
                    {dirtyCount}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        {}
        <div className="mt-auto px-2.5 pt-4 border-t border-border-subtle">
          <div className="text-[11px] text-text-muted">{t('settings.manual.currentGain', 'Текущий прирост')}</div>
          <div className="mt-1 flex items-baseline gap-1.5">
            <span className="text-[22px] font-semibold text-text-primary tabular-nums leading-none">
              +{liveGain}%
            </span>
            <span className="text-[11px] text-text-muted">
              {t('settings.manual.gainOutOf', { defaultValue: 'из +{{max}}%', max: 43 })}
            </span>
          </div>
          <div className="mt-2 h-[3px] rounded-full bg-track-faint overflow-hidden">
            <div
              className="h-full bg-accent transition-[width] duration-300"
              style={{ width: `${gainBarPct}%` }}
            />
          </div>
        </div>
      </aside>

      {}
      <div className="flex-1 h-full flex flex-col overflow-hidden">
        <div className="flex-1 overflow-y-auto">
          <div className="max-w-[760px] mx-auto px-12 pt-10 pb-12">
            <AnimatePresence mode="wait" initial={false}>
              <motion.div
                key={active}
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                exit   ={{ opacity: 0, y: -6 }}
                transition={{ duration: 0.2, ease: EASE_DEPTH }}
              >
                {active === 'display'      && <DisplaySection />}
                {active === 'quality'      && <QualitySection />}
                {active === 'antiAliasing' && <AntiAliasingSection />}
                {active === 'world'        && <WorldSection />}
                {active === 'advanced'     && <AdvancedSection />}
              </motion.div>
            </AnimatePresence>
          </div>
        </div>

        {}
        <div
          className={
            'shrink-0 overflow-hidden transition-[max-height,opacity] duration-300 ease-out ' +
            (isDirty ? 'max-h-20 opacity-100' : 'max-h-0 opacity-0')
          }
        >
          <div className="border-t border-border-subtle bg-bg-base">
            <div className="max-w-[760px] mx-auto px-12 py-3.5 flex items-center gap-3">
              <span className="text-[12px] text-text-muted">
                {t('settings.manual.dirtyCount', {
                  count: dirtyTotal,
                  defaultValue: '{{count}} изменений',
                  defaultValue_one: '{{count}} изменение',
                  defaultValue_few: '{{count}} изменения',
                  defaultValue_many: '{{count}} изменений',
                })}
                {liveGain > 0 && <>{' '}{t('settings.manual.gainSuffix', { defaultValue: '· +{{gain}}% прирост', gain: liveGain })}</>}
              </span>
              <div className="flex-1" />
              <button
                type="button"
                onClick={reset}
                disabled={!isDirty || applying}
                title={t('settings.manual.resetTitle', 'Откатить к загруженным значениям')}
                style={{ outline: 'none' }}
                className="px-3 h-9 rounded-lg text-[13px] text-text-secondary
                           hover:text-text-primary hover:bg-glass
                           disabled:opacity-30 disabled:cursor-not-allowed
                           transition-colors duration-150"
              >
                {t('common.cancelAction', 'Отменить')}
              </button>
              <button
                type="button"
                onClick={onApply}
                disabled={!isDirty || applying}
                style={{ outline: 'none' }}
                className="inline-flex items-center gap-2 px-4 h-9 rounded-lg text-[13px] font-medium
                           bg-accent text-text-on-accent
                           hover:bg-accent-hover
                           disabled:opacity-40 disabled:cursor-not-allowed
                           transition-colors duration-150"
              >
                {applying ? <Loader2 size={13} className="animate-spin" /> : null}
                <span>{applying
                  ? t('settings.manual.saving', 'Сохраняем…')
                  : t('common.apply', 'Применить')}</span>
              </button>
            </div>
          </div>
        </div>
      </div>

      <Toast
        open={toast.open}
        tone={toast.tone}
        message={toast.message}
        autoCloseMs={toast.tone === 'error' ? 6000 : 4000}
        onClose={() => setToast(t => ({ ...t, open: false }))}
      />
    </div>
  );
}
