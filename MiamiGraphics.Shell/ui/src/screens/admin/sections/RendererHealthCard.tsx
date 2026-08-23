import { useCallback, useEffect, useState } from 'react';
import { CheckCircle2, XCircle, Loader2, RefreshCw, Download, Beaker, AlertTriangle } from 'lucide-react';
import { bridge } from '@/bridge';
import { GlassPanel } from '@/design';
import type { RendererProbe, RendererTestRender, RendererBootstrapProgress } from '@/bridge/IAppBridge';

type Phase = 'idle' | 'probing' | 'installing' | 'testing' | 'reinstalling';

export function RendererHealthCard() {
  const [probe, setProbe] = useState<RendererProbe | null>(null);
  const [phase, setPhase] = useState<Phase>('probing');
  const [bootstrap, setBootstrap] = useState<RendererBootstrapProgress | null>(null);
  const [testResult, setTestResult] = useState<RendererTestRender | null>(null);
  const [installResultMsg, setInstallResultMsg] = useState<string | null>(null);

  const runProbe = useCallback(async () => {
    setPhase('probing');
    try {
      const p = await bridge.rendererProbe();
      setProbe(p);
    } finally {
      setPhase('idle');
    }
  }, []);

  useEffect(() => { void runProbe(); }, [runProbe]);

  useEffect(() => {
    const handler = (data: RendererBootstrapProgress) => setBootstrap(data);
    bridge.events.on('admin:rendererBootstrap', handler);
    return () => bridge.events.off('admin:rendererBootstrap', handler);
  }, []);

  const handleInstall = async () => {
    setPhase('installing');
    setBootstrap({ phase: 'downloading', percent: 0 });
    setInstallResultMsg(null);
    try {
      const r = await bridge.rendererEnsureInstalled();
      setInstallResultMsg(r.success
        ? (r.alreadyInstalled ? 'Уже было установлено.' : `Установлено. Скачано ${(r.downloadedBytes / 1024 / 1024).toFixed(1)} MB.`)
        : `Ошибка: ${r.errorMessage ?? 'unknown'}`);
      await runProbe();
    } catch (e) {
      setInstallResultMsg(`Не удалось: ${(e as Error).message}`);
    } finally {
      setPhase('idle');
      setBootstrap(null);
    }
  };

  const handleReinstall = async () => {
    if (!confirm('Удалит существующую папку Renderer/ и скачает заново (~140 MB). Продолжить?')) return;
    setPhase('reinstalling');
    setBootstrap({ phase: 'cleanup', percent: 0 });
    setInstallResultMsg(null);
    try {
      const r = await bridge.rendererForceReinstall();
      setInstallResultMsg(r.success
        ? `Переустановлено. Скачано ${(r.downloadedBytes / 1024 / 1024).toFixed(1)} MB.`
        : `Ошибка: ${r.errorMessage ?? 'unknown'}`);
      await runProbe();
    } catch (e) {
      setInstallResultMsg(`Не удалось: ${(e as Error).message}`);
    } finally {
      setPhase('idle');
      setBootstrap(null);
    }
  };

  const handleTest = async () => {
    setPhase('testing');
    setTestResult(null);
    try {
      const r = await bridge.rendererTestRender();
      setTestResult(r);
    } catch (e) {
      setTestResult({
        success: false, elapsedMs: 0, outputBytes: null, outputPath: null,
        stdoutTail: null, stderrTail: null, errorMessage: (e as Error).message,
      });
    } finally {
      setPhase('idle');
    }
  };

  const checks = probe ? [
    { label: 'Папка Renderer/',         ok: probe.baseDirExists },
    { label: 'node.exe',                ok: probe.nodeExeExists },
    { label: 'render.js',               ok: probe.renderJsExists },
    { label: `node_modules (${probe.nodeModulesSizeMb} MB)`, ok: probe.nodeModulesExists && probe.nodeModulesSizeMb >= 50 },
    { label: probe.nodeVersion ? `Node.js ${probe.nodeVersion}` : 'Node.js работает', ok: !!probe.nodeVersion && !probe.nodeError },
    { label: 'Chromium (chrome-headless-shell)', ok: !!probe.chromiumInstalled },
  ] : [];

  const busy = phase !== 'idle';

  return (
    <GlassPanel
      depth="z3" tint="ultra" rounded="3xl" highlight edge
      className="relative overflow-hidden border border-white/[0.08]"
    >
      <span
        aria-hidden
        className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                   bg-gradient-to-r from-transparent via-white/40 to-transparent"
      />
      <span
        aria-hidden
        className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
        style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
      />
      <div className="relative p-6 flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          {probe?.isUsable
            ? <CheckCircle2 size={18} className="text-status-success" />
            : <AlertTriangle size={18} className="text-status-warning" />}
          <h3 className="text-base font-semibold text-text-primary">Рендер превью ганов</h3>
        </div>
        <button
          type="button"
          onClick={() => void runProbe()}
          disabled={busy}
          title="Перепроверить состояние"
          className="inline-flex items-center justify-center w-8 h-8 rounded-lg
                     text-text-secondary hover:text-text-primary hover:bg-glass
                     disabled:opacity-50 transition-colors"
          style={{ outline: 'none' }}
        >
          <RefreshCw size={14} className={phase === 'probing' ? 'animate-spin' : ''} />
        </button>
      </div>

      <p className="text-xs text-text-muted -mt-1">
        Renderer нужен для генерации PNG-превью ганов при заливке ганпаков. Если что-то красное - нажми «Установить» или «Переустановить».
      </p>

      {probe && (
        <div className="flex flex-col gap-1.5">
          {checks.map((c, i) => (
            <div key={i} className="flex items-center gap-2 text-sm">
              {c.ok
                ? <CheckCircle2 size={14} className="text-status-success shrink-0" />
                : <XCircle size={14} className="text-status-error shrink-0" />}
              <span className={c.ok ? 'text-text-primary' : 'text-text-secondary'}>{c.label}</span>
            </div>
          ))}
        </div>
      )}

      {probe?.actionableHint && !probe.isUsable && (
        <div className="rounded-lg bg-status-warning/10 px-3 py-2 text-xs text-text-secondary leading-relaxed">
          {probe.actionableHint}
        </div>
      )}

      {bootstrap && (
        <div className="rounded-lg border border-glass-border bg-glass px-3 py-2 flex flex-col gap-1.5">
          <div className="flex items-center gap-2 text-xs">
            <Loader2 size={12} className="animate-spin text-accent" />
            <span className="text-text-secondary capitalize">{bootstrap.phase}</span>
            <span className="ml-auto text-text-muted tabular-nums">{bootstrap.percent.toFixed(0)}%</span>
          </div>
          <div className="h-1.5 rounded-full bg-glass-border overflow-hidden">
            <div className="h-full bg-accent transition-[width]" style={{ width: `${bootstrap.percent}%` }} />
          </div>
        </div>
      )}

      {installResultMsg && (
        <div className="text-xs text-text-secondary leading-relaxed">{installResultMsg}</div>
      )}

      {testResult && (
        <div className={`rounded-lg border px-3 py-2 text-xs leading-relaxed flex flex-col gap-2 ${
          testResult.success
            ? 'border-status-success/30 bg-status-success/10 text-text-primary'
            : 'border-status-error/30 bg-status-error/10 text-text-primary'}`}>
          <div>
            {testResult.success
              ? <>✓ Тест прошёл за {testResult.elapsedMs} ms. PNG {testResult.outputBytes ? `(${(testResult.outputBytes / 1024).toFixed(1)} KB)` : ''} создан.</>
              : <>✗ Тест упал ({testResult.elapsedMs} ms): {testResult.errorMessage ?? 'unknown'}</>}
          </div>
          {(testResult.stdoutTail || testResult.stderrTail) && (
            <details className="cursor-pointer">
              <summary className="text-text-muted text-[10px] uppercase tracking-wide">Логи node.exe</summary>
              {testResult.stdoutTail && (
                <pre className="mt-2 text-[10px] font-mono whitespace-pre-wrap text-text-secondary max-h-48 overflow-auto bg-black/30 rounded p-2">
{testResult.stdoutTail}
                </pre>
              )}
              {testResult.stderrTail && (
                <pre className="mt-2 text-[10px] font-mono whitespace-pre-wrap text-status-error/80 max-h-48 overflow-auto bg-black/30 rounded p-2">
{testResult.stderrTail}
                </pre>
              )}
            </details>
          )}
        </div>
      )}

      <div className="flex flex-wrap gap-2">
        {!probe?.isUsable && (
          <button
            type="button"
            onClick={() => void handleInstall()}
            disabled={busy}
            className="inline-flex items-center justify-center gap-2 px-4 py-2 rounded-lg
                       bg-accent-soft text-text-primary
                       border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                       hover:bg-[color-mix(in_srgb,var(--accent)_20%,transparent)] hover:border-accent
                       text-sm font-bold uppercase tracking-wider
                       disabled:opacity-50 transition-colors"
            style={{ outline: 'none' }}
          >
            {phase === 'installing' ? <Loader2 size={14} className="animate-spin" /> : <Download size={14} />}
            <span>Установить Renderer</span>
          </button>
        )}
        <button
          type="button"
          onClick={() => void handleTest()}
          disabled={busy || !probe?.isUsable}
          title={!probe?.isUsable ? 'Сначала установи Renderer' : 'Прогнать тестовый рендер'}
          className="inline-flex items-center gap-2 px-3 py-2 rounded-lg bg-glass border border-glass-border text-sm text-text-secondary hover:text-text-primary hover:border-accent/40 hover:bg-glass-strong disabled:opacity-50 transition-colors"
          style={{ outline: 'none' }}
        >
          {phase === 'testing' ? <Loader2 size={14} className="animate-spin" /> : <Beaker size={14} />}
          <span>Тест рендера</span>
        </button>
        {probe?.baseDirExists && (
          <button
            type="button"
            onClick={() => void handleReinstall()}
            disabled={busy}
            title="Удалить и скачать заново"
            className="inline-flex items-center gap-2 px-3 py-2 rounded-lg bg-glass border border-glass-border text-sm text-text-secondary hover:text-status-error hover:border-status-error/40 hover:bg-glass-strong disabled:opacity-50 transition-colors"
            style={{ outline: 'none' }}
          >
            {phase === 'reinstalling' ? <Loader2 size={14} className="animate-spin" /> : <RefreshCw size={14} />}
            <span>Переустановить</span>
          </button>
        )}
      </div>

      {probe?.rendererPath && (
        <div className="text-[10px] text-text-muted font-mono break-all">{probe.rendererPath}</div>
      )}
      </div>
    </GlassPanel>
  );
}
