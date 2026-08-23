import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Trash2, X, Save, Loader2, CheckCircle2, AlertCircle, Pencil, Image as ImageIcon, ImagePlus, Upload } from 'lucide-react';
import { bridge } from '@/bridge';
import type { LibraryComponent, ReduxAnalysis } from '@/bridge/types';
import { Toast } from '@/components/Toast';

const COMPONENT_LABELS_RU: Record<string, string> = {
  minimap:   'Минимап',
  crosshair: 'Прицел',
  tracers:   'Трейсера',
  bloodfx:   'Эффекты',
  timecycle: 'Таймциклы',
  arena:     'Арена',
};

interface UploadDraft {
  componentName: string;
  name: string;
  author: string;
  description: string;
}

interface EditDraft {
  name: string;
  author: string;
  description: string;
}

export function LibrarySection() {
  const { t } = useTranslation();
  const [items, setItems] = useState<LibraryComponent[]>([]);
  const [loading, setLoading] = useState(false);
  const [analysis, setAnalysis] = useState<ReduxAnalysis | null>(null);
  const [analysisBusy, setAnalysisBusy] = useState(false);
  const [drafts, setDrafts] = useState<Record<string, UploadDraft>>({});
  const [savingComponent, setSavingComponent] = useState<string | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);

  const [editDrafts, setEditDrafts] = useState<Record<string, EditDraft>>({});
  const [savingEditId, setSavingEditId] = useState<string | null>(null);

  const [uploadingPreviewId, setUploadingPreviewId] = useState<string | null>(null);
  const [uploadingGalleryId, setUploadingGalleryId] = useState<string | null>(null);

  const [stubFormOpen, setStubFormOpen] = useState<'minimap' | 'crosshair' | null>(null);

  const [soundsForm, setSoundsForm] = useState<'zip' | 'awc' | null>(null);

  const onUploadPreview = async (libraryId: string) => {
    try {
      const localPath = await bridge.openFileDialog(
        'Скриншот для библиотечного компонента',
        '*.png;*.jpg;*.jpeg;*.webp');
      if (!localPath) return;
      if (typeof bridge.adminUploadLibraryPreview !== 'function') {
        setToast({ tone: 'error', message: 'Перезапусти лаунчер - UI bridge устарел.' });
        return;
      }
      setUploadingPreviewId(libraryId);
      const url = await bridge.adminUploadLibraryPreview(libraryId, localPath);

      setItems(prev => prev.map(x => x.id === libraryId ? { ...x, previewUrl: url } : x));
      setToast({ tone: 'success', message: 'Скриншот загружен.' });
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message || 'Ошибка загрузки скриншота.' });
    } finally {
      setUploadingPreviewId(null);
    }
  };

  const onAddGallery = async (libraryId: string) => {
    try {
      if (typeof bridge.adminUploadLibraryGallery !== 'function') {
        setToast({ tone: 'error', message: 'Перезапусти лаунчер - UI bridge устарел.' });
        return;
      }
      const picked = await bridge.openFileDialogMulti(
        'Добавить фото в галерею', '*.png;*.jpg;*.jpeg;*.webp');
      if (!picked || picked.length === 0) return;
      setUploadingGalleryId(libraryId);
      const urls = await bridge.adminUploadLibraryGallery(libraryId, picked);
      setItems(prev => prev.map(x => x.id === libraryId ? { ...x, galleryUrls: urls } : x));
      setToast({ tone: 'success', message: `Добавлено фото: +${picked.length}. Всего в галерее: ${urls.length}.` });
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message || 'Ошибка добавления фото.' });
    } finally {
      setUploadingGalleryId(null);
    }
  };

  const load = async () => {
    setLoading(true);
    try { setItems(await bridge.libraryList()); }
    catch (e) { setToast({ tone: 'error', message: (e as Error).message }); }
    finally { setLoading(false); }
  };

  useEffect(() => { void load(); }, []);

  const byType = useMemo(() => {
    const groups: Record<string, LibraryComponent[]> = {};
    for (const it of items) {
      (groups[it.type] ??= []).push(it);
    }
    return groups;
  }, [items]);

  const onPickAndAnalyze = async () => {
    const path = await bridge.openFileDialog('GTA RPF', '*.rpf');
    if (!path) {
      const folder = await bridge.openFolderDialog();
      if (!folder) return;
      void runAnalyze(folder);
      return;
    }
    void runAnalyze(path);
  };

  const runAnalyze = async (sourcePath: string) => {
    setAnalysisBusy(true);
    try {
      const a = await bridge.adminReduxAnalyze(sourcePath);
      setAnalysis(a);

      const next: Record<string, UploadDraft> = {};
      for (const [name, info] of Object.entries(a.components)) {
        if (info.isFound) next[name] = { componentName: name, name: '', author: '', description: '' };
      }
      setDrafts(next);
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setAnalysisBusy(false);
    }
  };

  const onSaveComponent = async (componentName: string) => {
    if (!analysis) return;
    const d = drafts[componentName];
    if (!d || !d.name.trim()) {
      setToast({ tone: 'error', message: t('admin.library.errorNameRequired') });
      return;
    }
    setSavingComponent(componentName);
    try {
      await bridge.libraryUploadComponent({
        workDir: analysis.tempWorkDir,
        componentName,
        name: d.name.trim(),
        author: d.author.trim(),
        description: d.description.trim(),
      });
      setToast({ tone: 'success', message: t('admin.library.saved', { component: componentName }) });

      setDrafts(prev => {
        const next = { ...prev };
        delete next[componentName];
        return next;
      });
      void load();
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setSavingComponent(null);
    }
  };

  const onDelete = async (id: string, name: string) => {
    if (!confirm(t('admin.library.deleteConfirm', { name }))) return;
    try {
      await bridge.libraryDelete(id);
      setToast({ tone: 'success', message: t('admin.library.deleted', { name }) });
      void load();
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    }
  };

  const onStartEdit = (item: LibraryComponent) => {
    setEditDrafts(prev => ({
      ...prev,
      [item.id]: { name: item.name, author: item.author, description: item.description },
    }));
  };

  const onCancelEdit = (id: string) => {
    setEditDrafts(prev => {
      const next = { ...prev };
      delete next[id];
      return next;
    });
  };

  const onSaveEdit = async (id: string) => {
    const draft = editDrafts[id];
    if (!draft) return;
    if (!draft.name.trim()) {
      setToast({ tone: 'error', message: t('admin.library.errorNameRequired') });
      return;
    }
    setSavingEditId(id);
    try {
      const updated = await bridge.libraryPatch({
        id,
        name: draft.name.trim(),
        author: draft.author.trim(),
        description: draft.description.trim(),
      });

      setItems(prev => prev.map(it => (it.id === id ? updated : it)));
      onCancelEdit(id);
      setToast({ tone: 'success', message: t('admin.library.editSaved', { name: updated.name }) });
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setSavingEditId(null);
    }
  };

  return (
    <div className="h-full flex flex-col">
      <header className="h-14 px-6 flex items-center gap-3 border-b border-border-subtle shrink-0">
        <h1 className="text-base font-bold text-text-primary">{t('admin.library.title')}</h1>
        <span className="text-xs text-text-muted">{t('admin.library.count', { count: items.length })}</span>
        <div className="flex-1" />
        {}
        <button
          type="button"
          onClick={() => setStubFormOpen('minimap')}
          className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg
                     border border-border-strong text-sm text-text-primary
                     hover:bg-bg-elevated transition-colors"
        >
          <ImageIcon size={14} />
          Загрузить миникарту
        </button>
        <button
          type="button"
          onClick={() => setStubFormOpen('crosshair')}
          className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg
                     border border-border-strong text-sm text-text-primary
                     hover:bg-bg-elevated transition-colors"
        >
          <ImageIcon size={14} />
          Загрузить прицел
        </button>
        <button
          type="button"
          onClick={() => setSoundsForm('zip')}
          className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg
                     border border-border-strong text-sm text-text-primary
                     hover:bg-bg-elevated transition-colors"
        >
          <ImageIcon size={14} />
          Загрузить звуки
        </button>
        <button
          type="button"
          onClick={() => setSoundsForm('awc')}
          className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg
                     border border-border-strong text-sm text-text-primary
                     hover:bg-bg-elevated transition-colors"
        >
          <ImageIcon size={14} />
          Загрузить .awc звук
        </button>
        <button
          type="button"
          onClick={onPickAndAnalyze}
          disabled={analysisBusy}
          className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover disabled:opacity-50"
        >
          {analysisBusy ? <Loader2 size={14} className="animate-spin" /> : <Plus size={14} />}
          {analysisBusy ? t('admin.library.analyzing') : t('admin.library.uploadButton')}
        </button>
      </header>
      {stubFormOpen && (
        <QuickAddStubForm
          type={stubFormOpen}
          onClose={() => setStubFormOpen(null)}
          onCreated={(created) => {
            setItems(prev => [created, ...prev]);
            setToast({ tone: 'success', message: `Запись «${created.name}» создана.` });
            setStubFormOpen(null);
          }}
        />
      )}
      {soundsForm && (
        <SoundsAddForm
          mode={soundsForm}
          onClose={() => setSoundsForm(null)}
          onCreated={(created) => {
            setItems(prev => [created, ...prev]);
            setToast({ tone: 'success', message: `Звук «${created.name}» создан.` });
            setSoundsForm(null);
          }}
        />
      )}

      <div className="flex-1 overflow-y-auto px-6 py-4 flex flex-col gap-6">
        {}
        {analysis && Object.keys(drafts).length > 0 && (
          <section className="rounded-2xl bg-bg-surface border border-accent-40 p-5 flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <h2 className="font-bold text-text-primary">{t('admin.library.draftHeader')}</h2>
              <button type="button" onClick={() => { setAnalysis(null); setDrafts({}); }} className="text-text-muted hover:text-text-primary">
                <X size={14} />
              </button>
            </div>
            <p className="text-xs text-text-muted">{t('admin.library.draftHint')}</p>

            {Object.entries(drafts).map(([cname, d]) => (
              <div key={cname} className="rounded-xl bg-bg-elevated border border-border-subtle p-4 flex flex-col gap-3">
                <div className="flex items-center gap-2">
                  <span className="text-xs uppercase tracking-wider text-accent font-bold">
                    {COMPONENT_LABELS_RU[cname] ?? cname}
                  </span>
                  <span className="text-[10px] text-text-muted font-mono">{cname}</span>
                </div>
                <input
                  type="text"
                  placeholder={t('admin.library.draftNamePlaceholder')}
                  value={d.name}
                  onChange={e => setDrafts(prev => ({ ...prev, [cname]: { ...d, name: e.target.value } }))}
                  className="px-3 py-2 bg-bg-base border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
                />
                <div className="grid grid-cols-2 gap-2">
                  <input
                    type="text"
                    placeholder={t('admin.library.draftAuthorPlaceholder')}
                    value={d.author}
                    onChange={e => setDrafts(prev => ({ ...prev, [cname]: { ...d, author: e.target.value } }))}
                    className="px-3 py-2 bg-bg-base border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
                  />
                  <input
                    type="text"
                    placeholder={t('admin.library.draftDescPlaceholder')}
                    value={d.description}
                    onChange={e => setDrafts(prev => ({ ...prev, [cname]: { ...d, description: e.target.value } }))}
                    className="px-3 py-2 bg-bg-base border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
                  />
                </div>
                <div className="flex items-center justify-between gap-2">
                  <button
                    type="button"
                    onClick={() => setDrafts(prev => { const n = { ...prev }; delete n[cname]; return n; })}
                    className="text-xs text-text-muted hover:text-text-primary"
                  >
                    {t('admin.library.draftSkip')}
                  </button>
                  <button
                    type="button"
                    disabled={!d.name.trim() || savingComponent !== null}
                    onClick={() => void onSaveComponent(cname)}
                    className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover disabled:opacity-50"
                  >
                    {savingComponent === cname ? <Loader2 size={14} className="animate-spin" /> : <Save size={14} />}
                    {savingComponent === cname ? t('admin.library.draftSaving') : t('admin.library.draftSaveButton')}
                  </button>
                </div>
              </div>
            ))}
          </section>
        )}

        {}
        {loading ? (
          <div className="text-center py-12 text-text-muted"><Loader2 size={20} className="animate-spin mx-auto" /></div>
        ) : items.length === 0 && !analysis ? (
          <div className="text-center text-text-muted py-12">
            <AlertCircle size={20} className="mx-auto mb-2" />
            <p className="text-sm">{t('admin.library.empty')}</p>
          </div>
        ) : (
          Object.keys(byType).sort().map(type => (
            <section key={type} className="flex flex-col gap-2">
              <h3 className="text-xs uppercase tracking-wider text-text-muted">
                {COMPONENT_LABELS_RU[type] ?? type} <span className="text-text-muted/60">({byType[type].length})</span>
              </h3>
              <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-2">
                {byType[type].map(it => {
                  const editing = editDrafts[it.id];
                  if (editing) {
                    const isSaving = savingEditId === it.id;
                    return (
                      <div key={it.id} className="rounded-xl bg-bg-surface border border-accent/40 p-3 flex flex-col gap-2">
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-[10px] uppercase tracking-wider text-accent font-bold">
                            {t('admin.library.editTitle', 'Редактирование пресета')}
                          </span>
                          <span className="text-[10px] text-text-muted font-mono truncate" title={it.id}>{it.id}</span>
                        </div>
                        <input
                          type="text"
                          placeholder={t('admin.library.draftNamePlaceholder')}
                          value={editing.name}
                          onChange={e => setEditDrafts(prev => ({ ...prev, [it.id]: { ...editing, name: e.target.value } }))}
                          className="px-2 py-1.5 bg-bg-base border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
                        />
                        <input
                          type="text"
                          placeholder={t('admin.library.draftAuthorPlaceholder')}
                          value={editing.author}
                          onChange={e => setEditDrafts(prev => ({ ...prev, [it.id]: { ...editing, author: e.target.value } }))}
                          className="px-2 py-1.5 bg-bg-base border border-border-subtle rounded-lg text-xs text-text-primary placeholder:text-text-muted outline-none focus:border-accent"
                        />
                        <textarea
                          rows={2}
                          placeholder={t('admin.library.draftDescPlaceholder')}
                          value={editing.description}
                          onChange={e => setEditDrafts(prev => ({ ...prev, [it.id]: { ...editing, description: e.target.value } }))}
                          className="px-2 py-1.5 bg-bg-base border border-border-subtle rounded-lg text-xs text-text-primary placeholder:text-text-muted outline-none focus:border-accent resize-none"
                        />
                        <div className="flex items-center justify-between gap-2 pt-1">
                          <button
                            type="button"
                            onClick={() => onCancelEdit(it.id)}
                            disabled={isSaving}
                            className="text-xs text-text-muted hover:text-text-primary disabled:opacity-40"
                          >
                            {t('admin.library.editCancel', 'Отмена')}
                          </button>
                          <button
                            type="button"
                            onClick={() => void onSaveEdit(it.id)}
                            disabled={!editing.name.trim() || isSaving}
                            className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg bg-accent text-text-on-accent text-xs font-medium hover:bg-accent-hover disabled:opacity-50"
                          >
                            {isSaving ? <Loader2 size={12} className="animate-spin" /> : <Save size={12} />}
                            {isSaving ? t('admin.library.editSaving', 'Сохраняем…') : t('admin.library.editSave', 'Сохранить')}
                          </button>
                        </div>
                      </div>
                    );
                  }
                  const uploading = uploadingPreviewId === it.id;
                  return (
                    <div key={it.id} className="rounded-xl bg-bg-surface border border-border-subtle p-3 flex flex-col gap-2">
                      {}
                      <div className="flex items-start gap-2.5">
                        <button
                          type="button"
                          onClick={() => void onUploadPreview(it.id)}
                          disabled={uploading}
                          title={it.previewUrl ? 'Заменить скриншот' : 'Прикрепить скриншот'}
                          className="shrink-0 w-[68px] h-[44px] rounded-lg overflow-hidden bg-bg-base border border-border-subtle
                                     hover:border-accent transition-colors flex items-center justify-center relative
                                     disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                          {uploading ? (
                            <Loader2 size={14} className="animate-spin text-accent" />
                          ) : it.previewUrl ? (
                            <img
                              src={it.previewUrl}
                              alt=""
                              onError={(e) => (e.currentTarget.style.display = 'none')}
                              className="w-full h-full object-cover"
                            />
                          ) : (
                            <ImageIcon size={14} className="text-text-muted" />
                          )}
                          {!uploading && (
                            <span className="absolute inset-0 flex items-center justify-center
                                            bg-black/60 opacity-0 hover:opacity-100 transition-opacity
                                            text-[9px] uppercase tracking-wider text-white font-bold">
                              <Upload size={11} className="mr-1" /> {it.previewUrl ? 'Заменить' : 'Загрузить'}
                            </span>
                          )}
                        </button>
                        <div className="flex-1 min-w-0">
                          <span className="font-medium text-text-primary truncate block" title={it.name}>{it.name}</span>
                          {it.author && <span className="text-[11px] text-text-muted truncate block">by {it.author}</span>}
                        </div>
                        <div className="flex items-center gap-1 shrink-0">
                          <button
                            type="button"
                            onClick={() => void onAddGallery(it.id)}
                            disabled={uploadingGalleryId === it.id}
                            title={`Добавить фото в галерею${it.galleryUrls?.length ? ` (сейчас ${it.galleryUrls.length})` : ''}`}
                            aria-label="Добавить фото в галерею"
                            className="inline-flex items-center gap-0.5 text-text-muted hover:text-accent disabled:opacity-40"
                          >
                            {uploadingGalleryId === it.id
                              ? <Loader2 size={12} className="animate-spin" />
                              : <ImagePlus size={12} />}
                            {!!it.galleryUrls?.length && (
                              <span className="text-[9px] font-bold tabular-nums leading-none">{it.galleryUrls.length}</span>
                            )}
                          </button>
                          <button
                            type="button"
                            onClick={() => onStartEdit(it)}
                            title={t('admin.library.editButtonTitle', 'Редактировать')}
                            aria-label={t('admin.library.editButtonTitle', 'Редактировать')}
                            className="text-text-muted hover:text-accent"
                          >
                            <Pencil size={12} />
                          </button>
                          <button type="button" onClick={() => void onDelete(it.id, it.name)} title={t('admin.library.delete')} className="text-text-muted hover:text-status-error">
                            <Trash2 size={12} />
                          </button>
                        </div>
                      </div>
                      {it.description && <p className="text-xs text-text-secondary line-clamp-2">{it.description}</p>}
                      <div className="flex items-center gap-2 text-[10px] text-text-muted font-mono mt-auto pt-1">
                        <span>{(it.sizeBytes / 1024).toFixed(0)} KB</span>
                        {it.sourceRpfVersion && <><span>·</span><span>{it.sourceRpfVersion}</span></>}
                        <span className="ml-auto inline-flex items-center gap-1" title={it.sha256}>
                          <CheckCircle2 size={10} style={{ color: 'var(--status-success)' }} />
                          {it.sha256.slice(0, 8)}
                        </span>
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>
          ))
        )}
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

function QuickAddStubForm({
  type, onClose, onCreated,
}: {
  type: 'minimap' | 'crosshair';
  onClose: () => void;
  onCreated: (created: LibraryComponent) => void;
}) {
  const isReticle = type === 'crosshair';
  const labels = {
    title:        isReticle ? 'Создать запись прицела'                                                                            : 'Создать запись миникарты',
    catalogName:  isReticle ? '/Прицелы'                                                                                          : '/Миникарты',
    description:  isReticle
      ? 'Запись попадёт в каталог /Прицелы с обложкой. Без .gfx это превью-запись (визуальная справка); с .gfx она устанавливается.'
      : 'Запись попадёт в каталог /Миникарты с обложкой. Установка такой записи невозможна (нет .gfx) - но это полезно как визуальная справка.',
    placeholder:  isReticle ? 'Например: «Точка-крестик»'                                                                         : 'Например: «Race Map Allegri»',
    gfxFileLabel: isReticle ? 'hud_reticle.gfx'                                                                                   : 'minimap.gfx',
    photoTitle:   isReticle ? 'Скриншот прицела'                                                                                  : 'Скриншот миникарты',
    gfxDialog:    isReticle ? 'Файл hud_reticle.gfx'                                                                              : 'Файл minimap.gfx',
    catalogHint:  isReticle
      ? 'С .gfx-файлом - запись будет установимой на /Прицелы и в визарде сборки. Без него - только превью в каталоге.'
      : 'С .gfx-файлом - запись будет установимой на /Миникарты и в визарде сборки. Без него - только превью в каталоге (для визуальной справки).',
  };

  const [name, setName]               = useState('');
  const [author, setAuthor]           = useState('');
  const [description, setDescription] = useState('');

  const [photoPath, setPhotoPath]     = useState<string | null>(null);
  const [photoLabel, setPhotoLabel]   = useState<string>('');

  const [galleryPaths, setGalleryPaths] = useState<string[]>([]);
  const [gfxPath, setGfxPath]         = useState<string | null>(null);
  const [gfxLabel, setGfxLabel]       = useState<string>('');
  const [busy, setBusy]               = useState(false);
  const [error, setError]             = useState<string | null>(null);

  const [createdRow, setCreatedRow]   = useState<LibraryComponent | null>(null);

  const onPickPhoto = async () => {
    setError(null);
    try {

      const picked = await bridge.openFileDialogMulti(
        labels.photoTitle, '*.png;*.jpg;*.jpeg;*.webp');
      if (!picked || picked.length === 0) return;
      const [cover, ...rest] = picked;
      setPhotoPath(cover);
      const m = cover.split(/[\\/]/);
      setPhotoLabel(m[m.length - 1] ?? cover);
      setGalleryPaths(rest);
    } catch (e) { setError((e as Error).message); }
  };

  const onPickGfx = async () => {
    setError(null);
    try {
      const p = await bridge.openFileDialog(labels.gfxDialog, '*.gfx');
      if (!p) return;
      setGfxPath(p);
      const m = p.split(/[\\/]/);
      setGfxLabel(m[m.length - 1] ?? p);
    } catch (e) { setError((e as Error).message); }
  };

  const onSubmit = async () => {
    if (!name.trim()) { setError('Имя обязательно.'); return; }
    if (!photoPath)   { setError('Прикрепи фото - без него запись не имеет смысла.'); return; }
    setBusy(true);
    setError(null);
    try {

      let created: LibraryComponent;
      if (createdRow) {
        created = createdRow;
      } else if (gfxPath) {

        const creator = isReticle ? bridge.adminCreateLibraryReticle : bridge.adminCreateLibraryMinimap;
        if (typeof creator !== 'function') {
          setError('Перезапусти лаунчер - UI bridge устарел.');
          setBusy(false);
          return;
        }
        created = isReticle
          ? await bridge.adminCreateLibraryReticle(
              name.trim(), author.trim(), description.trim(), gfxPath, photoPath)
          : await bridge.adminCreateLibraryMinimap(
              name.trim(), author.trim(), description.trim(), gfxPath, photoPath);
        setCreatedRow(created);
      } else {

        if (typeof bridge.adminCreateLibraryStub !== 'function') {
          setError('Перезапусти лаунчер - UI bridge устарел.');
          setBusy(false);
          return;
        }
        created = await bridge.adminCreateLibraryStub(
          type, name.trim(), author.trim(), description.trim(), photoPath);
        setCreatedRow(created);
      }

      if (galleryPaths.length > 0) {
        if (typeof bridge.adminUploadLibraryGallery !== 'function') {
          setError('Перезапусти лаунчер - bridge не знает adminUploadLibraryGallery.');
          setBusy(false);
          return;
        }
        try {
          const urls = await bridge.adminUploadLibraryGallery(created.id, galleryPaths);
          created = { ...created, galleryUrls: urls };
        } catch (e) {
          const msg = (e as Error).message ?? '';

          const looksLikeMissingColumn =
            /gallery_urls/i.test(msg) ||
            /42703/.test(msg) ||
            /column.*not exist/i.test(msg) ||
            /unknown column/i.test(msg);
          if (looksLikeMissingColumn) {
            setError(
              'Не нашёл колонку gallery_urls в library_components. ' +
              'Запусти в Supabase SQL: ' +
              "ALTER TABLE library_components ADD COLUMN gallery_urls JSONB DEFAULT '[]'::jsonb; " +
              'и нажми Создать ещё раз.'
            );
          } else {
            setError(`Не удалось залить галерею: ${msg}`);
          }
          setBusy(false);
          return;
        }
      }
      onCreated(created);
    } catch (e) {
      setError((e as Error).message || 'Ошибка создания.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6"
         onClick={busy ? undefined : onClose}>
      <div
        className="w-full max-w-[480px] rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-center justify-between gap-2">
          <h2 className="text-base font-bold text-text-primary">{labels.title}</h2>
          <button type="button" onClick={onClose} disabled={busy}
                  className="text-text-muted hover:text-text-primary disabled:opacity-40">
            <X size={16} />
          </button>
        </div>
        <p className="text-xs text-text-muted -mt-2">{labels.description}</p>

        <label className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wider text-text-muted">Название *</span>
          <input
            type="text"
            value={name}
            onChange={e => setName(e.target.value)}
            placeholder={labels.placeholder}
            className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent"
          />
        </label>
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
          <span className="text-xs uppercase tracking-wider text-text-muted">Описание</span>
          <textarea
            rows={2}
            value={description}
            onChange={e => setDescription(e.target.value)}
            className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent resize-none"
          />
        </label>

        <div className="flex items-center gap-3 flex-wrap">
          <button
            type="button"
            onClick={onPickPhoto}
            disabled={busy}
            className="inline-flex items-center gap-2 px-3 py-2 rounded-lg
                       border border-border-strong text-sm text-text-primary
                       hover:bg-bg-elevated transition-colors disabled:opacity-50"
          >
            <Upload size={13} />
            <span>{photoPath ? 'Сменить фото' : 'Выбрать фото *'}</span>
          </button>
          {photoLabel && (
            <span className="font-mono text-[10.5px] text-text-muted truncate flex-1 min-w-0" title={photoLabel}>
              {photoLabel}
            </span>
          )}
          {galleryPaths.length > 0 && (
            <span
              className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md
                         bg-white/[0.06] border border-white/[0.10] text-text-secondary
                         text-[10.5px] font-semibold uppercase tracking-wider"
              title={galleryPaths.join('\n')}
            >
              +{galleryPaths.length} в галерею
            </span>
          )}
        </div>
        <p className="text-[11px] text-text-muted -mt-1.5 leading-snug">
          Можно выбрать несколько файлов сразу: первый станет обложкой каталога, остальные - галереей карточки (стрелочки при наведении).
        </p>

        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={onPickGfx}
            disabled={busy}
            className="inline-flex items-center gap-2 px-3 py-2 rounded-lg
                       border border-border-strong text-sm text-text-primary
                       hover:bg-bg-elevated transition-colors disabled:opacity-50"
          >
            <Upload size={13} />
            <span>{gfxPath ? `Сменить ${labels.gfxFileLabel}` : `Прикрепить ${labels.gfxFileLabel} (опционально)`}</span>
          </button>
          {gfxLabel && (
            <span className="font-mono text-[10.5px] text-text-muted truncate flex-1" title={gfxLabel}>
              {gfxLabel}
            </span>
          )}
        </div>
        <p className="text-[11px] text-text-muted -mt-1.5 leading-snug">{labels.catalogHint}</p>

        {createdRow && (
          <div className="flex items-start gap-2 text-[11px] leading-snug px-3 py-2 rounded-lg
                          bg-white/[0.04] border border-white/[0.10] text-text-secondary">
            <CheckCircle2 size={12} className="mt-0.5 shrink-0" />
            <span>
              Запись «{createdRow.name}» уже сохранена в базе (id <span className="font-mono">{createdRow.id}</span>).
              Осталось залить галерею - нажми «Создать», чтобы повторить только этот шаг.
            </span>
          </div>
        )}

        {error && (
          <div className="flex items-start gap-2 text-xs leading-snug" style={{ color: 'var(--status-error)' }}>
            <AlertCircle size={12} className="mt-0.5 shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <div className="flex justify-end gap-2 pt-1">
          <button type="button" onClick={onClose} disabled={busy}
                  className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated disabled:opacity-40">
            Отмена
          </button>
          <button
            type="button"
            onClick={onSubmit}
            disabled={busy || !name.trim() || !photoPath}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg
                       bg-accent text-text-on-accent text-sm font-medium
                       hover:bg-accent-hover disabled:opacity-50"
          >
            {busy ? <Loader2 size={13} className="animate-spin" /> : <Save size={13} />}
            <span>{busy ? 'Создаём…' : 'Создать'}</span>
          </button>
        </div>
      </div>
    </div>
  );
}

function SoundsAddForm({
  mode, onClose, onCreated,
}: {
  mode: 'zip' | 'awc';
  onClose: () => void;
  onCreated: (created: LibraryComponent) => void;
}) {
  const isAwc = mode === 'awc';
  const [name, setName]               = useState('');
  const [author, setAuthor]           = useState('');
  const [description, setDescription] = useState('');
  const [photoPath, setPhotoPath]     = useState<string | null>(null);
  const [photoLabel, setPhotoLabel]   = useState<string>('');
  const [galleryPaths, setGalleryPaths] = useState<string[]>([]);
  const [zipPath, setZipPath]         = useState<string | null>(null);
  const [zipLabel, setZipLabel]       = useState<string>('');
  const [videoPath, setVideoPath]     = useState<string | null>(null);
  const [videoLabel, setVideoLabel]   = useState<string>('');

  const [videoSource, setVideoSource] = useState<'file' | 'youtube'>('file');
  const [youtubeUrl, setYoutubeUrl]   = useState<string>('');
  const [busy, setBusy]               = useState(false);
  const [error, setError]             = useState<string | null>(null);
  const [createdRow, setCreatedRow]   = useState<LibraryComponent | null>(null);

  const onPickPhoto = async () => {
    setError(null);
    try {
      const picked = await bridge.openFileDialogMulti(
        'Скриншот пака звуков', '*.png;*.jpg;*.jpeg;*.webp');
      if (!picked || picked.length === 0) return;
      const [cover, ...rest] = picked;
      setPhotoPath(cover);
      const m = cover.split(/[\\/]/);
      setPhotoLabel(m[m.length - 1] ?? cover);
      setGalleryPaths(rest);
    } catch (e) { setError((e as Error).message); }
  };

  const onPickZip = async () => {
    setError(null);
    try {
      const p = isAwc
        ? await bridge.openFileDialog('Файл звука .awc', '*.awc')
        : await bridge.openFileDialog('Архив с .rpf файлами', '*.zip;*.rar');
      if (!p) return;
      setZipPath(p);
      const m = p.split(/[\\/]/);
      setZipLabel(m[m.length - 1] ?? p);
    } catch (e) { setError((e as Error).message); }
  };

  const onPickVideo = async () => {
    setError(null);
    try {
      const p = await bridge.openFileDialog('Видео-превью пака', '*.mp4;*.webm;*.mov;*.m4v');
      if (!p) return;
      setVideoPath(p);
      const m = p.split(/[\\/]/);
      setVideoLabel(m[m.length - 1] ?? p);
    } catch (e) { setError((e as Error).message); }
  };

  const onSubmit = async () => {
    if (!name.trim()) { setError('Имя обязательно.'); return; }
    if (!zipPath && !createdRow) { setError(isAwc ? 'Прикрепи .awc файл.' : 'Прикрепи .zip с .rpf файлами.'); return; }

    const hasYoutubeFallback = videoSource === 'youtube' && !!youtubeUrl.trim();
    if (!photoPath && !hasYoutubeFallback) {
      setError('Прикрепи фото обложки или ссылку на YouTube - нужна хоть какая-то картинка для каталога.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      let created: LibraryComponent;
      if (createdRow) {
        created = createdRow;
      } else {
        const createFn = (isAwc ? bridge.adminCreateLibraryAwc : bridge.adminCreateLibrarySounds)?.bind(bridge);
        if (typeof createFn !== 'function') {
          setError('Перезапусти лаунчер - UI bridge устарел.');
          setBusy(false);
          return;
        }

        created = await createFn(
          name.trim(), author.trim(), description.trim(), zipPath!, photoPath ?? '');
        setCreatedRow(created);
      }

      if (galleryPaths.length > 0) {
        try {
          if (typeof bridge.adminUploadLibraryGallery === 'function') {
            const urls = await bridge.adminUploadLibraryGallery(created.id, galleryPaths);
            created = { ...created, galleryUrls: urls };
          }
        } catch (e) {
          setError(`Запись создана, но галерею залить не удалось: ${(e as Error).message}`);
          setBusy(false);
          return;
        }
      }

      const videoSourcePath =
        videoSource === 'youtube' && youtubeUrl.trim()
          ? youtubeUrl.trim()
          : videoPath;
      if (videoSourcePath) {
        try {
          if (typeof bridge.adminUploadLibraryVideo === 'function') {
            const url = await bridge.adminUploadLibraryVideo(created.id, videoSourcePath);
            created = { ...created, previewVideoUrl: url };
          }
        } catch (e) {
          const msg = (e as Error).message ?? '';
          const looksLikeMissingColumn =
            /preview_video_url/i.test(msg) ||
            /42703/.test(msg) ||
            /column.*not exist/i.test(msg);
          if (looksLikeMissingColumn) {
            setError(
              'Не нашёл колонку preview_video_url в library_components. ' +
              'Запусти в Supabase SQL: ' +
              'ALTER TABLE library_components ADD COLUMN preview_video_url TEXT; ' +
              'и нажми «Создать» ещё раз.'
            );
          } else {
            setError(`Запись создана, но видео залить не удалось: ${msg}`);
          }
          setBusy(false);
          return;
        }
      }
      onCreated(created);
    } catch (e) {
      setError((e as Error).message || 'Ошибка создания.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6"
         onClick={busy ? undefined : onClose}>
      <div
        className="w-full max-w-[520px] rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-center justify-between gap-2">
          <h2 className="text-base font-bold text-text-primary">
            {isAwc ? 'Добавить .awc звук' : 'Создать пак звуков'}
          </h2>
          <button type="button" onClick={onClose} disabled={busy}
                  className="text-text-muted hover:text-text-primary disabled:opacity-40">
            <X size={16} />
          </button>
        </div>
        <p className="text-xs text-text-muted -mt-2 leading-snug">
          {isAwc ? (
            <>
              Один файл <span className="font-mono text-[10.5px]">.awc</span>. При установке лаунчер
              находит ванильный файл с таким же именем внутри
              <span className="font-mono text-[10.5px] mx-1">update.rpf</span> и заменяет его.
              Имя .awc должно совпадать с оригиналом (напр. <span className="font-mono text-[10.5px]">weapon_heavy_rifle.awc</span>).
            </>
          ) : (
            <>
              Пак - это .zip с одним или несколькими файлами .rpf, которые ставятся в
              <span className="font-mono text-[10.5px] mx-1">x64/audio/sfx/</span>. При установке
              лаунчер сделает резервную копию оригиналов и заменит файлы.
            </>
          )}
        </p>

        <label className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wider text-text-muted">Название *</span>
          <input
            type="text"
            value={name}
            onChange={e => setName(e.target.value)}
            placeholder="Например: «Тяжёлые выстрелы»"
            className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent"
          />
        </label>
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
          <span className="text-xs uppercase tracking-wider text-text-muted">Описание</span>
          <textarea
            rows={2}
            value={description}
            onChange={e => setDescription(e.target.value)}
            className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent resize-none"
          />
        </label>

        <div className="flex items-center gap-3 flex-wrap">
          <button
            type="button"
            onClick={onPickPhoto}
            disabled={busy}
            className="inline-flex items-center gap-2 px-3 py-2 rounded-lg
                       border border-border-strong text-sm text-text-primary
                       hover:bg-bg-elevated transition-colors disabled:opacity-50"
          >
            <Upload size={13} />
            <span>{photoPath ? 'Сменить фото' : 'Выбрать фото (опционально)'}</span>
          </button>
          {photoLabel && (
            <span className="font-mono text-[10.5px] text-text-muted truncate flex-1 min-w-0" title={photoLabel}>
              {photoLabel}
            </span>
          )}
          {galleryPaths.length > 0 && (
            <span
              className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md
                         bg-white/[0.06] border border-white/[0.10] text-text-secondary
                         text-[10.5px] font-semibold uppercase tracking-wider"
              title={galleryPaths.join('\n')}
            >
              +{galleryPaths.length} в галерею
            </span>
          )}
        </div>
        <p className="text-[11px] text-text-muted -mt-1.5 leading-snug">
          Можно выбрать несколько файлов сразу: первый станет обложкой каталога, остальные - галереей. Если оставишь пустым и укажешь YouTube-ссылку - обложкой автоматически станет превью с YouTube.
        </p>

        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={onPickZip}
            disabled={busy || !!createdRow}
            className="inline-flex items-center gap-2 px-3 py-2 rounded-lg
                       border border-border-strong text-sm text-text-primary
                       hover:bg-bg-elevated transition-colors disabled:opacity-50"
          >
            <Upload size={13} />
            <span>
              {isAwc
                ? (zipPath ? 'Сменить .awc' : 'Прикрепить .awc *')
                : (zipPath ? 'Сменить архив' : 'Прикрепить .zip / .rar *')}
            </span>
          </button>
          {zipLabel && (
            <span className="font-mono text-[10.5px] text-text-muted truncate flex-1" title={zipLabel}>
              {zipLabel}
            </span>
          )}
        </div>
        <p className="text-[11px] text-text-muted -mt-1.5 leading-snug">
          {isAwc
            ? <>Один файл <span className="font-mono">.awc</span>. Лаунчер заменит ванильный файл с тем же именем внутри <span className="font-mono">update.rpf</span> (где бы он ни лежал). Перед заменой оригинал сохраняется для отката.</>
            : <>Принимаем .zip и .rar. Внутри должны быть .rpf файлы из <span className="font-mono">x64/audio/sfx</span> (могут лежать в любой подпапке - ищем рекурсивно). Лаунчер заменит только те файлы, которые есть в стандартной GTA.</>}
        </p>

        <div className="flex flex-col gap-2 mt-1">
          <span className="text-xs uppercase tracking-wider text-text-muted">Видео-превью (опционально)</span>
          {}
          <div className="inline-flex items-center gap-1 p-1 rounded-lg
                          bg-bg-elevated border border-border-subtle self-start">
            <button
              type="button"
              onClick={() => setVideoSource('file')}
              disabled={busy}
              className={
                'px-3 py-1 rounded-md text-[11px] font-bold uppercase tracking-wider transition-colors '
                + (videoSource === 'file'
                    ? 'bg-bg-base text-text-primary shadow-[inset_0_0_0_1px_rgba(255,255,255,0.10)]'
                    : 'text-text-muted hover:text-text-secondary')
              }
            >
              Файл .mp4
            </button>
            <button
              type="button"
              onClick={() => setVideoSource('youtube')}
              disabled={busy}
              className={
                'px-3 py-1 rounded-md text-[11px] font-bold uppercase tracking-wider transition-colors '
                + (videoSource === 'youtube'
                    ? 'bg-bg-base text-text-primary shadow-[inset_0_0_0_1px_rgba(255,255,255,0.10)]'
                    : 'text-text-muted hover:text-text-secondary')
              }
            >
              YouTube ссылка
            </button>
          </div>

          {videoSource === 'file' ? (
            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={onPickVideo}
                disabled={busy}
                className="inline-flex items-center gap-2 px-3 py-2 rounded-lg
                           border border-border-strong text-sm text-text-primary
                           hover:bg-bg-elevated transition-colors disabled:opacity-50"
              >
                <Upload size={13} />
                <span>{videoPath ? 'Сменить видео' : 'Прикрепить .mp4 / .webm'}</span>
              </button>
              {videoLabel && (
                <span className="font-mono text-[10.5px] text-text-muted truncate flex-1" title={videoLabel}>
                  {videoLabel}
                </span>
              )}
            </div>
          ) : (
            <input
              type="url"
              value={youtubeUrl}
              onChange={e => setYoutubeUrl(e.target.value)}
              disabled={busy}
              placeholder="https://www.youtube.com/watch?v=…"
              spellCheck={false}
              className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg
                         text-sm text-text-primary placeholder:text-text-muted
                         outline-none focus:border-accent disabled:opacity-50"
            />
          )}
        </div>
        <p className="text-[11px] text-text-muted -mt-1 leading-snug">
          На карточке в каталоге начнёт играть через 2 сек после наведения - юзер сможет послушать звуки до установки.
          {videoSource === 'youtube'
            ? ' YouTube-ролик скачается на бэкенде и будет залит в R2 как обычный mp4 (выбираем лучший muxed-поток - обычно 720p).'
            : ' Подойдёт короткое .mp4/.webm/.mov (до 30 сек, ~50 MB).'}
        </p>

        {createdRow && (
          <div className="flex items-start gap-2 text-[11px] leading-snug px-3 py-2 rounded-lg
                          bg-white/[0.04] border border-white/[0.10] text-text-secondary">
            <CheckCircle2 size={12} className="mt-0.5 shrink-0" />
            <span>
              Пак «{createdRow.name}» уже сохранён (id <span className="font-mono">{createdRow.id}</span>).
              Осталось залить галерею - нажми «Создать», чтобы повторить только этот шаг.
            </span>
          </div>
        )}

        {error && (
          <div className="flex items-start gap-2 text-xs leading-snug" style={{ color: 'var(--status-error)' }}>
            <AlertCircle size={12} className="mt-0.5 shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <div className="flex justify-end gap-2 pt-1">
          <button type="button" onClick={onClose} disabled={busy}
                  className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated disabled:opacity-40">
            Отмена
          </button>
          <button
            type="button"
            onClick={onSubmit}
            disabled={busy || !name.trim() || (!zipPath && !createdRow)
                      || (!photoPath && !(videoSource === 'youtube' && !!youtubeUrl.trim()))}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg
                       bg-accent text-text-on-accent text-sm font-medium
                       hover:bg-accent-hover disabled:opacity-50"
          >
            {busy ? <Loader2 size={13} className="animate-spin" /> : <Save size={13} />}
            <span>{busy ? 'Создаём…' : 'Создать'}</span>
          </button>
        </div>
      </div>
    </div>
  );
}
