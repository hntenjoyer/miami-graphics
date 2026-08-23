import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Loader2, Check, RefreshCw, Copy, ScrollText, HardDriveDownload } from 'lucide-react';
import { SettingsSection } from '@/components/settings/SettingsSection';
import { bridge } from '@/bridge';
import type { DownloadLogTail } from '@/bridge/types';

export function DownloadLogSection({ onToast }: {
  onToast?: (t: { tone: 'success' | 'error'; message: string }) => void;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [log, setLog] = useState<DownloadLogTail | null>(null);
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const preRef = useRef<HTMLPreElement | null>(null);

  const load = async () => {
    if (!bridge.downloadGetLog) {
      setLog({ path: null, text: t('settings.downloadLog.unsupported',
        'Эта версия лаунчера не отдаёт журнал загрузок. Обнови приложение.') });
      return;
    }
    setBusy(true);
    try {
      setLog(await bridge.downloadGetLog(64));
    } catch (e) {
      setLog({ path: null, text: t('settings.downloadLog.readFailed', {
        defaultValue: 'Не удалось прочитать журнал: {{error}}',
        error: e instanceof Error ? e.message : String(e),
      }) });
    } finally { setBusy(false); }
  };

  useEffect(() => {
    const el = preRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [log]);

  const toggle = () => {
    const next = !open;
    setOpen(next);
    if (next && !log) void load();
  };

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(log?.text ?? '');
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      onToast?.({ tone: 'error', message: t('settings.downloadLog.copyFailed', 'Не удалось скопировать журнал.') });
    }
  };

  return (
    <SettingsSection
      icon={HardDriveDownload}
      title={t('settings.downloadLog.title', 'Журнал загрузок')}
      description={t('settings.downloadLog.description',
        'Каждая загрузка оставляет здесь пару строк: с какого сервера качалось, с какой скоростью, куда и почему переезжали. Если загрузка зависла или упала - скопируй журнал и пришли в поддержку.')}
    >
      <div className="pt-1">
        <button
          type="button"
          onClick={toggle}
          className="inline-flex items-center gap-2 px-3 h-8 rounded-lg border transition-colors
                     text-[11.5px] font-semibold
                     bg-white/[0.04] text-text-secondary border-white/[0.08]
                     hover:text-text-primary hover:bg-white/[0.08]"
        >
          <ScrollText size={13} strokeWidth={2.4} />
          {open
            ? t('settings.downloadLog.hide', 'Скрыть лог')
            : t('settings.downloadLog.show', 'Лог загрузок')}
        </button>

        {open && (
          <div className="mt-2 rounded-xl bg-bg-elevated border border-border-subtle overflow-hidden">
            <div className="flex items-center justify-between gap-2 px-3 py-2 border-b border-border-subtle flex-wrap">
              <div className="text-[10.5px] text-text-muted font-mono truncate min-w-0"
                   title={log?.path ?? undefined}>
                {log?.path ?? 'downloads.log'}
              </div>
              <div className="flex items-center gap-1.5 shrink-0">
                <button
                  type="button"
                  onClick={() => void load()}
                  disabled={busy}
                  className="inline-flex items-center gap-1.5 px-2.5 h-7 rounded-md border transition-colors
                             text-[10.5px] font-semibold
                             bg-white/[0.04] text-text-secondary border-white/[0.08]
                             hover:text-text-primary hover:bg-white/[0.08]
                             disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  {busy ? <Loader2 size={12} className="animate-spin" /> : <RefreshCw size={12} strokeWidth={2.4} />}
                  {t('settings.downloadLog.refresh', 'Обновить')}
                </button>
                <button
                  type="button"
                  onClick={() => void copy()}
                  disabled={!log?.text}
                  className="inline-flex items-center gap-1.5 px-2.5 h-7 rounded-md border transition-colors
                             text-[10.5px] font-semibold
                             bg-white/[0.04] text-text-secondary border-white/[0.08]
                             hover:text-text-primary hover:bg-white/[0.08]
                             disabled:opacity-40 disabled:cursor-not-allowed"
                >
                  {copied ? <Check size={12} strokeWidth={2.6} /> : <Copy size={12} strokeWidth={2.4} />}
                  {copied
                    ? t('settings.downloadLog.copied', 'Скопировано')
                    : t('settings.downloadLog.copy', 'Скопировать')}
                </button>
              </div>
            </div>
            <pre
              ref={preRef}
              className="m-0 px-3 py-2.5 max-h-64 overflow-auto font-mono text-[10.5px] leading-relaxed
                         text-text-secondary whitespace-pre-wrap break-words"
            >
              {log
                ? (log.text || t('settings.downloadLog.empty', 'Журнал пока пуст - строки появятся после первой загрузки.'))
                : t('settings.downloadLog.loading', 'Загружаю...')}
            </pre>
          </div>
        )}
      </div>
    </SettingsSection>
  );
}
