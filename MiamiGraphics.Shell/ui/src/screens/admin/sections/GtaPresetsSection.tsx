import { useEffect, useMemo, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Plus, Sliders, Trash2, Eye, EyeOff, Trophy, Upload, FileX, Loader2, AlertCircle, Sparkles, X, Pencil } from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { bridge } from '@/bridge';
import { useGtaSettingsStore } from '@/store/gtaSettingsStore';
import type { GtaPreset, GtaPresetPatch, GtaPresetUploadRequest, GtaSettingsAnalysis } from '@/bridge/types';

export function GtaPresetsSection() {
  const presets       = useGtaSettingsStore(s => s.adminPresets);
  const loading       = useGtaSettingsStore(s => s.loadingAdmin);
  const loadAdmin     = useGtaSettingsStore(s => s.loadAdminPresets);
  const uploadPreset  = useGtaSettingsStore(s => s.uploadPreset);
  const patchPreset   = useGtaSettingsStore(s => s.patchPreset);
  const deletePreset  = useGtaSettingsStore(s => s.deletePreset);
  const analyzeXml    = useGtaSettingsStore(s => s.analyzeXml);

  useEffect(() => { void loadAdmin(); }, [loadAdmin]);

  return (
    <div className="h-full overflow-y-auto px-8 py-6 space-y-5">
      <header className="flex items-center gap-4">
        <span className="shrink-0 w-11 h-11 rounded-2xl bg-accent-soft text-accent
                         flex items-center justify-center shadow-z1">
          <Sliders size={18} />
        </span>
        <div className="min-w-0 flex-1">
          <h1 className="font-display text-xl font-bold tracking-[0.12em] text-text-primary uppercase leading-tight">
            GTA Presets
          </h1>
          <p className="text-xs text-text-secondary mt-0.5">
            Готовые settings.xml - то, что юзер видит в табе «PRO Сеттинги».
          </p>
        </div>
      </header>

      <UploadForm
        onUpload={uploadPreset}
        onAnalyze={analyzeXml}
      />

      <CatalogTable
        presets={presets}
        loading={loading}
        onPatch={patchPreset}
        onDelete={deletePreset}
      />
    </div>
  );
}

