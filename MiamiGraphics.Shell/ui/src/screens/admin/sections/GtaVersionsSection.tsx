import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Edit2, Trash2, X, Save, Loader2, FileSearch, Upload } from 'lucide-react';
import { bridge } from '@/bridge';
import type { GtaVersion } from '@/bridge/types';
import { Toast } from '@/components/Toast';

const EMPTY: GtaVersion = {
  exeVersion: '',
  updateRpfSize: 0,
  updateRpfSha256: '',
  cleanUpdateUrl: '',
  notes: '',
  createdAt: '',
  updatedAt: '',
};

interface AutoFillSession {
  cleanRpfPath: string;
  exeVersion: string;
  updateRpfSize: number;
  updateRpfSha256: string;
  notes: string;
}

export function GtaVersionsSection() {
  const { t } = useTranslation();
  const [items, setItems] = useState<GtaVersion[]>([]);
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<GtaVersion | null>(null);
  const [autoFill, setAutoFill] = useState<AutoFillSession | null>(null);
  const [autoFillBusy, setAutoFillBusy] = useState<'reading' | 'uploading' | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);

  const onPickAndAutoFill = async () => {
    const path = await bridge.openFileDialog('GTA update.rpf', '*.rpf');
    if (!path) return;
    setAutoFillBusy('reading');
    try {
      const r = await bridge.gtaVersionsAutoFill(path);
      setAutoFill({
        cleanRpfPath: path,
        exeVersion: r.exeVersion || '',
        updateRpfSize: r.updateRpfSize,
        updateRpfSha256: r.updateRpfSha256,
        notes: '',
      });
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setAutoFillBusy(null);
    }
  };

  const onAutoFillUpload = async () => {
    if (!autoFill || autoFillBusy) return;
    setAutoFillBusy('uploading');
    try {
      const saved = await bridge.gtaVersionsUpload(autoFill.cleanRpfPath, autoFill.exeVersion, autoFill.notes);
      setAutoFill(null);
      setToast({ tone: 'success', message: t('admin.gtaVersions.uploaded', { version: saved.exeVersion }) });
      void load();
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setAutoFillBusy(null);
    }
  };

  const load = async () => {
    setLoading(true);
    try { setItems(await bridge.gtaVersionsList()); }
    catch (e) { setToast({ tone: 'error', message: (e as Error).message }); }
    finally { setLoading(false); }
  };

  useEffect(() => { void load(); }, []);

  const onSave = async (v: GtaVersion) => {
    try {
      await bridge.gtaVersionsUpsert(v);
      setEditing(null);
      setToast({ tone: 'success', message: t('admin.gtaVersions.saved') });
      void load();
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    }
  };

  const onDelete = async (exeVersion: string) => {
    if (!confirm(t('admin.gtaVersions.deleteConfirm', { version: exeVersion }))) return;
    try {
      await bridge.gtaVersionsDelete(exeVersion);
      setToast({ tone: 'success', message: t('admin.gtaVersions.deleted', { version: exeVersion }) });
      void load();
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    }
  };

  return (
    <div className="h-full flex flex-col">
      <header className="h-14 px-6 flex items-center gap-3 border-b border-border-subtle shrink-0">
        <h1 className="text-base font-bold text-text-primary">{t('admin.gtaVersions.title')}</h1>
        <span className="text-xs text-text-muted">{t('admin.gtaVersions.count', { count: items.length })}</span>
        <div className="flex-1" />
        <button
          type="button"
          onClick={onPickAndAutoFill}
          disabled={autoFillBusy !== null}
          className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover disabled:opacity-50"
        >
          {autoFillBusy === 'reading' ? <Loader2 size={14} className="animate-spin" /> : <FileSearch size={14} />}
          {autoFillBusy === 'reading' ? t('admin.gtaVersions.autoFillReading') : t('admin.gtaVersions.autoFillButton')}
        </button>
        <button
          type="button"
          onClick={() => setEditing(EMPTY)}
          className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated"
        >
          <Plus size={14} /> {t('admin.gtaVersions.addManually')}
        </button>
      </header>

      <div className="flex-1 overflow-auto px-6 py-4">
        {loading ? (
          <div className="text-center py-12 text-text-muted">
            <Loader2 size={20} className="animate-spin mx-auto" />
          </div>
        ) : items.length === 0 ? (
          <div className="text-center text-text-muted py-12">{t('admin.gtaVersions.empty')}</div>
        ) : (
          <table className="w-full border-separate border-spacing-y-1 text-sm">
            <thead>
              <tr className="text-text-muted text-[10px] uppercase tracking-wider">
                <th className="text-left px-3 py-2 font-medium">{t('admin.gtaVersions.colVersion')}</th>
                <th className="text-right px-3 py-2 font-medium">{t('admin.gtaVersions.colSize')}</th>
                <th className="text-left px-3 py-2 font-medium">{t('admin.gtaVersions.colSha')}</th>
                <th className="text-left px-3 py-2 font-medium">{t('admin.gtaVersions.colUrl')}</th>
                <th className="text-right px-3 py-2 font-medium w-32">{t('admin.gtaVersions.colActions')}</th>
              </tr>
            </thead>
            <tbody>
              {items.map(v => (
                <tr key={v.exeVersion} className="bg-bg-surface border border-border-subtle">
                  <td className="px-3 py-2 rounded-l-lg font-mono">{v.exeVersion}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-text-secondary">
                    {(v.updateRpfSize / (1024 * 1024)).toFixed(0)} MB
                  </td>
                  <td className="px-3 py-2 font-mono text-xs text-text-muted truncate max-w-[200px]" title={v.updateRpfSha256}>
                    {v.updateRpfSha256.slice(0, 16)}…
                  </td>
                  <td className="px-3 py-2 text-text-muted truncate max-w-[280px]" title={v.cleanUpdateUrl}>
                    {v.cleanUpdateUrl || <span className="text-status-warning">- not set -</span>}
                  </td>
                  <td className="px-3 py-2 rounded-r-lg">
                    <div className="flex items-center justify-end gap-1">
                      <IconBtn icon={<Edit2 size={14} />} onClick={() => setEditing(v)} title={t('admin.gtaVersions.edit')} />
                      <IconBtn icon={<Trash2 size={14} />} onClick={() => void onDelete(v.exeVersion)} title={t('admin.gtaVersions.delete')} danger />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {editing && <EditModal initial={editing} onSave={onSave} onClose={() => setEditing(null)} />}

      {autoFill && (
        <AutoFillModal
          session={autoFill}
          busy={autoFillBusy === 'uploading'}
          onChange={setAutoFill}
          onUpload={onAutoFillUpload}
          onClose={() => setAutoFill(null)}
        />
      )}

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

function IconBtn({ icon, onClick, title, danger }: { icon: React.ReactNode; onClick: () => void; title: string; danger?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={title}
      aria-label={title}
      className={
        'w-7 h-7 rounded-md flex items-center justify-center transition-colors ' +
        (danger ? 'text-text-muted hover:text-status-error hover:bg-bg-elevated' : 'text-text-muted hover:text-text-primary hover:bg-bg-elevated')
      }
    >
      {icon}
    </button>
  );
}

function EditModal({ initial, onSave, onClose }: { initial: GtaVersion; onSave: (v: GtaVersion) => void | Promise<void>; onClose: () => void }) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<GtaVersion>(initial);

  const valid = draft.exeVersion.trim().length > 0 && draft.cleanUpdateUrl.trim().length > 0;

  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6" onClick={onClose}>
      <div className="w-full max-w-[600px] rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between">
          <h2 className="text-base font-bold text-text-primary">
            {initial.exeVersion ? t('admin.gtaVersions.editTitle', { version: initial.exeVersion }) : t('admin.gtaVersions.addTitle')}
          </h2>
          <button type="button" onClick={onClose} className="text-text-muted hover:text-text-primary"><X size={16} /></button>
        </div>

        <FieldRow
          label={t('admin.gtaVersions.colVersion')}
          value={draft.exeVersion}
          onChange={v => setDraft({ ...draft, exeVersion: v })}
          mono
          placeholder="1.0.3788.0"
          disabled={initial.exeVersion.length > 0}
        />
        <FieldRow
          label={t('admin.gtaVersions.colSize') + ' (bytes)'}
          value={String(draft.updateRpfSize || '')}
          onChange={v => setDraft({ ...draft, updateRpfSize: parseInt(v, 10) || 0 })}
          mono
          placeholder="2010816512"
        />
        <FieldRow
          label={t('admin.gtaVersions.colSha')}
          value={draft.updateRpfSha256}
          onChange={v => setDraft({ ...draft, updateRpfSha256: v.toLowerCase() })}
          mono
          placeholder="64-char lowercase hex"
        />
        <FieldRow
          label={t('admin.gtaVersions.colUrl')}
          value={draft.cleanUpdateUrl}
          onChange={v => setDraft({ ...draft, cleanUpdateUrl: v })}
          mono
          placeholder="https://<r2-public>/clean_update_<version>.rpf"
        />
        <FieldRow
          label="Notes"
          value={draft.notes}
          onChange={v => setDraft({ ...draft, notes: v })}
          textarea
        />

        <div className="flex justify-end gap-2 pt-2">
          <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated">
            {t('admin.gtaVersions.cancel')}
          </button>
          <button
            type="button"
            disabled={!valid}
            onClick={() => void onSave(draft)}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <Save size={14} /> {t('admin.gtaVersions.save')}
          </button>
        </div>
      </div>
    </div>
  );
}

function AutoFillModal({ session, busy, onChange, onUpload, onClose }: {
  session: AutoFillSession;
  busy: boolean;
  onChange: (s: AutoFillSession) => void;
  onUpload: () => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const sizeMb = (session.updateRpfSize / (1024 * 1024)).toFixed(0);
  const canUpload = session.exeVersion.trim().length > 0;

  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6" onClick={busy ? undefined : onClose}>
      <div className="w-full max-w-[640px] rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between">
          <h2 className="text-base font-bold text-text-primary">{t('admin.gtaVersions.autoFillTitle')}</h2>
          {!busy && <button type="button" onClick={onClose} className="text-text-muted hover:text-text-primary"><X size={16} /></button>}
        </div>

        <p className="text-xs text-text-muted">{t('admin.gtaVersions.autoFillSubtitle')}</p>

        <FieldRow
          label={t('admin.gtaVersions.colVersion')}
          value={session.exeVersion}
          onChange={v => onChange({ ...session, exeVersion: v })}
          mono
          placeholder="1.0.3788.0"
        />

        <div className="grid grid-cols-2 gap-3">
          <ReadOnlyField label={t('admin.gtaVersions.colSize')} value={`${sizeMb} MB`} />
          <ReadOnlyField label={t('admin.gtaVersions.colSha')} value={session.updateRpfSha256.slice(0, 16) + '…'} title={session.updateRpfSha256} />
        </div>

        <FieldRow
          label="Notes"
          value={session.notes}
          onChange={v => onChange({ ...session, notes: v })}
          textarea
        />

        <div className="flex items-start gap-2 p-3 rounded-lg bg-status-warning-soft border border-status-warning-border text-xs text-text-secondary">
          {t('admin.gtaVersions.autoFillUploadHint', { size: sizeMb })}
        </div>

        <div className="flex justify-end gap-2 pt-1">
          <button type="button" disabled={busy} onClick={onClose} className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated disabled:opacity-50">
            {t('admin.gtaVersions.cancel')}
          </button>
          <button
            type="button"
            disabled={!canUpload || busy}
            onClick={onUpload}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {busy ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
            {busy ? t('admin.gtaVersions.uploading') : t('admin.gtaVersions.uploadToR2')}
          </button>
        </div>
      </div>
    </div>
  );
}

function ReadOnlyField({ label, value, title }: { label: string; value: string; title?: string }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
      <div className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-secondary font-mono truncate" title={title ?? value}>
        {value}
      </div>
    </label>
  );
}

function FieldRow({ label, value, onChange, mono, placeholder, textarea, disabled }: { label: string; value: string; onChange: (v: string) => void; mono?: boolean; placeholder?: string; textarea?: boolean; disabled?: boolean }) {
  const cls = `px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent disabled:opacity-50 ${mono ? 'font-mono' : ''}`;
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
      {textarea ? (
        <textarea rows={2} value={value} onChange={e => onChange(e.target.value)} placeholder={placeholder} className={cls + ' resize-none'} disabled={disabled} />
      ) : (
        <input type="text" value={value} onChange={e => onChange(e.target.value)} placeholder={placeholder} className={cls} disabled={disabled} />
      )}
    </label>
  );
}
