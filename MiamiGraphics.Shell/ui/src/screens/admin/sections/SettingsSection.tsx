import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Eye, EyeOff, Loader2, CheckCircle2, XCircle, Wifi, FileSearch, FolderSearch } from 'lucide-react';
import { bridge } from '@/bridge';
import type { AdminConfig, TestConnectionResult } from '@/bridge/types';
import { Toast } from '@/components/Toast';
import { GlassPanel, ArrowButton } from '@/design';
import { useSessionStore } from '@/store/sessionStore';
import { useAdminDirtyStore } from '@/store/adminDirtyStore';
import { RendererHealthCard } from './RendererHealthCard';

const R2_FIELDS: { key: keyof AdminConfig; labelKey: string; secret?: boolean }[] = [
  { key: 'r2Endpoint',  labelKey: 'admin.settings.endpoint' },
  { key: 'r2Bucket',    labelKey: 'admin.settings.bucket' },
  { key: 'r2PublicUrl', labelKey: 'admin.settings.publicUrl' },
  { key: 'r2AccessKey', labelKey: 'admin.settings.accessKey', secret: true },
  { key: 'r2SecretKey', labelKey: 'admin.settings.secretKey', secret: true },
];

const SUPABASE_FIELDS: { key: keyof AdminConfig; label: string; secret?: boolean; hint?: string }[] = [
  { key: 'supabaseServiceKey', label: 'Supabase Service Role Key', secret: true,
    hint: 'Нужен чтобы Delete редуксов/ганпаков из админки реально удалял из БД (RLS блокирует anon-ключ). Скопируй из Supabase Studio → Settings → API → service_role (раскрыть «Reveal»).' },
  { key: 'adminApiToken', label: 'Admin API token', secret: true,
    hint: 'Токен записи в каталог (загрузка/удаление редуксов, ганпаков, брони и т.п.). Должен совпадать с ADMIN_API_TOKEN на vps-api. Без него админ-записи в каталог дают ошибку «токен админ-доступа не задан».' },
];

