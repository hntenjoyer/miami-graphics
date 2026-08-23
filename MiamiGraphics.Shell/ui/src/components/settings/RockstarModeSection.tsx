import { useEffect, useRef, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { Gamepad2, Loader2, Check, AlertTriangle, Shield, ShieldAlert, LogIn, LogOut, ScrollText, RefreshCw, Copy, Wrench } from 'lucide-react';
import { SettingsSection } from '@/components/settings/SettingsSection';
import { bridge } from '@/bridge';
import type { HotSwapStatus, HotSwapLogTail } from '@/bridge/types';

interface HotSwapMethodInfo {
  id:    number;
  titleKey: string;
  title: string;
  hintKey: string;
  hint:  string;
  manual: boolean;
}

const HOT_SWAP_METHODS: readonly HotSwapMethodInfo[] = [
  {
    id: 1, manual: false,
    titleKey: 'settings.rockstarMode.methodAutoTitle',
    title: 'Автоматически, при запуске игры',
    hintKey: 'settings.rockstarMode.methodAutoHint',
    hint:  'Лаунчер сам замечает, что ты запустил GTA, подставляет моды за доли секунды и возвращает чистые файлы через три секунды после того, как игра закрылась. Нажимать ничего не надо. Взамен в Планировщике Windows живёт фоновая задача - она появляется при включении режима и убирается при выключении.',
  },
  {
    id: 3, manual: true,
    titleKey: 'settings.rockstarMode.methodManualTitle',
    title: 'Вручную, по кнопке',
    hintKey: 'settings.rockstarMode.methodManualHint',
    hint:  'Ничего фонового в системе не заводится. Перед запуском GTA жмёшь «Захожу в игру», после выхода - «Вышел из игры». Подмена такая же мгновенная. Но если забыть нажать «Вышел» и открыть лаунчер Rockstar, он найдёт моды и снесёт их.',
  },
];

const LEGACY_METHOD_TITLES: Record<number, { key: string; def: string }> = {
  2: { key: 'settings.rockstarMode.legacyMethod2', def: 'автоматически, копии в отдельной папке' },
  4: { key: 'settings.rockstarMode.legacyMethod4', def: 'вручную, копии в отдельной папке (копированием)' },
  5: { key: 'settings.rockstarMode.legacyMethod5', def: 'как ReplaceX: копирование и гашение процессов игры' },
};

function normalizeMethod(id: number | undefined | null): number {
  if (HOT_SWAP_METHODS.some(m => m.id === id)) return id as number;
  return id === 4 ? 3 : 1;
}

export function RockstarModeSection({ onToast }: {
  onToast?: (t: { tone: 'success' | 'error'; message: string }) => void;
}) {
  const { t, i18n } = useTranslation();
  const [st, setSt] = useState<HotSwapStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const lock = () => { if (busyRef.current) return false; busyRef.current = true; setBusy(true); return true; };
  const unlock = () => { busyRef.current = false; setBusy(false); };
  const [method, setMethod] = useState(1);
  const seeded = useRef(false);

  const refresh = () => {
    void bridge.hotSwapGetStatus?.().then(s => {
      setSt(s);
      if (s?.method && (s.enabled || !seeded.current)) setMethod(normalizeMethod(s.method));
      if (s) seeded.current = true;
    }).catch(() => setSt(null));
  };
  useEffect(() => {
    refresh();
    const id = window.setInterval(refresh, 5000);
    return () => window.clearInterval(id);
  }, []);

  const toggle = async () => {
    if (!st || !lock()) return;
    try {
      const r = await bridge.hotSwapSetEnabled!(!st.enabled, method);
      if (r.success) {
        onToast?.({ tone: 'success', message: !st.enabled
          ? t('settings.rockstarMode.toastEnabled', 'Режим Rockstar включён: файлы игры чистые, моды подставятся при запуске.')
          : t('settings.rockstarMode.toastDisabled', 'Режим выключен: моды снова стоят в игре постоянно.') });
      } else {
        onToast?.({ tone: 'error', message: r.errorMessage ?? t('settings.rockstarMode.toastToggleFailed', 'Не удалось переключить режим.') });
      }
      refresh();
    } catch (e) {
      onToast?.({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally { unlock(); }
  };

  const manual = async (arm: boolean) => {
    if (!st || !lock()) return;
    try {
      const call = arm ? bridge.hotSwapArmNow : bridge.hotSwapDisarmNow;
      if (!call) {
        onToast?.({ tone: 'error', message: t('settings.rockstarMode.toastNoManualApi', 'Эта версия лаунчера не умеет ручной триггер. Обнови приложение.') });
        return;
      }
      const r = await call.call(bridge);
      if (r.success) {
        onToast?.({ tone: 'success', message: arm
          ? t('settings.rockstarMode.toastArmed', 'Моды подставлены. Можно запускать игру.')
          : t('settings.rockstarMode.toastDisarmed', 'Чистые файлы вернулись на место. Лаунчер ничего не заметит.') });
      } else {
        onToast?.({ tone: 'error', message: r.errorMessage ?? t('settings.rockstarMode.toastManualFailed', 'Не получилось.') });
      }
      refresh();
    } catch (e) {
      onToast?.({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally { unlock(); }
  };

  const rebuild = async () => {
    if (!st) return;
    if (!bridge.hotSwapRebuild) {
      onToast?.({ tone: 'error', message: t('settings.rockstarMode.toastNoRebuildApi', 'Эта версия лаунчера не умеет пересборку. Обнови приложение.') });
      return;
    }
    if (!lock()) return;
    try {
      const r = await bridge.hotSwapRebuild();
      if (r.success) {
        onToast?.({ tone: 'success', message:
          t('settings.rockstarMode.toastRebuilt', 'Готово: режим выключен, новые файлы игры на месте. Поставь сборку заново и включи режим снова.') });
      } else {
        onToast?.({ tone: 'error', message: r.errorMessage ?? t('settings.rockstarMode.toastRebuildFailed', 'Не удалось пересобрать.') });
      }
      refresh();
    } catch (e) {
      onToast?.({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    } finally { unlock(); }
  };

  const [logOpen, setLogOpen] = useState(false);
  const [log, setLog] = useState<HotSwapLogTail | null>(null);
  const [logBusy, setLogBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const logPreRef = useRef<HTMLPreElement | null>(null);

  const loadLog = async () => {
    if (!bridge.hotSwapGetLog) {
      setLog({ path: null, text: t('settings.rockstarMode.logNoApi', 'Эта версия лаунчера не отдаёт лог hotswap. Обнови приложение.') });
      return;
    }
    setLogBusy(true);
    try {
      setLog(await bridge.hotSwapGetLog(64));
    } catch (e) {
      setLog({ path: null, text: t('settings.rockstarMode.logReadFailed', 'Не удалось прочитать лог: {{error}}', { error: e instanceof Error ? e.message : String(e) }) });
    } finally { setLogBusy(false); }
  };

  useEffect(() => {
    const el = logPreRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [log]);

  const toggleLog = () => {
    const next = !logOpen;
    setLogOpen(next);
    if (next && !log) void loadLog();
  };

  const copyLog = async () => {
    try {
      await navigator.clipboard.writeText(log?.text ?? '');
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      onToast?.({ tone: 'error', message: t('settings.rockstarMode.toastCopyFailed', 'Не удалось скопировать лог.') });
    }
  };

  const chosen     = HOT_SWAP_METHODS.find(m => m.id === method) ?? HOT_SWAP_METHODS[0];
  const enabled    = st?.enabled === true;
  const legacyRunning = enabled && st?.method != null && LEGACY_METHOD_TITLES[st.method] != null
    ? st.method : null;
  const stale      = st?.stale === true;
  const lockMethod = busy || !st || enabled;
  const showManual = enabled && (st?.manualTrigger ?? chosen.manual);

  const status = !st ? t('settings.rockstarMode.statusChecking', 'Проверяю...')
    : stale ? t('settings.rockstarMode.statusStale', 'Включён, но модов в игре НЕТ')
    : st.armed ? t('settings.rockstarMode.statusArmed', 'Идёт игра - моды подставлены')
    : st.enabled
      ? (showManual
          ? t('settings.rockstarMode.statusWaitManual', 'Включён, ждёт кнопки «Захожу в игру»')
          : st.agentAlive
            ? t('settings.rockstarMode.statusAgentAlive', 'Включён, агент следит за запуском игры')
            : t('settings.rockstarMode.statusAgentDead', 'Включён, агент не отвечает (перезайди в Windows)'))
      : t('settings.rockstarMode.statusDisabled', 'Выключен - моды стоят в игре постоянно');

  return (
    <SettingsSection
      icon={Gamepad2}
      title={t('settings.rockstarMode.title', 'Режим Rockstar Launcher')}
      description={t('settings.rockstarMode.description', 'Для тех, у кого GTA V куплена в Rockstar Games Launcher: он проверяет целостность файлов при запуске и сносит моды. В этом режиме файлы игры лежат чистыми, а моды подставляются автоматически в момент старта игры и убираются при выходе.')}
    >
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <div className="flex items-start gap-2.5 min-w-0">
          <span className={'shrink-0 w-7 h-7 rounded-lg flex items-center justify-center mt-px ' +
            (stale ? 'bg-[color-mix(in_srgb,var(--status-error)_18%,transparent)] text-status-error'
              : st?.armed ? 'bg-accent-soft text-accent'
              : st?.enabled ? 'bg-accent-soft text-accent'
              : 'bg-white/[0.06] text-text-muted')}>
            {stale ? <ShieldAlert size={14} strokeWidth={2.4} />
              : st?.enabled ? <Shield size={14} strokeWidth={2.4} />
              : <Gamepad2 size={14} strokeWidth={2.4} />}
          </span>
          <div className="min-w-0">
            <div className={'text-[12.5px] font-semibold ' + (stale ? 'text-status-error' : 'text-text-primary')}>
              {status}
            </div>
            {st?.note && !stale && <div className="text-[11px] text-status-warning mt-0.5">{st.note}</div>}
            {st?.enabled && st.imageRoot && (
              <div className="text-[10.5px] text-text-muted font-mono mt-0.5 truncate">{st.imageRoot}</div>
            )}
          </div>
        </div>
        <button
          onClick={() => void toggle()}
          disabled={busy || !st || (!st.enabled && !st.supported) || !!st?.armed}
          title={st?.armed ? t('settings.rockstarMode.cantToggleInGame', 'Нельзя переключать во время игры') : undefined}
          className={'inline-flex items-center justify-center gap-2 px-4 h-9 rounded-lg shrink-0 border ' +
            'text-[12px] font-bold uppercase tracking-wider transition-all ' +
            'disabled:opacity-40 disabled:cursor-not-allowed ' +
            (st?.enabled
              ? 'bg-white/[0.04] text-text-secondary border-white/[0.08] hover:text-status-error'
              : 'bg-bg-elevated/70 text-text-primary border-white/[0.10] ' +
                'hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)] hover:shadow-glow-accent')}
          style={{ outline: 'none' }}
        >
          {busy ? <Loader2 size={15} className="animate-spin" />
            : st?.enabled ? <AlertTriangle size={15} strokeWidth={2.6} />
            : null}
          {busy
            ? t('settings.rockstarMode.applying', 'Применяю...')
            : st?.enabled
              ? t('settings.rockstarMode.turnOff', 'Выключить')
              : t('settings.rockstarMode.turnOn', 'Включить')}
        </button>
      </div>

      {stale && (
        <div className="mt-3 rounded-xl overflow-hidden
                        bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)]
                        border border-[color-mix(in_srgb,var(--status-error)_38%,transparent)]">
          <div className="flex items-start gap-2.5 px-3.5 py-3">
            <span className="shrink-0 w-7 h-7 rounded-lg flex items-center justify-center mt-px
                             bg-[color-mix(in_srgb,var(--status-error)_20%,transparent)] text-status-error">
              <AlertTriangle size={15} strokeWidth={2.6} />
            </span>
            <div className="min-w-0">
              <div className="text-[13px] font-bold text-status-error leading-snug">
                {t('settings.rockstarMode.staleTitle', 'Rockstar обновил игру - моды в образе устарели, в игре их НЕТ')}
              </div>
              {st?.staleNote && (
                <div className="text-[11.5px] text-text-secondary leading-relaxed mt-1">{st.staleNote}</div>
              )}
              <div className="text-[11.5px] text-text-secondary leading-relaxed mt-1.5">
                {t('settings.rockstarMode.staleWhy',
                  'Лаунчер Rockstar сверяет файлы игры со своим списком и, найдя расхождение, скачивает свой update.rpf поверх модов. Теперь файл в игре новее, чем копии в образе, и подставлять старые моды нельзя - игра просто не запустится. Режим формально включён, но не делает ничего.')}
              </div>
              <div className="text-[11.5px] text-text-secondary leading-relaxed mt-1.5">
                <Trans
                  i18nKey="settings.rockstarMode.staleWhatToDo"
                  defaults="Кнопка ниже разберёт образ, не тронув новые файлы игры, выключит режим и обновит чистую копию под новую версию. После неё <b>поставь сборку заново</b> и включи режим снова - моды старой версии восстановить нечем."
                  components={{ b: <b className="text-text-primary font-semibold" /> }}
                />
              </div>
              <button
                type="button"
                onClick={() => void rebuild()}
                disabled={busy}
                className="mt-2.5 inline-flex items-center justify-center gap-2 px-3.5 h-9 rounded-lg border
                           text-[12.5px] font-bold uppercase tracking-wider transition-colors
                           bg-status-error text-white border-status-error hover:brightness-110
                           disabled:opacity-40 disabled:cursor-not-allowed"
              >
                {busy ? <Loader2 size={14} className="animate-spin" /> : <Wrench size={14} strokeWidth={2.6} />}
                {busy
                  ? t('settings.rockstarMode.rebuildBusy', 'Работаю...')
                  : t('settings.rockstarMode.rebuild', 'Пересобрать под новую версию игры')}
              </button>
              {st?.staleAtUtc && (
                <div className="text-[10.5px] text-text-muted mt-1.5">
                  {t('settings.rockstarMode.staleAt', 'Подмена файлов замечена: {{when}}',
                    { when: new Date(st.staleAtUtc).toLocaleString(i18n.language) })}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      <div className="flex flex-col gap-2 pt-3">
        <div className="flex items-baseline justify-between gap-3 flex-wrap">
          <div className="text-[12px] font-semibold text-text-primary">
            {t('settings.rockstarMode.methodHeading', 'Способ подмены')}
          </div>
          <div className="text-[11px] text-text-muted">
            {enabled
              ? t('settings.rockstarMode.methodLockedHint', 'Чтобы сменить способ, сперва выключи режим.')
              : t('settings.rockstarMode.methodPickHint', 'Не знаешь, что выбрать - бери «Автоматически».')}
          </div>
        </div>

        {legacyRunning != null && (
          <div className="text-[11px] text-text-secondary leading-relaxed rounded-xl
                          bg-bg-elevated border border-border-subtle px-3 py-2.5">
            {t('settings.rockstarMode.legacyRunning',
              'Режим сейчас работает на старом способе подмены ({{method}}). Он никуда не делся и работает как раньше - трогать ничего не надо. Из настроек его убрали: если захочешь перейти на один из двух ниже, выключи режим и включи снова.',
              { method: t(LEGACY_METHOD_TITLES[legacyRunning].key, LEGACY_METHOD_TITLES[legacyRunning].def) })}
          </div>
        )}

        <div role="radiogroup" aria-label={t('settings.rockstarMode.methodGroupLabel', 'Способ подмены файлов')} className="flex flex-col gap-1.5">
          {HOT_SWAP_METHODS.map(m => {
            const active = legacyRunning == null && m.id === method;
            return (
              <button
                key={m.id}
                type="button"
                role="radio"
                aria-checked={active}
                disabled={lockMethod}
                onClick={() => setMethod(m.id)}
                className={
                  'text-left rounded-xl px-3 py-2.5 border transition-colors outline-none ' +
                  'focus-visible:shadow-[0_0_0_3px_var(--accent-soft)] ' +
                  'disabled:cursor-not-allowed ' +
                  (active
                    ? 'bg-accent-soft border-[color-mix(in_srgb,var(--accent)_45%,transparent)]'
                    : 'bg-bg-elevated border-border-subtle hover:border-white/[0.18] ' +
                      (lockMethod ? '' : 'hover:bg-white/[0.04]')) +
                  (lockMethod && !active ? ' opacity-40' : '')
                }
                style={{ outline: 'none' }}
              >
                <div className="flex items-start gap-2.5">
                  <span
                    aria-hidden
                    className={
                      'shrink-0 mt-[3px] w-4 h-4 rounded-full border flex items-center justify-center ' +
                      (active ? 'border-accent' : 'border-white/25')
                    }
                  >
                    {active && <span className="w-2 h-2 rounded-full bg-accent" />}
                  </span>
                  <span className="min-w-0">
                    <span className="flex items-center gap-2 flex-wrap">
                      <span className={'text-[12.5px] font-semibold ' + (active ? 'text-text-primary' : 'text-text-secondary')}>
                        {t(m.titleKey, m.title)}
                      </span>
                      {!m.manual && (
                        <span className="px-1.5 py-px rounded-md text-[10px] font-semibold uppercase tracking-wider
                                         bg-white/[0.07] text-text-muted">
                          {t('settings.rockstarMode.methodUsualChoice', 'обычный выбор')}
                        </span>
                      )}
                    </span>
                    <span className="block text-[11px] text-text-muted leading-snug mt-0.5">{t(m.hintKey, m.hint)}</span>
                  </span>
                </div>
              </button>
            );
          })}
        </div>

        <div className="text-[11px] text-text-muted leading-relaxed">
          <Trans
            i18nKey="settings.rockstarMode.methodCompare"
            defaults="<b>Разница между ними одна: кто говорит «пора».</b> Всё остальное совпадает - оба держат файлы игры чистыми, хранят моды на диске с игрой и подменяют файл мгновенным переименованием, а не копированием гигабайтов. По скорости и надёжности самой подмены они одинаковые.<br/>Бери <b>«Автоматически»</b>, если не хочешь ничего помнить: лаунчер сам поймает запуск игры и сам приберёт за собой. Бери <b>«Вручную»</b>, если не хочешь, чтобы в Планировщике Windows висела фоновая задача: тогда две кнопки до и после игры на тебе."
            components={{ b: <b className="text-text-secondary font-semibold" />, br: <br /> }}
          />
        </div>
      </div>

      {showManual && (
        <div className="flex items-center justify-between gap-3 flex-wrap rounded-xl mt-3
                        bg-bg-elevated border border-border-subtle px-3 py-3">
          <div className="min-w-0">
            <div className="text-[12.5px] font-semibold text-text-primary">
              {stale ? t('settings.rockstarMode.manualStale', 'Подмена запрещена - образ устарел')
                : st?.armed
                  ? t('settings.rockstarMode.manualArmed', 'Моды сейчас в игре')
                  : t('settings.rockstarMode.manualDisarmed', 'Моды сняты, файлы чистые')}
            </div>
            <div className="text-[11px] text-text-muted leading-snug">
              {t('settings.rockstarMode.manualHint',
                'Жми «Захожу в игру» перед запуском GTA и «Вышел из игры» после того, как закрыл её. Пока моды подставлены, лаунчер Rockstar запускать нельзя - проверка целостности их снесёт.')}
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <button
              type="button"
              onClick={() => void manual(true)}
              disabled={busy || stale || st?.armed === true}
              className="inline-flex items-center justify-center gap-2 px-3.5 h-9 rounded-lg border
                         text-[12.5px] font-bold uppercase tracking-wider transition-colors
                         bg-accent text-black border-accent hover:brightness-110
                         disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {busy ? <Loader2 size={14} className="animate-spin" /> : <LogIn size={14} strokeWidth={2.6} />}
              {t('settings.rockstarMode.armNow', 'Захожу в игру')}
            </button>
            <button
              type="button"
              onClick={() => void manual(false)}
              disabled={busy || st?.armed !== true}
              className="inline-flex items-center justify-center gap-2 px-3.5 h-9 rounded-lg border
                         text-[12.5px] font-bold uppercase tracking-wider transition-colors
                         bg-white/[0.04] text-text-secondary border-white/[0.08]
                         hover:text-text-primary hover:bg-white/[0.08]
                         disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {busy ? <Loader2 size={14} className="animate-spin" /> : <LogOut size={14} strokeWidth={2.6} />}
              {t('settings.rockstarMode.disarmNow', 'Вышел из игры')}
            </button>
          </div>
        </div>
      )}

      {st && !st.supported && !st.enabled && (
        <div className="text-[11px] text-text-muted leading-relaxed">
          {t('settings.rockstarMode.unsupportedHint',
            'Чтобы включить режим, нужна установленная через лаунчер сборка (у нас должна быть чистая копия файлов игры) и достаточно места на диске с игрой - образ занимает столько же, сколько модифицированные файлы.')}
        </div>
      )}
      {st?.enabled && !stale && (
        <div className="flex items-start gap-2.5 px-3 py-2.5 rounded-xl
                        bg-[color-mix(in_srgb,var(--status-warning)_9%,transparent)]
                        border border-[color-mix(in_srgb,var(--status-warning)_26%,transparent)]">
          <span className="shrink-0 w-6 h-6 rounded-lg flex items-center justify-center
                           bg-[color-mix(in_srgb,var(--status-warning)_18%,transparent)] text-status-warning mt-px">
            <AlertTriangle size={13} strokeWidth={2.4} />
          </span>
          <span className="text-[11px] leading-relaxed text-text-secondary">
            <Trans
              i18nKey="settings.rockstarMode.installBlocked"
              defaults="<b>Пока режим включён, установка модов заблокирована.</b> Файлы игры сейчас чистые - новая установка просто не сохранилась бы. Чтобы поставить или изменить моды: выключи режим здесь → поставь всё, что нужно → включи режим снова."
              components={{ b: <b className="text-text-primary font-semibold" /> }}
            />
          </span>
        </div>
      )}

      <div className="pt-3">
        <button
          type="button"
          onClick={toggleLog}
          className="inline-flex items-center gap-2 px-3 h-8 rounded-lg border transition-colors
                     text-[11.5px] font-semibold
                     bg-white/[0.04] text-text-secondary border-white/[0.08]
                     hover:text-text-primary hover:bg-white/[0.08]"
        >
          <ScrollText size={13} strokeWidth={2.4} />
          {logOpen
            ? t('settings.rockstarMode.logHide', 'Скрыть лог')
            : t('settings.rockstarMode.logShow', 'Показать лог')}
        </button>

        {logOpen && (
          <div className="mt-2 rounded-xl bg-bg-elevated border border-border-subtle overflow-hidden">
            <div className="flex items-center justify-between gap-2 px-3 py-2 border-b border-border-subtle flex-wrap">
              <div className="text-[10.5px] text-text-muted font-mono truncate min-w-0"
                   title={log?.path ?? undefined}>
                {log?.path ?? 'hotswap.log'}
              </div>
              <div className="flex items-center gap-1.5 shrink-0">
                <button
                  type="button"
                  onClick={() => void loadLog()}
                  disabled={logBusy}
                  className="inline-flex items-center gap-1.5 px-2.5 h-7 rounded-md border transition-colors
                             text-[10.5px] font-semibold
                             bg-white/[0.04] text-text-secondary border-white/[0.08]
                             hover:text-text-primary hover:bg-white/[0.08]
                             disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  {logBusy ? <Loader2 size={12} className="animate-spin" /> : <RefreshCw size={12} strokeWidth={2.4} />}
                  {t('settings.rockstarMode.logRefresh', 'Обновить')}
                </button>
                <button
                  type="button"
                  onClick={() => void copyLog()}
                  disabled={!log?.text}
                  className="inline-flex items-center gap-1.5 px-2.5 h-7 rounded-md border transition-colors
                             text-[10.5px] font-semibold
                             bg-white/[0.04] text-text-secondary border-white/[0.08]
                             hover:text-text-primary hover:bg-white/[0.08]
                             disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  {copied ? <Check size={12} strokeWidth={2.6} /> : <Copy size={12} strokeWidth={2.4} />}
                  {copied
                    ? t('settings.rockstarMode.logCopied', 'Скопировано')
                    : t('settings.rockstarMode.logCopy', 'Скопировать')}
                </button>
              </div>
            </div>
            <pre
              ref={logPreRef}
              className="m-0 px-3 py-2.5 max-h-64 overflow-auto font-mono text-[10.5px] leading-relaxed
                         text-text-secondary whitespace-pre-wrap break-words"
            >
              {log
                ? (log.text || t('settings.rockstarMode.logEmpty', 'Лог пока пуст - записи появятся после включения режима или запуска игры.'))
                : t('settings.rockstarMode.logLoading', 'Загружаю...')}
            </pre>
          </div>
        )}
      </div>
    </SettingsSection>
  );
}
