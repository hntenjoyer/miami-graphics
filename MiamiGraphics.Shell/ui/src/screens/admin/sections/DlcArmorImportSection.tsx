import { useEffect, useState } from 'react';
import { motion } from 'framer-motion';
import {
  FolderOpen, Loader2, FileBox, AlertTriangle, CheckCircle2,
  XCircle, RefreshCw, ChevronDown, ChevronRight, Upload,
} from 'lucide-react';
import { bridge } from '@/bridge';
import type { DlcArmorInspectionResult, DlcArmorCandidate, DlcArmorImportResult } from '@/bridge/types';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { ArmorPreview3D } from '@/screens/armor/ArmorPreview3D';

export function DlcArmorImportSection() {
  const [path, setPath] = useState<string>('');
  const [busy, setBusy] = useState(false);

  const [cancelling, setCancelling] = useState(false);
  const [report, setReport] = useState<DlcArmorInspectionResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const pickFile = async () => {
    try {
      const picked = await bridge.openFileDialog('GTA V DLC RPF', '*.rpf');
      if (picked) setPath(picked);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const runInspect = async () => {
    if (!path) return;
    setBusy(true);
    setCancelling(false);
    setError(null);
    setReport(null);
    try {
      const r = await bridge.inspectDlcRpfArmor(path);
      setReport(r);
      if (r.errorMessage) setError(r.errorMessage);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
      setCancelling(false);
    }
  };

  const cancelInspect = async () => {
    if (!busy || cancelling) return;
    setCancelling(true);
    try { await bridge.inspectDlcRpfArmorCancel(); }
    catch (e) { console.warn('[DlcImport] cancel failed:', e); }

  };

  return (
    <div className="h-full overflow-y-auto p-6">
      <div className="max-w-5xl mx-auto flex flex-col gap-6">
        <header className="flex items-center gap-3">
          <span className="w-12 h-12 rounded-2xl bg-accent-soft text-accent
                           flex items-center justify-center shrink-0">
            <FileBox size={20} />
          </span>
          <div>
            <h1 className="text-2xl font-display font-bold text-text-primary tracking-tight">
              Импорт брони из DLC RPF
            </h1>
            <p className="text-sm text-text-muted mt-0.5">
              Положи DLC RPF (типа bogowskiy.rpf), лаунчер найдёт броники внутри,
              сверит имена текстур и сгенерирует 3D превью. Это инспекция -
              файл не модифицируется.
            </p>
          </div>
        </header>

        {}
        <GlassPanel depth="z2" tint="strong" rounded="2xl" className="p-4 flex items-center gap-3">
          <input
            type="text"
            value={path}
            onChange={e => setPath(e.target.value)}
            placeholder="Путь к .rpf файлу"
            className="flex-1 h-10 px-3 rounded-xl bg-glass border border-glass-border
                       text-sm text-text-primary placeholder:text-text-muted
                       focus:outline-none focus:border-accent-soft transition-colors"
          />
          <motion.button
            type="button"
            onClick={pickFile}
            disabled={busy}
            whileHover={!busy ? { y: -1 } : undefined}
            whileTap={!busy ? { scale: 0.97 } : undefined}
            transition={{ duration: 0.15, ease: EASE_DEPTH }}
            className="shrink-0 inline-flex items-center gap-2 h-10 px-3.5 rounded-xl
                       bg-glass hover:bg-glass-strong text-text-primary
                       border border-glass-border transition-colors text-sm font-semibold btn-no-press"
            style={{ outline: 'none' }}
          >
            <FolderOpen size={14} />
            <span>Выбрать файл</span>
          </motion.button>
          {}
          {!busy ? (
            <motion.button
              type="button"
              onClick={runInspect}
              disabled={!path}
              whileHover={path ? { y: -1 } : undefined}
              whileTap={path ? { scale: 0.97 } : undefined}
              transition={{ duration: 0.15, ease: EASE_DEPTH }}
              className="shrink-0 inline-flex items-center gap-2 h-10 px-4 rounded-xl
                         bg-accent-soft text-text-primary
                         border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                         hover:bg-[color-mix(in_srgb,var(--accent)_20%,transparent)] hover:border-accent
                         disabled:opacity-50 disabled:cursor-not-allowed
                         transition-colors text-sm font-bold uppercase tracking-wider btn-no-press"
              style={{ outline: 'none' }}
            >
              <RefreshCw size={14} />
              <span>Инспект</span>
            </motion.button>
          ) : (
            <motion.button
              type="button"
              onClick={cancelInspect}
              disabled={cancelling}
              title="Прервать инспекцию - вернётся то, что успело собраться"
              whileHover={!cancelling ? { y: -1 } : undefined}
              whileTap={!cancelling ? { scale: 0.97 } : undefined}
              transition={{ duration: 0.15, ease: EASE_DEPTH }}
              className="shrink-0 inline-flex items-center gap-2 h-10 px-4 rounded-xl
                         bg-red-500/15 text-red-200
                         border border-red-500/40
                         hover:bg-red-500/25 hover:border-red-400
                         disabled:opacity-50 disabled:cursor-wait
                         transition-colors text-sm font-bold uppercase tracking-wider btn-no-press"
              style={{ outline: 'none' }}
            >
              {cancelling
                ? <Loader2 size={14} className="animate-spin" />
                : <XCircle size={14} />}
              <span>{cancelling ? 'Отменяю...' : 'Отменить'}</span>
            </motion.button>
          )}
        </GlassPanel>

        {}
        {busy && (
          <div className="px-1 py-2 inline-flex items-center gap-2.5
                          text-[12px] text-text-muted">
            <Loader2 size={13} className="animate-spin text-accent" />
            <span>
              {cancelling
                ? 'Останавливаю сканер, дожидаемся завершения текущего drawable...'
                : 'Сканирую DLC и собираю превью брони. На больших RPF - до пары минут.'}
            </span>
          </div>
        )}

        {}
        {error && (
          <GlassPanel depth="z1" rounded="2xl"
                      className="p-4 flex items-start gap-3 border border-red-500/30 bg-red-500/10">
            <AlertTriangle size={18} className="text-red-300 shrink-0 mt-0.5" />
            <div className="flex-1 min-w-0">
              <div className="text-sm font-semibold text-red-200">Ошибка инспекции</div>
              <div className="text-sm text-red-200/80 mt-0.5 break-words">{error}</div>
            </div>
          </GlassPanel>
        )}

        {}
        {report?.warnings && report.warnings.length > 0 && (
          <GlassPanel depth="z1" rounded="2xl"
                      className="p-4 flex flex-col gap-2 border border-amber-500/30 bg-amber-500/5">
            <div className="text-xs uppercase tracking-wider font-bold text-amber-300">
              Предупреждения
            </div>
            {report.warnings.map((w, i) => (
              <div key={i} className="text-sm text-amber-200/90 inline-flex items-start gap-2">
                <span className="text-amber-300 mt-0.5">•</span>
                <span className="flex-1">{w}</span>
              </div>
            ))}
          </GlassPanel>
        )}

        {}
        {report && report.candidates.length === 0 && !error && (
          <GlassPanel depth="z2" tint="strong" rounded="2xl" className="p-8 text-center">
            <FileBox size={36} strokeWidth={1.5} className="mx-auto text-text-muted opacity-50" />
            <div className="text-sm text-text-muted mt-3">
              В RPF не найдено ни одного armor-drawable.
            </div>
          </GlassPanel>
        )}

        {report && report.candidates.length > 0 && (
          <div className="flex flex-col gap-3">
            <div className="text-xs uppercase tracking-[0.18em] font-bold text-text-muted px-1">
              Найдено кандидатов: {report.candidates.length}
            </div>
            {report.candidates.map((c, i) => (
              <CandidateCard
                key={c.yddInternalPath + ':' + i}
                candidate={c}
                index={i}
                dlcRpfPath={report.dlcRpfPath}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function CandidateCard({
  candidate: c, index, dlcRpfPath,
}: {
  candidate: DlcArmorCandidate;
  index: number;
  dlcRpfPath: string;
}) {
  const [open, setOpen] = useState(index === 0);
  const ok = !c.parseError && !c.hasNameMismatch;

  const [importName, setImportName] = useState<string>(

    c.drawableInternalName || c.yddName.replace(/\.ydd$/i, '') || 'Armor');
  const [importAuthor, setImportAuthor] = useState<string>('');
  const [applyAutoFix, setApplyAutoFix] = useState<boolean>(c.suggestedRename !== null);
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<DlcArmorImportResult | null>(null);

  const [extraOpen, setExtraOpen] = useState(false);
  const [extraPath, setExtraPath] = useState('');
  const [extraBusy, setExtraBusy] = useState(false);
  const [extraReport, setExtraReport] = useState<DlcArmorInspectionResult | null>(null);
  const [extraYdd, setExtraYdd] = useState<string>('');
  const [extraError, setExtraError] = useState<string | null>(null);

  const inspectExtra = async () => {
    if (!extraPath.trim()) return;
    setExtraBusy(true); setExtraError(null); setExtraReport(null); setExtraYdd('');
    try {
      const r = await bridge.inspectDlcRpfArmor(extraPath.trim());
      setExtraReport(r);
      if (r.candidates.length > 0) setExtraYdd(r.candidates[0].yddInternalPath);
      if (r.errorMessage) setExtraError(r.errorMessage);
    } catch (e) {
      setExtraError(e instanceof Error ? e.message : String(e));
    } finally {
      setExtraBusy(false);
    }
  };

  const runImport = async () => {
    if (!importName.trim()) return;
    setImporting(true);
    setImportResult(null);
    try {
      const r = await bridge.importDlcRpfArmor({
        dlcRpfPath,
        yddInternalPath: c.yddInternalPath,
        name:            importName.trim(),
        author:          importAuthor.trim() || null,
        applyAutoFix:    applyAutoFix && c.suggestedRename !== null,
        renameYtdFileName:    applyAutoFix && c.suggestedRename
          ? leafFileName(c.suggestedRename.ytdInternalPath) : null,
        renameOldTextureName: applyAutoFix && c.suggestedRename
          ? c.suggestedRename.oldTextureName : null,
        renameNewTextureName: applyAutoFix && c.suggestedRename
          ? c.suggestedRename.newTextureName : null,
        extraSources: extraYdd && extraPath.trim()
          ? [{ dlcRpfPath: extraPath.trim(), yddInternalPath: extraYdd }]
          : null,
      });
      setImportResult(r);
    } catch (e) {
      setImportResult({
        success: false, armorId: null, armorRpfUrl: null, glbUrl: null,
        errorMessage: e instanceof Error ? e.message : String(e),
      });
    } finally {
      setImporting(false);
    }
  };

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.32, ease: EASE_DEPTH, delay: index * 0.05 }}
    >
      <GlassPanel depth="z2" tint="strong" rounded="2xl" className="overflow-hidden">
        {}
        <button
          type="button"
          onClick={() => setOpen(o => !o)}
          className="w-full p-4 flex items-center gap-3 text-left hover:bg-glass-strong transition-colors"
          style={{ outline: 'none' }}
        >
          <span className={
            'w-9 h-9 rounded-xl flex items-center justify-center shrink-0 ' +
            (c.parseError      ? 'bg-red-500/15 text-red-300'
             : c.hasNameMismatch ? 'bg-amber-500/15 text-amber-300'
             :                     'bg-emerald-500/15 text-emerald-300')
          }>
            {c.parseError      ? <XCircle size={16} />
             : c.hasNameMismatch ? <AlertTriangle size={16} />
             :                     <CheckCircle2 size={16} />}
          </span>
          <div className="flex-1 min-w-0">
            <div className="text-sm font-bold text-text-primary truncate">{c.yddName}</div>
            <div className="text-[11px] text-text-muted truncate font-mono">
              {c.yddInternalPath}
            </div>
          </div>
          {ok && <span className="text-[11px] uppercase tracking-wider text-emerald-300 font-bold shrink-0">ОК</span>}
          {c.hasNameMismatch && (
            <span className="text-[11px] uppercase tracking-wider text-amber-300 font-bold shrink-0">
              MISMATCH
            </span>
          )}
          {c.parseError && (
            <span className="text-[11px] uppercase tracking-wider text-red-300 font-bold shrink-0">
              FAIL
            </span>
          )}
          {open ? <ChevronDown size={16} className="text-text-muted shrink-0" />
                : <ChevronRight size={16} className="text-text-muted shrink-0" />}
        </button>

        {}
        {open && (
          <div className="px-4 pb-4 flex flex-col gap-4">
            {c.parseError && (
              <div className="text-sm text-red-200 bg-red-500/10 border border-red-500/30
                              rounded-xl px-3 py-2">
                {c.parseError}
              </div>
            )}

            {}
            {c.drawableInternalName && (
              <div className="text-[11px] text-text-muted">
                Drawable internal name:{' '}
                <code className="text-text-secondary font-mono">{c.drawableInternalName}</code>
              </div>
            )}

            {}
            {c.previewGlbUrl
              ? <CandidateGlbPreview fileUrl={c.previewGlbUrl} />
              : <div className="text-[11px] text-text-muted">
                  3D превью не сгенерировано (RageLib не справился - обычно
                  нестандартный шейдер).
                </div>}

            {}
            <div className="flex flex-col gap-1.5">
              <div className="text-[11px] uppercase tracking-[0.16em] font-bold text-text-muted">
                Что ожидает шейдер
              </div>
              {c.samplerExpectations.length === 0 ? (
                <div className="text-[12px] text-text-muted">- шейдер не использует именованные текстуры</div>
              ) : (
                <div className="grid grid-cols-[160px,1fr] gap-y-1 gap-x-3 text-[12px] font-mono">
                  {c.samplerExpectations.map((s, i) => {
                    const isMissing = c.missingExpectedDiffuses.some(
                      m => m.toLowerCase() === s.expectedTextureName.toLowerCase());
                    return (
                      <>
                        <span key={'k' + i} className="text-text-muted truncate">{s.samplerName}</span>
                        <span key={'v' + i} className={
                          'truncate ' + (isMissing ? 'text-amber-300' : 'text-text-secondary')
                        }>
                          {s.expectedTextureName}
                          {isMissing && <span className="ml-2 text-[10px] uppercase tracking-wider text-amber-400">missing</span>}
                        </span>
                      </>
                    );
                  })}
                </div>
              )}
            </div>

            {}
            <div className="flex flex-col gap-1.5">
              <div className="text-[11px] uppercase tracking-[0.16em] font-bold text-text-muted">
                YTD'ы в той же папке ({c.candidateYtds.length})
              </div>
              {c.candidateYtds.length === 0 ? (
                <div className="text-[12px] text-amber-300">
                  ⚠ Нет YTD рядом с YDD - текстуры взять неоткуда.
                </div>
              ) : (
                c.candidateYtds.map((y, i) => (
                  <div key={i} className="text-[12px] font-mono pl-3 border-l-2 border-glass-border">
                    <div className="text-text-secondary">{y.fileName}</div>
                    {y.parseError ? (
                      <div className="text-red-300 text-[11px] mt-0.5">{y.parseError}</div>
                    ) : (
                      <div className="text-[11px] text-text-muted mt-0.5">
                        внутри: {y.innerTextureNames.length === 0
                          ? <span className="text-red-300">пусто</span>
                          : y.innerTextureNames.join(', ')}
                      </div>
                    )}
                  </div>
                ))
              )}
            </div>

            {c.suggestedRename && (
              <div className="rounded-xl bg-amber-500/10 border border-amber-500/30 p-3">
                <div className="text-[11px] uppercase tracking-wider font-bold text-amber-300 mb-1.5">
                  Предлагаемая правка (auto-fix)
                </div>
                <div className="text-[12px] font-mono text-amber-100 break-all">
                  В <span className="text-amber-300">{leafFileName(c.suggestedRename.ytdInternalPath)}</span>:{'  '}
                  <span className="text-text-muted">{c.suggestedRename.oldTextureName}</span>
                  {' → '}
                  <span className="text-emerald-300">{c.suggestedRename.newTextureName}</span>
                </div>
                <div className="text-[10px] text-amber-200/70 mt-2">
                  Поставь галку «Auto-fix» ниже - лаунчер переименует текстуру внутри YTD
                  при импорте, чтобы шейдер нашёл свою diff и модель прокрасилась.
                </div>
              </div>
            )}

            <div className="rounded-xl bg-glass-strong border border-glass-border p-3 flex flex-col gap-3">
              <div className="text-[11px] uppercase tracking-[0.16em] font-bold text-text-muted">
                Импорт в каталог
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                <label className="flex flex-col gap-1">
                  <span className="text-[10.5px] uppercase tracking-wider text-text-muted">Название</span>
                  <input
                    type="text"
                    value={importName}
                    onChange={e => setImportName(e.target.value)}
                    placeholder="Allegri V3 / Bogowskiy / ..."
                    className="h-9 px-2.5 rounded-lg bg-glass border border-glass-border
                               text-sm text-text-primary placeholder:text-text-muted
                               focus:outline-none focus:border-accent-soft transition-colors"
                  />
                </label>
                <label className="flex flex-col gap-1">
                  <span className="text-[10.5px] uppercase tracking-wider text-text-muted">Автор</span>
                  <input
                    type="text"
                    value={importAuthor}
                    onChange={e => setImportAuthor(e.target.value)}
                    placeholder="(опционально)"
                    className="h-9 px-2.5 rounded-lg bg-glass border border-glass-border
                               text-sm text-text-primary placeholder:text-text-muted
                               focus:outline-none focus:border-accent-soft transition-colors"
                  />
                </label>
              </div>

              {c.suggestedRename && (
                <label className="inline-flex items-center gap-2 text-[12px] text-text-secondary cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={applyAutoFix}
                    onChange={e => setApplyAutoFix(e.target.checked)}
                    className="w-4 h-4 accent-accent"
                  />
                  <span>
                    Применить auto-fix -{' '}
                    <span className="text-text-muted">
                      переименовать{' '}
                      <code className="text-amber-300 font-mono">{c.suggestedRename.oldTextureName}</code>
                      {' → '}
                      <code className="text-emerald-300 font-mono">{c.suggestedRename.newTextureName}</code>
                    </span>
                  </span>
                </label>
              )}

              <div className="rounded-xl border border-white/[0.08] bg-white/[0.02] p-3 flex flex-col gap-2">
                <button
                  type="button"
                  onClick={() => setExtraOpen(o => !o)}
                  className="flex items-center gap-2 text-[12px] font-bold uppercase tracking-wider text-text-secondary"
                  style={{ outline: 'none' }}
                >
                  {extraOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                  <span>＋ версия для другого сервера {extraYdd ? '(добавлена)' : '(необязательно)'}</span>
                </button>
                {extraOpen && (
                  <div className="flex flex-col gap-2">
                    <div className="text-[11px] text-text-muted leading-snug">
                      Напр. этот броник = task/january2016 для Majestic, а вторая версия = jbib/bikerdlc для 5RP.
                      Обе сольются в один armor.rpf - движок каждого сервера возьмёт свою.
                    </div>
                    <div className="flex items-center gap-2">
                      <input
                        type="text"
                        value={extraPath}
                        onChange={e => setExtraPath(e.target.value)}
                        placeholder="путь к dlc.rpf второго сервера"
                        className="flex-1 h-9 px-3 rounded-lg bg-bg-elevated/60 border border-white/[0.1]
                                   text-[12px] text-text-primary font-mono"
                        style={{ outline: 'none' }}
                      />
                      <button
                        type="button"
                        onClick={inspectExtra}
                        disabled={extraBusy || !extraPath.trim()}
                        className="h-9 px-3 rounded-lg bg-bg-elevated/55 border border-white/[0.12]
                                   text-[11px] font-bold uppercase tracking-wider text-text-secondary
                                   hover:bg-bg-elevated/75 disabled:opacity-50"
                        style={{ outline: 'none' }}
                      >
                        {extraBusy ? <Loader2 size={13} className="animate-spin" /> : 'Проверить'}
                      </button>
                    </div>
                    {extraError && <div className="text-[11px] text-red-300">{extraError}</div>}
                    {extraReport && extraReport.candidates.length > 0 && (
                      <select
                        value={extraYdd}
                        onChange={e => setExtraYdd(e.target.value)}
                        className="h-9 px-2 rounded-lg bg-bg-elevated/60 border border-white/[0.1] text-[12px] text-text-primary"
                        style={{ outline: 'none' }}
                      >
                        {extraReport.candidates.map((ec, i) => (
                          <option key={ec.yddInternalPath + ':' + i} value={ec.yddInternalPath}>
                            {ec.yddName} - {ec.yddInternalPath}
                          </option>
                        ))}
                      </select>
                    )}
                    {extraReport && extraReport.candidates.length === 0 && !extraError && (
                      <div className="text-[11px] text-amber-300">В этом DLC не найдено броне-дроублов.</div>
                    )}
                    {extraYdd && (
                      <div className="text-[11px] text-emerald-300">
                        ✓ вторая версия будет влита: <code className="font-mono">{leafFileName(extraYdd)}</code>
                      </div>
                    )}
                  </div>
                )}
              </div>

              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={runImport}
                  disabled={importing || !importName.trim() || (importResult?.success ?? false)}
                  className="inline-flex items-center gap-2 h-10 px-4 rounded-xl
                             bg-accent-soft text-text-primary
                             border border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                             hover:bg-[color-mix(in_srgb,var(--accent)_20%,transparent)] hover:border-accent
                             disabled:opacity-50 disabled:cursor-not-allowed
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {importing ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
                  <span>
                    {importing ? 'Импортирую...'
                      : importResult?.success ? 'Импортировано'
                      : 'Импортировать в каталог'}
                  </span>
                </button>

                {importResult?.success && (
                  <span className="inline-flex items-center gap-1.5 text-[12px] text-emerald-300">
                    <CheckCircle2 size={14} />
                    <span>id: <code className="font-mono">{importResult.armorId}</code></span>
                  </span>
                )}
                {importResult && !importResult.success && (
                  <span className="inline-flex items-center gap-1.5 text-[12px] text-red-300">
                    <XCircle size={14} />
                    <span>{importResult.errorMessage ?? 'неизвестная ошибка'}</span>
                  </span>
                )}
              </div>

              {importResult?.success && importResult.errorMessage && (
                <div className="text-[11px] text-amber-300/90 mt-1">
                  {importResult.errorMessage}
                </div>
              )}
            </div>
          </div>
        )}
      </GlassPanel>
    </motion.div>
  );
}

function leafFileName(path: string | null | undefined): string {
  if (!path) return '';
  const slash = Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\'));
  return slash < 0 ? path : path.substring(slash + 1);
}

function CandidateGlbPreview({ fileUrl }: { fileUrl: string }) {
  const [blobUrl, setBlobUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    let createdUrl: string | null = null;
    setError(null);
    setBlobUrl(null);

    const path = decodeFileUrl(fileUrl);
    if (!path) {
      setError('Не удалось распарсить URL.');
      return;
    }

    bridge.readLocalFileBase64(path).then(b64 => {
      if (!alive) return;
      if (!b64) {
        setError('Не удалось прочитать GLB. Возможно, файл удалён или превышает 32 MB.');
        return;
      }
      try {

        const bin = atob(b64);
        const arr = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
        const blob = new Blob([arr], { type: 'model/gltf-binary' });
        createdUrl = URL.createObjectURL(blob);
        setBlobUrl(createdUrl);
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
      }
    }).catch(e => {
      if (alive) setError(e instanceof Error ? e.message : String(e));
    });

    return () => {
      alive = false;
      if (createdUrl) URL.revokeObjectURL(createdUrl);
    };
  }, [fileUrl]);

  return (
    <div className="rounded-xl overflow-hidden bg-glass border border-glass-border h-[260px] relative">
      {error
        ? <div className="absolute inset-0 flex items-center justify-center text-[11px] text-amber-300 px-3 text-center">
            {error}
          </div>
        : !blobUrl
          ? <div className="absolute inset-0 flex items-center justify-center gap-2 text-text-muted text-[11px]">
              <Loader2 size={14} className="animate-spin" />
              <span>Загружаю GLB...</span>
            </div>
          : <ArmorPreview3D glbUrl={blobUrl} />}
    </div>
  );
}

function decodeFileUrl(fileUrl: string): string | null {
  if (!fileUrl) return null;
  const prefix = 'file:///';
  if (!fileUrl.startsWith(prefix)) return null;
  try {
    return decodeURIComponent(fileUrl.substring(prefix.length)).replace(/\//g, '\\');
  } catch {
    return null;
  }
}