function UploadForm({
  onUpload, onAnalyze,
}: {
  onUpload:  (req: GtaPresetUploadRequest) => Promise<GtaPreset>;
  onAnalyze: (path: string) => Promise<GtaSettingsAnalysis>;
}) {
  const [xmlPath, setXmlPath]         = useState<string>('');
  const [analysis, setAnalysis]       = useState<GtaSettingsAnalysis | null>(null);
  const [analysing, setAnalysing]     = useState(false);
  const [analyzeError, setAnalyzeErr] = useState<string | null>(null);

  const [name, setName]               = useState('');
  const [description, setDesc]        = useState('');
  const [author, setAuthor]           = useState('');
  const [fpsLow, setFpsLow]           = useState<string>('');
  const [fpsHigh, setFpsHigh]         = useState<string>('');
  const [hwLabel, setHwLabel]         = useState('');
  const [isTournament, setTournament] = useState(false);
  const [viewerPriority, setPriority] = useState<string>('0');
  const [status, setStatus]           = useState<'published' | 'hidden'>('published');

  const [uploading, setUploading]     = useState(false);
  const [uploadError, setUploadErr]   = useState<string | null>(null);
  const [uploadOk, setUploadOk]       = useState<string | null>(null);

  const [batchProgress, setBatchProgress] = useState<{
    active: boolean; total: number; done: number; current: string | null; errors: string[];
  } | null>(null);

  const pickXml = async () => {
    const path = await bridge.openFileDialog('GTA settings.xml', '*.xml');
    if (!path) return;
    setXmlPath(path);
    setAnalysis(null);
    setAnalyzeErr(null);
    setAnalysing(true);
    try {
      const result = await onAnalyze(path);
      setAnalysis(result);
    } catch (e) {
      setAnalyzeErr(e instanceof Error ? e.message : String(e));
    } finally {
      setAnalysing(false);
    }
  };

  const reset = () => {
    setXmlPath('');
    setAnalysis(null);
    setAnalyzeErr(null);
    setName(''); setDesc(''); setAuthor('');
    setFpsLow(''); setFpsHigh(''); setHwLabel('');
    setTournament(false); setPriority('0'); setStatus('published');
    setUploadErr(null); setUploadOk(null);
  };

  const submit = async () => {
    if (!xmlPath) { setUploadErr('Сначала выбери XML.'); return; }
    if (!name.trim()) { setUploadErr('Имя пресета обязательно.'); return; }
    setUploadErr(null); setUploadOk(null); setUploading(true);
    try {
      const req: GtaPresetUploadRequest = {
        sourceXmlPath:   xmlPath,
        name:            name.trim(),
        description:     description.trim(),
        author:          author.trim(),
        expectedFpsLow:  parseFps(fpsLow),
        expectedFpsHigh: parseFps(fpsHigh),
        baselineHwLabel: hwLabel.trim() || null,
        isTournament,
        viewerPriority:  parseInt(viewerPriority, 10) || 0,
        status,
      };
      const row = await onUpload(req);
      setUploadOk(`Загружен: «${row.name}» (gain ${row.computedGainPercent}%)`);

      reset();
    } catch (e) {
      setUploadErr(e instanceof Error ? e.message : String(e));
    } finally {
      setUploading(false);
    }
  };

  const pickAndUploadBatch = async () => {

    if (typeof bridge.openFileDialogMulti !== 'function') {
      setUploadErr('Перезапусти лаунчер - UI bridge устарел (нет multi-file picker).');
      return;
    }
    let paths: string[] = [];
    try {
      paths = await bridge.openFileDialogMulti('GTA settings.xml', '*.xml');
    } catch (e) {
      setUploadErr((e as Error).message || 'Не удалось открыть диалог.');
      return;
    }
    if (!paths || paths.length === 0) return;

    setUploadErr(null); setUploadOk(null);
    const errors: string[] = [];
    setBatchProgress({ active: true, total: paths.length, done: 0, current: paths[0], errors });

    for (let i = 0; i < paths.length; i++) {
      const p = paths[i];
      setBatchProgress(s => s ? { ...s, current: p, done: i } : s);
      try {

        const stem = p.split(/[\\/]/).pop()?.replace(/\.xml$/i, '') ?? `preset-${i+1}`;
        const baseName = name.trim() || stem;
        const finalName = paths.length > 1 ? `${baseName} #${i+1}` : baseName;
        const req: GtaPresetUploadRequest = {
          sourceXmlPath:   p,
          name:            finalName,
          description:     description.trim(),
          author:          author.trim(),
          expectedFpsLow:  parseFps(fpsLow),
          expectedFpsHigh: parseFps(fpsHigh),
          baselineHwLabel: hwLabel.trim() || null,
          isTournament,
          viewerPriority:  parseInt(viewerPriority, 10) || 0,
          status,
        };
        await onUpload(req);
      } catch (e) {
        const msg = e instanceof Error ? e.message : String(e);
        errors.push(`${p.split(/[\\/]/).pop()}: ${msg}`);
        setBatchProgress(s => s ? { ...s, errors: [...errors] } : s);
      }
    }

    setBatchProgress({ active: false, total: paths.length, done: paths.length, current: null, errors });
    if (errors.length === 0) {
      setUploadOk(`Загружено пресетов: ${paths.length}`);
    } else {
      setUploadErr(`Часть не прошла (${errors.length} из ${paths.length}). См. список ниже.`);
    }
  };

  return (
    <GlassPanel depth="z1" tint="soft" rounded="2xl" className="px-6 py-5 space-y-4">
      <div className="flex items-center gap-3">
        <Plus size={16} className="text-accent" />
        <h2 className="font-display text-sm font-bold tracking-[0.14em] text-text-primary uppercase">
          Загрузить новый пресет
        </h2>
      </div>

      {}
      <div className="grid grid-cols-1 lg:grid-cols-[2fr,1fr] gap-3">
        <button
          type="button"
          onClick={pickXml}
          disabled={uploading || analysing}
          style={{ outline: 'none' }}
          className="flex items-center gap-3 px-4 py-3 rounded-xl
                     bg-bg-elevated border border-dashed border-glass-border
                     hover:border-accent/60 transition-colors
                     disabled:opacity-50 text-left"
        >
          <Upload size={16} className="shrink-0 text-accent" />
          <div className="min-w-0 flex-1">
            {xmlPath ? (
              <>
                <div className="text-[10px] uppercase tracking-[0.16em] text-text-muted">XML</div>
                <div className="font-mono text-xs text-text-primary truncate" title={xmlPath}>{xmlPath}</div>
              </>
            ) : (
              <>
                <div className="text-sm font-semibold text-text-primary">Выбрать settings.xml</div>
                <div className="text-[11px] text-text-muted">после выбора покажу gain%</div>
              </>
            )}
          </div>
        </button>

        <button
          type="button"
          onClick={pickAndUploadBatch}
          disabled={uploading || analysing || !!batchProgress?.active}
          style={{ outline: 'none' }}
          className="flex items-center gap-3 px-4 py-3 rounded-xl
                     bg-bg-elevated border border-dashed border-glass-border
                     hover:border-accent/60 transition-colors
                     disabled:opacity-50 text-left"
        >
          <Upload size={16} className="shrink-0 text-accent" />
          <div className="min-w-0 flex-1">
            <div className="text-sm font-semibold text-text-primary">Залить пачкой</div>
            <div className="text-[11px] text-text-muted">
              {batchProgress?.active
                ? `Загружаем ${batchProgress.done + 1} / ${batchProgress.total}…`
                : 'выбрать несколько XML - поедут с теми же мета-полями'}
            </div>
          </div>
        </button>

        <AnalysisPreview
          state={
            analysing ? { kind: 'loading' }
              : analyzeError ? { kind: 'error', message: analyzeError }
              : analysis ? { kind: 'ready', analysis }
              : { kind: 'idle' }
          }
        />
      </div>

      {}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        <Field label="Имя">
          <input
            type="text" value={name} onChange={e => setName(e.target.value)}
            placeholder="Blago Tournament 240Hz"
            className={inputClasses}
          />
        </Field>
        <Field label="Автор">
          <input
            type="text" value={author} onChange={e => setAuthor(e.target.value)}
            placeholder="Blago"
            className={inputClasses}
          />
        </Field>
        <Field label="Описание" full>
          <textarea
            value={description} onChange={e => setDesc(e.target.value)}
            rows={2}
            placeholder="Кратко: для каких машин, что отключено."
            className={inputClasses + ' resize-none'}
          />
        </Field>
        <Field label="Min FPS">
          <input
            type="number" value={fpsLow} onChange={e => setFpsLow(e.target.value)}
            placeholder="200" className={inputClasses}
          />
        </Field>
        <Field label="Max FPS">
          <input
            type="number" value={fpsHigh} onChange={e => setFpsHigh(e.target.value)}
            placeholder="240" className={inputClasses}
          />
        </Field>
        <Field label="Железо (подпись под FPS)" full>
          <input
            type="text" value={hwLabel} onChange={e => setHwLabel(e.target.value)}
            placeholder="i5-13600KF + RTX 4070"
            className={inputClasses}
          />
        </Field>
        <Field label="Viewer priority (выше = выше в каталоге)">
          <input
            type="number" value={viewerPriority} onChange={e => setPriority(e.target.value)}
            className={inputClasses}
          />
        </Field>
        <Field label="Статус">
          <select
            value={status} onChange={e => setStatus(e.target.value as 'published' | 'hidden')}
            className={inputClasses + ' cursor-pointer'}
          >
            <option value="published">Published - виден всем</option>
            <option value="hidden">Hidden - только в админке</option>
          </select>
        </Field>
        <label className="md:col-span-2 flex items-center gap-2 text-sm text-text-secondary cursor-pointer">
          <input
            type="checkbox"
            checked={isTournament}
            onChange={e => setTournament(e.target.checked)}
            className="accent-accent w-4 h-4"
          />
          <Trophy size={14} className="text-yellow-400" />
          Турнирный пресет (получит трофей-бейдж в каталоге)
        </label>
      </div>

      {batchProgress && batchProgress.errors.length > 0 && (
        <div className="rounded-xl border border-status-error/40 bg-status-error/5 px-4 py-3 text-[12px] text-status-error space-y-1">
          <div className="font-semibold">Ошибки в батче ({batchProgress.errors.length}):</div>
          {batchProgress.errors.slice(0, 8).map((e, i) => (
            <div key={i} className="font-mono text-text-secondary truncate" title={e}>{e}</div>
          ))}
          {batchProgress.errors.length > 8 && (
            <div className="text-text-muted">…и ещё {batchProgress.errors.length - 8}</div>
          )}
        </div>
      )}

      {uploadError && (
        <div className="flex items-start gap-2 px-3 py-2 rounded-xl bg-status-error/10 border border-status-error/30">
          <AlertCircle size={14} className="shrink-0 mt-0.5 text-status-error" />
          <span className="text-xs text-status-error">{uploadError}</span>
        </div>
      )}
      {uploadOk && (
        <div className="flex items-start gap-2 px-3 py-2 rounded-xl bg-accent-soft border border-accent/30">
          <Sparkles size={14} className="shrink-0 mt-0.5 text-accent" />
          <span className="text-xs text-accent">{uploadOk}</span>
        </div>
      )}

      <div className="flex items-center justify-end gap-2">
        <button
          type="button"
          onClick={reset}
          disabled={uploading}
          className="px-4 h-10 rounded-xl text-sm font-semibold text-text-muted hover:text-text-primary transition-colors"
        >
          Сбросить форму
        </button>
        <button
          type="button"
          onClick={submit}
          disabled={uploading || !xmlPath}
          style={{ outline: 'none' }}
          className="inline-flex items-center gap-2 px-5 h-10 rounded-xl text-sm font-display font-bold uppercase tracking-[0.06em]
                     bg-accent text-text-on-accent
                     hover:bg-[color-mix(in_srgb,var(--accent)_85%,white)]
                     disabled:opacity-50 disabled:cursor-not-allowed
                     transition-colors"
        >
          {uploading ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
          {uploading ? 'Грузим…' : 'Загрузить'}
        </button>
      </div>
    </GlassPanel>
  );
}

function CatalogTable({
  presets, loading, onPatch, onDelete,
}: {
  presets: GtaPreset[];
  loading: boolean;
  onPatch: (id: string, patch: GtaPresetPatch) => Promise<void>;
  onDelete: (id: string) => Promise<void>;
}) {

  const [confirmDelete, setConfirmDelete] = useState<GtaPreset | null>(null);
  const [editTarget,    setEditTarget]    = useState<GtaPreset | null>(null);

  const sorted = useMemo(
    () => [...presets].sort((a, b) =>
      b.viewerPriority - a.viewerPriority
      || b.uploadedAt.localeCompare(a.uploadedAt)),
    [presets]);

  return (
    <GlassPanel depth="z1" tint="soft" rounded="2xl" className="px-6 py-5">
      <div className="flex items-center justify-between mb-4">
        <h2 className="font-display text-sm font-bold tracking-[0.14em] text-text-primary uppercase">
          Каталог
        </h2>
        <span className="text-[10px] uppercase tracking-[0.18em] text-text-muted tabular-nums">
          {sorted.length} {plural(sorted.length, 'пресет', 'пресета', 'пресетов')}
        </span>
      </div>

      {loading && presets.length === 0 ? (
        <div className="py-12 flex items-center justify-center text-text-muted gap-2">
          <Loader2 size={14} className="animate-spin" />
          <span className="text-xs">Загружаем…</span>
        </div>
      ) : sorted.length === 0 ? (
        <div className="py-12 flex flex-col items-center justify-center text-text-muted gap-2">
          <FileX size={36} className="opacity-30" />
          <p className="text-xs">Каталог пуст. Загрузи первый пресет выше.</p>
        </div>
      ) : (
        <div className="space-y-2">
          {sorted.map(p => (
            <PresetRow
              key={p.id}
              preset={p}
              onTogglePublished={() => onPatch(p.id, { status: p.status === 'published' ? 'hidden' : 'published' })}
              onEdit={() => setEditTarget(p)}
              onDelete={() => setConfirmDelete(p)}
            />
          ))}
        </div>
      )}

      <AnimatePresence>
        {confirmDelete && (
          <DeleteConfirm
            preset={confirmDelete}
            onConfirm={async () => {
              const id = confirmDelete.id;
              setConfirmDelete(null);
              await onDelete(id);
            }}
            onCancel={() => setConfirmDelete(null)}
          />
        )}
        {editTarget && (
          <EditPresetModal
            preset={editTarget}
            onSave={async patch => { await onPatch(editTarget.id, patch); setEditTarget(null); }}
            onCancel={() => setEditTarget(null)}
          />
        )}
      </AnimatePresence>
    </GlassPanel>
  );
}

function PresetRow({
  preset, onTogglePublished, onEdit, onDelete,
}: {
  preset: GtaPreset;
  onTogglePublished: () => void;
  onEdit: () => void;
  onDelete: () => void;
}) {
  return (
    <div className="flex items-center gap-3 px-3 py-3 rounded-xl bg-bg-elevated/40
                    border border-transparent
                    hover:border-[color-mix(in_srgb,var(--accent)_40%,transparent)]
                    hover:shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_18%,transparent),0_8px_22px_-10px_color-mix(in_srgb,var(--accent)_40%,transparent)]
                    transition-[border-color,box-shadow] duration-300 ease-depth">
      <span className="shrink-0 w-9 h-9 rounded-lg flex items-center justify-center text-xs font-bold tabular-nums"
            style={{
              background: 'color-mix(in srgb, var(--accent) 15%, transparent)',
              color: 'var(--accent)',
            }}>
        +{preset.computedGainPercent}%
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-text-primary truncate">{preset.name}</span>
          {preset.isTournament && <Trophy size={12} className="shrink-0 text-yellow-400" />}
          <span className={
            'shrink-0 text-[9px] uppercase tracking-[0.14em] px-1.5 py-0.5 rounded ' +
            (preset.status === 'published'
              ? 'bg-accent-soft text-accent'
              : 'bg-glass-strong text-text-muted')
          }>
            {preset.status}
          </span>
        </div>
        <div className="flex items-center gap-2 text-[11px] text-text-muted truncate">
          {preset.author && <span>{preset.author}</span>}
          {preset.expectedFpsLow !== null && preset.expectedFpsHigh !== null && (
            <>
              <span>·</span>
              <span className="tabular-nums">{preset.expectedFpsLow}–{preset.expectedFpsHigh} FPS</span>
            </>
          )}
          <span>·</span>
          <span className="tabular-nums">{preset.downloadCount} установок</span>
        </div>
      </div>
      <button
        type="button"
        onClick={onEdit}
        title="Редактировать"
        className="shrink-0 w-9 h-9 rounded-lg flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-glass-strong transition-colors"
        style={{ outline: 'none' }}
      >
        <Pencil size={14} />
      </button>
      <button
        type="button"
        onClick={onTogglePublished}
        title={preset.status === 'published' ? 'Скрыть от пользователей' : 'Опубликовать'}
        className="shrink-0 w-9 h-9 rounded-lg flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-glass-strong transition-colors"
        style={{ outline: 'none' }}
      >
        {preset.status === 'published' ? <Eye size={14} /> : <EyeOff size={14} />}
      </button>
      <button
        type="button"
        onClick={onDelete}
        title="Удалить"
        className="shrink-0 w-9 h-9 rounded-lg flex items-center justify-center text-text-muted hover:text-status-error hover:bg-status-error/10 transition-colors"
        style={{ outline: 'none' }}
      >
        <Trash2 size={14} />
      </button>
    </div>
  );
}

