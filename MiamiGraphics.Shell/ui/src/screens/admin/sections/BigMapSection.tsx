import { useEffect, useMemo, useState } from 'react';
import {
  Loader2, Upload, FolderOpen, X, Trash2, CheckCircle2, AlertCircle,
  AlertTriangle, Save, ArrowUp, ArrowDown, ImagePlus, Map as MapIcon, Pencil,
} from 'lucide-react';
import { bridge } from '@/bridge';
import type { BigMap, BigMapAnalysis } from '@/bridge/types';
import { Toast } from '@/components/Toast';

const MAX_PHOTOS = 7;

const SERVER_LABELS: Record<string, string> = {
  gta5rp:   'GTA5RP',
  majestic: 'Majestic',
};

export function BigMapSection() {
  const [maps, setMaps] = useState<BigMap[]>([]);
  const [loadingMaps, setLoadingMaps] = useState(false);
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);

  const [sourcePath, setSourcePath] = useState<string | null>(null);
  const [analysis, setAnalysis]     = useState<BigMapAnalysis | null>(null);
  const [analyzing, setAnalyzing]   = useState(false);

  const [name, setName]               = useState('');
  const [author, setAuthor]           = useState('');
  const [authorLink, setAuthorLink]   = useState('');
  const [description, setDescription] = useState('');
  const [videoUrl, setVideoUrl]       = useState('');
  const [servers, setServers]         = useState<string[]>(['majestic']);
  const [photos, setPhotos]           = useState<string[]>([]);
  const [existingId, setExistingId]   = useState<string | null>(null);
  const [publishing, setPublishing]   = useState(false);
  const [deletingId, setDeletingId]   = useState<string | null>(null);

  const loadMaps = async () => {
    setLoadingMaps(true);
    try { setMaps(await bridge.adminBigMapList()); }
    catch (e) { setToast({ tone: 'error', message: (e as Error).message }); }
    finally { setLoadingMaps(false); }
  };
  useEffect(() => { void loadMaps(); }, []);

  const resetDraft = () => {
    setSourcePath(null);
    setAnalysis(null);
    setName(''); setAuthor(''); setAuthorLink('');
    setDescription(''); setVideoUrl('');
    setServers(['majestic']);
    setPhotos([]);
    setExistingId(null);
  };

  const runAnalyze = async (path: string) => {
    setAnalyzing(true);
    setAnalysis(null);
    try {
      const a = await bridge.adminBigMapAnalyze(path);
      setSourcePath(path);
      setAnalysis(a);
      setPhotos(prev => prev.length > 0 ? prev : (a.photoPaths ?? []).slice(0, MAX_PHOTOS));
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setAnalyzing(false);
    }
  };

  const onPickArchive = async () => {
    const p = await bridge.openFileDialog('Архив с большой картой', '*.zip;*.rar;*.7z');
    if (!p) return;
    void runAnalyze(p);
  };

  const onPickFolder = async () => {
    const p = await bridge.openFolderDialog();
    if (!p) return;
    void runAnalyze(p);
  };

  const onAddPhotos = async () => {
    try {
      const picked = await bridge.openFileDialogMulti(
        'Фото карты (первое = обложка)', '*.png;*.jpg;*.jpeg;*.webp');
      if (!picked || picked.length === 0) return;
      setPhotos(prev => [...prev, ...picked.filter(p => !prev.includes(p))].slice(0, MAX_PHOTOS));
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    }
  };

  const movePhoto = (idx: number, dir: -1 | 1) => {
    setPhotos(prev => {
      const next = [...prev];
      const j = idx + dir;
      if (j < 0 || j >= next.length) return prev;
      [next[idx], next[j]] = [next[j], next[idx]];
      return next;
    });
  };

  const toggleServer = (s: string) => {
    setServers(prev => prev.includes(s) ? prev.filter(x => x !== s) : [...prev, s]);
  };

  const startEditExisting = (m: BigMap) => {
    setExistingId(m.id);
    setName(m.name);
    setAuthor(m.author);
    setAuthorLink(m.authorLink);
    setDescription(m.description);
    setVideoUrl(m.videoUrl ?? '');
    setServers(m.supportedServers.length > 0 ? [...m.supportedServers] : ['majestic']);
  };

  const canPublish = !!sourcePath && !!analysis && analysis.installable
    && !!name.trim() && servers.length > 0 && !publishing;

  const onPublish = async () => {
    if (!sourcePath || !name.trim() || servers.length === 0) return;
    setPublishing(true);
    try {
      const created = await bridge.adminBigMapPublish({
        sourcePath,
        name: name.trim(),
        author: author.trim(),
        authorLink: authorLink.trim(),
        description: description.trim(),
        supportedServers: servers,
        photoPaths: photos.slice(0, MAX_PHOTOS),
        videoUrl: videoUrl.trim() || null,
        existingId,
      });
      setToast({ tone: 'success', message: `Карта «${created.name}» опубликована.` });
      resetDraft();
      void loadMaps();
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message || 'Ошибка публикации.' });
    } finally {
      setPublishing(false);
    }
  };

  const onDelete = async (m: BigMap) => {
    if (!confirm(`Удалить карту «${m.name}»? Запись и пакет пропадут из каталога.`)) return;
    setDeletingId(m.id);
    try {
      await bridge.adminBigMapDelete(m.id);
      setMaps(prev => prev.filter(x => x.id !== m.id));
      if (existingId === m.id) setExistingId(null);
      setToast({ tone: 'success', message: `Карта «${m.name}» удалена.` });
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setDeletingId(null);
    }
  };

  const totalMb = useMemo(
    () => analysis ? (analysis.totalBytes / (1024 * 1024)).toFixed(1) : null,
    [analysis]);

  return (
    <div className="h-full flex flex-col">
      <header className="h-14 px-6 flex items-center gap-3 border-b border-border-subtle shrink-0">
        <MapIcon size={16} className="text-accent" />
        <h1 className="text-base font-bold text-text-primary">Big Map Upload</h1>
        <span className="text-xs text-text-muted">карт в каталоге: {maps.length}</span>
        <div className="flex-1" />
        <button
          type="button"
          onClick={() => void onPickFolder()}
          disabled={analyzing}
          className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg
                     border border-border-strong text-sm text-text-primary
                     hover:bg-bg-elevated transition-colors disabled:opacity-50"
        >
          <FolderOpen size={14} />
          Выбрать папку
        </button>
        <button
          type="button"
          onClick={() => void onPickArchive()}
          disabled={analyzing}
          className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg bg-accent text-text-on-accent
                     text-sm font-medium hover:bg-accent-hover disabled:opacity-50"
        >
          {analyzing ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
          {analyzing ? 'Анализируем…' : 'Выбрать .zip'}
        </button>
      </header>

      <div className="flex-1 overflow-y-auto px-6 py-4 flex flex-col gap-6">
        {analysis && sourcePath && (
          <section className="rounded-2xl bg-bg-surface border border-accent-40 p-5 flex flex-col gap-3">
            <div className="flex items-center justify-between gap-2">
              <h2 className="font-bold text-text-primary">Отчёт анализатора</h2>
              <button type="button" onClick={resetDraft} className="text-text-muted hover:text-text-primary">
                <X size={14} />
              </button>
            </div>
            <div className="font-mono text-[10.5px] text-text-muted truncate" title={sourcePath}>{sourcePath}</div>

            <div className="flex items-center gap-4 flex-wrap text-sm">
              <span className={
                'inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-bold ' +
                (analysis.installable
                  ? 'bg-green-500/15 text-green-300 border border-green-500/30'
                  : 'bg-red-500/15 text-red-300 border border-red-500/30')
              }>
                {analysis.installable ? <CheckCircle2 size={12} /> : <AlertCircle size={12} />}
                {analysis.installable ? 'Устанавливаемый пак' : 'Пак НЕ устанавливаемый'}
              </span>
              <span className="text-xs text-text-secondary">
                Формат: <span className="font-mono text-text-primary">{analysis.packFormat || '-'}</span>
              </span>
              <span className="text-xs text-text-secondary">
                Размер: <span className="font-mono text-text-primary tabular-nums">{totalMb} MB</span>
              </span>
              {analysis.photoPaths.length > 0 && (
                <span className="text-xs text-text-secondary">
                  Фото в паке: <span className="font-mono text-text-primary tabular-nums">{analysis.photoPaths.length}</span>
                </span>
              )}
            </div>

            {analysis.warnings.length > 0 && (
              <div className="rounded-xl border border-[color-mix(in_srgb,var(--status-warning)_35%,transparent)] p-3 flex flex-col gap-1.5"
                   style={{ background: 'color-mix(in srgb, var(--status-warning) 8%, transparent)' }}>
                {analysis.warnings.map((w, i) => (
                  <div key={i} className="flex items-start gap-2 text-xs leading-snug"
                       style={{ color: 'var(--status-warning)' }}>
                    <AlertTriangle size={12} className="mt-0.5 shrink-0" />
                    <span>{w}</span>
                  </div>
                ))}
              </div>
            )}

            {analysis.targetPaths.length > 0 && (
              <details className="text-xs text-text-secondary">
                <summary className="cursor-pointer select-none text-text-muted hover:text-text-primary">
                  Целевые пути в update.rpf ({analysis.targetPaths.length})
                </summary>
                <ul className="mt-2 flex flex-col gap-0.5 max-h-48 overflow-y-auto">
                  {analysis.targetPaths.map((p, i) => (
                    <li key={i} className="font-mono text-[10.5px] text-text-muted truncate" title={p}>{p}</li>
                  ))}
                </ul>
              </details>
            )}

            <div className="h-px bg-border-subtle my-1" />

            {existingId && (
              <div className="flex items-center gap-2 text-xs px-3 py-2 rounded-lg
                              bg-accent-soft text-accent border border-[color-mix(in_srgb,var(--accent)_35%,transparent)]">
                <Pencil size={12} />
                <span className="flex-1">Обновление существующей карты (id <span className="font-mono">{existingId.slice(0, 8)}…</span>)</span>
                <button type="button" onClick={() => setExistingId(null)} className="hover:text-text-primary">
                  <X size={12} />
                </button>
              </div>
            )}

            <label className="flex flex-col gap-1">
              <span className="text-xs uppercase tracking-wider text-text-muted">Название *</span>
              <input
                type="text"
                value={name}
                onChange={e => setName(e.target.value)}
                placeholder="Например: «Vector Map 4K»"
                className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
              />
            </label>
            <div className="grid grid-cols-2 gap-2">
              <label className="flex flex-col gap-1">
                <span className="text-xs uppercase tracking-wider text-text-muted">Автор</span>
                <input
                  type="text"
                  value={author}
                  onChange={e => setAuthor(e.target.value)}
                  className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent"
                />
              </label>
              <label className="flex flex-col gap-1">
                <span className="text-xs uppercase tracking-wider text-text-muted">Ссылка автора</span>
                <input
                  type="url"
                  value={authorLink}
                  onChange={e => setAuthorLink(e.target.value)}
                  placeholder="https://…"
                  spellCheck={false}
                  className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
                />
              </label>
            </div>
            <label className="flex flex-col gap-1">
              <span className="text-xs uppercase tracking-wider text-text-muted">Описание</span>
              <textarea
                rows={3}
                value={description}
                onChange={e => setDescription(e.target.value)}
                className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent resize-none"
              />
            </label>
            <label className="flex flex-col gap-1">
              <span className="text-xs uppercase tracking-wider text-text-muted">Видео (YouTube, опционально)</span>
              <input
                type="url"
                value={videoUrl}
                onChange={e => setVideoUrl(e.target.value)}
                placeholder="https://www.youtube.com/watch?v=…"
                spellCheck={false}
                className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
              />
            </label>

            <div className="flex flex-col gap-1.5">
              <span className="text-xs uppercase tracking-wider text-text-muted">Сервера *</span>
              <div className="flex items-center gap-4">
                {(['gta5rp', 'majestic'] as const).map(s => (
                  <label key={s} className="inline-flex items-center gap-2 text-sm text-text-primary cursor-pointer select-none">
                    <input
                      type="checkbox"
                      checked={servers.includes(s)}
                      onChange={() => toggleServer(s)}
                      className="accent-[var(--accent)]"
                    />
                    {SERVER_LABELS[s]}
                  </label>
                ))}
              </div>
            </div>

            <div className="flex flex-col gap-2">
              <div className="flex items-center gap-2">
                <span className="text-xs uppercase tracking-wider text-text-muted">
                  Фото ({photos.length}/{MAX_PHOTOS}) - первое станет обложкой
                </span>
                <div className="flex-1" />
                <button
                  type="button"
                  onClick={() => void onAddPhotos()}
                  disabled={photos.length >= MAX_PHOTOS}
                  className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg
                             border border-border-strong text-xs text-text-primary
                             hover:bg-bg-elevated transition-colors disabled:opacity-40"
                >
                  <ImagePlus size={12} />
                  Добавить фото
                </button>
              </div>
              {photos.length === 0 ? (
                <p className="text-[11px] text-text-muted">
                  Фото не выбраны. Если в архиве есть картинки, они подставились бы автоматически из анализа.
                </p>
              ) : (
                <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-2">
                  {photos.map((p, i) => {
                    const fileName = p.split(/[\\/]/).pop() ?? p;
                    return (
                      <div key={p} className="rounded-xl bg-bg-elevated border border-border-subtle overflow-hidden flex flex-col">
                        <div className="relative aspect-video bg-bg-base">
                          <img
                            src={`file:///${p.replace(/\\/g, '/')}`}
                            alt=""
                            className="absolute inset-0 w-full h-full object-cover"
                            onError={e => (e.currentTarget.style.display = 'none')}
                          />
                          {i === 0 && (
                            <span className="absolute top-1.5 left-1.5 px-1.5 py-0.5 rounded
                                             bg-accent/85 text-text-on-accent text-[9px] font-bold uppercase tracking-wider">
                              Обложка
                            </span>
                          )}
                        </div>
                        <div className="flex items-center gap-1 px-2 py-1.5">
                          <span className="flex-1 min-w-0 font-mono text-[10px] text-text-muted truncate" title={p}>
                            {fileName}
                          </span>
                          <button
                            type="button"
                            onClick={() => movePhoto(i, -1)}
                            disabled={i === 0}
                            title="Выше"
                            className="text-text-muted hover:text-accent disabled:opacity-30"
                          >
                            <ArrowUp size={12} />
                          </button>
                          <button
                            type="button"
                            onClick={() => movePhoto(i, 1)}
                            disabled={i === photos.length - 1}
                            title="Ниже"
                            className="text-text-muted hover:text-accent disabled:opacity-30"
                          >
                            <ArrowDown size={12} />
                          </button>
                          <button
                            type="button"
                            onClick={() => setPhotos(prev => prev.filter((_, j) => j !== i))}
                            title="Убрать"
                            className="text-text-muted hover:text-status-error"
                          >
                            <Trash2 size={12} />
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            <div className="flex items-center justify-end gap-2 pt-1">
              {!analysis.installable && (
                <span className="text-xs mr-auto" style={{ color: 'var(--status-error)' }}>
                  Пак не прошёл анализ - публикация заблокирована.
                </span>
              )}
              <button
                type="button"
                onClick={() => void onPublish()}
                disabled={!canPublish}
                className="inline-flex items-center gap-2 px-4 py-2 rounded-lg
                           bg-accent text-text-on-accent text-sm font-medium
                           hover:bg-accent-hover disabled:opacity-50"
              >
                {publishing ? <Loader2 size={14} className="animate-spin" /> : <Save size={14} />}
                <span>{publishing ? 'Публикуем… (пакуем + льём в R2)' : existingId ? 'Обновить' : 'Опубликовать'}</span>
              </button>
            </div>
          </section>
        )}

        <section className="flex flex-col gap-2">
          <h3 className="text-xs uppercase tracking-wider text-text-muted">
            Опубликованные карты <span className="text-text-muted/60">({maps.length})</span>
          </h3>
          {loadingMaps ? (
            <div className="text-center py-10 text-text-muted"><Loader2 size={20} className="animate-spin mx-auto" /></div>
          ) : maps.length === 0 ? (
            <div className="text-center text-text-muted py-10">
              <AlertCircle size={20} className="mx-auto mb-2" />
              <p className="text-sm">Пока пусто. Выбери .zip или папку с картой сверху.</p>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-2">
              {maps.map(m => (
                <div key={m.id} className="rounded-xl bg-bg-surface border border-border-subtle p-3 flex flex-col gap-2">
                  <div className="flex items-start gap-2.5">
                    <div className="shrink-0 w-[68px] h-[44px] rounded-lg overflow-hidden bg-bg-base border border-border-subtle
                                    flex items-center justify-center">
                      {m.previewUrl ? (
                        <img
                          src={m.previewUrl}
                          alt=""
                          onError={e => (e.currentTarget.style.display = 'none')}
                          className="w-full h-full object-cover"
                        />
                      ) : (
                        <MapIcon size={14} className="text-text-muted" />
                      )}
                    </div>
                    <div className="flex-1 min-w-0">
                      <span className="font-medium text-text-primary truncate block" title={m.name}>{m.name}</span>
                      {m.author && <span className="text-[11px] text-text-muted truncate block">by {m.author}</span>}
                    </div>
                    <div className="flex items-center gap-1.5 shrink-0">
                      <button
                        type="button"
                        onClick={() => startEditExisting(m)}
                        title="Обновить (перезалить пак / метаданные)"
                        aria-label="Обновить"
                        className="text-text-muted hover:text-accent"
                      >
                        <Pencil size={12} />
                      </button>
                      <button
                        type="button"
                        onClick={() => void onDelete(m)}
                        disabled={deletingId === m.id}
                        title="Удалить"
                        aria-label="Удалить"
                        className="text-text-muted hover:text-status-error disabled:opacity-40"
                      >
                        {deletingId === m.id ? <Loader2 size={12} className="animate-spin" /> : <Trash2 size={12} />}
                      </button>
                    </div>
                  </div>
                  <div className="flex items-center gap-2 text-[10px] text-text-muted font-mono mt-auto pt-1 flex-wrap">
                    <span>{m.supportedServers.map(s => SERVER_LABELS[s] ?? s).join(' + ') || '-'}</span>
                    <span>·</span>
                    <span>формат {m.packFormat || '-'}</span>
                    <span>·</span>
                    <span className="tabular-nums">{(m.sizeBytes / (1024 * 1024)).toFixed(0)} MB</span>
                    <span>·</span>
                    <span className="tabular-nums">{m.downloadCount} уст.</span>
                    {m.isVerified && (
                      <span className="ml-auto inline-flex items-center gap-1" title="Verified">
                        <CheckCircle2 size={10} style={{ color: 'var(--status-success)' }} />
                        verified
                      </span>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 3000}
      />
    </div>
  );
}
