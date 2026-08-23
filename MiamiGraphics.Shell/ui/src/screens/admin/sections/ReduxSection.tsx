import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Plus, Play, Square, Upload, FileArchive, X, Loader2, CheckCircle2, AlertTriangle, Clock,
  ChevronDown, ChevronRight, Copy, ClipboardPaste, RefreshCw,
} from 'lucide-react';
import { useAdminStore } from '@/store/adminStore';
import { useSessionStore } from '@/store/sessionStore';
import { bridge } from '@/bridge';
import type { QueueItem, ReduxAnalysis, ReduxItem, ComponentInfo, DuplicateHashMatch, VersionSpec } from '@/bridge/types';
import { Toast } from '@/components/Toast';
import { GlassPanel } from '@/design';

function extractYouTubeId(url: string): string | null {
  const m = url.match(/(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^"&?\/\s]{11})/);
  return m ? m[1] : null;
}

type Stage = 'queue' | 'source' | 'metadata';

export function ReduxSection() {
  const { t } = useTranslation();
  const queue = useAdminStore(s => s.queue);
  const loadQueue = useAdminStore(s => s.loadQueue);
  const removeFromQueue = useAdminStore(s => s.removeFromQueue);
  const runQueue = useAdminStore(s => s.runQueue);
  const analysis = useAdminStore(s => s.analysis);
  const analysisPending = useAdminStore(s => s.analysisPending);
  const analysisError = useAdminStore(s => s.analysisError);
  const draftSource = useAdminStore(s => s.draftSourcePath);
  const setDraftSource = useAdminStore(s => s.setDraftSource);
  const setAnalysis = useAdminStore(s => s.setAnalysis);
  const setAnalysisError = useAdminStore(s => s.setAnalysisError);
  const startBackgroundAnalyze = useAdminStore(s => s.startBackgroundAnalyze);
  const clearAnalysis = useAdminStore(s => s.clearAnalysis);

  const [dupMatch, setDupMatch] = useState<DuplicateHashMatch | null>(null);
  const rewriteSeed = useAdminStore(s => s.rewriteSeed);
  const appendVersionSeed = useAdminStore(s => s.appendVersionSeed);
  const setAppendVersionSeed = useAdminStore(s => s.setAppendVersionSeed);

  const [stage, setStage] = useState<Stage>('queue');
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => { void loadQueue(); }, [loadQueue]);

  const handledSeedRef = useRef<string | null>(null);
  useEffect(() => {
    if (!rewriteSeed) return;
    if (handledSeedRef.current === rewriteSeed.id) return;
    handledSeedRef.current = rewriteSeed.id;
    setAnalysis(null);
    setDraftSource(null);
    setStage('source');
  }, [rewriteSeed, setAnalysis, setDraftSource]);

  const handledAppendRef = useRef<string | null>(null);
  useEffect(() => {
    if (!appendVersionSeed) return;
    const key = `${appendVersionSeed.parent.id}:${appendVersionSeed.nextSlot}`;
    if (handledAppendRef.current === key) return;
    handledAppendRef.current = key;
    setAnalysis(null);
    setDraftSource(null);
    setStage('source');
  }, [appendVersionSeed, setAnalysis, setDraftSource]);

  const handledDones = useRef<Set<string>>(new Set());
  useEffect(() => {
    for (const item of queue) {
      if (item.status !== 'done' || handledDones.current.has(item.tempId)) continue;
      handledDones.current.add(item.tempId);
      setToast({
        tone: 'success',
        message: t('admin.redux.queueDone', { name: item.metadata.name || '-' }),
      });
      const id = item.tempId;
      window.setTimeout(() => { void removeFromQueue(id); }, 4000);

      const reduxId = item.metadata.id;
      const hasArmor = !!item.metadata.components?.armor?.isFound;
      if (reduxId && hasArmor) {
        void bridge.reduxArmorRenderPreview(reduxId)
          .then(url => {
            if (url) console.log('[redux.upload] armor preview ready', reduxId, url);
          })
          .catch(e => console.warn('[redux.upload] armor preview render failed', e));
      }
    }
  }, [queue, removeFromQueue, t]);

  const onAddNew = () => { clearAnalysis(); setStage('source'); };
  const backToQueue = () => {
    clearAnalysis();
    setAppendVersionSeed(null);
    handledSeedRef.current = null;
    handledAppendRef.current = null;
    setStage('queue');
  };

  const goMetadata = (sourcePath: string) => {
    setDraftSource(sourcePath);
    setAnalysis(null);
    setAnalysisError(null);
    setStage('metadata');
    void runBackgroundAnalyze(sourcePath);
  };

  const runBackgroundAnalyze = async (sourcePath: string) => {
    let a: ReduxAnalysis;
    try {
      a = await startBackgroundAnalyze(sourcePath);
    } catch {
      return;
    }
    if (!a.sourceSha256) return;
    try {
      const match = await bridge.adminFindByHash(a.sourceSha256);
      if (match) setDupMatch(match);
    } catch (dupErr) {
      console.warn('[redux.upload] adminFindByHash failed; skipping dup check', dupErr);
    }
  };

  const onDupCancel = () => { setDupMatch(null); backToQueue(); };
  const onDupContinue = () => { setDupMatch(null);  };

  return (
    <div className="h-full flex flex-col">
      <header className="h-14 px-6 flex items-center gap-3 border-b border-border-subtle shrink-0">
        <h1 className="text-base font-bold text-text-primary">{t('admin.redux.title')}</h1>
        <div className="flex-1" />
        {stage === 'queue' && (
          <>
            <button
              type="button"
              onClick={onAddNew}
              className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated transition-colors"
            >
              <Plus size={14} />
              <span>{t('admin.redux.addNew')}</span>
            </button>
            <button
              type="button"
              disabled={queue.filter(x => x.status === 'pending').length === 0}
              onClick={() => void runQueue()}
              className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg bg-accent text-text-on-accent text-sm hover:bg-accent-hover transition-colors disabled:opacity-50"
            >
              <Play size={14} />
              <span>{t('admin.redux.runQueue')}</span>
            </button>
          </>
        )}
        {stage !== 'queue' && (
          <button
            type="button"
            onClick={backToQueue}
            className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated transition-colors"
          >
            <X size={14} />
            <span>{t('admin.redux.cancel')}</span>
          </button>
        )}
      </header>

      <div className="flex-1 overflow-hidden">
        {stage === 'queue' && (
          <QueueView queue={queue} onRemove={removeFromQueue} />
        )}
        {stage === 'source' && (
          <SourceStage onContinue={src => goMetadata(src)} />
        )}
        {stage === 'metadata' && draftSource && appendVersionSeed && (
          <AppendVersionStage
            analysis={analysis}
            analysisPending={analysisPending}
            analysisError={analysisError}
            sourcePath={draftSource}
            seed={appendVersionSeed}
            onDone={backToQueue}
            onAdded={() => setToast({
              tone: 'success',
              message: t('admin.redux.appendAddedToQueue', {
                slot: appendVersionSeed.nextSlot,
                name: appendVersionSeed.parent.name,
              }),
            })}
          />
        )}
        {stage === 'metadata' && draftSource && !appendVersionSeed && (
          <MetadataStage
            analysis={analysis}
            analysisPending={analysisPending}
            analysisError={analysisError}
            sourcePath={draftSource}
            rewriteSeed={rewriteSeed}
            onDone={backToQueue}
            onAdded={() => setToast({
              tone: 'success',
              message: rewriteSeed
                ? t('admin.redux.rewriteAddedToQueue', { name: rewriteSeed.name })
                : t('admin.redux.addedToQueue'),
            })}
          />
        )}
      </div>

      {dupMatch && (
        <DuplicateHashDialog match={dupMatch} onCancel={onDupCancel} onContinue={onDupContinue} />
      )}

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
      />
    </div>
  );
}

function QueueView({ queue, onRemove }: { queue: QueueItem[]; onRemove: (id: string) => Promise<void> }) {
  const { t } = useTranslation();
  if (queue.length === 0) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-text-muted">
        <FileArchive size={48} className="mb-3 opacity-50" />
        <p className="text-sm">{t('admin.redux.queueEmpty')}</p>
      </div>
    );
  }
  return (
    <div className="h-full overflow-y-auto px-6 py-4 flex flex-col gap-2">
      {queue.map(it => <QueueRow key={it.tempId} item={it} onRemove={onRemove} />)}
    </div>
  );
}

