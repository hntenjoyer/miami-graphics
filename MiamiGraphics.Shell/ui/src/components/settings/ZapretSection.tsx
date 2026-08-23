import { useEffect, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { Shield, ExternalLink, FolderOpen, Check, AlertTriangle, Loader2, RotateCw } from 'lucide-react';
import { SettingsSection } from '@/components/settings/SettingsSection';
import { bridge } from '@/bridge';
import type { ZapretApplyResult } from '@/bridge/IAppBridge';

export const ZAPRET_PATH_KEY = 'hntgraph.zapretPath';
function loadZapretPath(): string {
  try { return localStorage.getItem(ZAPRET_PATH_KEY) ?? ''; } catch { return ''; }
}

export function ZapretSection() {
  const { t } = useTranslation();
  const [path, setPath]       = useState<string>(loadZapretPath);
  const [busy, setBusy]       = useState(false);
  const [result, setResult]   = useState<ZapretApplyResult | null>(null);

  useEffect(() => {
    try { localStorage.setItem(ZAPRET_PATH_KEY, path); } catch {  }
  }, [path]);

  const onPickFolder = async () => {
    try {
      const picked = await bridge.openFolderDialog();
      if (picked) setPath(picked);
    } catch (e) {
      console.warn('[zapret] folder pick failed', e);
    }
  };

  const onApply = async () => {
    if (!path.trim() || busy) return;
    setBusy(true);
    setResult(null);
    try {
      const r = await bridge.zapretApplyWhitelist(path.trim());
      setResult(r);
      if (r.success) {
        void bridge.zapretDetect(path.trim()).catch(() => {});
      }
    } catch (e) {
      setResult({
        success: false,
        errorMessage: e instanceof Error ? e.message : String(e),
        domainLinesAdded: 0,
        ipsetLinesAdded: 0,
        listsDir: null,
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <SettingsSection
      icon={Shield}
      title={t('settings.zapret.title', 'Zapret - обход блокировок (РФ)')}
      description={t('settings.zapret.description',
        'Для юзеров из РФ - добавляем miamigraphicsstorage.uk и IP-диапазоны Cloudflare в локальный whitelist Zapret. Это даёт максимальную скорость скачки модов через DPI-обход.')}
    >
      <div className="flex flex-col gap-3">
        <a
          href="https://github.com/Flowseal/zapret-discord-youtube/releases/latest"
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center gap-1.5 text-[12px] text-accent hover:underline self-start"
        >
          <ExternalLink size={11} />
          {t('settings.zapret.downloadLink', 'Скачать Zapret (github.com/flowseal/zapret-discord-youtube)')}
        </a>

        <div className="flex flex-col gap-1.5">
          <label className="text-[11px] uppercase tracking-[0.18em] text-text-muted">
            {t('settings.zapret.pathLabel', 'Путь к папке Zapret')}
          </label>
          <div className="flex items-stretch gap-2">
            <input
              type="text"
              value={path}
              onChange={(e) => setPath(e.target.value)}
              placeholder={t('settings.zapret.pathPlaceholder', 'Например: C:\\zapret-discord-youtube')}
              className="flex-1 px-3 h-10 rounded-lg bg-bg-elevated-soft border border-border-subtle
                         text-[13px] text-text-primary placeholder-text-muted
                         outline-none focus:border-accent transition-colors font-mono"
            />
            <button
              type="button"
              onClick={onPickFolder}
              disabled={busy}
              className="inline-flex items-center gap-1.5 px-3 h-10 rounded-lg
                         bg-bg-elevated-soft hover:bg-bg-elevated
                         border border-border-subtle hover:border-border-strong
                         text-[12px] text-text-secondary hover:text-text-primary
                         transition-colors disabled:opacity-50"
            >
              <FolderOpen size={13} />
              <span>{t('settings.zapret.pick', 'Выбрать')}</span>
            </button>
          </div>
          <p className="text-[11px] text-text-muted">
            <Trans
              i18nKey="settings.zapret.pathHint"
              defaults="Это та папка где лежат <bat>general.bat</bat>, <lists>lists/</lists> и <bin>bin/</bin>."
              components={{
                bat:   <code className="font-mono" />,
                lists: <code className="font-mono" />,
                bin:   <code className="font-mono" />,
              }}
            />
          </p>
        </div>

        <button
          type="button"
          onClick={onApply}
          disabled={!path.trim() || busy}
          className="inline-flex items-center justify-center gap-2 h-10 px-4 rounded-lg
                     bg-accent-soft text-text-primary
                     border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                     hover:bg-[color-mix(in_srgb,var(--accent)_20%,transparent)] hover:border-accent
                     disabled:opacity-50 disabled:cursor-not-allowed
                     transition-colors text-[13px] font-bold uppercase tracking-wider self-start"
        >
          {busy ? <Loader2 size={14} className="animate-spin" /> : <Shield size={14} />}
          <span>{busy
            ? t('settings.zapret.applying', 'Применяю…')
            : t('settings.zapret.apply', 'Применить к Zapret')}</span>
        </button>

        {result && (
          <div
            className={`flex items-start gap-2 px-3 py-2.5 rounded-lg border text-[12.5px] ${
              result.success
                ? 'bg-[color-mix(in_srgb,#22c55e_8%,transparent)] border-[color-mix(in_srgb,#22c55e_30%,transparent)] text-text-primary'
                : 'bg-[color-mix(in_srgb,#ef4444_8%,transparent)] border-[color-mix(in_srgb,#ef4444_30%,transparent)] text-text-primary'
            }`}
          >
            {result.success ? (
              <Check size={14} className="shrink-0 mt-0.5 text-[#22c55e]" />
            ) : (
              <AlertTriangle size={14} className="shrink-0 mt-0.5 text-[#ef4444]" />
            )}
            <div className="flex-1 leading-relaxed">
              {result.success ? (
                result.domainLinesAdded + result.ipsetLinesAdded === 0 ? (
                  <>
                    <div className="font-semibold mb-1">
                      {t('settings.zapret.alreadyTitle', 'У вас уже установлены наши IP адреса.')}
                    </div>
                    <div className="text-text-secondary">
                      {t('settings.zapret.alreadyBody', 'Домен и IP-диапазоны уже прописаны в Zapret - ничего менять не нужно.')}
                    </div>
                  </>
                ) : (
                  <>
                    <div className="font-semibold mb-1">{t('settings.zapret.doneTitle', 'Готово.')}</div>
                    <div className="text-text-secondary">
                      {t('settings.zapret.doneBody', {
                        defaultValue: 'Добавлено: домен - {{domains}}, IP-диапазоны - {{ipsets}}.',
                        domains: result.domainLinesAdded,
                        ipsets:  result.ipsetLinesAdded,
                      })}
                    </div>
                    <div className="mt-2 inline-flex items-center gap-1.5 text-accent">
                      <RotateCw size={11} />
                      {t('settings.zapret.restartHint', 'Перезапусти Zapret (general.bat), чтобы изменения вступили в силу.')}
                    </div>
                  </>
                )
              ) : (
                <span>{result.errorMessage ?? t('settings.zapret.applyFailed', 'Не удалось применить.')}</span>
              )}
            </div>
          </div>
        )}
      </div>
    </SettingsSection>
  );
}
