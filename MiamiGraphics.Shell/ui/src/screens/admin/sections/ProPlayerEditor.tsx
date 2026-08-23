import { useMemo, useState } from 'react';
import { motion } from 'framer-motion';
import {
  ArrowLeft, Save, Loader2, ImagePlus, FileJson, Info, Layers, Trophy,
  Crosshair, Mouse, Keyboard, Monitor, Headphones, Sparkles, Search,
} from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { bridge } from '@/bridge';
import type { UserBuildDto, BuildDevices } from '@/bridge/types';
import { useAdminStore } from '@/store/adminStore';
import { useSubmitDraftStore } from '@/store/submitDraftStore';
import { useNavStore } from '@/store/navStore';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { PerGunSelector } from '@/screens/players/SubmitProfileScreen';

interface Props {
  onCancel: () => void;
  onSave:   (payload: Partial<UserBuildDto>) => Promise<void>;
}

export function ProPlayerEditor({ onCancel, onSave }: Props) {
  const draft = useAdminStore(s => s.proPlayerDraft);
  const patch = useAdminStore(s => s.patchProPlayerDraft);
  const target = draft.editingTarget;
  const isNew = target === 'new' || target === null;

  const startPick = useSubmitDraftStore(s => s.startPick);
  const requestNavigate = useNavStore(s => s.requestNavigate);

  const onOpenReduxCatalog = () => {
    startPick('redux', 'admin');
    requestNavigate('redux');
  };
  const onOpenGunpackCatalog = () => {
    startPick('gunpack', 'admin');
    requestNavigate('guns');
  };

  const reduxItems   = useReduxStore(s => s.items);
  const gunpackItems = useGunpackStore(s => s.publicPacks);

  const reduxDisplayName = useMemo(() => {
    if (draft.reduxNameSnapshot) return draft.reduxNameSnapshot;
    if (!draft.reduxId) return '';
    return reduxItems.find(r => r.id === draft.reduxId)?.name ?? draft.reduxId;
  }, [draft.reduxId, draft.reduxNameSnapshot, reduxItems]);
  const gunpackDisplayName = useMemo(() => {
    if (draft.gunpackNameSnapshot) return draft.gunpackNameSnapshot;
    if (!draft.gunpackId) return '';
    return gunpackItems.find(g => g.id === draft.gunpackId)?.name ?? draft.gunpackId;
  }, [draft.gunpackId, draft.gunpackNameSnapshot, gunpackItems]);

  const [saving, setSaving] = useState(false);
  const [uploadingCover, setUploadingCover] = useState(false);
  const [uploadingSettings, setUploadingSettings] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const devices: BuildDevices = draft.devices;
  const setDevice = (key: keyof BuildDevices, name: string) => {
    const next: BuildDevices = { ...devices };
    if (name) next[key] = { name } as BuildDevices[typeof key];
    else delete next[key];
    patch({ devices: next });
  };

  const onPickCover = async () => {
    if (uploadingCover) return;
    try {
      const path = await bridge.openFileDialog('Изображения', '*.png;*.jpg;*.jpeg;*.webp');
      if (!path) return;
      setUploadingCover(true);
      const url = await bridge.userBuildUploadCover(path);
      patch({ coverUrl: url });
    } catch (e) {
      setError('Не удалось загрузить cover: ' + (e as Error).message);
    } finally {
      setUploadingCover(false);
    }
  };

  const onPickSettings = async () => {
    if (uploadingSettings) return;
    try {
      const path = await bridge.openFileDialog('GTA settings', '*.xml');
      if (!path) return;
      setUploadingSettings(true);
      const targetId = target && target !== 'new' ? target.id : 'pro-' + Math.random().toString(36).slice(2, 10);
      const url = await bridge.userBuildUploadSettingsXml(targetId, path);
      patch({ settingsXmlUrl: url });
    } catch (e) {
      setError('Не удалось загрузить settings.xml: ' + (e as Error).message);
    } finally {
      setUploadingSettings(false);
    }
  };

  const onSubmit = async () => {
    if (saving) return;
    if (!draft.name.trim() || !draft.reduxId) {
      setError('Заполните хотя бы имя и выберите редукс.');
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const payload: Partial<UserBuildDto> = {
        name: draft.name.trim(),
        authorUsername: draft.authorUsername.trim() || 'PRO',
        reduxId:   draft.reduxId,
        gunpackId: draft.gunpackId || '',
        reduxNameSnapshot:   reduxDisplayName,
        gunpackNameSnapshot: gunpackDisplayName,
        gunSlotsJson:    JSON.stringify(draft.gunSlots),
        devicesJson:     Object.keys(devices).length > 0 ? JSON.stringify(devices) : null,
        sensitivity:     parseFiniteOrNull(draft.sensitivity),
        dpi:             parseFiniteOrNull(draft.dpi),
        resolution:      draft.resolution.trim() || null,
        videoUrl:        draft.videoUrl.trim() || null,
        settingsXmlUrl:  draft.settingsXmlUrl.trim() || null,
        coverUrl:        draft.coverUrl.trim() || null,
        description: '',

        tier:           draft.tier,
        family:         draft.family.trim() || null,
        categoryLabel:  draft.categoryLabel.trim() || null,
        fpsAvg:         parseFiniteOrNull(draft.fpsAvg),
        monitorHz:      parseFiniteOrNull(draft.monitorHz),
        adminNotes:     draft.adminNotes.trim() || null,
        status:         'approved',
      };
      await onSave(payload);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  return (
    <motion.div
      className="h-full overflow-y-auto"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.32, ease: EASE_DEPTH }}
    >
      <div className="max-w-[1280px] mx-auto px-8 py-6 flex flex-col gap-5">
        <header className="flex items-center justify-between gap-4">
          <button
            type="button"
            onClick={onCancel}
            className="inline-flex items-center gap-2 px-3 h-9 rounded-xl
                       bg-glass border border-glass-border text-sm text-text-secondary
                       hover:text-text-primary hover:bg-glass-strong transition-colors"
            style={{ outline: 'none' }}
          >
            <ArrowLeft size={14} /> <span>Назад</span>
          </button>
          <h1 className="font-display font-bold text-xl text-text-primary uppercase tracking-tight">
            {isNew ? 'Новый PRO-игрок' : `Редактирование: ${target.name}`}
          </h1>
          <div className="flex-1" />
          <button
            type="button"
            onClick={() => void onSubmit()}
            disabled={saving}
            className="inline-flex items-center gap-2 px-4 h-10 rounded-xl
                       bg-accent text-text-on-accent font-semibold text-sm
                       hover:bg-accent-hover shadow-glow-accent
                       disabled:opacity-60 transition-colors"
            style={{ outline: 'none' }}
          >
            {saving ? <Loader2 size={14} className="animate-spin" /> : <Save size={14} />}
            <span>{saving ? 'Сохраняем…' : 'Сохранить'}</span>
          </button>
        </header>

        {error && (
          <div className="px-4 py-3 rounded-xl border border-red-500/30 bg-red-500/10
                          text-sm text-red-300">
            {error}
          </div>
        )}

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
          {}
          <Section title="Идентификация">
            <Field label="Ник">
              <Input value={draft.name} onChange={v => patch({ name: v })} placeholder="OTELLO" />
            </Field>
            <Field label="Семья / клан">
              <Input value={draft.family} onChange={v => patch({ family: v })} placeholder="TIER 1 PLAYERS" />
            </Field>
            <Field label="Категория (надпись над ником)">
              <Input value={draft.categoryLabel} onChange={v => patch({ categoryLabel: v })} placeholder="ЭЛИТНЫЙ ИГРОК" />
            </Field>
            <Field label="Tier">
              <select
                value={draft.tier}
                onChange={(e) => patch({ tier: Number(e.target.value) })}
                className="w-full h-10 px-3 rounded-xl bg-glass border border-glass-border
                           text-sm text-text-primary outline-none focus:border-accent/40"
              >
                <option value={1}>Tier 1 - топ</option>
                <option value={2}>Tier 2</option>
                <option value={3}>Tier 3</option>
              </select>
            </Field>
            <Field label="Автор записи">
              <Input value={draft.authorUsername} onChange={v => patch({ authorUsername: v })} placeholder="admin" />
            </Field>
          </Section>

          {}
          <Section title="Чувствительность & Дисплей">
            <div className="grid grid-cols-2 gap-3">
              <Field label="DPI">
                <Input value={draft.dpi} onChange={v => patch({ dpi: v })} placeholder="450" />
              </Field>
              <Field label="Sens">
                <Input value={draft.sensitivity} onChange={v => patch({ sensitivity: v })} placeholder="0.85" />
              </Field>
              <Field label="Монитор Hz">
                <Input value={draft.monitorHz} onChange={v => patch({ monitorHz: v })} placeholder="240" />
              </Field>
              <Field label="Игровой FPS">
                <Input value={draft.fpsAvg} onChange={v => patch({ fpsAvg: v })} placeholder="240" />
              </Field>
            </div>
            <Field label="Разрешение">
              <Input value={draft.resolution} onChange={v => patch({ resolution: v })} placeholder="1920x1080" />
            </Field>
            <Field label="Ссылка на видео (YouTube)">
              <Input value={draft.videoUrl} onChange={v => patch({ videoUrl: v })} placeholder="https://youtu.be/..." />
            </Field>
          </Section>

          {}
          <Section title="Обложка & Файлы">
            <Field label="Обложка (фото)">
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => void onPickCover()}
                  disabled={uploadingCover}
                  className="inline-flex items-center gap-2 px-3 h-10 rounded-xl
                             bg-accent-soft border border-accent/30 text-accent text-sm font-semibold
                             hover:bg-accent/20 disabled:opacity-60 transition-colors"
                  style={{ outline: 'none' }}
                >
                  {uploadingCover ? <Loader2 size={14} className="animate-spin" /> : <ImagePlus size={14} />}
                  <span>{uploadingCover ? 'Загружаем…' : 'Выбрать фото'}</span>
                </button>
                {draft.coverUrl && (
                  <span className="text-xs text-text-muted truncate max-w-[180px]" title={draft.coverUrl}>
                    {draft.coverUrl.split('/').pop()}
                  </span>
                )}
              </div>
            </Field>
            <Field label="settings.xml">
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => void onPickSettings()}
                  disabled={uploadingSettings}
                  className="inline-flex items-center gap-2 px-3 h-10 rounded-xl
                             bg-glass border border-glass-border text-sm font-semibold
                             text-text-primary hover:bg-glass-strong disabled:opacity-60 transition-colors"
                  style={{ outline: 'none' }}
                >
                  {uploadingSettings ? <Loader2 size={14} className="animate-spin" /> : <FileJson size={14} />}
                  <span>{uploadingSettings ? 'Загружаем…' : 'Выбрать XML'}</span>
                </button>
                {draft.settingsXmlUrl && (
                  <span className="text-xs text-text-muted truncate max-w-[180px]" title={draft.settingsXmlUrl}>
                    {draft.settingsXmlUrl.split('/').pop()}
                  </span>
                )}
              </div>
            </Field>
          </Section>

          {}
          <Section title="База: Редукс + Ган-пак">
            <Field label="Базовый редукс">
              <CatalogPickButton
                pickedName={reduxDisplayName}
                pickedId={draft.reduxId}
                icon={<Layers size={12} />}
                onOpen={onOpenReduxCatalog}
              />
            </Field>
            <Field label="Ган-пак (опционально)">
              <CatalogPickButton
                pickedName={gunpackDisplayName}
                pickedId={draft.gunpackId}
                icon={<Crosshair size={12} />}
                onOpen={onOpenGunpackCatalog}
                emptyLabel="- Открыть каталог и выбрать ган-пак -"
              />
            </Field>
            {draft.gunpackId && (
              <div className="mt-1">
                <div className="text-[11px] font-semibold uppercase tracking-wider text-text-muted mb-2 inline-flex items-center gap-1.5">
                  <Sparkles size={11} className="text-accent" />
                  Выборочные пушки
                </div>
                <PerGunSelector
                  gunpackId={draft.gunpackId}
                  gunSlots={draft.gunSlots}
                  onChange={next => patch({ gunSlots: next })}
                />
              </div>
            )}
          </Section>

          {}
          <Section title="Если у игрока кастомный update.rpf" colSpan={2}>
            <div className="flex items-start gap-2.5 p-3 rounded-xl border border-accent/30 bg-accent-soft">
              <Info size={16} className="shrink-0 mt-0.5 text-accent" />
              <div className="text-[12.5px] text-text-secondary leading-relaxed">
                Залейте его файл через <span className="text-text-primary font-semibold">Admin → Redux</span>{' '}
                как обычный редукс, затем в{' '}
                <span className="text-text-primary font-semibold">Admin → Database</span>{' '}
                переключите ему status в <code className="px-1 py-0.5 rounded bg-bg-elevated text-text-primary text-[11px] font-mono">hidden</code>{' '}
                - он не появится в публичном каталоге <code className="px-1 py-0.5 rounded bg-bg-elevated text-text-primary text-[11px] font-mono">/redux</code>,
                но будет доступен в каталоге выше (поиск по UUID работает) для привязки к этому игроку. Установка
                у получателя - обычный <code className="px-1 py-0.5 rounded bg-bg-elevated text-text-primary text-[11px] font-mono">reduxInstall(id)</code>{' '}
                по тому же flow что и публичные редуксы.
              </div>
            </div>
          </Section>

          {}
          <Section title="Девайсы" colSpan={2}>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
              <Field label="Mouse">
                <Input
                  value={devices.mouse?.name ?? ''}
                  onChange={v => setDevice('mouse', v)}
                  placeholder="Logitech G PRO X Superlight"
                />
              </Field>
              <Field label="Keyboard">
                <Input
                  value={devices.keyboard?.name ?? ''}
                  onChange={v => setDevice('keyboard', v)}
                  placeholder="Wooting 80HE"
                />
              </Field>
              <Field label="Headset">
                <Input
                  value={devices.headset?.name ?? ''}
                  onChange={v => setDevice('headset', v)}
                  placeholder="Logitech PRO X 2"
                />
              </Field>
              <Field label="Display">
                <Input
                  value={devices.monitor?.name ?? ''}
                  onChange={v => setDevice('monitor', v)}
                  placeholder="ROG STRIX OLED"
                />
              </Field>
              <Field label="Surface">
                <Input
                  value={devices.surface?.name ?? ''}
                  onChange={v => setDevice('surface', v)}
                  placeholder="HyperX Fury XL"
                />
              </Field>
              <Field label="Audio Input">
                <Input
                  value={devices.audioInput?.name ?? ''}
                  onChange={v => setDevice('audioInput', v)}
                  placeholder="Fifine AM8"
                />
              </Field>
              <Field label="Graphics">
                <Input
                  value={devices.graphics?.name ?? ''}
                  onChange={v => setDevice('graphics', v)}
                  placeholder="RTX 4090"
                />
              </Field>
              <Field label="Processor">
                <Input
                  value={devices.processor?.name ?? ''}
                  onChange={v => setDevice('processor', v)}
                  placeholder="Ryzen 7 9800X3D"
                />
              </Field>
            </div>
          </Section>

          {}
          <Section title="Заметка модератора (внутренняя)" colSpan={2}>
            <textarea
              value={draft.adminNotes}
              onChange={(e) => patch({ adminNotes: e.target.value })}
              rows={3}
              placeholder="Откуда RPF, кто верифицировал, что менять"
              className="w-full px-3 py-2 rounded-xl bg-glass border border-glass-border
                         text-sm text-text-primary outline-none focus:border-accent/40"
            />
          </Section>
        </div>
      </div>
    </motion.div>
  );
}

