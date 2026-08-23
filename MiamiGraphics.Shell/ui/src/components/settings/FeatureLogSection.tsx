import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Check, ClipboardList, Copy, Loader2, RefreshCw } from 'lucide-react';
import { bridge } from '@/bridge';
import type { HotSwapLogTail } from '@/bridge/types';

export function FeatureLogSection() {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [log, setLog] = useState<HotSwapLogTail | null>(null);
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const preRef = useRef<HTMLPreElement | null>(null);

  const loadLog = async () => {
    if (!bridge.featureGetLog) {
      setLog({ path: null, text: t('settings.featureLog.noApi', 'Эта версия лаунчера не отдаёт журнал. Обнови приложение.') });
      return;
    }
    setBusy(true);
    try {
      setLog(await bridge.featureGetLog(64));
    } catch (e) {
      setLog({ path: null, text: t('settings.featureLog.readFailed', 'Не удалось прочитать журнал: {{error}}', { error: e instanceof Error ? e.message : String(e) }) });
    } finally { setBusy(false); }
  };

  useEffect(() => {
    const el = preRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [log]);

  const toggle = () => {
    const next = !open;
    setOpen(next);
    if (next && !log) void loadLog();
  };

  const copyLog = async () => {
    if (!log?.text) return;
    try {
      await navigator.clipboard.writeText(log.text);
      setCopied(true);
      setTimeout(() => setCopied(false), 1600);
    } catch {  }
  };

  return (
    <div className="rounded-2xl border border-white/[0.08] bg-glass-ultra backdrop-blur-glass-ultra px-4 py-3 flex flex-col gap-2.5">
      <div className="flex items-start gap-3">
        <div className="w-9 h-9 rounded-xl bg-white/[0.06] border border-white/[0.10]
                        flex items-center justify-center text-accent shrink-0">
          <ClipboardList size={17} strokeWidth={2} />
        </div>
        <div className="flex-1 min-w-0">
          <h3 className="text-[13px] font-display font-bold text-text-primary uppercase tracking-[0.06em]">
            {t('settings.featureLog.title', 'Журнал прицела и «Другого»')}
          </h3>
          <p className="text-[11.5px] text-text-secondary leading-snug mt-0.5">
            {t('settings.featureLog.description', 'Что делал лаунчер при установке прицела, залазов, трейсеров, дыма, рюкзаков и миникарты. Если что-то не поставилось - скопируй и пришли этот текст.')}
          </p>
        </div>
        <button
          type="button"
          onClick={toggle}
          className="shrink-0 px-3 h-8 rounded-lg border text-[11px] font-semibold transition-colors
                     bg-white/[0.04] text-text-secondary border-white/[0.08]
                     hover:text-text-primary hover:bg-white/[0.08]"
        >
          {open
            ? t('settings.featureLog.hide', 'Скрыть')
            : t('settings.featureLog.show', 'Показать')}
        </button>
      </div>

      {open && (
        <div className="rounded-xl bg-bg-elevated border border-border-subtle overflow-hidden">
          <div className="flex items-center justify-between gap-2 px-3 py-2 border-b border-border-subtle flex-wrap">
            <div className="text-[10.5px] text-text-muted font-mono truncate min-w-0" title={log?.path ?? undefined}>
              {log?.path ?? 'features.log'}
            </div>
            <div className="flex items-center gap-1.5 shrink-0">
              <button
                type="button"
                onClick={() => void loadLog()}
                disabled={busy}
                className="inline-flex items-center gap-1.5 px-2.5 h-7 rounded-md border transition-colors
                           text-[10.5px] font-semibold
                           bg-white/[0.04] text-text-secondary border-white/[0.08]
                           hover:text-text-primary hover:bg-white/[0.08]
                           disabled:opacity-40 disabled:cursor-not-allowed"
              >
                {busy ? <Loader2 size={12} className="animate-spin" /> : <RefreshCw size={12} strokeWidth={2.4} />}
                {t('settings.featureLog.refresh', 'Обновить')}
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
                  ? t('settings.featureLog.copied', 'Скопировано')
                  : t('settings.featureLog.copy', 'Скопировать')}
              </button>
            </div>
          </div>
          <pre
            ref={preRef}
            className="m-0 px-3 py-2.5 max-h-64 overflow-auto font-mono text-[10.5px] leading-relaxed
                       text-text-secondary whitespace-pre-wrap break-words"
          >
            {log
              ? (log.text || t('settings.featureLog.empty', 'Журнал пока пуст - записи появятся после установки прицела или чего-то из «Другого».'))
              : t('settings.featureLog.loading', 'Загружаю...')}
          </pre>
        </div>
      )}
    </div>
  );
}