export function SettingsSection() {
  const { t } = useTranslation();
  const systemInfo = useSessionStore(s => s.systemInfo);
  const [config, setConfig] = useState<AdminConfig | null>(null);
  const [revealed, setRevealed] = useState<Record<string, boolean>>({});
  const [busy, setBusy] = useState(false);
  const [test, setTest] = useState<TestConnectionResult | null>(null);
  const [toastOpen, setToastOpen] = useState(false);
  const setDirty = useAdminDirtyStore(s => s.setDirty);

  useEffect(() => {
    void bridge.adminConfigGet().then(c => { setConfig(c); setDirty(false); });
    return () => setDirty(false);
  }, [setDirty]);

  if (!config) return <div className="p-8 text-text-muted">…</div>;

  const update = (key: keyof AdminConfig, value: string) => {
    setConfig({ ...config, [key]: value });
    setDirty(true);
  };

  const handleTest = async () => {
    setBusy(true); setTest(null);
    try { setTest(await bridge.adminConfigTestR2(config)); }
    catch (e) { setTest({ success: false, message: (e as Error).message, objectCount: null }); }
    finally { setBusy(false); }
  };

  const handleSave = async () => {
    setBusy(true);
    try { await bridge.adminConfigSave(config); setDirty(false); setToastOpen(true); }
    finally { setBusy(false); }
  };

  return (
    <div className="h-full overflow-y-auto px-8 py-6">
      <div className="max-w-[1100px] mx-auto flex flex-col gap-6">
        <Card title={t('admin.settings.r2Title')}>
          {R2_FIELDS.map(f => (
            <Field
              key={f.key}
              label={t(f.labelKey)}
              value={(config[f.key] as string | null) ?? ''}
              secret={f.secret}
              revealed={revealed[f.key]}
              onToggleReveal={() => setRevealed(r => ({ ...r, [f.key]: !r[f.key] }))}
              onChange={v => update(f.key, v)}
            />
          ))}
          <div className="pt-2 flex flex-col gap-2">
            <button
              type="button"
              onClick={handleTest}
              disabled={busy}
              className="self-start inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated transition-colors disabled:opacity-50"
            >
              {busy ? <Loader2 size={14} className="animate-spin" /> : <Wifi size={14} />}
              <span>{t('admin.settings.testConnection')}</span>
            </button>
            {test && (
              <div className="flex items-start gap-2 text-sm" style={{ color: test.success ? 'var(--status-success)' : 'var(--status-error)' }}>
                {test.success ? <CheckCircle2 size={14} className="mt-0.5" /> : <XCircle size={14} className="mt-0.5" />}
                <span>
                  {test.success
                    ? t('admin.settings.connected', { count: test.objectCount ?? 0 })
                    : t('admin.settings.connectionFailed', { message: test.message })}
                </span>
              </div>
            )}
          </div>
        </Card>

        <Card title="Supabase">
          {SUPABASE_FIELDS.map(f => (
            <div key={f.key} className="flex flex-col gap-1.5">
              <Field
                label={f.label}
                value={(config[f.key] as string | null) ?? ''}
                secret={f.secret}
                revealed={revealed[f.key]}
                onToggleReveal={() => setRevealed(r => ({ ...r, [f.key]: !r[f.key] }))}
                onChange={v => update(f.key, v)}
              />
              {f.hint && <p className="text-xs text-text-muted">{f.hint}</p>}
            </div>
          ))}
        </Card>

        <Card title={t('admin.settings.pathsTitle')}>
          <Field
            label={t('admin.settings.cleanUpdatePath')}
            value={config.cleanUpdateRpfPath}
            onChange={v => update('cleanUpdateRpfPath', v)}
            pickFile={async () => {
              const p = await bridge.openFileDialog('GTA RPF', '*.rpf');
              if (p) update('cleanUpdateRpfPath', p);
            }}
          />
          <p className="text-xs text-text-muted -mt-2">{t('admin.settings.cleanUpdatePathHint')}</p>
          <Field
            label={t('admin.settings.gtaPathOverride')}
            value={config.gtaPathOverride ?? ''}
            onChange={v => update('gtaPathOverride', v)}
            placeholder={
              systemInfo?.gtaPath
                ? t('admin.settings.gtaPathAuto', { path: systemInfo.gtaPath })
                : t('admin.settings.gtaPathPlaceholder')
            }
            pickFolder={async () => {
              const p = await bridge.openFolderDialog();
              if (p) update('gtaPathOverride', p);
            }}
          />
          <Field
            label={t('admin.settings.workDirOverride')}
            value={config.workDirOverride ?? ''}
            onChange={v => update('workDirOverride', v)}
            placeholder={t('admin.settings.workDirOverridePlaceholder')}
            pickFolder={async () => {
              const p = await bridge.openFolderDialog();
              if (p) update('workDirOverride', p);
            }}
          />
          <p className="text-xs text-text-muted -mt-2">{t('admin.settings.workDirOverrideHint')}</p>
        </Card>

        <RendererHealthCard />

        <ArrowButton
          type="button"
          fullWidth
          busy={busy}
          onClick={handleSave}
          label={t('admin.settings.save')}
        />
      </div>

      <Toast open={toastOpen} tone="success" message={t('admin.settings.saved')} onClose={() => setToastOpen(false)} />
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
        <h2 className="text-sm font-bold text-text-primary mb-1 tracking-tight">{title}</h2>
        {children}
      </div>
    </GlassPanel>
  );
}

interface FieldProps {
  label: string;
  value: string;
  onChange: (v: string) => void;
  secret?: boolean;
  revealed?: boolean;
  onToggleReveal?: () => void;
  placeholder?: string;
  pickFile?: () => void | Promise<void>;
  pickFolder?: () => void | Promise<void>;
}

function Field({ label, value, onChange, secret, revealed, onToggleReveal, placeholder, pickFile, pickFolder }: FieldProps) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
      <div className="flex gap-2">
        <div className="relative flex-1">
          <input
            type={secret && !revealed ? 'password' : 'text'}
            value={value}
            onChange={e => onChange(e.target.value)}
            placeholder={placeholder}
            className="w-full px-3 py-2 pr-10 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary placeholder:text-text-muted outline-none focus:border-accent transition-colors"
          />
          {secret && (
            <button
              type="button"
              tabIndex={-1}
              onClick={onToggleReveal}
              className="absolute right-2 top-1/2 -translate-y-1/2 p-1.5 text-text-muted hover:text-text-primary"
            >
              {revealed ? <EyeOff size={14} /> : <Eye size={14} />}
            </button>
          )}
        </div>
        {pickFile && (
          <button
            type="button"
            onClick={() => void pickFile()}
            title="Browse file"
            aria-label="Browse file"
            className="px-3 rounded-lg border border-border-subtle bg-bg-elevated text-text-muted hover:text-text-primary hover:bg-bg-surface"
          >
            <FileSearch size={14} />
          </button>
        )}
        {pickFolder && (
          <button
            type="button"
            onClick={() => void pickFolder()}
            title="Browse folder"
            aria-label="Browse folder"
            className="px-3 rounded-lg border border-border-subtle bg-bg-elevated text-text-muted hover:text-text-primary hover:bg-bg-surface"
          >
            <FolderSearch size={14} />
          </button>
        )}
      </div>
    </label>
  );
}