function QueueRow({ item, onRemove }: { item: QueueItem; onRemove: (id: string) => Promise<void> }) {
  const { t } = useTranslation();
  const sizeM = (item.metadata.patchSizeBytes / (1024 * 1024)).toFixed(0);
  const componentCount = Object.values(item.metadata.components).filter(c => c.isFound).length;

  return (
    <div className="flex flex-col gap-2 p-3 rounded-xl bg-bg-surface border border-border-subtle">
      <div className="flex items-center gap-4">
        <div className="w-12 h-12 rounded-lg bg-bg-elevated flex items-center justify-center shrink-0 text-text-muted">
          <FileArchive size={20} />
        </div>
        <div className="flex-1 min-w-0">
          <div className="text-sm font-semibold text-text-primary truncate">{item.metadata.name || '-'}</div>
          <div className="text-xs text-text-muted truncate">
            {sizeM} {t('admin.queue.mb')} · {componentCount} {t('admin.queue.components')}
          </div>
        </div>
        {item.uploadToR2 && (
          <span className="text-[10px] uppercase tracking-wider text-accent border border-accent rounded px-1.5 py-0.5">R2</span>
        )}
        <StatusBadge item={item} />
        {(item.status === 'pending' || item.status === 'done' || item.status === 'error') && (
          <button
            type="button"
            onClick={() => void onRemove(item.tempId)}
            className="w-7 h-7 rounded-md flex items-center justify-center text-text-muted hover:text-status-error hover:bg-bg-elevated transition-colors"
            aria-label="remove"
          >
            <X size={14} />
          </button>
        )}
      </div>
      {item.status === 'error' && item.errorMessage && (
        <div
          className="ml-16 text-xs font-mono px-3 py-2 rounded-lg bg-bg-base border border-status-error/30"
          style={{ color: 'var(--status-error)' }}
        >
          {item.errorMessage}
        </div>
      )}
    </div>
  );
}

function StatusBadge({ item }: { item: QueueItem }) {
  const { t } = useTranslation();
  switch (item.status) {
    case 'pending':
      return (
        <span className="flex items-center gap-1 text-xs text-text-muted">
          <Clock size={12} /> {t('admin.queue.pending')}
        </span>
      );
    case 'processing':
      return (
        <span className="flex items-center gap-2 text-xs text-accent">
          <Loader2 size={12} className="animate-spin" />
          <span>{item.currentPhase} · {item.percent ?? 0}%</span>
        </span>
      );
    case 'done':
      return (
        <span className="flex items-center gap-1 text-xs" style={{ color: 'var(--status-success)' }}>
          <CheckCircle2 size={12} /> {t('admin.queue.done')}
        </span>
      );
    case 'error':
      return (
        <span className="flex items-center gap-1 text-xs" style={{ color: 'var(--status-error)' }} title={item.errorMessage ?? ''}>
          <AlertTriangle size={12} /> {t('admin.queue.error')}
        </span>
      );
  }
}

interface SourceStageProps {
  onContinue: (sourcePath: string) => void;
}

function SourceStage({ onContinue }: SourceStageProps) {
  const { t } = useTranslation();
  const [path, setPath] = useState('');

  const onPickFile = async () => {
    const p = await bridge.openFileDialog(t('admin.redux.archiveOrRpf'), '*.zip;*.rar;*.rpf');
    if (p) setPath(p);
  };

  return (
    <div className="h-full overflow-y-auto px-8 py-6">
      <div className="max-w-[600px] mx-auto flex flex-col gap-5">
        <h2 className="text-sm font-semibold text-text-primary">{t('admin.redux.sourceTitle')}</h2>

        <button
          type="button"
          onClick={onPickFile}
          className="h-48 rounded-2xl border-2 border-dashed border-border-strong hover:border-accent flex flex-col items-center justify-center gap-2 text-text-muted hover:text-text-primary transition-colors"
        >
          <Upload size={32} />
          <span className="text-sm font-medium">{t('admin.redux.dropZone')}</span>
          <span className="text-xs">{t('admin.redux.supports')}</span>
        </button>

        <div className="flex items-center gap-3 text-xs uppercase tracking-wider text-text-muted">
          <div className="flex-1 h-px bg-border-subtle" />
          <span>{t('common.or')}</span>
          <div className="flex-1 h-px bg-border-subtle" />
        </div>

        <div className="flex flex-col gap-2">
          <label className="text-xs uppercase tracking-wider text-text-muted">{t('admin.redux.directPath')}</label>
          <input
            type="text"
            value={path}
            onChange={e => setPath(e.target.value)}
            placeholder="C:\path\to\update.rpf"
            className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent transition-colors"
          />
        </div>

        <button
          type="button"
          onClick={() => onContinue(path.trim())}
          disabled={!path.trim()}
          className="self-end inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-accent text-text-on-accent font-medium hover:bg-accent-hover transition-colors disabled:opacity-50"
        >
          <span>{t('admin.redux.continue', 'Продолжить')}</span>
        </button>

        <p className="text-xs text-text-muted leading-relaxed">
          Анализ запустится в фоне после нажатия «Продолжить» - заполняй метаданные
          параллельно, а кнопка «Сохранить» активируется когда парсер закончит.
        </p>
      </div>
    </div>
  );
}

function DuplicateHashDialog({
  match, onCancel, onContinue,
}: {
  match: DuplicateHashMatch;
  onCancel: () => void;
  onContinue: () => void;
}) {
  const { t } = useTranslation();
  const isLegacy = match.label === '(legacy)' || match.slot === 0;
  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6" onClick={onCancel}>
      <div
        className="w-full max-w-[480px] rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-start gap-3">
          <AlertTriangle size={20} className="text-status-warning shrink-0 mt-0.5" />
          <div className="flex flex-col gap-1">
            <h2 className="text-base font-bold text-text-primary">
              {t('admin.redux.dup.title')}
            </h2>
            <p className="text-sm text-text-secondary">
              {isLegacy
                ? t('admin.redux.dup.bodyLegacy', { name: match.reduxName })
                : t('admin.redux.dup.bodyVersion', { name: match.reduxName, slot: match.slot, label: match.label })}
            </p>
          </div>
        </div>
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onCancel}
            className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated"
          >
            {t('admin.redux.dup.cancel')}
          </button>
          <button
            type="button"
            onClick={onContinue}
            className="px-4 py-2 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover"
          >
            {t('admin.redux.dup.continue')}
          </button>
        </div>
      </div>
    </div>
  );
}

