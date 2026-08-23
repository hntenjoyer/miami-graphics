import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Loader2 } from 'lucide-react';
import { EASE_DEPTH } from '@/design';
import { useGtaSettingsStore } from '@/store/gtaSettingsStore';
import { Toast, type ToastTone } from '@/components/Toast';
import { PresetListRow } from './PresetListRow';
import { PresetDetail } from './PresetDetail';

export function PresetsTab() {
  const { t } = useTranslation();
  const presets           = useGtaSettingsStore(s => s.publicPresets);
  const loading           = useGtaSettingsStore(s => s.loadingPublic);
  const applyingId        = useGtaSettingsStore(s => s.applyingId);
  const installedPresetId = useGtaSettingsStore(s => s.installedPresetId);
  const loadPresets       = useGtaSettingsStore(s => s.loadPublicPresets);
  const applyPreset       = useGtaSettingsStore(s => s.applyPreset);

  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [toast, setToast] = useState<{ open: boolean; tone: ToastTone; message: string }>({
    open: false, tone: 'success', message: '',
  });

  useEffect(() => { void loadPresets(null); }, [loadPresets]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return presets;
    return presets.filter(p =>
      p.name.toLowerCase().includes(q) || p.author.toLowerCase().includes(q));
  }, [presets, search]);

  useEffect(() => {
    if (filtered.length === 0) {
      setSelectedId(null);
      return;
    }
    if (!selectedId || !filtered.some(p => p.id === selectedId)) {
      const preferred = installedPresetId && filtered.find(p => p.id === installedPresetId);
      setSelectedId(preferred ? preferred.id : filtered[0].id);
    }
  }, [filtered, selectedId, installedPresetId]);

  const selected = useMemo(
    () => filtered.find(p => p.id === selectedId) ?? null,
    [filtered, selectedId]);

  const onApply = async (id: string, name: string) => {
    const result = await applyPreset(id);
    if (!result.success) {
      setToast({
        open: true, tone: 'error',
        message: result.errorMessage
          ? t('gtaSettings.presetsTab.applyFailedWithReason', 'Не удалось применить «{{name}}»: {{reason}}',
              { name, reason: result.errorMessage })
          : t('gtaSettings.presetsTab.applyFailed', 'Не удалось применить «{{name}}».', { name }),
      });
    } else if (result.gameWasRunning) {
      setToast({
        open: true, tone: 'warning',
        message: t(
          'gtaSettings.presetsTab.appliedGameRunning',
          '«{{name}}» применён, но GTA запущена - закрой игру, иначе настройки откатятся.',
          { name },
        ),
      });
    } else {
      setToast({
        open: true, tone: 'success',
        message: t('gtaSettings.presetsTab.applied', '«{{name}}» успешно применён.', { name }),
      });
    }
  };

  const isFirstLoad = loading && presets.length === 0;
  const empty = !loading && presets.length === 0;

  return (
    <div className="h-full overflow-hidden px-5 pb-5 pt-3 gap-5 flex">

      {}
      <aside
        className="relative w-[340px] shrink-0 h-full flex flex-col rounded-2xl overflow-hidden
                   bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid
                   border border-white/[0.07]
                   shadow-[0_18px_40px_-18px_rgba(0,0,0,0.6),0_6px_18px_rgba(0,0,0,0.35)]"
      >
        <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                   bg-gradient-to-r from-transparent via-white/40 to-transparent" />
        <span aria-hidden className="absolute -top-20 -left-16 w-56 h-56 pointer-events-none blur-3xl"
              style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 16%, transparent) 0%, transparent 70%)' }} />

        <div className="relative z-[1] px-5 pt-5 pb-3 shrink-0">
          <input
            type="text"
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder={t('gtaSettings.presetsTab.searchPlaceholder', 'Поиск')}
            style={{ outline: 'none' }}
            className="w-full px-3 h-9 rounded-lg
                       bg-bg-elevated-soft hover:bg-bg-elevated
                       text-[13px] text-text-primary placeholder:text-text-muted
                       border border-transparent
                       focus:border-accent focus:bg-bg-elevated
                       transition-[border-color,background-color] duration-150"
          />
        </div>

        <div className="relative z-[1] flex-1 overflow-y-auto px-2 pb-6 space-y-0.5">
          {isFirstLoad && (
            <div className="py-12 flex items-center justify-center text-text-muted gap-2">
              <Loader2 size={14} className="animate-spin" />
              <span className="text-[12px]">{t('gtaSettings.presetsTab.loading', 'Загружаем…')}</span>
            </div>
          )}
          {empty && (
            <EmptyHint
              title={t('gtaSettings.presetsTab.emptyTitle', 'Каталог пуст')}
              hint={t('gtaSettings.presetsTab.emptyHint', 'Когда админ загрузит первый XML-пресет, он появится здесь.')}
            />
          )}
          {!empty && filtered.length === 0 && search.trim() && (
            <EmptyHint
              title={t('gtaSettings.presetsTab.noResultsTitle', 'Ничего не нашли')}
              hint={t('gtaSettings.presetsTab.noResultsHint', 'По «{{query}}» нет пресетов.', { query: search.trim() })}
            />
          )}
          <AnimatePresence initial={false}>
            {filtered.map(p => (
              <motion.div
                key={p.id}
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.18, ease: EASE_DEPTH }}
              >
                <PresetListRow
                  preset={p}
                  active={p.id === selectedId}
                  installed={p.id === installedPresetId}
                  onSelect={() => setSelectedId(p.id)}
                />
              </motion.div>
            ))}
          </AnimatePresence>
        </div>
      </aside>

      {}
      <main
        className="relative flex-1 h-full overflow-hidden rounded-2xl
                   bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid
                   border border-white/[0.07]
                   shadow-[0_18px_40px_-18px_rgba(0,0,0,0.6),0_6px_18px_rgba(0,0,0,0.35)]"
      >
        <span aria-hidden className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                   bg-gradient-to-r from-transparent via-white/40 to-transparent" />
        <span aria-hidden className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl z-0"
              style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 16%, transparent) 0%, transparent 70%)' }} />

        <AnimatePresence mode="wait" initial={false}>
          {selected ? (
            <motion.div
              key={selected.id}
              initial={{ opacity: 0, x: 12 }}
              animate={{ opacity: 1, x: 0 }}
              exit   ={{ opacity: 0, x: -12 }}
              transition={{ duration: 0.22, ease: EASE_DEPTH }}
              className="relative z-[1] h-full"
            >
              <PresetDetail
                preset={selected}
                applying={applyingId === selected.id}
                installed={selected.id === installedPresetId}
                onApply={() => onApply(selected.id, selected.name)}
              />
            </motion.div>
          ) : (
            <motion.div
              key="empty-detail"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit   ={{ opacity: 0 }}
              transition={{ duration: 0.2 }}
              className="h-full flex items-center justify-center px-12"
            >
              <EmptyDetail />
            </motion.div>
          )}
        </AnimatePresence>
      </main>

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

function EmptyHint({ title, hint }: { title: string; hint: string }) {
  return (
    <div className="py-12 px-4 flex flex-col items-center justify-center text-center gap-1.5">
      <div className="text-[13px] text-text-primary font-medium">{title}</div>
      <div className="text-[11px] text-text-muted leading-relaxed max-w-[260px]">{hint}</div>
    </div>
  );
}

function EmptyDetail() {
  const { t } = useTranslation();
  return (
    <div className="text-center max-w-sm">
      <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-2">
        {t('gtaSettings.presetsTab.pickPresetTitle', 'Выбери пресет')}
      </div>
      <div className="text-[13px] text-text-secondary leading-relaxed">
        {t('gtaSettings.presetsTab.pickPresetHint',
          'Слева - список конфигов. Кликни любой, и здесь покажется подробный разбор.')}
      </div>
    </div>
  );
}
