import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Plus, Upload, X, Loader2, CheckCircle2, AlertTriangle, Clock,
  Crosshair, Trash2, Edit3, EyeOff, Eye, FileArchive, Image as ImageIcon, Link2,
  Layers, FolderOpen, Star, Pencil,
} from 'lucide-react';
import { bridge } from '@/bridge';
import { useGunpackStore } from '@/store/gunpackStore';
import type { Gunpack, GunpackGun, GunpackQueueItem, GunpackBatchEntry, GunpackVariant } from '@/bridge/types';
import { Toast } from '@/components/Toast';
import { ConfirmModal } from '@/components/ConfirmModal';
import { useGlobalToastStore } from '@/store/globalToastStore';

type Stage = 'list' | 'upload' | 'batch' | 'detail';

export function GunsSection() {
  const { t } = useTranslation();
  const packs            = useGunpackStore(s => s.packs);
  const queue            = useGunpackStore(s => s.queue);
  const whitelist        = useGunpackStore(s => s.whitelist);
  const selectedPack     = useGunpackStore(s => s.selectedPack);
  const selectedGuns     = useGunpackStore(s => s.selectedGuns);
  const loadingDetail    = useGunpackStore(s => s.loadingDetail);
  const loadPacks        = useGunpackStore(s => s.loadPacks);
  const loadQueue        = useGunpackStore(s => s.loadQueue);
  const loadWhitelist    = useGunpackStore(s => s.loadWhitelist);
  const selectPack       = useGunpackStore(s => s.selectPack);
  const removeQueueItem  = useGunpackStore(s => s.removeQueueItem);
  const applyQueueUpdate = useGunpackStore(s => s.applyQueueUpdate);
  const deletePack       = useGunpackStore(s => s.deletePack);

  const [stage, setStage]   = useState<Stage>('list');
  const [toast, setToast]   = useState<{ tone: 'success' | 'error' | 'warning'; message: string } | null>(null);

  useEffect(() => {
    void loadWhitelist();
    void loadPacks();
    void loadQueue();
    const handler = (snap: GunpackQueueItem) => applyQueueUpdate(snap);
    bridge.events.on('admin:gunpackQueueProgress', handler);
    return () => bridge.events.off('admin:gunpackQueueProgress', handler);
  }, [loadWhitelist, loadPacks, loadQueue, applyQueueUpdate]);

  const rendererBootstrapFired = useRef(false);
  useEffect(() => {
    if (rendererBootstrapFired.current) return;
    rendererBootstrapFired.current = true;
    void (async () => {
      try {
        const r = await bridge.rendererEnsureInstalled();
        if (r.success && !r.alreadyInstalled) {
          useGlobalToastStore.getState().push('success',
            'Инструменты для превью пушек установлены - PNG будут рендериться автоматически.');
        } else if (!r.success) {
          useGlobalToastStore.getState().push('warning',
            `Не удалось установить инструменты для превью: ${r.errorMessage ?? 'unknown'}. ` +
            'Паки будут заливаться без PNG-превью (3D-просмотр по GLB всё ещё работает).');
        }
      } catch (e) {
        console.warn('[admin.guns] rendererEnsureInstalled failed', e);
      }
    })();
  }, []);

  const handledDones = useRef<Set<string>>(new Set());
  useEffect(() => {
    for (const item of queue) {
      if (item.status !== 'done' || handledDones.current.has(item.tempId)) continue;
      handledDones.current.add(item.tempId);
      const hasWarnings = (item.warnings?.length ?? 0) > 0;
      if (hasWarnings) {

        setToast({
          tone: 'warning',
          message: `Пак «${item.metadata.name || '-'}» загружен с предупреждениями:\n• ` +
                   item.warnings!.join('\n• '),
        });
      } else {
        setToast({
          tone: 'success',
          message: t('admin.guns.uploadDone', { name: item.metadata.name || '-' }),
        });
      }
      const id = item.tempId;

      window.setTimeout(() => { void removeQueueItem(id); }, hasWarnings ? 12000 : 4000);
    }
  }, [queue, removeQueueItem, t]);

  const onOpenPack = async (id: string) => {
    await selectPack(id);
    setStage('detail');
  };
  const onBackToList = () => {
    setStage('list');
    void selectPack(null);
  };
  const onUploaded = () => {
    setStage('list');
    setToast({ tone: 'success', message: t('admin.guns.uploadEnqueued') });
  };

  return (
    <div className="h-full flex flex-col">
      <header className="h-14 px-6 flex items-center gap-3 border-b border-border-subtle shrink-0">
        <h1 className="text-base font-bold text-text-primary">{t('admin.guns.title')}</h1>
        <div className="flex-1" />
        {stage === 'list' && (
          <>
            <button
              type="button"
              onClick={() => setStage('batch')}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated transition-colors"
              title={t('admin.guns.batchUpload', 'Массовая загрузка')}
            >
              <Layers size={14} />
              <span>{t('admin.guns.batchUpload', 'Массовая загрузка')}</span>
            </button>
            <button
              type="button"
              onClick={() => setStage('upload')}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated transition-colors"
            >
              <Plus size={14} />
              <span>{t('admin.guns.addNew')}</span>
            </button>
          </>
        )}
        {stage !== 'list' && (
          <button
            type="button"
            onClick={onBackToList}
            className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated transition-colors"
          >
            <X size={14} />
            <span>{t('admin.guns.back')}</span>
          </button>
        )}
      </header>

      <div className="flex-1 overflow-hidden">
        {stage === 'list' && (
          <ListStage
            packs={packs}
            queue={queue}
            onOpen={onOpenPack}
            onDelete={deletePack}
            onRemoveQueue={removeQueueItem}
          />
        )}
        {stage === 'upload' && whitelist.length > 0 && (
          <UploadStage onDone={onUploaded} onCancel={onBackToList} />
        )}
        {stage === 'upload' && whitelist.length === 0 && (
          <div className="h-full flex items-center justify-center text-text-muted">
            <Loader2 size={20} className="animate-spin mr-2" />
            <span>{t('admin.guns.loadingWhitelist')}</span>
          </div>
        )}
        {stage === 'batch' && (
          <BatchUploadStage
            onDone={() => {
              setStage('list');
              setToast({ tone: 'success', message: t('admin.guns.batchEnqueued', 'Паки добавлены в очередь') });
            }}
            onCancel={onBackToList}
          />
        )}
        {stage === 'detail' && selectedPack && (
          <DetailStage
            pack={selectedPack}
            guns={selectedGuns}
            loading={loadingDetail}
          />
        )}
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'warning' ? 14000 : toast?.tone === 'error' ? 8000 : 4000}
      />
    </div>
  );
}