interface MetadataStageProps {
  analysis: ReduxAnalysis | null;
  analysisPending: boolean;
  analysisError: string | null;
  sourcePath: string;
  rewriteSeed: ReduxItem | null;
  onDone: () => void;
  onAdded?: () => void;
}

function MetadataStage({ analysis, analysisPending, analysisError, sourcePath, rewriteSeed, onDone, onAdded }: MetadataStageProps) {
  const { t } = useTranslation();
  const addToQueue = useAdminStore(s => s.addToQueue);
  const uploaderUsername = useSessionStore(s => s.auth?.username) ?? 'admin';

  const slugify = (s: string) => s.toLowerCase().trim()
    .replace(/[^a-z0-9а-яё]+/g, '_').replace(/^_|_$/g, '');

  const seedServer = (() => {
    if (!rewriteSeed) return 'majestic' as const;
    const s = new Set(rewriteSeed.supportedServers);
    if (s.has('majestic') && s.has('gta5rp')) return 'both' as const;
    if (s.has('gta5rp')) return 'gta5rp' as const;
    return 'majestic' as const;
  })();

  const [name, setName] = useState(rewriteSeed?.name ?? '');
  const [id, setId] = useState(rewriteSeed?.id ?? '');
  const [author, setAuthor] = useState(rewriteSeed?.author ?? '');
  const [authorLink, setAuthorLink] = useState(rewriteSeed?.authorLink ?? '');
  const [description, setDescription] = useState(rewriteSeed?.description ?? '');
  const [videoUrl, setVideoUrl] = useState(rewriteSeed?.videoUrl ?? '');
  const [previewUrl, setPreviewUrl] = useState(rewriteSeed?.previewUrl ?? '');
  const [previewOverridden, setPreviewOverridden] = useState(!!rewriteSeed?.previewUrl);
  const [uploadingPreview, setUploadingPreview] = useState(false);
  const [componentScreenshots, setComponentScreenshots] = useState<Record<string, string>>(
    rewriteSeed?.componentScreenshots ?? {});
  const setComponentScreenshot = (key: string, url: string) =>
    setComponentScreenshots(prev => {
      const next = { ...prev };
      const trimmed = url.trim();
      if (trimmed) next[key] = trimmed;
      else delete next[key];
      return next;
    });
  const [server, setServer] = useState<'majestic' | 'gta5rp' | 'both'>(seedServer);
  const [kaptEnabled, setKaptEnabled] = useState(!!rewriteSeed?.supportedServers?.includes('kapt'));
  const [rpEnabled,   setRpEnabled]   = useState(!!rewriteSeed?.supportedServers?.includes('rp'));
  const [verified, setVerified] = useState(rewriteSeed?.isVerified ?? false);
  const [uploadToR2, setUploadToR2] = useState(true);

  const [versions, setVersions] = useState<VersionDraft[]>([]);

  const seededRef = useRef(false);
  useEffect(() => {
    if (!analysis || seededRef.current) return;
    seededRef.current = true;
    setVersions([{
      uid: crypto.randomUUID(),
      slot: 1,
      label: 'V1',
      sourcePath,
      analysis,
    }]);
  }, [analysis, sourcePath]);

  const firstAnalysis: ReduxAnalysis | null = versions[0]?.analysis ?? analysis ?? null;
  const components = firstAnalysis?.components ?? {};
  const setComponents = (next: Record<string, ComponentInfo>) => {
    setVersions(prev => prev.length === 0 ? prev : [
      { ...prev[0], analysis: { ...prev[0].analysis, components: next } },
      ...prev.slice(1),
    ]);
  };

  const [error, setError] = useState<string | null>(null);
  const [importOpen, setImportOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  const onVideoChange = (url: string) => {
    setVideoUrl(url);
    if (previewOverridden) return;
    const yid = extractYouTubeId(url);
    if (yid) setPreviewUrl(`https://i.ytimg.com/vi/${yid}/maxresdefault.jpg`);
  };
  const onPreviewChange = (url: string) => { setPreviewUrl(url); setPreviewOverridden(true); };
  const onUploadPreview = async () => {
    if (uploadingPreview) return;
    try {
      const path = await bridge.openFileDialog('Изображение превью (PNG/JPG/WEBP/GIF)', '*.png;*.jpg;*.jpeg;*.webp;*.gif');
      if (!path) return;
      setUploadingPreview(true);
      const url = await bridge.adminUploadGunpackCover(path);
      onPreviewChange(url);
    } catch (e) {
      console.error('[redux.uploadPreview] fail', e);
    } finally {
      setUploadingPreview(false);
    }
  };

  const sizeM = firstAnalysis ? (firstAnalysis.sizeBytes / (1024 * 1024)).toFixed(1) : '-';
  const foundComponents = firstAnalysis
    ? Object.entries(firstAnalysis.components).filter(([, c]) => c.isFound)
    : [];

  const canSubmit = !!firstAnalysis && !analysisPending && !analysisError;

  const handleNameChange = (v: string) => {
    setName(v);
    if (!rewriteSeed) setId(slugify(v));
  };

  const onSubmit = async () => {
    if (!name.trim()) {
      setError(t('admin.redux.errorNameRequired'));
      return;
    }
    if (versions.length === 0 || !versions[0]?.analysis) {
      setError('Анализ ещё не завершён, подожди.');
      return;
    }
    setError(null);
    setBusy(true);
    try {
      const safeComponents: Record<string, ComponentInfo> = {};
      for (const [k, c] of Object.entries(components)) {
        safeComponents[k] = {
          isFound: !!c.isFound,
          sourceRpf: c.sourceRpf ?? '',
          internalPaths: c.internalPaths ?? [],
          flags: c.flags ?? [],
        };
      }

      const isMultiVersion = !rewriteSeed;
      const firstVersion = versions[0];

      if (isMultiVersion) {
        const trimmed = versions.map(v => v.label.trim());
        if (trimmed.some(l => !l)) {
          setError(t('admin.redux.versions.errLabelRequired'));
          setBusy(false);
          return;
        }
        const slots = versions.map(v => v.slot);
        if (new Set(slots).size !== slots.length) {
          setError(t('admin.redux.versions.errSlotDup'));
          setBusy(false);
          return;
        }
      }

      const item: ReduxItem = {
        id: rewriteSeed?.id ?? (id || slugify(name)),
        name: name.trim(),
        author: author.trim(),
        authorLink: authorLink.trim(),
        description: description.trim(),
        videoUrl: videoUrl.trim(),
        previewUrl: previewUrl.trim(),
        galleryUrls: rewriteSeed?.galleryUrls ?? [],
        r2Urls: rewriteSeed?.r2Urls ?? null,
        patchSizeBytes: firstVersion.analysis.sizeBytes,
        targetGtaVersion: firstVersion.analysis.targetGtaVersion,
        supportedServers: [
          ...(server === 'both' ? ['majestic', 'gta5rp'] : [server]),
          ...(kaptEnabled ? ['kapt'] : []),
          ...(rpEnabled ? ['rp'] : []),
        ],
        isVerified: verified,
        components: safeComponents,
        uploadedAt: new Date().toISOString(),
        uploadedBy: uploaderUsername,
        status: rewriteSeed?.status ?? 'published',
        viewerPriority: rewriteSeed?.viewerPriority ?? 0,
        downloadCount: rewriteSeed?.downloadCount ?? 0,
        tagNew:  rewriteSeed?.tagNew  ?? false,
        tagBest: rewriteSeed?.tagBest ?? false,
        armorStandaloneInstallHidden: rewriteSeed?.armorStandaloneInstallHidden ?? false,
        componentScreenshots,
      };

      const versionsSpec: VersionSpec[] | null = isMultiVersion
        ? versions.map(v => ({
            slot: v.slot,
            label: v.label.trim(),
            sourceUpdateRpfPath: v.sourcePath,
            tempWorkDir: v.analysis.tempWorkDir,
            sizeBytes: v.analysis.sizeBytes,
            targetGtaVersion: v.analysis.targetGtaVersion,
            components: v.analysis.components,
            sourceSha256: v.analysis.sourceSha256 ?? '',
          }))
        : null;

      await addToQueue({
        tempId: '', metadata: item,
        sourceUpdateRpfPath: firstVersion.sourcePath,
        tempWorkDir:         firstVersion.analysis.tempWorkDir,
        uploadToR2, status: 'pending', percent: null, currentPhase: null, errorMessage: null,
        addedAt: new Date().toISOString(),
        versions: versionsSpec,
      });
      await useAdminStore.getState().loadQueue();
      onAdded?.();
      onDone();
    } catch (e) {
      console.error('[admin.queue.add] failed', e);
      setError((e as Error).message || 'Add to queue failed');
    } finally { setBusy(false); }
  };

  return (
    <div className="h-full overflow-y-auto px-8 py-6">
      <div className="max-w-[1100px] mx-auto flex flex-col gap-6">
        {rewriteSeed && (
          <div className="rounded-2xl bg-accent-soft border border-accent/40 p-4 flex items-start gap-3">
            <RefreshCw size={16} className="mt-0.5 text-accent shrink-0" />
            <div className="flex flex-col gap-0.5 min-w-0 flex-1">
              <h3 className="text-sm font-semibold text-text-primary">{t('admin.redux.rewriteMode.title')}</h3>
              <p className="text-xs text-text-secondary">
                {t('admin.redux.rewriteMode.description', { name: rewriteSeed.name, id: rewriteSeed.id })}
              </p>
            </div>
          </div>
        )}

        <div className="rounded-3xl p-4 bg-white/[0.05] backdrop-blur-2xl border border-white/[0.08]">
          <h3 className="text-xs uppercase tracking-wider text-text-muted mb-2">{t('admin.redux.metadata.analysis')}</h3>
          {analysisError ? (
            <div className="flex items-center gap-2 text-sm" style={{ color: 'var(--status-error)' }}>
              <AlertTriangle size={14} />
              <span>Ошибка анализа: {analysisError}</span>
            </div>
          ) : !firstAnalysis || analysisPending ? (
            <div className="flex items-center gap-2 text-sm text-text-secondary">
              <Loader2 size={14} className="animate-spin text-accent" />
              <span>Анализирую файл в фоне - заполняй метаданные параллельно. Кнопка «В очередь» активируется когда парсер закончит.</span>
            </div>
          ) : (
            <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-text-secondary">
              <span><CheckCircle2 size={12} className="inline mr-1" style={{ color: 'var(--status-success)' }} />{t('admin.redux.metadata.analysisComplete')}</span>
              <span>{t('admin.redux.metadata.size')}: <span className="text-text-primary">{sizeM} MB</span></span>
              <span>{t('admin.redux.metadata.gtaVersion')}: <span className="text-text-primary font-mono">{firstAnalysis.targetGtaVersion}</span></span>
              <span>{t('admin.redux.metadata.components')}: <span className="text-text-primary">{foundComponents.map(([k]) => k).join(', ')} ({foundComponents.length})</span></span>
            </div>
          )}
        </div>

        <Card title={t('admin.redux.metadata.basicTitle')}>
          <Field label={t('admin.redux.metadata.name')} value={name} onChange={handleNameChange} required />
          <Field label="ID" value={id} onChange={setId} mono readOnly={!!rewriteSeed} />
          <Field label={t('admin.redux.metadata.author')} value={author} onChange={setAuthor} />
          <Field label={t('admin.redux.metadata.authorLink')} value={authorLink} onChange={setAuthorLink} />
          <FieldArea label={t('admin.redux.metadata.description')} value={description} onChange={setDescription} />
        </Card>

        <Card title={t('admin.redux.metadata.mediaTitle')}>
          <Field label={t('admin.redux.metadata.videoUrl')} value={videoUrl} onChange={onVideoChange} />
          <p className="text-xs text-text-muted -mt-1.5">{t('admin.redux.youtubeAutoHint')}</p>
          <Field label={t('admin.redux.metadata.previewUrl')} value={previewUrl} onChange={onPreviewChange} />
          <button
            type="button"
            onClick={onUploadPreview}
            disabled={uploadingPreview}
            className="-mt-1 self-start inline-flex items-center gap-1.5 px-3 h-9 rounded-lg
                       bg-bg-elevated-soft hover:bg-bg-elevated border border-border-subtle hover:border-border-strong
                       text-xs text-text-secondary hover:text-text-primary transition-colors disabled:opacity-50"
          >
            {uploadingPreview ? <Loader2 size={13} className="animate-spin" /> : <Upload size={13} />}
            {uploadingPreview ? 'Загрузка…' : 'Загрузить с компьютера'}
          </button>
          {previewUrl && (
            <div className="mt-1">
              <img
                src={previewUrl}
                alt=""
                onError={e => (e.currentTarget.style.display = 'none')}
                className="w-40 h-24 object-cover rounded-lg border border-border-subtle"
              />
            </div>
          )}
        </Card>

        <Card title={t('admin.redux.metadata.metaTitle')}>
          <div className="flex flex-col gap-2">
            <span className="text-xs uppercase tracking-wider text-text-muted">{t('admin.redux.metadata.server')}</span>
            <div className="flex gap-2">
              {(['majestic', 'gta5rp', 'both'] as const).map(s => (
                <button
                  key={s}
                  type="button"
                  onClick={() => setServer(s)}
                  className={
                    'px-3 py-1.5 rounded-lg text-xs uppercase tracking-wider border transition-colors ' +
                    (server === s
                      ? 'bg-accent-soft text-accent border-accent'
                      : 'bg-bg-base text-text-secondary border-border-subtle hover:border-border-strong')
                  }
                >
                  {s}
                </button>
              ))}
            </div>
            <div className="flex gap-2 pt-1">
              {([['kapt', 'КАПТ', kaptEnabled, setKaptEnabled], ['rp', 'РП', rpEnabled, setRpEnabled]] as const).map(
                ([key, label, on, setOn]) => (
                  <button
                    key={key}
                    type="button"
                    onClick={() => setOn(v => !v)}
                    className={
                      'px-3 py-1.5 rounded-lg text-xs uppercase tracking-wider border transition-colors ' +
                      (on
                        ? 'bg-accent-soft text-accent border-accent'
                        : 'bg-bg-base text-text-secondary border-border-subtle hover:border-border-strong')
                    }
                  >
                    {label}
                  </button>
                ),
              )}
            </div>
          </div>
          <label className="flex items-center gap-2 text-sm text-text-secondary cursor-pointer">
            <input type="checkbox" checked={verified} onChange={e => setVerified(e.target.checked)} className="w-4 h-4" style={{ accentColor: 'var(--accent)' }} />
            <span>{t('admin.redux.metadata.verified')}</span>
          </label>
        </Card>

        {!rewriteSeed && (
          <VersionsManager versions={versions} setVersions={setVersions} />
        )}

        <Card title={t('admin.redux.metadata.componentMap')}>
          <div className="flex gap-2 -mt-1 mb-2">
            <button
              type="button"
              onClick={() => setImportOpen(true)}
              disabled={!firstAnalysis}
              className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md border border-border-strong text-xs text-text-secondary hover:bg-bg-elevated transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
            >
              <ClipboardPaste size={12} /> {t('admin.redux.componentMapImport')}
            </button>
            <button
              type="button"
              onClick={() => {
                navigator.clipboard.writeText(JSON.stringify({ Components: components }, null, 2));
              }}
              disabled={!firstAnalysis}
              className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md border border-border-strong text-xs text-text-secondary hover:bg-bg-elevated transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
            >
              <Copy size={12} /> {t('admin.redux.componentMapExport')}
            </button>
          </div>
          {!firstAnalysis ? (
            <div className="flex flex-col gap-2 py-2">
              {[1, 2, 3, 4, 5].map(i => (
                <div key={i} className="h-8 rounded-md bg-bg-elevated animate-pulse" />
              ))}
              <p className="text-xs text-text-muted mt-2">
                {analysisPending ? 'Парсер ищет компоненты в RPF…' : 'Ждём анализ…'}
              </p>
            </div>
          ) : (
            <ul className="flex flex-col text-sm">
              {Object.entries(components).map(([k, c]) => (
                <ComponentRow
                  key={k}
                  name={k}
                  info={c}
                  onToggleFound={(next) => setComponents({ ...components, [k]: { ...c, isFound: next } })}
                />
              ))}
            </ul>
          )}
        </Card>

        <Card title="Скриншоты компонентов">
          <p className="text-xs text-text-muted -mt-1.5 mb-1">
            По одному изображению на компонент. Юзеры увидят их в окне «Компоненты» на детали редукса. Можно вставить ссылку на любой хостинг (R2, imgur и т.д.).
          </p>
          <div className="flex flex-col gap-2.5">
            {COMPONENT_SCREENSHOT_SLOTS.map(slot => {
              const url = componentScreenshots[slot.key] ?? '';
              const componentInfo = components[slot.key];
              const present = componentInfo?.isFound ?? false;
              return (
                <ComponentScreenshotSlot
                  key={slot.key}
                  label={slot.label}
                  componentKey={slot.key}
                  reduxId={id || (rewriteSeed?.id ?? '')}
                  url={url}
                  presentInRedux={present}
                  onChange={(v) => setComponentScreenshot(slot.key, v)}
                />
              );
            })}
          </div>
        </Card>

        {error && (
          <div className="text-sm" style={{ color: 'var(--status-error)' }}>{error}</div>
        )}
        <div className="flex items-center gap-3">
          <label className="flex items-center gap-2 text-sm text-text-secondary cursor-pointer">
            <input type="checkbox" checked={uploadToR2} onChange={e => setUploadToR2(e.target.checked)} className="w-4 h-4" style={{ accentColor: 'var(--accent)' }} />
            <span>{t('admin.redux.metadata.uploadToR2')}</span>
          </label>
          <div className="flex-1" />
          <button
            type="button"
            onClick={onSubmit}
            disabled={busy || !name.trim() || !canSubmit}
            title={
              !canSubmit
                ? (analysisError
                  ? `Анализ упал: ${analysisError}`
                  : 'Анализ ещё в процессе - кнопка станет активной когда парсер завершит')
                : undefined
            }
            className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-accent text-text-on-accent font-medium hover:bg-accent-hover transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {busy
              ? <Loader2 size={14} className="animate-spin" />
              : analysisPending
                ? <Loader2 size={14} className="animate-spin opacity-70" />
                : <Plus size={14} />}
            <span>{
              analysisPending && !busy
                ? 'Анализирую…'
                : t('admin.redux.metadata.addToQueue')
            }</span>
          </button>
        </div>
      </div>

      {importOpen && (
        <ImportComponentMapModal
          onClose={() => setImportOpen(false)}
          onApply={next => { setComponents(next); setImportOpen(false); }}
        />
      )}
    </div>
  );
}

interface AppendVersionStageProps {
  analysis: ReduxAnalysis | null;
  analysisPending: boolean;
  analysisError: string | null;
  sourcePath: string;
  seed: { parent: ReduxItem; nextSlot: number };
  onDone: () => void;
  onAdded?: () => void;
}

function AppendVersionStage({ analysis, analysisPending, analysisError, sourcePath, seed, onDone, onAdded }: AppendVersionStageProps) {
  const { t } = useTranslation();
  const addToQueue = useAdminStore(s => s.addToQueue);
  const uploaderUsername = useSessionStore(s => s.auth?.username) ?? 'admin';

  const [label, setLabel] = useState(`V${seed.nextSlot}`);
  const [uploadToR2, setUploadToR2] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canSubmit = !!analysis && !analysisPending && !analysisError && label.trim().length > 0;
  const sizeM = analysis ? (analysis.sizeBytes / (1024 * 1024)).toFixed(1) : '-';
  const foundComponents = analysis
    ? Object.entries(analysis.components).filter(([, c]) => c.isFound)
    : [];

  const onSubmit = async () => {
    if (!analysis) {
      setError('Анализ ещё не завершён, подожди.');
      return;
    }
    setError(null);
    setBusy(true);
    try {
      const safeComponents: Record<string, ComponentInfo> = {};
      for (const [k, c] of Object.entries(analysis.components)) {
        safeComponents[k] = {
          isFound: !!c.isFound,
          sourceRpf: c.sourceRpf ?? '',
          internalPaths: c.internalPaths ?? [],
          flags: c.flags ?? [],
        };
      }

      const versionsSpec: VersionSpec[] = [{
        slot: seed.nextSlot,
        label: label.trim(),
        sourceUpdateRpfPath: sourcePath,
        tempWorkDir: analysis.tempWorkDir,
        sizeBytes: analysis.sizeBytes,
        targetGtaVersion: analysis.targetGtaVersion,
        components: safeComponents,
        sourceSha256: analysis.sourceSha256 ?? '',
      }];

      const metadata: ReduxItem = {
        ...seed.parent,
        uploadedAt: new Date().toISOString(),
        uploadedBy: uploaderUsername,
      };

      await addToQueue({
        tempId: '',
        metadata,
        sourceUpdateRpfPath: sourcePath,
        tempWorkDir: analysis.tempWorkDir,
        uploadToR2,
        status: 'pending',
        percent: null,
        currentPhase: null,
        errorMessage: null,
        addedAt: new Date().toISOString(),
        versions: versionsSpec,
        appendToReduxId: seed.parent.id,
      });
      await useAdminStore.getState().loadQueue();
      onAdded?.();
      onDone();
    } catch (e) {
      console.error('[admin.queue.append] failed', e);
      setError((e as Error).message || 'Add to queue failed');
    } finally { setBusy(false); }
  };

  return (
    <div className="h-full overflow-y-auto px-8 py-6">
      <div className="max-w-[640px] mx-auto flex flex-col gap-5">
        <div className="rounded-2xl bg-accent-soft border border-accent/40 p-4 flex items-start gap-3">
          <Plus size={16} className="mt-0.5 text-accent shrink-0" />
          <div className="flex flex-col gap-0.5 min-w-0 flex-1">
            <h3 className="text-sm font-semibold text-text-primary">
              {t('admin.redux.appendMode.title', { slot: seed.nextSlot })}
            </h3>
            <p className="text-xs text-text-secondary">
              {t('admin.redux.appendMode.description', { name: seed.parent.name, id: seed.parent.id })}
            </p>
          </div>
        </div>

        <div className="rounded-3xl p-4 bg-white/[0.05] backdrop-blur-2xl border border-white/[0.08]">
          <h3 className="text-xs uppercase tracking-wider text-text-muted mb-2">{t('admin.redux.metadata.analysis')}</h3>
          {analysisError ? (
            <div className="flex items-center gap-2 text-sm" style={{ color: 'var(--status-error)' }}>
              <AlertTriangle size={14} />
              <span>Ошибка анализа: {analysisError}</span>
            </div>
          ) : !analysis || analysisPending ? (
            <div className="flex items-center gap-2 text-sm text-text-secondary">
              <Loader2 size={14} className="animate-spin text-accent" />
              <span>Парсер читает update.rpf - кнопка «В очередь» активируется автоматически.</span>
            </div>
          ) : (
            <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-text-secondary">
              <span><CheckCircle2 size={12} className="inline mr-1" style={{ color: 'var(--status-success)' }} />{t('admin.redux.metadata.analysisComplete')}</span>
              <span>{t('admin.redux.metadata.size')}: <span className="text-text-primary">{sizeM} MB</span></span>
              <span>{t('admin.redux.metadata.gtaVersion')}: <span className="text-text-primary font-mono">{analysis.targetGtaVersion}</span></span>
              <span>{t('admin.redux.metadata.components')}: <span className="text-text-primary">{foundComponents.map(([k]) => k).join(', ')} ({foundComponents.length})</span></span>
            </div>
          )}
        </div>

        <div className="rounded-3xl bg-white/[0.05] backdrop-blur-2xl border border-white/[0.08] p-4 flex flex-col gap-3">
          <label className="flex flex-col gap-1.5">
            <span className="text-xs uppercase tracking-wider text-text-muted">
              {t('admin.redux.appendMode.labelField')}
            </span>
            <input
              type="text"
              value={label}
              onChange={e => setLabel(e.target.value)}
              placeholder={`V${seed.nextSlot}`}
              className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent"
            />
            <span className="text-xs text-text-muted">
              {t('admin.redux.appendMode.labelHint', { slot: seed.nextSlot })}
            </span>
          </label>
        </div>

        {error && (
          <div className="text-sm" style={{ color: 'var(--status-error)' }}>{error}</div>
        )}

        <div className="flex items-center gap-3">
          <label className="flex items-center gap-2 text-sm text-text-secondary cursor-pointer">
            <input type="checkbox" checked={uploadToR2} onChange={e => setUploadToR2(e.target.checked)} className="w-4 h-4" style={{ accentColor: 'var(--accent)' }} />
            <span>{t('admin.redux.metadata.uploadToR2')}</span>
          </label>
          <div className="flex-1" />
          <button
            type="button"
            onClick={onSubmit}
            disabled={busy || !canSubmit}
            title={
              !canSubmit
                ? (analysisError
                  ? `Анализ упал: ${analysisError}`
                  : analysisPending
                    ? 'Анализ ещё в процессе - кнопка станет активной когда парсер завершит'
                    : !label.trim() ? 'Введи подпись версии' : undefined)
                : undefined
            }
            className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-accent text-text-on-accent font-medium hover:bg-accent-hover transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {busy
              ? <Loader2 size={14} className="animate-spin" />
              : analysisPending
                ? <Loader2 size={14} className="animate-spin opacity-70" />
                : <Plus size={14} />}
            <span>{
              analysisPending && !busy
                ? 'Анализирую…'
                : t('admin.redux.metadata.addToQueue')
            }</span>
          </button>
        </div>
      </div>
    </div>
  );
}

function ComponentRow({ name, info, onToggleFound }: { name: string; info: ComponentInfo; onToggleFound: (next: boolean) => void }) {
  const [open, setOpen] = useState(false);
  return (
    <li className="border-b border-border-subtle last:border-b-0">
      <div className="flex items-center gap-3 py-2 hover:bg-bg-elevated rounded-md px-2 -mx-2 transition-colors">
        <input
          type="checkbox"
          checked={info.isFound}
          onChange={e => onToggleFound(e.target.checked)}
          className="w-4 h-4"
          style={{ accentColor: 'var(--accent)' }}
        />
        <span className={info.isFound ? 'text-status-success' : 'text-text-muted'}>{info.isFound ? '✓' : '○'}</span>
        <span className="font-mono text-text-primary flex-1 truncate">{name}</span>
        <button
          type="button"
          onClick={() => setOpen(o => !o)}
          className="w-6 h-6 rounded flex items-center justify-center text-text-muted hover:text-text-primary"
        >
          {open ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        </button>
      </div>
      {open && (
        <div className="ml-7 mb-2 flex flex-col gap-1.5 text-xs">
          <div className="flex gap-2">
            <span className="text-text-muted uppercase tracking-wider w-16">Source</span>
            <span className="font-mono text-text-secondary truncate">{info.sourceRpf || '-'}</span>
          </div>
          {info.internalPaths.length > 0 && (
            <div className="flex gap-2">
              <span className="text-text-muted uppercase tracking-wider w-16">Paths</span>
              <ul className="flex flex-col gap-0.5 flex-1 min-w-0">
                {info.internalPaths.map(p => (
                  <li key={p} className="flex items-center gap-1.5 group/path">
                    <span className="font-mono text-text-secondary truncate flex-1">{p}</span>
                    <button
                      type="button"
                      onClick={() => navigator.clipboard.writeText(p)}
                      className="opacity-0 group-hover/path:opacity-100 text-text-muted hover:text-accent transition-all"
                      title="copy"
                    >
                      <Copy size={11} />
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {info.flags.length > 0 && (
            <div className="flex gap-2">
              <span className="text-text-muted uppercase tracking-wider w-16">Flags</span>
              <div className="flex gap-1 flex-wrap">
                {info.flags.map(f => (
                  <span key={f} className="px-1.5 py-0.5 rounded bg-bg-elevated text-text-secondary text-[10px] uppercase tracking-wider">{f}</span>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </li>
  );
}

function ImportComponentMapModal({ onClose, onApply }: { onClose: () => void; onApply: (m: Record<string, ComponentInfo>) => void }) {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const [err, setErr] = useState<string | null>(null);

  const apply = () => {
    try {
      const parsed = JSON.parse(text);
      const components = parsed.Components ?? parsed.components ?? parsed;
      if (typeof components !== 'object' || Array.isArray(components)) throw new Error('Expected object');
      onApply(components as Record<string, ComponentInfo>);
    } catch (e) {
      setErr((e as Error).message);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6" onClick={onClose}>
      <div className="w-full max-w-[600px] rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4" onClick={e => e.stopPropagation()}>
        <h2 className="text-base font-bold text-text-primary">{t('admin.redux.componentMapImportTitle')}</h2>
        <textarea
          rows={12}
          value={text}
          onChange={e => setText(e.target.value)}
          className="font-mono text-xs px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-text-primary outline-none focus:border-accent resize-none"
          placeholder='{ "Components": { ... } }'
        />
        {err && <div className="text-sm" style={{ color: 'var(--status-error)' }}>{err}</div>}
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated">
            {t('admin.database.cancel')}
          </button>
          <button type="button" onClick={apply} className="px-4 py-2 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover">
            {t('admin.redux.componentMapImportApply')}
          </button>
        </div>
      </div>
    </div>
  );
}

const COMPONENT_SCREENSHOT_SLOTS: ReadonlyArray<{ key: string; label: string }> = [
  { key: 'arena',     label: 'Арена'      },
  { key: 'minimap',   label: 'Минимапа'   },
  { key: 'crosshair', label: 'Прицел'     },
  { key: 'tracers',   label: 'Трейсера'   },
  { key: 'bloodfx',   label: 'Кровь'      },
  { key: 'timecycle', label: 'Таймцикл'   },
];

function ComponentScreenshotSlot({
  label, componentKey, reduxId, url, presentInRedux, onChange,
}: {
  label: string;
  componentKey: string;
  reduxId: string;
  url: string;
  presentInRedux: boolean;
  onChange: (url: string) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onPick = async () => {
    if (!reduxId) {
      setError('Сначала укажи имя редукса (выше) - оно нужно для пути в R2.');
      return;
    }
    setError(null);
    let localPath: string | null = null;
    try {
      localPath = await bridge.openFileDialog(
        `Скриншот компонента "${label}"`,
        '*.png;*.jpg;*.jpeg;*.webp');
    } catch (e) {
      setError((e as Error).message || 'Ошибка выбора файла.');
      return;
    }
    if (!localPath) return;
    if (typeof bridge.adminUploadComponentScreenshot !== 'function') {
      setError('Перезапусти лаунчер - UI bridge устарел (HMR не подцепил новый метод).');
      return;
    }
    setBusy(true);
    try {
      const publicUrl = await bridge.adminUploadComponentScreenshot(
        reduxId, componentKey, localPath);
      onChange(publicUrl);
    } catch (e) {
      console.error('[admin.screenshot.upload] failed', e);
      setError((e as Error).message || 'Не удалось загрузить - см. консоль.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex items-start gap-3 px-3 py-2.5 rounded-xl bg-bg-elevated border border-border-subtle">
      <div className="shrink-0 w-[80px] h-[52px] rounded-lg overflow-hidden bg-bg-base border border-border-subtle flex items-center justify-center">
        {url ? (
          <img
            src={url}
            alt=""
            onError={(e) => (e.currentTarget.style.display = 'none')}
            className="w-full h-full object-cover"
          />
        ) : (
          <span className="text-[9px] uppercase tracking-wider text-text-muted">no img</span>
        )}
      </div>
      <div className="flex-1 min-w-0 flex flex-col gap-1.5">
        <div className="flex items-center gap-2">
          <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
          <span className="font-mono text-[10px] text-text-muted/70">{componentKey}</span>
          {!presentInRedux && (
            <span className="text-[9px] uppercase tracking-wider px-1.5 py-0.5 rounded bg-bg-surface text-text-muted/70 border border-border-subtle">
              не найден
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onPick}
            disabled={busy || !reduxId}
            className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-md
                       border border-border-strong bg-bg-base text-xs text-text-primary
                       hover:bg-bg-elevated transition-colors
                       disabled:opacity-50 disabled:cursor-not-allowed"
            title={!reduxId ? 'Заполни имя редукса выше' : 'Выбрать .png / .jpg / .webp'}
          >
            {busy
              ? <Loader2 size={11} className="animate-spin" />
              : <Upload size={11} />}
            <span>{busy ? 'Загрузка…' : (url ? 'Заменить' : 'Загрузить файл')}</span>
          </button>
          {url && !busy && (
            <button
              type="button"
              onClick={() => onChange('')}
              className="inline-flex items-center gap-1 px-2 py-1.5 rounded-md
                         text-[11px] text-text-muted hover:text-status-error transition-colors"
              title="Удалить ссылку (файл в R2 останется)"
            >
              <X size={11} />
              <span>Очистить</span>
            </button>
          )}
          {url && (
            <span className="font-mono text-[10px] text-text-muted/70 truncate flex-1" title={url}>
              {url.replace(/^https?:\/\//, '')}
            </span>
          )}
        </div>
        {error && (
          <span className="text-[10.5px] text-status-error truncate" title={error}>{error}</span>
        )}
      </div>
    </div>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
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
      <div className="relative p-6 flex flex-col gap-3">
        <h3 className="text-sm font-bold text-text-primary mb-1 tracking-tight">{title}</h3>
        {children}
      </div>
    </GlassPanel>
  );
}

function Field({ label, value, onChange, required, mono, readOnly }: { label: string; value: string; onChange: (v: string) => void; required?: boolean; mono?: boolean; readOnly?: boolean }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs uppercase tracking-wider text-text-muted">{label}{required && <span className="text-status-error"> *</span>}</span>
      <input
        type="text"
        value={value}
        onChange={e => onChange(e.target.value)}
        readOnly={readOnly}
        className={
          'w-full px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent transition-colors '
          + (mono ? 'font-mono ' : '')
          + (readOnly ? 'opacity-60 cursor-not-allowed focus:border-border-subtle' : '')
        }
      />
    </label>
  );
}

function FieldArea({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
      <textarea
        rows={4}
        value={value}
        onChange={e => onChange(e.target.value)}
        className="w-full px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent transition-colors resize-none"
      />
    </label>
  );
}

interface VersionDraft {
  uid:        string;
  slot:       number;
  label:      string;
  sourcePath: string;
  analysis:   ReduxAnalysis;
}

function VersionsManager({
  versions, setVersions,
}: {
  versions: VersionDraft[];
  setVersions: React.Dispatch<React.SetStateAction<VersionDraft[]>>;
}) {
  const { t } = useTranslation();
  const [adding, setAdding] = useState(false);
  const usedSlots = new Set(versions.map(v => v.slot));
  const nextSlot = [1, 2, 3, 4, 5].find(s => !usedSlots.has(s)) ?? 5;
  const canAddMore = versions.length < 5;

  const updateLabel = (uid: string, label: string) => {
    setVersions(prev => prev.map(v => v.uid === uid ? { ...v, label } : v));
  };
  const remove = (uid: string) => {
    setVersions(prev => prev.length <= 1 ? prev : prev.filter(v => v.uid !== uid));
  };

  return (
    <Card title={`${t('admin.redux.versions.title')} (${versions.length}/5)`}>
      <p className="text-xs text-text-muted -mt-1.5">{t('admin.redux.versions.hint')}</p>

      <ul className="flex flex-col gap-1.5">
        {versions.map((v, idx) => {
          const sizeMb = v.analysis.sizeBytes > 0 ? `${(v.analysis.sizeBytes / (1024 * 1024)).toFixed(0)} MB` : '-';
          const sha8   = v.analysis.sourceSha256 ? v.analysis.sourceSha256.slice(0, 8) : '-';
          const compCount = Object.values(v.analysis.components).filter(c => c.isFound).length;
          const canDelete = versions.length > 1;
          return (
            <li key={v.uid} className="flex items-center gap-2 px-2 py-1.5 rounded-md bg-bg-elevated border border-border-subtle">
              <span className="inline-flex items-center justify-center w-7 h-7 rounded bg-accent-soft text-accent text-xs font-bold tabular-nums shrink-0">
                {v.slot}
              </span>
              <input
                type="text"
                value={v.label}
                onChange={e => updateLabel(v.uid, e.target.value)}
                placeholder={`V${idx + 1}`}
                className="flex-1 px-2 py-1 bg-bg-base border border-border-subtle rounded text-sm text-text-primary outline-none focus:border-accent min-w-0"
              />
              <span className="text-xs text-text-muted tabular-nums shrink-0 w-14 text-right">{sizeMb}</span>
              <span className="text-[10px] text-text-muted shrink-0 w-16 text-right" title={`components found: ${compCount}`}>{compCount} comp.</span>
              <span className="text-[10px] text-text-muted font-mono shrink-0" title={v.analysis.sourceSha256 ?? ''}>{sha8}</span>
              <button
                type="button"
                onClick={() => remove(v.uid)}
                disabled={!canDelete}
                className="w-7 h-7 rounded-md flex items-center justify-center text-text-muted hover:text-status-error hover:bg-bg-base disabled:opacity-30 disabled:hover:text-text-muted disabled:hover:bg-transparent"
                title={canDelete ? t('admin.redux.versions.delete') : t('admin.redux.versions.cantDeleteLast')}
              >
                <X size={13} />
              </button>
            </li>
          );
        })}
      </ul>

      {canAddMore && !adding && (
        <button
          type="button"
          onClick={() => setAdding(true)}
          className="self-start inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated transition-colors"
        >
          <Plus size={13} />
          <span>{t('admin.redux.versions.add')}</span>
        </button>
      )}

      {adding && (
        <AddVersionInline
          suggestedSlot={nextSlot}
          suggestedLabel={`V${versions.length + 1}`}
          onCancel={() => setAdding(false)}
          onAdded={(draft) => {
            setVersions(prev => [...prev, draft]);
            setAdding(false);
          }}
        />
      )}
    </Card>
  );
}

function AddVersionInline({
  suggestedSlot, suggestedLabel, onCancel, onAdded,
}: {
  suggestedSlot:  number;
  suggestedLabel: string;
  onCancel:       () => void;
  onAdded:        (v: VersionDraft) => void;
}) {
  const { t } = useTranslation();
  const [path, setPath] = useState('');
  const [label, setLabel] = useState(suggestedLabel);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [pending, setPending] = useState<{ analysis: ReduxAnalysis; src: string } | null>(null);
  const [dup, setDup] = useState<DuplicateHashMatch | null>(null);

  const onPickFile = async () => {
    const p = await bridge.openFileDialog(t('admin.redux.archiveOrRpf'), '*.zip;*.rar;*.rpf');
    if (p) setPath(p);
  };

  const commit = (analysis: ReduxAnalysis, src: string) => {
    onAdded({
      uid:        crypto.randomUUID(),
      slot:       suggestedSlot,
      label:      label.trim() || suggestedLabel,
      sourcePath: src,
      analysis,
    });
  };

  const onAnalyze = async () => {
    if (!path.trim()) return;
    if (!label.trim()) { setErr(t('admin.redux.versions.errLabelRequired')); return; }
    setBusy(true); setErr(null);
    try {
      const a = await bridge.adminReduxAnalyze(path.trim());
      if (a.sourceSha256) {
        try {
          const match = await bridge.adminFindByHash(a.sourceSha256);
          if (match) {
            setPending({ analysis: a, src: path.trim() });
            setDup(match);
            return;
          }
        } catch (e) {
          console.warn('[redux.upload] adminFindByHash failed; skipping dup check', e);
        }
      }
      commit(a, path.trim());
    } catch (e) {
      setErr((e as Error).message);
    } finally { setBusy(false); }
  };

  return (
    <div className="rounded-md bg-bg-base border border-accent/40 p-3 flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <span className="text-xs uppercase tracking-wider text-accent">{t('admin.redux.versions.addTitle')}</span>
        <span className="text-[10px] text-text-muted">slot {suggestedSlot}</span>
      </div>
      <div className="flex flex-col gap-2">
        <label className="flex flex-col gap-1">
          <span className="text-[10px] uppercase text-text-muted">{t('admin.redux.versions.fieldLabel')}</span>
          <input
            type="text"
            value={label}
            onChange={e => setLabel(e.target.value)}
            placeholder={suggestedLabel}
            className="px-2 py-1.5 bg-bg-elevated border border-border-subtle rounded text-sm text-text-primary outline-none focus:border-accent"
          />
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-[10px] uppercase text-text-muted">{t('admin.redux.versions.fieldSource')}</span>
          <div className="flex gap-2">
            <input
              type="text"
              value={path}
              onChange={e => setPath(e.target.value)}
              placeholder="C:\path\to\update.rpf"
              className="flex-1 px-2 py-1.5 bg-bg-elevated border border-border-subtle rounded text-sm text-text-primary outline-none focus:border-accent font-mono"
            />
            <button
              type="button"
              onClick={onPickFile}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded border border-border-strong text-xs text-text-primary hover:bg-bg-elevated"
            >
              <Upload size={12} />
              <span>{t('admin.redux.dropZone')}</span>
            </button>
          </div>
        </label>
      </div>
      {err && <span className="text-xs text-status-error">{err}</span>}
      <div className="flex justify-end gap-2 pt-1">
        <button
          type="button"
          disabled={busy}
          onClick={onCancel}
          className="px-3 py-1.5 rounded-md border border-border-strong text-sm text-text-primary hover:bg-bg-elevated disabled:opacity-50"
        >
          {t('admin.redux.cancel')}
        </button>
        <button
          type="button"
          disabled={busy || !path.trim() || !label.trim()}
          onClick={() => void onAnalyze()}
          className="px-3 py-1.5 rounded-md bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover disabled:opacity-50 inline-flex items-center gap-2"
        >
          {busy && <Loader2 size={12} className="animate-spin" />}
          <span>{t('admin.redux.analyze')}</span>
        </button>
      </div>

      {dup && pending && (
        <DuplicateHashDialog
          match={dup}
          onCancel={() => { setDup(null); setPending(null); }}
          onContinue={() => {
            const p = pending;
            setDup(null); setPending(null);
            commit(p.analysis, p.src);
          }}
        />
      )}
    </div>
  );
}

void Square;