function Section({
  title, colSpan = 1, children,
}: {
  title: string;
  colSpan?: 1 | 2;
  children: React.ReactNode;
}) {
  return (
    <GlassPanel
      depth="z2"
      tint="strong"
      rounded="2xl"
      className={(colSpan === 2 ? 'lg:col-span-2 ' : '') + 'p-5 flex flex-col gap-3'}
    >
      <h3 className="text-[11px] font-bold uppercase tracking-[0.22em] text-text-muted">
        {title}
      </h3>
      {children}
    </GlassPanel>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">
        {label}
      </span>
      {children}
    </label>
  );
}

function Input({
  value, onChange, placeholder,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
}) {
  return (
    <input
      type="text"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      className="w-full h-10 px-3 rounded-xl bg-glass border border-glass-border
                 text-sm text-text-primary placeholder:text-text-muted/60
                 outline-none focus:border-accent/40 focus:bg-glass-strong transition-colors"
    />
  );
}

function CatalogPickButton({
  pickedName, pickedId, icon, onOpen, emptyLabel,
}: {
  pickedName: string;
  pickedId:   string;
  icon:       React.ReactNode;
  onOpen:     () => void;
  emptyLabel?: string;
}) {
  const empty = !pickedId;
  return (
    <button
      type="button"
      onClick={onOpen}
      className="w-full h-10 px-3 rounded-xl
                 bg-glass border border-glass-border hover:border-accent/40
                 text-sm text-left flex items-center gap-2
                 transition-colors"
      style={{ outline: 'none' }}
    >
      <span className="shrink-0 text-text-muted">{icon}</span>
      <span className={'flex-1 truncate ' + (empty ? 'text-text-muted' : 'text-text-primary font-semibold')}>
        {empty
          ? (emptyLabel ?? '- Открыть каталог и выбрать -')
          : pickedName}
      </span>
      <span className="text-[10px] uppercase tracking-[0.18em] text-accent shrink-0 inline-flex items-center gap-1">
        <Search size={10} />
        {empty ? 'Выбрать →' : 'Сменить →'}
      </span>
    </button>
  );
}

function parseFiniteOrNull(raw: string): number | null {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  const n = Number(trimmed);
  return Number.isFinite(n) ? n : null;
}

void Trophy; void Mouse; void Keyboard; void Monitor; void Headphones;
