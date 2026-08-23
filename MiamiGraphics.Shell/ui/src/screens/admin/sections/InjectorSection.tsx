import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Loader2, FileArchive, Folder, Syringe, RotateCcw, AlertTriangle, CheckCircle2, Database, Search, Timer } from 'lucide-react';
import { bridge } from '@/bridge';
import type { AdminConfig, ReduxItem, InjectResult } from '@/bridge/types';
import { Toast } from '@/components/Toast';
import { useSessionStore } from '@/store/sessionStore';

type Mode = 'local' | 'catalog';

export function InjectorSection() {
  const { t } = useTranslation();
  const systemInfo = useSessionStore(s => s.systemInfo);
  const [config, setConfig] = useState<AdminConfig | null>(null);
  const [mode, setMode] = useState<Mode>('local');
  const [moddedPath, setModdedPath] = useState('');
  const [catalog, setCatalog] = useState<ReduxItem[]>([]);
  const [selectedReduxId, setSelectedReduxId] = useState<string>('');
  const [busy, setBusy] = useState<'inject' | 'restore' | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [lastResult, setLastResult] = useState<InjectResult | null>(null);
  const [catalogQuery, setCatalogQuery] = useState('');

  const [elapsedMs, setElapsedMs] = useState<number>(0);
  const [lastDurationMs, setLastDurationMs] = useState<number | null>(null);

  useEffect(() => {
    void bridge.adminConfigGet().then(setConfig);
  }, []);

  useEffect(() => {
    if (busy !== 'inject') return;
    const start = Date.now();
    setElapsedMs(0);
    const id = window.setInterval(() => setElapsedMs(Date.now() - start), 1000);
    return () => window.clearInterval(id);
  }, [busy]);

  useEffect(() => {
    if (mode === 'catalog') {
      void bridge.adminCatalogList().then(items => {

        setCatalog(items.filter(i => i.status === 'published' && i.r2Urls?.patch));
      });
    }
  }, [mode]);

  const effectiveGtaPath = (config?.gtaPathOverride && config.gtaPathOverride.trim().length > 0)
    ? config.gtaPathOverride
    : (systemInfo?.gtaPath ?? '');
  const cleanRpfPath = config?.cleanUpdateRpfPath ?? '';

  const cleanReady = cleanRpfPath.trim().length > 0;
  const gtaReady = effectiveGtaPath.trim().length > 0;

  const ready = mode === 'catalog' ? gtaReady : (cleanReady && gtaReady);

  const onPickFile = async () => {
    const p = await bridge.openFileDialog('GTA RPF', '*.rpf');
    if (p) setModdedPath(p);
  };
  const onPickFolder = async () => {
    const p = await bridge.openFolderDialog();
    if (p) setModdedPath(p);
  };

  const onInject = async () => {
    if (busy) return;
    setBusy('inject');
    setLastResult(null);
    setLastDurationMs(null);
    const startedAt = Date.now();
    try {
      const r = mode === 'local'
        ? await bridge.adminInject(moddedPath)
        : await bridge.adminInjectFromCatalog(selectedReduxId);
      setLastResult(r);
      setToast(r.success
        ? { tone: 'success', message: t('admin.injector.injectDone') }
        : { tone: 'error', message: r.errorMessage ?? t('admin.injector.injectFailed') });
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setLastDurationMs(Date.now() - startedAt);
      setBusy(null);
    }
  };

  const onRestore = async () => {
    if (busy) return;
    if (!confirm(t('admin.injector.restoreConfirm'))) return;
    setBusy('restore');
    try {
      await bridge.adminRestoreCleanUpdate();
      setToast({ tone: 'success', message: t('admin.injector.restoreDone') });
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setBusy(null);
    }
  };

  const filteredCatalog = useMemo(() => {
    const q = catalogQuery.trim().toLowerCase();
    if (!q) return catalog;
    return catalog.filter(it =>
      it.id.toLowerCase().includes(q) ||
      (it.name ?? '').toLowerCase().includes(q) ||
      (it.author ?? '').toLowerCase().includes(q));
  }, [catalog, catalogQuery]);

  if (!config) return <div className="p-8 text-text-muted">…</div>;

  const canInject =
    ready && !!busy === false &&
    (mode === 'local' ? !!moddedPath : !!selectedReduxId);

  return (
    <div className="h-full overflow-y-auto px-8 py-6">
      <div className="max-w-[680px] mx-auto flex flex-col gap-6">
        <header>
          <h1 className="text-lg font-bold text-text-primary">{t('admin.injector.title')}</h1>
          <p className="text-sm text-text-muted mt-1">{t('admin.injector.subtitle')}</p>
        </header>

        <Card>
          <PathRow
            label={t('admin.injector.gtaPath')}
            value={effectiveGtaPath}
            okay={gtaReady}
            hintIfMissing={t('admin.injector.gtaPathMissing')}
          />
          <PathRow
            label={t('admin.injector.cleanRpf')}
            value={cleanRpfPath}
            okay={cleanReady}
            hintIfMissing={t('admin.injector.cleanRpfMissing')}
          />
        </Card>

        {}
        <div className="flex gap-1 p-1 rounded-xl bg-bg-elevated border border-border-subtle self-start">
          <ModeTab active={mode === 'local'}   onClick={() => setMode('local')}>
            <FileArchive size={14} /> {t('admin.injector.modeLocal')}
          </ModeTab>
          <ModeTab active={mode === 'catalog'} onClick={() => setMode('catalog')}>
            <Database size={14} /> {t('admin.injector.modeCatalog')}
          </ModeTab>
        </div>

        {mode === 'local' && (
          <Card>
            <label className="text-xs uppercase tracking-wider text-text-muted">
              {t('admin.injector.sourceLabel')}
            </label>
            <input
              type="text"
              value={moddedPath}
              onChange={e => setModdedPath(e.target.value)}
              placeholder={t('admin.injector.sourcePlaceholder')}
              className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent font-mono"
            />
            <div className="flex gap-2">
              <button
                type="button"
                onClick={onPickFile}
                className="flex-1 inline-flex items-center justify-center gap-2 px-4 py-2 rounded-lg bg-bg-elevated border border-border-subtle text-sm text-text-primary hover:bg-bg-surface"
              >
                <FileArchive size={14} /> {t('admin.injector.pickFile')}
              </button>
              <button
                type="button"
                onClick={onPickFolder}
                className="flex-1 inline-flex items-center justify-center gap-2 px-4 py-2 rounded-lg bg-bg-elevated border border-border-subtle text-sm text-text-primary hover:bg-bg-surface"
              >
                <Folder size={14} /> {t('admin.injector.pickFolder')}
              </button>
            </div>
          </Card>
        )}

        {mode === 'catalog' && (
          <Card>
            <label className="text-xs uppercase tracking-wider text-text-muted">
              {t('admin.injector.catalogLabel')}
            </label>
            {catalog.length === 0 ? (
              <p className="text-sm text-text-muted">{t('admin.injector.catalogEmpty')}</p>
            ) : (
              <>
                <div className="relative">
                  <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-text-muted" />
                  <input
                    type="text"
                    value={catalogQuery}
                    onChange={e => setCatalogQuery(e.target.value)}
                    placeholder={t('admin.injector.catalogSearchPlaceholder')}
                    className="w-full pl-9 pr-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
                  />
                </div>
                <ul className="flex flex-col gap-1 max-h-72 overflow-y-auto">
                  {filteredCatalog.length === 0 && (
                    <li className="text-xs text-text-muted px-3 py-2">{t('admin.injector.catalogNoMatch')}</li>
                  )}
                  {filteredCatalog.map(it => {
                    const active = it.id === selectedReduxId;
                    return (
                      <li key={it.id}>
                        <button
                          type="button"
                          onClick={() => setSelectedReduxId(it.id)}
                          className={
                            'w-full text-left px-3 py-2 rounded-lg border text-sm transition-colors ' +
                            (active
                              ? 'bg-accent-soft border-accent text-text-primary'
                              : 'bg-bg-elevated border-border-subtle text-text-secondary hover:bg-bg-surface hover:text-text-primary')
                          }
                        >
                          <div className="flex items-baseline justify-between gap-3">
                            <span className="font-medium truncate">{it.name || it.id}</span>
                            <span className="text-xs text-text-muted tabular-nums shrink-0">
                              {(it.patchSizeBytes / (1024 * 1024)).toFixed(0)} MB
                            </span>
                          </div>
                          <div className="flex items-baseline justify-between gap-3 text-xs text-text-muted mt-0.5">
                            <span className="font-mono truncate">{it.id}</span>
                            {it.author && <span className="truncate shrink-0">{it.author}</span>}
                          </div>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              </>
            )}
          </Card>
        )}

        <div className="flex flex-col gap-3">
          <button
            type="button"
            onClick={onInject}
            disabled={!canInject}
            className="inline-flex items-center justify-center gap-2 px-5 py-3 rounded-xl bg-accent text-text-on-accent font-semibold hover:bg-accent-hover disabled:opacity-50 disabled:cursor-not-allowed shadow-[0_4px_20px_var(--accent-soft)]"
          >
            {busy === 'inject' ? <Loader2 size={16} className="animate-spin" /> : <Syringe size={16} />}
            {busy === 'inject'
              ? `${t('admin.injector.injectBusy')} · ${formatDuration(elapsedMs)}`
              : t('admin.injector.injectButton')}
          </button>
          {!busy && lastDurationMs !== null && (
            <div className="inline-flex items-center justify-center gap-2 text-xs text-text-muted">
              <Timer size={12} />
              {t('admin.injector.lastDuration', { duration: formatDuration(lastDurationMs) })}
            </div>
          )}
          <button
            type="button"
            onClick={onRestore}
            disabled={!ready || !!busy}
            className="inline-flex items-center justify-center gap-2 px-5 py-3 rounded-xl border border-border-strong text-sm font-medium text-text-primary hover:bg-bg-elevated disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {busy === 'restore' ? <Loader2 size={16} className="animate-spin" /> : <RotateCcw size={16} />}
            {busy === 'restore' ? t('admin.injector.restoreBusy') : t('admin.injector.restoreButton')}
          </button>
        </div>

        {lastResult?.workDir && (
          <Card>
            <span className="text-xs uppercase tracking-wider text-text-muted">{t('admin.injector.lastWorkDir')}</span>
            <code className="text-xs text-text-secondary font-mono break-all" title={lastResult.workDir}>
              {lastResult.workDir}
            </code>
            <p className="text-xs text-text-muted">{t('admin.injector.workDirHint')}</p>
          </Card>
        )}
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 12000 : 4000}
      />
    </div>
  );
}

function formatDuration(ms: number): string {
  const totalSec = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(totalSec / 3600);
  const m = Math.floor((totalSec % 3600) / 60);
  const s = totalSec % 60;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}

function Card({ children }: { children: React.ReactNode }) {
  return (
    <div className="rounded-2xl bg-bg-surface border border-border-subtle p-5 flex flex-col gap-3">
      {children}
    </div>
  );
}

function ModeTab({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        'inline-flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-colors ' +
        (active
          ? 'bg-accent text-text-on-accent shadow-[0_2px_10px_var(--accent-soft)]'
          : 'text-text-muted hover:text-text-primary')
      }
    >
      {children}
    </button>
  );
}

function PathRow({ label, value, okay, hintIfMissing }: { label: string; value: string; okay: boolean; hintIfMissing: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
      {okay ? (
        <div className="flex items-center gap-2">
          <CheckCircle2 size={14} style={{ color: 'var(--status-success)' }} className="shrink-0" />
          <code className="text-sm text-text-primary font-mono truncate" title={value}>{value}</code>
        </div>
      ) : (
        <div className="flex items-center gap-2">
          <AlertTriangle size={14} style={{ color: 'var(--status-warning)' }} className="shrink-0" />
          <span className="text-sm text-text-secondary">{hintIfMissing}</span>
        </div>
      )}
    </div>
  );
}