function EditPresetModal({
  preset, onSave, onCancel,
}: {
  preset:   GtaPreset;
  onSave:   (patch: GtaPresetPatch) => Promise<void>;
  onCancel: () => void;
}) {

  const [name, setName]               = useState(preset.name);
  const [author, setAuthor]           = useState(preset.author ?? '');
  const [description, setDesc]        = useState(preset.description ?? '');
  const [fpsLow, setFpsLow]           = useState(preset.expectedFpsLow == null  ? '' : String(preset.expectedFpsLow));
  const [fpsHigh, setFpsHigh]         = useState(preset.expectedFpsHigh == null ? '' : String(preset.expectedFpsHigh));
  const [hwLabel, setHwLabel]         = useState(preset.baselineHwLabel ?? '');
  const [isTournament, setTournament] = useState(preset.isTournament);
  const [viewerPriority, setPriority] = useState(String(preset.viewerPriority));
  const [busy, setBusy] = useState(false);
  const [err, setErr]   = useState<string | null>(null);

  const save = async () => {
    if (busy) return;
    setBusy(true);
    setErr(null);
    try {
      const lo = fpsLow.trim()  === '' ? null : Math.max(0, Math.min(999, parseInt(fpsLow,  10) || 0));
      const hi = fpsHigh.trim() === '' ? null : Math.max(0, Math.min(999, parseInt(fpsHigh, 10) || 0));
      const prio = Math.max(0, Math.min(99, parseInt(viewerPriority, 10) || 0));
      await onSave({
        name:             name.trim(),
        author:           author.trim(),
        description:      description.trim(),
        expectedFpsLow:   lo,
        expectedFpsHigh:  hi,
        baselineHwLabel:  hwLabel.trim() || null,
        isTournament,
        viewerPriority:   prio,
      });
    } catch (e) {
      setErr(e instanceof Error ? e.message : String(e));
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/65 backdrop-blur-md flex items-center justify-center p-6"
         onClick={() => { if (!busy) onCancel(); }}>
      <motion.div
        initial={{ opacity: 0, scale: 0.97, y: 8 }}
        animate={{ opacity: 1, scale: 1,    y: 0 }}
        exit={{ opacity: 0, scale: 0.97 }}
        transition={{ duration: 0.22, ease: EASE_DEPTH }}
        className="w-full max-w-xl"
        onClick={e => e.stopPropagation()}
      >
        <GlassPanel depth="z3" tint="strong" rounded="2xl" className="p-6 flex flex-col gap-4">
          <header className="flex items-start justify-between gap-3">
            <div className="min-w-0 flex-1">
              <h3 className="font-display text-base font-bold text-text-primary tracking-tight">
                Редактировать пресет
              </h3>
              <p className="text-xs text-text-secondary mt-1">
                «{preset.name}» - текущие значения. XML с настройками не трогаем, только метаданные.
              </p>
            </div>
            <button type="button" onClick={onCancel} disabled={busy}
                    className="shrink-0 w-8 h-8 rounded-lg flex items-center justify-center
                               text-text-muted hover:text-text-primary hover:bg-glass-strong
                               disabled:opacity-40 disabled:cursor-not-allowed transition-colors">
              <X size={14} />
            </button>
          </header>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <EditField label="Название">
              <input type="text" value={name} onChange={e => setName(e.target.value)}
                     className={editFieldClass} />
            </EditField>
            <EditField label="Автор">
              <input type="text" value={author} onChange={e => setAuthor(e.target.value)}
                     className={editFieldClass} />
            </EditField>
            <EditField label="FPS - нижняя граница">
              <input type="number" min={0} max={999} value={fpsLow}
                     onChange={e => setFpsLow(e.target.value)}
                     className={editFieldClass} />
            </EditField>
            <EditField label="FPS - верхняя граница">
              <input type="number" min={0} max={999} value={fpsHigh}
                     onChange={e => setFpsHigh(e.target.value)}
                     className={editFieldClass} />
            </EditField>
            <EditField label="Базовое железо (метка)" className="md:col-span-2">
              <input type="text" value={hwLabel} onChange={e => setHwLabel(e.target.value)}
                     placeholder="напр. RTX 3060 + i5-12400F"
                     className={editFieldClass} />
            </EditField>
            <EditField label="Описание" className="md:col-span-2">
              <textarea value={description} onChange={e => setDesc(e.target.value)}
                        rows={3}
                        className={editFieldClass + ' resize-none py-2'} />
            </EditField>
            <EditField label="Приоритет в каталоге">
              <input type="number" min={0} max={99} value={viewerPriority}
                     onChange={e => setPriority(e.target.value)}
                     className={editFieldClass} />
            </EditField>
            <label className="flex items-center gap-2 cursor-pointer self-end pb-1">
              <input type="checkbox" checked={isTournament}
                     onChange={e => setTournament(e.target.checked)}
                     className="w-4 h-4 rounded cursor-pointer"
                     style={{ accentColor: 'var(--accent)' }} />
              <span className="text-sm text-text-primary inline-flex items-center gap-1.5">
                <Trophy size={12} className="text-yellow-400" /> Турнирный пресет
              </span>
            </label>
          </div>

          {err && (
            <div className="flex items-start gap-2 px-3 py-2 rounded-lg bg-status-error/10 text-status-error text-xs">
              <AlertCircle size={14} className="shrink-0 mt-0.5" />
              <span>{err}</span>
            </div>
          )}

          <div className="flex items-center justify-end gap-2 pt-1">
            <button type="button" onClick={onCancel} disabled={busy}
                    className="h-10 px-4 rounded-xl text-sm font-semibold uppercase tracking-wider
                               text-text-secondary hover:text-text-primary
                               disabled:opacity-40 disabled:cursor-not-allowed transition-colors">
              Отмена
            </button>
            <button type="button" onClick={() => void save()} disabled={busy || !name.trim()}
                    className="h-10 px-5 rounded-xl text-sm font-semibold uppercase tracking-wider
                               bg-accent text-bg-primary
                               hover:brightness-110 transition
                               disabled:opacity-50 disabled:cursor-not-allowed
                               inline-flex items-center gap-2">
              {busy ? <Loader2 size={14} className="animate-spin" /> : null}
              Сохранить
            </button>
          </div>
        </GlassPanel>
      </motion.div>
    </div>
  );
}

const editFieldClass =
  'w-full h-10 px-3 rounded-lg bg-glass border border-glass-border ' +
  'text-sm text-text-primary placeholder:text-text-muted ' +
  'focus:outline-none focus:border-accent transition-colors';

function EditField({ label, className = '', children }: { label: string; className?: string; children: React.ReactNode }) {
  return (
    <label className={'flex flex-col gap-1 ' + className}>
      <span className="text-[11px] uppercase tracking-wider text-text-muted">{label}</span>
      {children}
    </label>
  );
}

function DeleteConfirm({
  preset, onConfirm, onCancel,
}: {
  preset: GtaPreset;
  onConfirm: () => Promise<void>;
  onCancel: () => void;
}) {
  const [busy, setBusy] = useState(false);
  return (
    <div className="fixed inset-0 z-50 bg-black/65 backdrop-blur-md flex items-center justify-center p-6">
      <motion.div
        initial={{ opacity: 0, scale: 0.97 }}
        animate={{ opacity: 1, scale: 1 }}
        exit={{ opacity: 0, scale: 0.97 }}
        transition={{ duration: 0.22, ease: EASE_DEPTH }}
        className="w-full max-w-md"
      >
        <GlassPanel depth="z3" tint="strong" rounded="2xl" className="p-6 space-y-4">
          <div className="flex items-start gap-3">
            <span className="shrink-0 w-10 h-10 rounded-xl bg-status-error/15 text-status-error flex items-center justify-center">
              <Trash2 size={18} />
            </span>
            <div className="flex-1 min-w-0">
              <h3 className="font-display text-base font-bold text-text-primary tracking-tight">Удалить пресет?</h3>
              <p className="text-xs text-text-secondary mt-1 leading-relaxed">
                «{preset.name}» исчезнет из каталога; XML на R2 тоже будет удалён.
                Действие необратимо.
              </p>
            </div>
            <button
              type="button" onClick={onCancel}
              className="shrink-0 w-8 h-8 rounded-lg flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-glass-strong transition-colors"
              style={{ outline: 'none' }}
            >
              <X size={14} />
            </button>
          </div>
          <div className="flex items-center justify-end gap-2 pt-2">
            <button
              type="button" onClick={onCancel} disabled={busy}
              className="px-4 h-10 rounded-xl text-sm font-semibold text-text-muted hover:text-text-primary transition-colors"
            >
              Отмена
            </button>
            <button
              type="button"
              onClick={async () => { setBusy(true); try { await onConfirm(); } finally { setBusy(false); } }}
              disabled={busy}
              style={{ outline: 'none' }}
              className="inline-flex items-center gap-2 px-4 h-10 rounded-xl text-sm font-semibold
                         bg-status-error/85 text-white hover:bg-status-error
                         disabled:opacity-50 transition-colors"
            >
              {busy && <Loader2 size={14} className="animate-spin" />}
              Удалить
            </button>
          </div>
        </GlassPanel>
      </motion.div>
    </div>
  );
}

function AnalysisPreview({
  state,
}: {
  state:
    | { kind: 'idle' }
    | { kind: 'loading' }
    | { kind: 'error'; message: string }
    | { kind: 'ready'; analysis: GtaSettingsAnalysis };
}) {
  return (
    <div className="rounded-xl bg-bg-elevated/50 border border-glass-border px-3 py-3 flex flex-col justify-center min-h-[68px]">
      {state.kind === 'idle' && (
        <span className="text-[11px] text-text-muted">Gain покажу после выбора файла.</span>
      )}
      {state.kind === 'loading' && (
        <span className="inline-flex items-center gap-2 text-[11px] text-text-muted">
          <Loader2 size={12} className="animate-spin" />
          Анализируем XML…
        </span>
      )}
      {state.kind === 'error' && (
        <div className="text-[11px] text-status-error leading-relaxed">{state.message}</div>
      )}
      {state.kind === 'ready' && (
        <div className="flex items-center gap-3">
          <div className="flex items-baseline gap-1 text-accent">
            <span className="font-display text-2xl font-bold tabular-nums">+{state.analysis.gainPercent}</span>
            <span className="font-display text-sm font-bold">%</span>
          </div>
          <div className="text-[10px] uppercase tracking-[0.16em] text-text-muted">
            <div>прирост (cap +43%)</div>
            <div className="text-text-secondary normal-case tracking-normal">bias: {state.analysis.cpuBias}</div>
          </div>
        </div>
      )}
    </div>
  );
}

function Field({
  label, full = false, children,
}: {
  label: string;
  full?: boolean;
  children: React.ReactNode;
}) {
  return (
    <label className={'flex flex-col gap-1.5' + (full ? ' md:col-span-2' : '')}>
      <span className="text-[10px] uppercase tracking-[0.16em] text-text-muted">{label}</span>
      {children}
    </label>
  );
}

const inputClasses =
  'w-full px-3 h-10 rounded-xl bg-bg-elevated border border-glass-border ' +
  'text-sm text-text-primary placeholder:text-text-muted ' +
  'focus:border-accent transition-colors outline-none';

function parseFps(raw: string): number | null {
  const n = parseInt(raw, 10);
  return Number.isFinite(n) && n > 0 ? n : null;
}

function plural(n: number, one: string, few: string, many: string): string {
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 === 1 && mod100 !== 11) return one;
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return few;
  return many;
}