function ListStage({
  packs, queue, onOpen, onDelete, onRemoveQueue,
}: {
  packs: Gunpack[];
  queue: GunpackQueueItem[];
  onOpen: (id: string) => void;
  onDelete: (id: string) => Promise<void>;
  onRemoveQueue: (tempId: string) => Promise<void>;
}) {
  const { t } = useTranslation();
  const activeQueue = queue.filter(q => q.status === 'pending' || q.status === 'processing');
  const erroredQueue = queue.filter(q => q.status === 'error');

  return (
    <div className="h-full overflow-y-auto px-6 py-4 flex flex-col gap-4">
      {}
      {(activeQueue.length > 0 || erroredQueue.length > 0) && (
        <section className="flex flex-col gap-2">
          <h2 className="text-xs uppercase tracking-wider text-text-muted">
            {t('admin.guns.queueTitle')}
          </h2>
          {[...activeQueue, ...erroredQueue].map(item => (
            <QueueRow key={item.tempId} item={item} onRemove={onRemoveQueue} />
          ))}
        </section>
      )}

      <section className="flex flex-col gap-2">
        <h2 className="text-xs uppercase tracking-wider text-text-muted">
          {t('admin.guns.catalogTitle')} · {packs.length}
        </h2>
        {packs.length === 0 ? (
          <div className="py-12 flex flex-col items-center justify-center text-text-muted">
            <Crosshair size={48} className="mb-3 opacity-40" />
            <p className="text-sm">{t('admin.guns.empty')}</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
            {packs.map(p => (
              <PackCard key={p.id} pack={p} onOpen={onOpen} onDelete={onDelete} />
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

function PackCard({
  pack, onOpen, onDelete,
}: {
  pack: Gunpack;
  onOpen: (id: string) => void;
  onDelete: (id: string) => Promise<void>;
}) {
  const { t } = useTranslation();
  const sizeM = (pack.weaponsRpfSize / (1024 * 1024)).toFixed(1);

  const [confirmOpen, setConfirmOpen] = useState(false);

  const onClickDelete = (e: React.MouseEvent) => {
    e.stopPropagation();
    setConfirmOpen(true);
  };
  const handleConfirmedDelete = async () => {
    setConfirmOpen(false);
    try { await onDelete(pack.id); } catch (err) { console.error('[gunpack] delete', err); }
  };

  const isHidden = pack.status === 'hidden';

  return (
    <>
      <div
        role="button"
        tabIndex={0}
        onClick={() => onOpen(pack.id)}
        onKeyDown={(e) => { if (e.key === 'Enter') onOpen(pack.id); }}
        className="group flex items-stretch gap-3 p-3 rounded-xl bg-bg-surface border border-transparent
                   hover:border-[color-mix(in_srgb,var(--accent)_55%,transparent)]
                   hover:shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_22%,transparent),0_8px_24px_-10px_color-mix(in_srgb,var(--accent)_45%,transparent)]
                   cursor-pointer transition-[border-color,box-shadow] duration-300 ease-depth"
      >
        <div className="w-20 h-20 rounded-lg bg-bg-elevated overflow-hidden flex items-center justify-center text-text-muted shrink-0">
          {pack.coverKind === 'image' && pack.coverUrl ? (
            <img src={pack.coverUrl} alt="" className="w-full h-full object-cover"
                 onError={e => (e.currentTarget.style.display = 'none')} />
          ) : (
            <Crosshair size={28} />
          )}
        </div>
        <div className="flex-1 min-w-0 flex flex-col gap-1">
          <div className="flex items-center gap-2 min-w-0">
            <h3 className="text-sm font-semibold text-text-primary truncate">{pack.name}</h3>
            {isHidden && (
              <span className="text-[10px] uppercase tracking-wider px-1.5 py-0.5 rounded
                               bg-glass-strong text-text-muted shrink-0">
                {t('admin.guns.hidden')}
              </span>
            )}
            {pack.isVerified && (
              <span className="inline-flex items-center justify-center w-4 h-4 rounded-full
                               bg-[color-mix(in_srgb,var(--status-success)_22%,transparent)]
                               text-status-success shrink-0"
                    title="Verified">
                <CheckCircle2 size={12} strokeWidth={2.4} />
              </span>
            )}
          </div>
          <div className="text-xs text-text-muted truncate">
            {pack.author || '-'} · {sizeM} MB · {t('admin.guns.downloads', { n: pack.downloadCount })}
          </div>
          <div className="text-[10px] text-text-muted font-mono truncate">id: {pack.id.slice(0, 8)}</div>
        </div>
        <button
          type="button"
          onClick={onClickDelete}
          className="self-start opacity-0 group-hover:opacity-100 w-8 h-8 rounded-md flex items-center justify-center
                     text-text-muted hover:text-status-error hover:bg-bg-elevated transition-all shrink-0"
          aria-label={t('admin.guns.delete')}
          title={t('admin.guns.delete')}
        >
          <Trash2 size={14} />
        </button>
      </div>
      <ConfirmModal
        open={confirmOpen}
        title={t('admin.guns.confirmDeleteTitle')}
        message={t('admin.guns.confirmDelete', { name: pack.name })}
        confirmLabel={t('admin.guns.delete')}
        cancelLabel={t('admin.guns.cancel')}
        destructive
        onConfirm={handleConfirmedDelete}
        onCancel={() => setConfirmOpen(false)}
      />
    </>
  );
}

function QueueRow({
  item, onRemove,
}: {
  item: GunpackQueueItem;
  onRemove: (tempId: string) => Promise<void>;
}) {
  const { t } = useTranslation();
  const phaseLabel = item.currentPhase
    ? t(`admin.guns.phase.${item.currentPhase}`)
    : t(`admin.guns.status.${item.status}`);

  return (
    <div className="flex flex-col gap-2 p-3 rounded-xl bg-bg-surface border border-transparent">
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-lg bg-bg-elevated flex items-center justify-center shrink-0 text-text-muted">
          <FileArchive size={18} />
        </div>
        <div className="flex-1 min-w-0">
          <div className="text-sm font-semibold text-text-primary truncate">
            {item.metadata.name || '-'}
          </div>
          <div className="text-xs text-text-muted truncate">
            {phaseLabel}{item.percent != null ? ` · ${item.percent}%` : ''}
          </div>
        </div>
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
      {item.status === 'processing' && item.percent != null && (
        <div className="h-1 rounded-full bg-bg-elevated overflow-hidden">
          <div
            className="h-full bg-accent transition-all duration-200"
            style={{ width: `${item.percent}%` }}
          />
        </div>
      )}
      {item.status === 'error' && item.errorMessage && (
        <div

          className="ml-13 text-xs font-mono px-3 py-2 rounded-lg bg-bg-base border border-status-error/30
                     whitespace-pre-line leading-relaxed"
          style={{ color: 'var(--status-error)' }}
        >
          {item.errorMessage}
        </div>
      )}
    </div>
  );
}

function StatusBadge({ item }: { item: GunpackQueueItem }) {
  const { t } = useTranslation();
  switch (item.status) {
    case 'pending':
      return (
        <span className="flex items-center gap-1 text-xs text-text-muted">
          <Clock size={12} /> {t('admin.guns.status.pending')}
        </span>
      );
    case 'processing':
      return (
        <span className="flex items-center gap-1 text-xs text-accent">
          <Loader2 size={12} className="animate-spin" />
        </span>
      );
    case 'done':
      return (
        <span className="flex items-center gap-1 text-xs" style={{ color: 'var(--status-success)' }}>
          <CheckCircle2 size={12} /> {t('admin.guns.status.done')}
        </span>
      );
    case 'error':
      return (
        <span className="flex items-center gap-1 text-xs" style={{ color: 'var(--status-error)' }}>
          <AlertTriangle size={12} /> {t('admin.guns.status.error')}
        </span>
      );
  }
}

function UploadStage({ onDone, onCancel }: { onDone: () => void; onCancel: () => void }) {
  const { t } = useTranslation();
  const startUpload = useGunpackStore(s => s.startUpload);

  const [path, setPath]                 = useState('');
  const [name, setName]                 = useState('');
  const [author, setAuthor]             = useState('');
  const [authorLink, setAuthorLink]     = useState('');
  const [description, setDescription]   = useState('');
  const [coverKind, setCoverKind]       = useState<'image' | 'youtube'>('youtube');
  const [coverUrl, setCoverUrl]         = useState('');

  const [coverImageMode, setCoverImageMode] = useState<'url' | 'file'>('url');
  const [coverFilePath, setCoverFilePath]   = useState('');
  const [verified, setVerified]         = useState(false);
  const [uploadToR2, setUploadToR2]     = useState(true);
  const [busy, setBusy]                 = useState(false);
  const [error, setError]               = useState<string | null>(null);

  const [coverUploading, setCoverUploading] = useState(false);

  const onPickCoverImage = async () => {
    setError(null);
    const p = await bridge.openFileDialog(t('admin.guns.uploadCoverHint'), '*.png;*.jpg;*.jpeg;*.gif;*.webp');
    if (!p) return;
    if (typeof bridge.adminUploadGunpackCover !== 'function') {
      setError('Перезапусти лаунчер - UI bridge устарел.');
      return;
    }

    setCoverUploading(true);
    try {
      const url = await bridge.adminUploadGunpackCover(p);
      setCoverFilePath(url);
    } catch (e) {
      console.error('[admin.gunpack.cover.upload] failed', e);
      setError((e as Error).message || 'Не удалось загрузить обложку.');
    } finally {
      setCoverUploading(false);
    }
  };
  const effectiveCoverUrl = (() => {
    if (coverKind !== 'image') return coverUrl.trim();

    if (coverImageMode === 'file') return coverFilePath || '';
    return coverUrl.trim();
  })();

  const onPickFile = async () => {
    const p = await bridge.openFileDialog(t('admin.guns.dlcFile'), '*.rpf');
    if (p) {
      setPath(p);

      if (!name.trim()) {
        const parts = p.replace(/\\/g, '/').split('/');
        const parent = parts[parts.length - 2];
        if (parent && parent !== 'dlcpacks') setName(parent);
      }
    }
  };

  const onSubmit = async () => {
    if (!path.trim()) { setError(t('admin.guns.errSourceRequired')); return; }
    if (!name.trim()) { setError(t('admin.guns.errNameRequired')); return; }
    setBusy(true); setError(null);
    try {

      const now = new Date().toISOString();
      await startUpload({
        sourceDlcRpfPath: path.trim(),
        uploadToR2,
        metadata: {
          id: '',
          name: name.trim(),
          author: author.trim() || null,
          authorLink: authorLink.trim() || null,
          description: description.trim() || null,
          weaponsRpfUrl:    '',
          weaponsRpfSize:   0,
          weaponsRpfSha256: '',
          packZipUrl: null, packZipSize: null, packZipSha256: null,
          manifestUrl: null,
          coverKind,
          coverUrl: effectiveCoverUrl || null,
          galleryUrls: [],
          status: 'published',
          isVerified: verified,
          viewerPriority: 0,
          downloadCount: 0,
          uploadedAt: now,
          uploadedBy: null,
          updatedAt: now,
          notes: null,
        },
      });
      onDone();
    } catch (e) {
      setError((e as Error).message);
    } finally { setBusy(false); }
  };

  return (
    <div className="h-full overflow-y-auto px-8 py-6">
      <div className="max-w-[680px] mx-auto flex flex-col gap-5">
        <h2 className="text-sm font-semibold text-text-primary">{t('admin.guns.uploadTitle')}</h2>
        <p className="text-xs text-text-muted">{t('admin.guns.uploadHint')}</p>

        {}
        <Card title={t('admin.guns.sourceLabel')}>
          <button
            type="button"
            onClick={onPickFile}
            className="h-32 rounded-2xl border-2 border-dashed border-border-strong hover:border-accent flex flex-col items-center justify-center gap-2 text-text-muted hover:text-text-primary transition-colors"
          >
            <Upload size={24} />
            <span className="text-sm font-medium">{t('admin.guns.dropZone')}</span>
            <span className="text-xs">*.rpf</span>
          </button>
          <Field label={t('admin.guns.directPath')} value={path} onChange={setPath} mono />
        </Card>

        <Card title={t('admin.guns.metaTitle')}>
          <Field label={t('admin.guns.name')}      value={name}        onChange={setName} required />
          <Field label={t('admin.guns.author')}    value={author}      onChange={setAuthor} />
          <Field label={t('admin.guns.authorLink')} value={authorLink} onChange={setAuthorLink} />
          <FieldArea label={t('admin.guns.description')} value={description} onChange={setDescription} />
        </Card>

        <Card title={t('admin.guns.coverTitle')}>
          <div className="flex flex-col gap-2">
            <span className="text-xs uppercase tracking-wider text-text-muted">{t('admin.guns.coverKind')}</span>
            <div className="flex gap-2">
              {(['youtube', 'image'] as const).map(k => (
                <button
                  key={k}
                  type="button"
                  onClick={() => setCoverKind(k)}
                  className={
                    'px-3 py-1.5 rounded-lg text-xs uppercase tracking-wider border transition-colors ' +
                    (coverKind === k
                      ? 'bg-accent-soft text-accent border-accent'
                      : 'bg-bg-base text-text-secondary border-border-subtle hover:border-border-strong')
                  }
                >
                  {t(`admin.guns.coverKindOpt.${k}`)}
                </button>
              ))}
            </div>
          </div>

          {}
          {coverKind === 'youtube' && (
            <Field
              label={t('admin.guns.youtubeUrl')}
              value={coverUrl}
              onChange={setCoverUrl}
            />
          )}

          {}
          {coverKind === 'image' && (
            <>
              <div className="flex flex-col gap-2">
                <span className="text-xs uppercase tracking-wider text-text-muted">
                  {t('admin.guns.imageSource')}
                </span>
                <div className="flex gap-2">
                  {(['url', 'file'] as const).map(k => (
                    <button
                      key={k}
                      type="button"
                      onClick={() => setCoverImageMode(k)}
                      className={
                        'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs uppercase tracking-wider border transition-colors ' +
                        (coverImageMode === k
                          ? 'bg-accent-soft text-accent border-accent'
                          : 'bg-bg-base text-text-secondary border-border-subtle hover:border-border-strong')
                      }
                    >
                      {k === 'url' ? <Link2 size={12} /> : <ImageIcon size={12} />}
                      <span>{t(`admin.guns.imageSourceOpt.${k}`)}</span>
                    </button>
                  ))}
                </div>
              </div>

              {coverImageMode === 'url' && (
                <Field
                  label={t('admin.guns.imageUrl')}
                  value={coverUrl}
                  onChange={setCoverUrl}
                />
              )}

              {coverImageMode === 'file' && (
                <div className="flex flex-col gap-2">
                  <button
                    type="button"
                    onClick={onPickCoverImage}
                    disabled={coverUploading}
                    className="h-28 rounded-2xl border-2 border-dashed border-border-strong hover:border-accent
                               flex flex-col items-center justify-center gap-1.5
                               text-text-muted hover:text-text-primary transition-colors
                               disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {coverUploading
                      ? <Loader2 size={22} className="animate-spin text-accent" />
                      : <ImageIcon size={22} />}
                    <span className="text-sm font-medium">
                      {coverUploading
                        ? 'Загружаю в R2…'
                        : (coverFilePath ? 'Сменить обложку' : t('admin.guns.uploadCoverButton'))}
                    </span>
                    <span className="text-[10px] uppercase tracking-wider">
                      {t('admin.guns.uploadCoverHint')}
                    </span>
                  </button>
                  {coverFilePath && !coverUploading && (
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-[11px] text-text-muted truncate flex-1" title={coverFilePath}>
                        {coverFilePath.replace(/^https?:\/\//, '')}
                      </span>
                      <button
                        type="button"
                        onClick={() => setCoverFilePath('')}
                        className="text-[11px] text-text-muted hover:text-status-error transition-colors px-2 py-1 rounded"
                      >
                        {t('admin.guns.clearCoverFile')}
                      </button>
                    </div>
                  )}
                </div>
              )}

              {effectiveCoverUrl && (
                <img
                  src={effectiveCoverUrl}
                  alt=""
                  onError={e => (e.currentTarget.style.display = 'none')}
                  className="w-40 h-24 object-cover rounded-lg border border-border-subtle"
                />
              )}
            </>
          )}
        </Card>

        <Card title={t('admin.guns.flagsTitle')}>
          <label className="flex items-center gap-2 text-sm text-text-secondary cursor-pointer">
            <input type="checkbox" checked={verified} onChange={e => setVerified(e.target.checked)} className="w-4 h-4" style={{ accentColor: 'var(--accent)' }} />
            <span>{t('admin.guns.verified')}</span>
          </label>
        </Card>

        {error && <div className="text-sm" style={{ color: 'var(--status-error)' }}>{error}</div>}

        <div className="flex items-center gap-3">
          <label className="flex items-center gap-2 text-sm text-text-secondary cursor-pointer">
            <input type="checkbox" checked={uploadToR2} onChange={e => setUploadToR2(e.target.checked)} className="w-4 h-4" style={{ accentColor: 'var(--accent)' }} />
            <span>{t('admin.guns.uploadToR2')}</span>
          </label>
          <div className="flex-1" />
          <button
            type="button"
            onClick={onCancel}
            className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated"
          >
            {t('admin.guns.cancel')}
          </button>
          <button
            type="button"
            onClick={onSubmit}
            disabled={busy || !path.trim() || !name.trim()}
            className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-accent text-text-on-accent font-medium hover:bg-accent-hover transition-colors disabled:opacity-50"
          >
            {busy ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
            <span>{t('admin.guns.startUpload')}</span>
          </button>
        </div>
      </div>
    </div>
  );
}

function BatchUploadStage({ onDone, onCancel }: { onDone: () => void; onCancel: () => void }) {
  const { t } = useTranslation();
  const startUpload = useGunpackStore(s => s.startUpload);

  const [parentPath, setParentPath]     = useState('');
  const [scanning, setScanning]         = useState(false);
  const [entries, setEntries]           = useState<GunpackBatchEntry[]>([]);
  const [picked, setPicked]             = useState<Set<string>>(new Set());
  const [uploadToR2, setUploadToR2]     = useState(true);
  const [error, setError]               = useState<string | null>(null);
  const [submitting, setSubmitting]     = useState(false);
  const [progressCount, setProgressCount] = useState({ done: 0, total: 0 });

  const eligible = entries.filter(e => e.rpfPath !== null);

  const onPickFolder = async () => {
    setError(null);
    const folder = await bridge.openFolderDialog();
    if (!folder) return;
    setParentPath(folder);
    setScanning(true);
    try {
      const found = await bridge.scanGunpackBatchFolder(folder);
      setEntries(found);

      setPicked(new Set(found.filter(e => e.rpfPath).map(e => e.folderPath)));
    } catch (e) {
      setError((e as Error).message || 'Не удалось просканировать папку.');
    } finally {
      setScanning(false);
    }
  };

  const togglePick = (key: string) => {
    setPicked(prev => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else                next.add(key);
      return next;
    });
  };

  const allEligibleSelected = eligible.length > 0 && eligible.every(e => picked.has(e.folderPath));
  const togglePickAll = () => {
    if (allEligibleSelected) {
      setPicked(new Set());
    } else {
      setPicked(new Set(eligible.map(e => e.folderPath)));
    }
  };

  const onSubmit = async () => {
    if (picked.size === 0) {
      setError('Выбери хотя бы один пак.');
      return;
    }
    setError(null);
    setSubmitting(true);
    setProgressCount({ done: 0, total: picked.size });

    const targets = entries.filter(e => picked.has(e.folderPath) && e.rpfPath);
    const now = new Date().toISOString();
    let done = 0;
    let firstFailure: Error | null = null;

    for (const entry of targets) {
      try {

        let coverUrl: string | null = null;
        if (entry.imagePath) {
          try {
            if (typeof bridge.adminUploadGunpackCover === 'function') {
              coverUrl = await bridge.adminUploadGunpackCover(entry.imagePath);
            }
          } catch (e) {

            console.warn('[batch.cover.upload] failed for', entry.folderName, e);
          }
        }

        await startUpload({
          sourceDlcRpfPath: entry.rpfPath!,
          uploadToR2,
          metadata: {
            id: '',
            name: entry.folderName,
            author: null, authorLink: null, description: null,
            weaponsRpfUrl: '', weaponsRpfSize: 0, weaponsRpfSha256: '',
            packZipUrl: null, packZipSize: null, packZipSha256: null,
            manifestUrl: null,
            coverKind: coverUrl ? 'image' : 'youtube',
            coverUrl,
            galleryUrls: [],
            status: 'published',
            isVerified: false,
            viewerPriority: 0,
            downloadCount: 0,
            uploadedAt: now,
            uploadedBy: null,
            updatedAt: now,
            notes: null,
          },
        });
      } catch (e) {
        if (!firstFailure) firstFailure = e as Error;
      } finally {
        done += 1;
        setProgressCount({ done, total: targets.length });
      }
    }
    setSubmitting(false);
    if (firstFailure) {
      setError(`Часть паков не загрузилась: ${firstFailure.message}`);
      return;
    }
    onDone();
  };

  return (
    <div className="h-full overflow-y-auto px-8 py-6">
      <div className="max-w-[820px] mx-auto flex flex-col gap-5">
        <div>
          <h2 className="font-display font-bold text-2xl uppercase tracking-wide text-text-primary leading-tight">
            {t('admin.guns.batchTitle', 'Массовая загрузка ганпаков')}
          </h2>
          <p className="text-sm text-text-secondary mt-1.5 max-w-2xl leading-relaxed">
            {t(
              'admin.guns.batchHint',
              'Выбери папку, внутри которой каждый подкаталог = отдельный ганпак с dlc.rpf и превью-картинкой. Имя папки автоматически становится именем пака.',
            )}
          </p>
        </div>

        {}
        <div className="rounded-2xl px-5 py-5 flex items-center gap-4
                        bg-white/[0.04] border border-white/[0.06]">
          <span className="shrink-0 w-12 h-12 rounded-2xl flex items-center justify-center
                           bg-white/[0.06] border border-white/[0.10] text-text-primary">
            <FolderOpen size={20} strokeWidth={1.7} />
          </span>
          <div className="flex-1 min-w-0">
            <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted font-bold">
              {t('admin.guns.batchParentFolder', 'Родительская папка')}
            </div>
            <div className="font-mono text-[12px] text-text-primary truncate mt-0.5" title={parentPath}>
              {parentPath || (t('admin.guns.batchPickPrompt', 'Не выбрано - нажми «Выбрать»'))}
            </div>
          </div>
          <button
            type="button"
            onClick={onPickFolder}
            disabled={scanning || submitting}
            className="shrink-0 inline-flex items-center gap-2 h-10 px-4 rounded-xl border
                       bg-white/[0.04] border-white/[0.08] text-text-secondary
                       text-[12px] font-bold uppercase tracking-[0.12em]
                       hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                       disabled:opacity-50 disabled:cursor-not-allowed
                       transition-colors"
          >
            {scanning ? <Loader2 size={13} className="animate-spin" /> : <FolderOpen size={13} />}
            <span>{scanning ? t('admin.guns.batchScanning', 'Сканирую...') : t('admin.guns.batchChoose', 'Выбрать')}</span>
          </button>
        </div>

        {}
        {entries.length > 0 && (
          <div className="rounded-2xl bg-white/[0.04] border border-white/[0.06] overflow-hidden">
            <header className="flex items-center gap-3 px-5 h-11 border-b border-white/[0.06]">
              <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted font-bold">
                {t('admin.guns.batchDetected', 'Найдено')} · {entries.length}
              </span>
              <span className="text-[10px] uppercase tracking-[0.22em] text-text-muted">
                · {eligible.length} с .rpf
              </span>
              {eligible.length > 0 && (
                <button
                  type="button"
                  onClick={togglePickAll}
                  className="ml-auto text-[10px] uppercase tracking-[0.18em] font-bold
                             text-text-secondary hover:text-text-primary
                             px-2 py-1 rounded-md hover:bg-white/[0.06]
                             transition-colors"
                >
                  {allEligibleSelected ? 'Снять всё' : 'Выбрать всё'}
                </button>
              )}
            </header>
            <ul className="divide-y divide-white/[0.04]">
              {entries.map(e => {
                const isEligible = e.rpfPath !== null;
                const isPicked = picked.has(e.folderPath);
                return (
                  <li
                    key={e.folderPath}
                    className={
                      'flex items-center gap-3 px-5 py-3 transition-colors ' +
                      (isEligible ? 'cursor-pointer hover:bg-white/[0.04]' : 'opacity-50')
                    }
                    onClick={() => isEligible && togglePick(e.folderPath)}
                  >
                    <input
                      type="checkbox"
                      checked={isPicked}
                      disabled={!isEligible}
                      onChange={() => togglePick(e.folderPath)}
                      onClick={ev => ev.stopPropagation()}
                      className="w-4 h-4 shrink-0"
                      style={{ accentColor: 'var(--accent)' }}
                    />
                    <span className="shrink-0 w-9 h-9 rounded-lg flex items-center justify-center
                                     bg-white/[0.04] border border-white/[0.06] overflow-hidden">
                      {e.imagePath ? (

                        <img
                          src={`file:///${e.imagePath.replace(/\\/g, '/')}`}
                          alt=""
                          className="w-full h-full object-cover"
                          onError={ev => (ev.currentTarget.style.display = 'none')}
                        />
                      ) : (
                        <ImageIcon size={14} className="text-text-muted" />
                      )}
                    </span>
                    <div className="flex-1 min-w-0">
                      <div className="font-bold text-sm text-text-primary truncate">
                        {e.folderName}
                      </div>
                      <div className="text-[11px] text-text-muted truncate font-mono">
                        {e.rpfPath ?? 'нет .rpf - будет пропущено'}
                      </div>
                    </div>
                    {isEligible
                      ? <FileArchive size={13} className="text-text-muted shrink-0" />
                      : <AlertTriangle size={13} className="text-status-warning shrink-0" />}
                  </li>
                );
              })}
            </ul>
          </div>
        )}

        {}
        {submitting && (
          <div className="rounded-2xl px-5 py-3.5 flex items-center gap-3
                          bg-white/[0.04] border border-white/[0.08]">
            <Loader2 size={14} className="animate-spin text-text-primary" />
            <span className="text-sm text-text-secondary">
              Загружаю в очередь: <span className="text-text-primary font-bold tabular-nums">
                {progressCount.done}
              </span>
              <span className="text-text-muted"> / {progressCount.total}</span>
            </span>
          </div>
        )}

        {error && (
          <div className="text-sm rounded-lg px-3.5 py-2.5
                          bg-status-error/10 border border-status-error/30 text-status-error">
            {error}
          </div>
        )}

        <div className="flex items-center gap-3">
          <label className="flex items-center gap-2 text-sm text-text-secondary cursor-pointer">
            <input type="checkbox" checked={uploadToR2} onChange={e => setUploadToR2(e.target.checked)} className="w-4 h-4" style={{ accentColor: 'var(--accent)' }} />
            <span>{t('admin.guns.uploadToR2')}</span>
          </label>
          <div className="flex-1" />
          <button
            type="button"
            onClick={onCancel}
            disabled={submitting}
            className="px-4 py-2 rounded-lg border border-white/[0.10] text-sm text-text-primary
                       hover:bg-white/[0.06] disabled:opacity-50 transition-colors"
          >
            {t('admin.guns.cancel')}
          </button>
          <button
            type="button"
            onClick={onSubmit}
            disabled={submitting || picked.size === 0}
            className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg
                       bg-accent text-text-on-accent font-medium
                       hover:bg-accent-hover transition-colors disabled:opacity-50"
          >
            {submitting ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
            <span>
              {submitting
                ? `${progressCount.done} / ${progressCount.total}`
                : `Загрузить всё (${picked.size})`}
            </span>
          </button>
        </div>
      </div>
    </div>
  );
}

function DetailStage({
  pack, guns, loading,
}: {
  pack: Gunpack;
  guns: GunpackGun[];
  loading: boolean;
}) {
  const { t } = useTranslation();
  const patchGun     = useGunpackStore(s => s.patchGun);
  const deleteGun    = useGunpackStore(s => s.deleteGun);
  const variants     = useGunpackStore(s => s.selectedVariants);
  const patchVariant     = useGunpackStore(s => s.patchVariant);
  const deleteVariant    = useGunpackStore(s => s.deleteVariant);
  const setDefaultVariant = useGunpackStore(s => s.setDefaultVariant);
  const uploadVariant     = useGunpackStore(s => s.uploadVariant);

  const [addVariantOpen, setAddVariantOpen] = useState(false);

  const sizeM = (pack.weaponsRpfSize / (1024 * 1024)).toFixed(1);

  return (
    <div className="h-full overflow-y-auto px-6 py-4 flex flex-col gap-4">
      <Card title={pack.name}>
        <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-text-secondary">
          <span>id: <span className="font-mono text-text-primary">{pack.id}</span></span>
          <span>{t('admin.guns.size')}: <span className="text-text-primary">{sizeM} MB</span></span>
          <span>{t('admin.guns.gunsCount')}: <span className="text-text-primary">{guns.length}</span></span>
          <span>{t('admin.guns.downloads', { n: pack.downloadCount })}</span>
        </div>
        {(pack.status === 'hidden' || pack.isVerified) && (
          <div className="flex items-center gap-2 text-[10px] uppercase tracking-wider">
            {pack.status === 'hidden' && (
              <span className="px-1.5 py-0.5 rounded bg-glass-strong text-text-muted">
                {t('admin.guns.hidden')}
              </span>
            )}
            {pack.isVerified && (
              <span className="px-1.5 py-0.5 rounded bg-[color-mix(in_srgb,var(--status-success)_18%,transparent)] text-status-success inline-flex items-center gap-1">
                <CheckCircle2 size={10} strokeWidth={2.4} /> Verified
              </span>
            )}
          </div>
        )}
        {pack.description && (
          <p className="text-sm text-text-secondary whitespace-pre-line">{pack.description}</p>
        )}
      </Card>

      <section className="flex flex-col gap-2">
        <div className="flex items-center justify-between">
          <h3 className="text-xs uppercase tracking-wider text-text-muted">
            Варианты · {variants.length}
          </h3>
          <button
            type="button"
            onClick={() => setAddVariantOpen(true)}
            className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md
                       border border-border-strong text-xs text-text-secondary
                       hover:bg-bg-elevated hover:text-text-primary transition-colors"
          >
            <Plus size={12} />
            <span>Добавить вариант</span>
          </button>
        </div>
        <div className="flex flex-col gap-2">
          {variants.length === 0 ? (
            <div className="py-6 flex items-center justify-center text-text-muted text-sm">
              <AlertTriangle size={14} className="mr-2 opacity-60" />
              <span>Нет вариантов - что-то пошло не так с backfill миграции 0098.</span>
            </div>
          ) : (
            variants.map(v => (
              <VariantRow
                key={v.id}
                variant={v}
                canDelete={variants.length > 1 && !v.isDefault}
                onRename={(name) => patchVariant(v.id, { name })}
                onSetDefault={() => setDefaultVariant(v.id)}
                onDelete={() => deleteVariant(v.id)}
              />
            ))
          )}
        </div>
      </section>

      <section className="flex flex-col gap-2">
        <h3 className="text-xs uppercase tracking-wider text-text-muted">
          {t('admin.guns.gunsTitle')} · {guns.length}
        </h3>
        {loading ? (
          <div className="py-12 flex items-center justify-center text-text-muted">
            <Loader2 size={20} className="animate-spin mr-2" />
            <span>{t('admin.guns.loadingGuns')}</span>
          </div>
        ) : guns.length === 0 ? (
          <div className="py-12 flex flex-col items-center justify-center text-text-muted">
            <Crosshair size={36} className="mb-2 opacity-40" />
            <p className="text-sm">{t('admin.guns.noGuns')}</p>
          </div>
        ) : (
          <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-3">
            {guns.map(g => (
              <GunCard
                key={g.id}
                gun={g}
                onRename={(name) => patchGun(g.id, { displayName: name })}
                onToggleHidden={() => patchGun(g.id, { isHidden: !g.isHidden })}
                onDelete={() => deleteGun(g.id)}
              />
            ))}
          </div>
        )}
      </section>
      {addVariantOpen && (
        <AddVariantModal
          packName={pack.name}
          existingCount={variants.length}
          onClose={() => setAddVariantOpen(false)}
          onUpload={async (name, rpfPath, coverPath) => {
            return uploadVariant(pack.id, name, rpfPath, coverPath);
          }}
        />
      )}
    </div>
  );
}

function AddVariantModal({
  packName, existingCount, onClose, onUpload,
}: {
  packName: string;
  existingCount: number;
  onClose: () => void;
  onUpload: (name: string, rpfPath: string, coverPath: string | null) => Promise<GunpackQueueItem>;
}) {
  const initialName = existingCount === 1 ? 'White' : `Variant ${existingCount + 1}`;
  const [name, setName] = useState(initialName);
  const [rpfPath, setRpfPath] = useState('');
  const [coverPath, setCoverPath] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [trackedTempId, setTrackedTempId] = useState<string | null>(null);
  const trackedItem = useGunpackStore(s =>
    trackedTempId ? s.queue.find(q => q.tempId === trackedTempId) ?? null : null);

  const onPickRpf = async () => {
    setError(null);
    try {
      const p = await bridge.openFileDialog('RPF (weapons)', '*.rpf');
      if (p) setRpfPath(p);
    } catch (e) { console.warn('[variant.upload.pickRpf]', e); }
  };
  const onPickCover = async () => {
    setError(null);
    try {
      const p = await bridge.openFileDialog('Cover (PNG / JPG / WEBP / GIF)', '*.png;*.jpg;*.jpeg;*.gif;*.webp');
      if (p) setCoverPath(p);
    } catch (e) { console.warn('[variant.upload.pickCover]', e); }
  };

  const onSubmit = async () => {
    if (!name.trim()) { setError('Имя обязательно'); return; }
    if (!rpfPath.trim()) { setError('Выбери .rpf-файл'); return; }
    setBusy(true);
    setError(null);
    try {
      const queued = await onUpload(name.trim(), rpfPath, coverPath.trim() ? coverPath : null);
      setTrackedTempId(queued.tempId);
    } catch (e) {
      setError((e as Error).message || 'Не удалось загрузить вариант');
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6"
         onClick={busy ? undefined : onClose}>
      <div className="w-full max-w-[560px] rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4"
           onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between">
          <h2 className="text-base font-bold text-text-primary">Новый вариант для «{packName}»</h2>
          <button type="button" onClick={onClose} disabled={busy}
                  className="text-text-muted hover:text-text-primary disabled:opacity-50">
            <X size={16} />
          </button>
        </div>

        <label className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wider text-text-muted">Название</span>
          <input
            type="text"
            value={name}
            onChange={e => setName(e.target.value)}
            placeholder="White / Black / Camo / ..."
            disabled={busy}
            autoFocus
            className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent disabled:opacity-60"
          />
        </label>

        <div className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wider text-text-muted">.rpf-файл *</span>
          <div className="flex items-center gap-2">
            <input
              type="text"
              value={rpfPath}
              readOnly
              placeholder="Не выбран"
              className="flex-1 px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-secondary font-mono outline-none"
            />
            <button
              type="button"
              onClick={() => void onPickRpf()}
              disabled={busy}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg
                         bg-white/[0.04] border border-white/[0.08] text-text-secondary text-xs
                         hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                         transition-colors disabled:opacity-50"
            >
              <FolderOpen size={12} />
              <span>Выбрать</span>
            </button>
          </div>
          <span className="text-[10px] text-text-muted">
            Этот .rpf будет загружен в R2 как weapons.rpf этого варианта. Парсинг пушек не запускается - варианты делят оружие пака.
          </span>
        </div>

        <div className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wider text-text-muted">Обложка (опц.)</span>
          <div className="flex items-center gap-2">
            <input
              type="text"
              value={coverPath}
              readOnly
              placeholder="Не выбрана - будет использоваться обложка пака"
              className="flex-1 px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-secondary font-mono outline-none"
            />
            <button
              type="button"
              onClick={() => void onPickCover()}
              disabled={busy}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg
                         bg-white/[0.04] border border-white/[0.08] text-text-secondary text-xs
                         hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                         transition-colors disabled:opacity-50"
            >
              <ImageIcon size={12} />
              <span>Выбрать</span>
            </button>
          </div>
          {coverPath && (
            <button
              type="button"
              onClick={() => setCoverPath('')}
              className="self-start text-[10px] text-text-muted hover:text-text-primary transition-colors"
            >
              × Очистить
            </button>
          )}
        </div>

        {error && (
          <div className="text-xs text-status-error">{error}</div>
        )}

        {trackedItem && (
          <div className="rounded-xl bg-bg-elevated border border-border-subtle p-3 flex flex-col gap-2">
            <div className="flex items-center justify-between text-xs">
              <span className="text-text-secondary font-semibold">
                {trackedItem.status === 'done'  ? 'Готово' :
                 trackedItem.status === 'error' ? 'Ошибка' :
                 trackedItem.currentPhase
                   ? phaseLabel(trackedItem.currentPhase)
                   : 'Подготовка…'}
              </span>
              <span className="text-text-muted tabular-nums">
                {trackedItem.percent != null ? `${trackedItem.percent}%` : ''}
              </span>
            </div>
            <div className="h-1.5 rounded-full bg-white/[0.06] overflow-hidden">
              <div
                className={
                  'h-full transition-[width] duration-300 ease-out ' +
                  (trackedItem.status === 'error' ? 'bg-status-error' :
                   trackedItem.status === 'done'  ? 'bg-status-success' :
                                                    'bg-accent')
                }
                style={{ width: `${trackedItem.percent ?? (trackedItem.status === 'done' ? 100 : 5)}%` }}
              />
            </div>
            {trackedItem.errorMessage && (
              <div className="text-[11px] text-status-error break-words">{trackedItem.errorMessage}</div>
            )}
          </div>
        )}

        <div className="flex justify-end gap-2 pt-2">
          {trackedItem ? (
            <button
              type="button"
              disabled={trackedItem.status !== 'done' && trackedItem.status !== 'error'}
              onClick={onClose}
              className="px-4 py-2 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover disabled:opacity-50"
            >
              {trackedItem.status === 'done' || trackedItem.status === 'error' ? 'Закрыть' : 'Идёт обработка…'}
            </button>
          ) : (
            <>
              <button
                type="button"
                disabled={busy}
                onClick={onClose}
                className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated disabled:opacity-50"
              >
                Отмена
              </button>
              <button
                type="button"
                disabled={busy || !name.trim() || !rpfPath.trim()}
                onClick={() => void onSubmit()}
                className="inline-flex items-center gap-2 px-4 py-2 rounded-lg
                           bg-accent text-text-on-accent text-sm font-medium
                           hover:bg-accent-hover disabled:opacity-50"
              >
                {busy && <Loader2 size={12} className="animate-spin" />}
                <span>{busy ? 'Запускаем…' : 'Загрузить'}</span>
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

function phaseLabel(phase: string): string {
  switch (phase) {
    case 'analyzing':   return 'Анализирую DLC';
    case 'parsing':     return 'Парсю weapons.rpf';
    case 'repacking':   return 'Пересобираю weapons.rpf';
    case 'filtering':   return 'Фильтрую по whitelist';
    case 'converting':  return 'Конвертирую YDR → GLB';
    case 'compressing': return 'Сжимаю GLB (KTX2)';
    case 'rendering':   return 'Рендерю превью';
    case 'packing':     return 'Упаковываю pack.zip';
    case 'uploading':   return 'Заливаю в R2';
    case 'registering': return 'Регистрирую в БД';
    default:            return phase;
  }
}

function VariantRow({
  variant, canDelete, onRename, onSetDefault, onDelete,
}: {
  variant: GunpackVariant;
  canDelete: boolean;
  onRename: (name: string) => Promise<void>;
  onSetDefault: () => Promise<void>;
  onDelete: () => Promise<void>;
}) {
  const sizeM = (variant.weaponsRpfSize / (1024 * 1024)).toFixed(1);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(variant.name);
  const [saving, setSaving] = useState(false);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  const startEdit = () => { setDraft(variant.name); setEditing(true); };
  const cancelEdit = () => { setEditing(false); setDraft(variant.name); };
  const commitEdit = async () => {
    const next = draft.trim();
    if (!next || next === variant.name) { setEditing(false); return; }
    setSaving(true);
    try { await onRename(next); setEditing(false); }
    catch (e) { console.error('[variant.rename]', e); }
    finally { setSaving(false); }
  };

  return (
    <>
      <div className={
        'flex items-center gap-3 px-4 py-3 rounded-xl border transition-colors ' +
        (variant.isDefault
          ? 'border-[color-mix(in_srgb,var(--accent)_45%,transparent)] bg-accent-soft/30'
          : 'border-border-subtle bg-bg-surface')
      }>
        <div className="w-12 h-12 rounded-lg bg-bg-elevated overflow-hidden shrink-0 flex items-center justify-center text-text-muted">
          {variant.coverUrl
            ? <img src={variant.coverUrl} alt="" className="w-full h-full object-cover"
                   onError={e => (e.currentTarget.style.display = 'none')} />
            : <Layers size={18} />}
        </div>

        <div className="flex-1 min-w-0 flex flex-col gap-0.5">
          <div className="flex items-center gap-2 min-w-0">
            {editing ? (
              <input
                autoFocus
                value={draft}
                disabled={saving}
                onChange={(e) => setDraft(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') void commitEdit();
                  if (e.key === 'Escape') cancelEdit();
                }}
                className="flex-1 min-w-0 px-2 py-0.5 rounded-md bg-bg-elevated text-sm font-semibold text-text-primary
                           border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                           focus:outline-none focus:border-accent disabled:opacity-60"
              />
            ) : (
              <span className="font-semibold text-sm text-text-primary truncate">{variant.name}</span>
            )}
            {variant.isDefault && !editing && (
              <span className="text-[9px] uppercase tracking-wider px-1.5 py-0.5 rounded
                               bg-accent-soft text-accent font-bold shrink-0">
                Default
              </span>
            )}
          </div>
          <div className="text-[11px] text-text-muted truncate font-mono">
            {sizeM} MB · {variant.id.slice(0, 8)}
          </div>
        </div>

        <div className="flex items-center gap-1 shrink-0">
          {editing ? (
            <>
              <button
                type="button"
                onClick={() => void commitEdit()}
                disabled={saving}
                aria-label="Сохранить"
                title="Сохранить (Enter)"
                className="w-7 h-7 rounded-md flex items-center justify-center
                           text-status-success hover:bg-bg-elevated transition-colors disabled:opacity-50"
              >
                {saving ? <Loader2 size={12} className="animate-spin" /> : <CheckCircle2 size={12} />}
              </button>
              <button
                type="button"
                onClick={cancelEdit}
                disabled={saving}
                aria-label="Отмена"
                title="Отмена (Esc)"
                className="w-7 h-7 rounded-md flex items-center justify-center
                           text-text-muted hover:text-status-error hover:bg-bg-elevated transition-colors disabled:opacity-50"
              >
                <X size={12} />
              </button>
            </>
          ) : (
            <>
              <button
                type="button"
                onClick={startEdit}
                aria-label="Переименовать"
                title="Переименовать"
                className="w-7 h-7 rounded-md flex items-center justify-center
                           text-text-muted hover:text-text-primary hover:bg-bg-elevated transition-colors"
              >
                <Pencil size={12} />
              </button>
              {!variant.isDefault && (
                <button
                  type="button"
                  onClick={() => void onSetDefault()}
                  aria-label="Сделать default"
                  title="Сделать default - этот вариант будет ставиться по умолчанию"
                  className="w-7 h-7 rounded-md flex items-center justify-center
                             text-text-muted hover:text-accent hover:bg-bg-elevated transition-colors"
                >
                  <Star size={12} />
                </button>
              )}
              <button
                type="button"
                onClick={() => setConfirmDeleteOpen(true)}
                disabled={!canDelete}
                aria-label="Удалить вариант"
                title={canDelete
                  ? 'Удалить вариант'
                  : variant.isDefault
                    ? 'Нельзя удалить default-вариант. Сначала сделай другой default.'
                    : 'Пак должен иметь хотя бы один вариант.'}
                className="w-7 h-7 rounded-md flex items-center justify-center
                           text-text-muted hover:text-status-error hover:bg-bg-elevated transition-colors
                           disabled:opacity-30 disabled:cursor-not-allowed disabled:hover:bg-transparent
                           disabled:hover:text-text-muted"
              >
                <Trash2 size={12} />
              </button>
            </>
          )}
        </div>
      </div>
      <ConfirmModal
        open={confirmDeleteOpen}
        title="Удалить вариант?"
        message={`«${variant.name}» - будет удалён из БД, файлы из R2 (gunpacks/${variant.gunpackId}/variants/${variant.id}/) тоже подчистятся. Откатить нельзя.`}
        confirmLabel="Удалить"
        cancelLabel="Отмена"
        destructive
        onConfirm={async () => {
          setConfirmDeleteOpen(false);
          try { await onDelete(); } catch (e) { console.error('[variant.delete]', e); }
        }}
        onCancel={() => setConfirmDeleteOpen(false)}
      />
    </>
  );
}

function GunCard({
  gun, onRename, onToggleHidden, onDelete,
}: {
  gun: GunpackGun;
  onRename: (name: string) => Promise<void>;
  onToggleHidden: () => Promise<void>;
  onDelete: () => Promise<void>;
}) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [draftName, setDraftName] = useState(gun.displayName ?? gun.baseName);

  const sizeKb = (gun.sizeBytes / 1024).toFixed(0);

  const onSaveRename = async () => {
    const next = draftName.trim();
    if (!next || next === (gun.displayName ?? gun.baseName)) { setEditing(false); return; }
    try { await onRename(next); } catch (err) { console.error('[gun.rename]', err); }
    setEditing(false);
  };
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const handleConfirmedDelete = async () => {
    setConfirmDeleteOpen(false);
    try { await onDelete(); } catch (err) { console.error('[gun.delete]', err); }
  };

  return (
    <>
    <div className={
      'flex flex-col gap-2 p-3 rounded-xl bg-bg-surface border transition-all ' +
      (gun.isHidden
        ? 'border-border-subtle opacity-60'
        : 'border-border-subtle hover:border-accent/60')
    }>
      <div className="aspect-[2/1] rounded-lg bg-bg-elevated overflow-hidden flex items-center justify-center text-text-muted">
        {gun.previewUrl ? (
          <img src={gun.previewUrl} alt="" className="w-full h-full object-cover"
               onError={e => (e.currentTarget.style.display = 'none')} />
        ) : (
          <Crosshair size={28} className="opacity-40" />
        )}
      </div>
      {editing ? (
        <div className="flex gap-1">
          <input
            type="text"
            value={draftName}
            onChange={e => setDraftName(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') void onSaveRename();
              if (e.key === 'Escape') { setEditing(false); setDraftName(gun.displayName ?? gun.baseName); }
            }}
            autoFocus
            className="flex-1 px-2 py-1 bg-bg-base border border-accent rounded text-sm text-text-primary outline-none min-w-0"
          />
          <button
            type="button"
            onClick={() => void onSaveRename()}
            className="px-2 py-1 rounded bg-accent text-text-on-accent text-xs"
          >
            ✓
          </button>
        </div>
      ) : (
        <div className="text-sm font-semibold text-text-primary truncate" title={gun.displayName ?? gun.baseName}>
          {gun.displayName ?? gun.baseName}
        </div>
      )}
      <div className="text-[10px] text-text-muted font-mono truncate" title={gun.weaponPrefix + gun.baseName}>
        {gun.weaponPrefix}{gun.baseName} · {gun.category} · {sizeKb} KB
      </div>
      <div className="flex gap-1 mt-auto">
        <button
          type="button"
          onClick={() => { setDraftName(gun.displayName ?? gun.baseName); setEditing(true); }}
          className="flex-1 inline-flex items-center justify-center gap-1 py-1 rounded-md border border-border-subtle text-xs text-text-secondary hover:bg-bg-elevated"
          aria-label={t('admin.guns.rename')}
        >
          <Edit3 size={11} />
        </button>
        <button
          type="button"
          onClick={() => void onToggleHidden()}
          className="flex-1 inline-flex items-center justify-center gap-1 py-1 rounded-md border border-border-subtle text-xs text-text-secondary hover:bg-bg-elevated"
          aria-label={gun.isHidden ? t('admin.guns.show') : t('admin.guns.hide')}
        >
          {gun.isHidden ? <Eye size={11} /> : <EyeOff size={11} />}
        </button>
        <button
          type="button"
          onClick={() => setConfirmDeleteOpen(true)}
          className="flex-1 inline-flex items-center justify-center gap-1 py-1 rounded-md border border-border-subtle text-xs text-text-secondary hover:text-status-error hover:bg-bg-elevated"
          aria-label={t('admin.guns.delete')}
        >
          <Trash2 size={11} />
        </button>
      </div>
    </div>
    <ConfirmModal
      open={confirmDeleteOpen}
      title={t('admin.guns.confirmDeleteGunTitle')}
      message={t('admin.guns.confirmDeleteGun', { name: gun.displayName || gun.baseName })}
      confirmLabel={t('admin.guns.delete')}
      cancelLabel={t('admin.guns.cancel')}
      destructive
      onConfirm={handleConfirmedDelete}
      onCancel={() => setConfirmDeleteOpen(false)}
    />
    </>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-2xl bg-bg-surface border border-border-subtle p-5 flex flex-col gap-3">
      <h3 className="text-sm font-semibold text-text-primary mb-1">{title}</h3>
      {children}
    </div>
  );
}

function Field({
  label, value, onChange, required, mono,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  required?: boolean;
  mono?: boolean;
}) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs uppercase tracking-wider text-text-muted">
        {label}{required && <span className="text-status-error"> *</span>}
      </span>
      <input
        type="text"
        value={value}
        onChange={e => onChange(e.target.value)}
        className={
          'w-full px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent transition-colors '
          + (mono ? 'font-mono' : '')
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
        rows={3}
        value={value}
        onChange={e => onChange(e.target.value)}
        className="w-full px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent transition-colors resize-none"
      />
    </label>
  );
}
