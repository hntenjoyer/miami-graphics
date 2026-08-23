import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Database, RotateCcw, Loader2, Trash2, HardDriveDownload } from 'lucide-react';
import { SettingsSection } from './SettingsSection';
import { SettingsRow } from './SettingsRow';
import { Toggle3D } from '@/design';
import { bridge } from '@/bridge';
import i18n from '@/i18n';
import type { CacheSettings, CacheCleanupResult, DataMoveProgress } from '@/bridge/types';

const GB = 1024 ** 3;

function formatBytes(n: number): string {
  if (n >= GB)          return i18n.t('common.sizeGB', { value: (n / GB).toFixed(1), defaultValue: '{{value}} ГБ' });
  if (n >= 1024 ** 2)   return i18n.t('common.sizeMB', { value: Math.round(n / 1024 ** 2), defaultValue: '{{value}} МБ' });
  if (n >= 1024)        return i18n.t('common.sizeKB', { value: Math.round(n / 1024), defaultValue: '{{value}} КБ' });
  return i18n.t('common.sizeB', { value: n, defaultValue: '{{value}} Б' });
}

const LIMIT_PRESETS = [10, 12, 15, 25, 40] as const;

const MOVE_PHASE_LABEL: Record<DataMoveProgress['phase'], string> = {
  checking:  'Проверяю папку и место на диске…',
  copying:   'Копирую данные',
  verifying: 'Сверяю копию с оригиналом',
  switching: 'Переключаю путь',
  cleanup:   'Убираю старую папку',
  done:      'Готово',
  error:     'Ошибка',
};

export function CacheSection({
  onToast,
}: {
  onToast?: (t: { tone: 'success' | 'error'; message: string }) => void;
}) {
  const { t } = useTranslation();
  const [info, setInfo]   = useState<CacheSettings | null>(null);
  const [busy, setBusy]   = useState(false);
  const [move, setMove]   = useState<DataMoveProgress | null>(null);
  const [cleaning, setCleaning] = useState(false);
  const alive = useRef(true);

  const reload = async () => {
    try {
      const next = await bridge.cacheSettingsGet();
      if (alive.current) setInfo(next);
    } catch (e) { console.warn('[settings.storage] info fail', e); }
  };

  useEffect(() => {
    alive.current = true;
    void reload();
    const onMove = (p: DataMoveProgress) => { if (alive.current) setMove(p); };
    bridge.events.on('data:moveProgress', onMove);
    return () => { alive.current = false; bridge.events.off('data:moveProgress', onMove); };
  }, []);

  const fail = (e: unknown) =>
    onToast?.({ tone: 'error', message: e instanceof Error ? e.message : String(e) });

  const onToggle = async (v: boolean) => {
    if (busy || !info) return;
    setBusy(true);
    try {
      setInfo(await bridge.cacheSettingsSet(v, info.rootOverride));
      onToast?.({
        tone: 'success',
        message: v
          ? t('settings.storage.enabledToast', 'Кеширование включено - скачанные моды переиспользуются.')
          : t('settings.storage.disabledToast', 'Кеширование выключено - моды будут качаться заново при каждой установке.'),
      });
    } catch (e) { fail(e); await reload(); }
    finally { setBusy(false); }
  };

  const onLimit = async (gb: number) => {
    if (busy) return;
    setBusy(true);
    try {
      setInfo(await bridge.cacheLimitSet(Math.round(gb * GB)));
      onToast?.({
        tone: 'success',
        message: t('settings.storage.limitToast', { gb, defaultValue: 'Лимит хранилища: {{gb}} ГБ. Лишнее уберу автоматически.' }),
      });
    } catch (e) { fail(e); await reload(); }
    finally { setBusy(false); }
  };

  const holdersText = (r: CacheCleanupResult) =>
    (r.holders ?? []).filter(h => h.bytes > 0).slice(0, 3)
      .map(h => `${h.name} ${formatBytes(h.bytes)}`).join(' · ');

  const noVictimsText = (r: CacheCleanupResult) => {
    const v = {
      used:          formatBytes(r.afterBytes),
      protectedSize: formatBytes(r.protectedBytes),
      other:         formatBytes(r.otherBytes),
      holders:       holdersText(r),
    };
    if (v.holders && r.otherBytes > 0) {
      return t('settings.storage.cleanupNothingHoldersOther', {
        ...v,
        defaultValue: 'Удалять нечего: занято {{used}}, из них {{protectedSize}} неудаляемого ({{holders}}) и {{other}} прочего, которое лаунчер сам не чистит. Подними лимит или перенеси данные на диск побольше.',
      });
    }
    if (v.holders) {
      return t('settings.storage.cleanupNothingHolders', {
        ...v,
        defaultValue: 'Удалять нечего: занято {{used}}, из них {{protectedSize}} неудаляемого ({{holders}}). Подними лимит или перенеси данные на диск побольше.',
      });
    }
    if (r.otherBytes > 0) {
      return t('settings.storage.cleanupNothingOther', {
        ...v,
        defaultValue: 'Удалять нечего: занято {{used}}, из них {{protectedSize}} неудаляемого и {{other}} прочего, которое лаунчер сам не чистит. Подними лимит или перенеси данные на диск побольше.',
      });
    }
    return t('settings.storage.cleanupNothing', {
      ...v,
      defaultValue: 'Удалять нечего: занято {{used}}, из них {{protectedSize}} неудаляемого. Подними лимит или перенеси данные на диск побольше.',
    });
  };

  const onCleanup = async () => {
    if (busy || cleaning) return;
    setCleaning(true);
    try {
      const r = await bridge.cacheCleanupNow();
      await reload();

      if (r.deletedEntries > 0 || r.freedBytes > 0) {
        onToast?.({
          tone: 'success',
          message: t('settings.storage.cleanupFreed', {
            size: formatBytes(r.freedBytes),
            count: r.deletedEntries,
            defaultValue: 'Освободил {{size}} (записей: {{count}}).',
          }),
        });
        return;
      }

      switch (r.reason) {
        case 'under_limit':
          onToast?.({ tone: 'success', message: t('settings.storage.cleanupClean', 'Чисто - занято меньше лимита.') });
          break;

        case 'busy':
          onToast?.({
            tone: 'error',
            message: t('settings.storage.cleanupBusy',
              'Сейчас идёт установка или бэкап - уборка не трогает файлы из-под них. Нажми ещё раз, когда закончится.'),
          });
          break;

        case 'concurrent':
          onToast?.({
            tone: 'error',
            message: t('settings.storage.cleanupConcurrent',
              'Уборка уже идёт в фоне и пока обходит папки. Подожди немного и нажми ещё раз.'),
          });
          break;

        case 'delete_failed':
          onToast?.({
            tone: 'error',
            message: t('settings.storage.cleanupLocked', {
              size: formatBytes(r.reclaimableBytes),
              defaultValue: 'Есть что удалить ({{size}}), но файлы держит другая программа - антивирус, GTA или Rockstar Launcher. Закрой игру и попробуй ещё раз.',
            }),
          });
          break;

        case 'protected_over_limit':
          onToast?.({
            tone: 'error',
            message: t('settings.storage.cleanupProtectedOverLimit', {
              limit: formatBytes(limit),
              protectedSize: formatBytes(r.protectedBytes),
              holders: holdersText(r) || t('settings.storage.holdersFallback', 'бэкапы для отката'),
              defaultValue: 'Лимит {{limit}} меньше, чем занимает неудаляемое ({{protectedSize}}: {{holders}}). Подними лимит или перенеси данные на диск побольше.',
            }),
          });
          break;

        case 'error':
          onToast?.({
            tone: 'error',
            message: t('settings.storage.cleanupCrashed',
              'Уборка сорвалась на полпути. Подробности в логе лаунчера, строки [quota].'),
          });
          break;

        default:
          onToast?.({ tone: 'error', message: noVictimsText(r) });
      }
    } catch (e) { fail(e); }
    finally { setCleaning(false); }
  };

  const runMove = async (target: string) => {
    setBusy(true);
    setMove({ phase: 'checking', percent: 0, fileName: null, bytesProcessed: 0, bytesTotal: 0, errorMessage: null });
    try {
      const r = await bridge.dataRootMove(target);
      await reload();
      if (r.success) {
        onToast?.({
          tone: 'success',
          message: r.sourceRemoved
            ? t('settings.storage.moveDone', {
                size: formatBytes(r.movedBytes),
                root: r.effectiveRoot,
                defaultValue: 'Перенёс {{size}} в {{root}}. Старая папка удалена.',
              })
            : (r.errorMessage ?? t('settings.storage.moveFinished', 'Перенос завершён.')),
        });
      } else {
        onToast?.({
          tone: 'error',
          message: r.errorMessage ?? t('settings.storage.moveFailed', 'Перенос не удался. Данные остались на прежнем месте.'),
        });
      }
    } catch (e) { fail(e); await reload(); }
    finally { setBusy(false); setMove(null); }
  };

  const onMoveClick = async () => {
    if (busy || move || !info) return;
    const target = await bridge.openFolderDialog();
    if (!target) return;
    await runMove(target);
  };

  const onCancelMove = async () => {
    try { await bridge.dataRootMoveCancel(); }
    catch (e) { console.warn('[settings.storage] cancel fail', e); }
  };

  const resetFolder = async () => {
    if (busy || move || !info) return;
    await runMove(info.defaultDataRoot);
  };

  const used    = info?.totalBytes ?? 0;
  const limit   = info?.limitBytes ?? 0;
  const ratio   = limit > 0 ? Math.min(1, used / limit) : 0;
  const over    = limit > 0 && used > limit;
  const limitGb = limit > 0 ? +(limit / GB).toFixed(1) : 12;
  const busyAny = busy || cleaning || !!move;

  const usageText = (i: CacheSettings) => {
    const parts = [
      t('settings.storage.usageMain', {
        cleanable:     formatBytes(Math.max(0, used - i.protectedBytes - i.otherBytes)),
        protectedSize: formatBytes(i.protectedBytes),
        defaultValue: 'Можно очистить {{cleanable}} - это скачанные моды, они закачаются заново при надобности. Нельзя удалить {{protectedSize}}: чистая GTA и снимок твоей игры, без них не будет отката.',
      }),
    ];
    if (i.otherBytes > 0) {
      parts.push(t('settings.storage.usageOther', {
        other: formatBytes(i.otherBytes),
        defaultValue: 'Ещё {{other}} автоуборка не трогает.',
      }));
    }
    parts.push(t('settings.storage.usageBreakdown', {
      cache:   formatBytes(i.sizeBytes),
      backups: formatBytes(i.backupBytes),
      work:    formatBytes(i.workBytes),
      defaultValue: '(кеш {{cache}}, бэкапы {{backups}}, рабочая папка {{work}})',
    }));
    return parts.join(' ');
  };

  const folderText = (i: CacheSettings) => {
    const lines = [
      t('settings.storage.folderFree', {
        root: i.dataRoot,
        free: formatBytes(i.freeSpaceBytes),
        defaultValue: '{{root}}  ·  свободно на диске {{free}}',
      }),
    ];
    if (i.backupOnLegacyRoot) {
      lines.push(t('settings.storage.backupOnLegacyRoot', {
        root: i.backupRoot,
        defaultValue: 'Бэкапы всё ещё в {{root}} - нажми «Перенести», чтобы собрать всё в одном месте.',
      }));
    }
    if (i.workBytes > 0) {
      lines.push(t('settings.storage.workRootNote', {
        size: formatBytes(i.workBytes),
        root: i.workRoot,
        defaultValue: 'Рабочая папка ({{size}}) не переезжает и остаётся в {{root}}.',
      }));
    }
    return lines.join('\n');
  };

  const moveText = (m: DataMoveProgress) => {
    if (m.bytesTotal <= 0) {
      return t('settings.storage.moveHint', 'Не выключай лаунчер: данные копируются, потом сверяются побайтно.');
    }
    const done = t('settings.storage.moveBytes', {
      done:  formatBytes(m.bytesProcessed),
      total: formatBytes(m.bytesTotal),
      defaultValue: '{{done}} из {{total}}',
    });
    return m.fileName ? `${done} · ${m.fileName}` : done;
  };

  const btn = 'inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-white/[0.04] text-text-primary '
    + 'border border-white/[0.08] hover:bg-white/[0.08] hover:border-white/[0.18] '
    + 'disabled:opacity-50 disabled:cursor-wait transition-colors text-sm font-bold uppercase tracking-wider';

  return (
    <SettingsSection
      icon={Database}
      title={t('settings.storage.title', 'Хранилище: кеш и бэкапы')}
      description={t('settings.storage.description', 'Скачанные моды и бэкапы игры хранятся на диске: кеш - чтобы переустановка не качала заново, бэкапы - чтобы можно было вернуть чистую GTA.')}
    >
      <SettingsRow
        label={t('settings.storage.usageLabel', 'Занято')}
        description={info ? usageText(info) : t('settings.storage.loading', 'Загружаю…')}
        control={
          <div className="flex flex-col items-end gap-1.5 min-w-[220px]">
            <span className={`text-sm font-bold tabular-nums ${over ? 'text-red-300' : 'text-text-primary'}`}>
              {formatBytes(used)} / {formatBytes(limit)}
            </span>
            <div className="w-[220px] h-2 rounded-full bg-white/[0.07] overflow-hidden">
              <div
                className={`h-full rounded-full transition-[width] duration-500 ${
                  over ? 'bg-red-500/80' : ratio > 0.85 ? 'bg-amber-400/80' : 'bg-emerald-400/70'
                }`}
                style={{ width: `${Math.max(2, ratio * 100)}%` }}
              />
            </div>
          </div>
        }
      />

      <SettingsRow
        label={t('settings.storage.limitLabel', 'Лимит хранилища')}
        description={t('settings.storage.limitDescription',
          'Когда кеш и бэкапы вместе перерастают лимит, лишнее удаляется само: сначала то, что легко скачать заново. Чистый update.rpf и снимок твоей игры не трогаются никогда.')}
        control={
          <div className="flex items-center gap-1.5 flex-wrap justify-end">
            {LIMIT_PRESETS.map(gb => (
              <button
                key={gb}
                type="button"
                onClick={() => void onLimit(gb)}
                disabled={busyAny || !info}
                className={`px-3 py-1.5 rounded-lg text-xs font-bold tabular-nums border transition-colors
                  disabled:opacity-50 disabled:cursor-wait ${
                  Math.abs(limitGb - gb) < 0.05
                    ? 'bg-white/[0.14] text-text-primary border-white/[0.28]'
                    : 'bg-white/[0.03] text-text-secondary border-white/[0.08] hover:bg-white/[0.08]'
                }`}
                style={{ outline: 'none' }}
              >
                {t('common.sizeGB', { value: gb, defaultValue: '{{value}} ГБ' })}
              </button>
            ))}
          </div>
        }
      />

      <SettingsRow
        label={info?.rootOverride
          ? t('settings.storage.folderManual', 'Папка данных (выбрана вручную)')
          : t('settings.storage.folderDefault', 'Папка данных (стандартная)')}
        description={info ? folderText(info) : t('settings.storage.loading', 'Загружаю…')}
        control={
          <button type="button" onClick={() => void onMoveClick()} disabled={busyAny || !info} className={btn} style={{ outline: 'none' }}>
            {move ? <Loader2 size={14} className="animate-spin" /> : <HardDriveDownload size={14} />}
            <span>{t('settings.storage.move', 'Перенести на другой диск')}</span>
          </button>
        }
      />

      {move && (
        <SettingsRow
          label={t(`settings.storage.movePhase.${move.phase}`, MOVE_PHASE_LABEL[move.phase] ?? move.phase)}
          description={moveText(move)}
          control={
            <div className="flex flex-col items-end gap-1.5 min-w-[220px]">
              <span className="text-sm font-bold tabular-nums text-text-primary">{move.percent}%</span>
              <div className="w-[220px] h-2 rounded-full bg-white/[0.07] overflow-hidden">
                <div
                  className="h-full rounded-full bg-sky-400/80 transition-[width] duration-300"
                  style={{ width: `${Math.max(2, move.percent)}%` }}
                />
              </div>
              {(move.phase === 'copying' || move.phase === 'verifying' || move.phase === 'checking') && (
                <button
                  type="button"
                  onClick={() => void onCancelMove()}
                  className="text-xs font-bold uppercase tracking-wider text-text-secondary hover:text-red-300 transition-colors"
                  style={{ outline: 'none' }}
                >
                  {t('settings.storage.moveCancel', 'Остановить перенос')}
                </button>
              )}
            </div>
          }
        />
      )}

      <SettingsRow
        label={t('settings.storage.cleanupLabel', 'Очистить кеш сейчас')}
        description={t('settings.storage.cleanupDescription',
          'Удалит скачанное, что можно скачать снова (редуксы, патчи, донорские компоненты, картинки), начиная с самого давнего. Бэкапы для отката и твои сохранённые сборки остаются.')}
        control={
          <button type="button" onClick={() => void onCleanup()} disabled={busyAny} className={btn} style={{ outline: 'none' }}>
            {cleaning ? <Loader2 size={14} className="animate-spin" /> : <Trash2 size={14} />}
            <span>{cleaning ? t('settings.storage.cleaning', 'Убираю…') : t('settings.storage.cleanupButton', 'Очистить')}</span>
          </button>
        }
      />

      <SettingsRow
        label={t('settings.storage.toggleLabel', 'Кешировать скачанные моды')}
        description={info?.enabled === false
          ? t('settings.storage.toggleOffHint', 'Выключено: каждая установка качает файлы заново.')
          : t('settings.storage.toggleOnHint', 'Включено: повторная установка использует сохранённые файлы.')}
        control={
          <Toggle3D
            checked={info?.enabled ?? true}
            onChange={(v) => void onToggle(v)}
            ariaLabel={t('settings.storage.toggleLabel', 'Кешировать скачанные моды')}
          />
        }
      />

      {info?.rootOverride && (
        <SettingsRow
          label={t('settings.storage.resetLabel', 'Стандартная папка')}
          description={`${info.defaultDataRoot}\n`
            + t('settings.storage.resetHint', 'Данные переедут обратно с проверкой, старая папка освободится.')}
          control={
            <button
              type="button"
              onClick={() => void resetFolder()}
              disabled={busyAny}
              className={btn.replace('text-text-primary', 'text-text-secondary')}
              style={{ outline: 'none' }}
            >
              <RotateCcw size={14} />
              <span>{t('settings.storage.reset', 'Вернуть в стандартную папку')}</span>
            </button>
          }
        />
      )}
    </SettingsSection>
  );
}
