import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, type Variants } from 'framer-motion';
import {
  ArrowLeft, Check, Loader2, Upload, AlertTriangle, Layers, Crosshair,
  Mouse, Keyboard, Monitor, Headphones, Gauge, Video, FileText, Link2,
} from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { BackButton } from '@/components/BackButton';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useUserBuildsStore, type GunSlotState } from '@/store/userBuildsStore';
import { useSessionStore } from '@/store/sessionStore';
import { useSubmitDraftStore } from '@/store/submitDraftStore';
import { useNavStore } from '@/store/navStore';
import { generateHntCode } from '../userBuilds/hntCode';
import { Toast, type ToastTone } from '@/components/Toast';

interface Props {
  onClose: () => void;
  onSubmitted?: (buildId: string) => void;
}

const containerV: Variants = {
  hidden:  { opacity: 1 },
  visible: { opacity: 1, transition: { delayChildren: 0.04, staggerChildren: 0.06 } },
};
const itemV: Variants = {
  hidden:  { opacity: 0, y: 10 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.32, ease: EASE_DEPTH } },
};

const RESOLUTION_PRESETS = [
  '1280x720', '1440x1080', '1600x900', '1728x1080',
  '1920x1080', '2560x1080', '2560x1440', '3440x1440', '3840x2160',
];

export function SubmitProfileScreen({ onClose, onSubmitted }: Props) {
  const { t } = useTranslation();

  const reduxList   = useReduxStore(s => s.items) ?? [];
  const loadReduxes = useReduxStore(s => s.load);
  const publicPacks = useGunpackStore(s => s.publicPacks) ?? [];
  const loadPacks   = useGunpackStore(s => s.loadPublicPacks);

  const submitBuild       = useUserBuildsStore(s => s.submit);
  const uploadSettingsXml = useUserBuildsStore(s => s.uploadSettingsXml);

  const auth = useSessionStore(s => s.auth);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

  const [name, setName]                = useState('');

  const draft = useSubmitDraftStore();
  const requestNavigate = useNavStore(s => s.requestNavigate);
  const reduxPickedId = draft.reduxId ?? '';
  const packPickedId  = draft.gunpackId ?? '';
  const [reduxExtLink, setReduxExtLink] = useState('');
  const [packExtLink,  setPackExtLink]  = useState('');

  const [mouse, setMouse]            = useState('');
  const [keyboard, setKeyboard]      = useState('');
  const [monitor, setMonitor]        = useState('');
  const [monitorHz, setMonitorHz]    = useState<string>('');
  const [headset, setHeadset]        = useState('');
  const [sensitivity, setSensitivity]= useState<string>('');
  const [dpi, setDpi]                = useState<string>('');
  const [resolution, setResolution]  = useState<string>('1920x1080');
  const [videoUrl, setVideoUrl]      = useState('');
  const [description, setDescription]= useState('');

  const gunSlots = draft.gunSlots;
  const setGunSlots = draft.setGunSlots;

  const [localXmlPath, setLocalXmlPath] = useState<string | null>(null);

  const [submitting, setSubmitting] = useState(false);
  const [doneId, setDoneId] = useState<string | null>(null);
  const [toast, setToast] = useState<{ tone: ToastTone; message: string } | null>(null);

  useEffect(() => {
    if (reduxList.length === 0)   void loadReduxes();
    if (publicPacks.length === 0) void loadPacks();
  }, [reduxList.length, publicPacks.length, loadReduxes, loadPacks]);

  const pickedRedux = useMemo(
    () => (reduxPickedId ? reduxList.find(r => r.id === reduxPickedId) ?? null : null),
    [reduxPickedId, reduxList]);
  const pickedPack = useMemo(
    () => (packPickedId ? publicPacks.find(p => p.id === packPickedId) ?? null : null),
    [packPickedId, publicPacks]);

  const canSubmit = useMemo(() => {
    if (!userId) return false;
    if (!name.trim()) return false;

    if (!reduxPickedId && !reduxExtLink.trim()) return false;
    if (!packPickedId  && !packExtLink.trim())  return false;
    return true;
  }, [userId, name, reduxPickedId, reduxExtLink, packPickedId, packExtLink]);

  const onPickXml = async () => {
    const { bridge } = await import('@/bridge');
    try {

      const path = await bridge.openFileDialog('GTA settings.xml', '*.xml');
      if (path) setLocalXmlPath(path);
    } catch (e) {
      console.warn('[submit-profile] openFileDialog failed', e);

      const promptedPath = window.prompt(t('players.submit.promptXmlPath', 'Введите полный путь к settings.xml:'));
      if (promptedPath?.trim()) setLocalXmlPath(promptedPath.trim());
    }
  };

  const onSubmit = async () => {
    if (!canSubmit || !userId || submitting) return;
    setSubmitting(true);
    try {

      const devices: Record<string, unknown> = {};
      if (mouse.trim())    devices.mouse    = { name: mouse.trim() };
      if (keyboard.trim()) devices.keyboard = { name: keyboard.trim() };
      if (monitor.trim()) {
        const hz = monitorHz.trim() ? Number(monitorHz.trim()) : undefined;
        devices.monitor = hz && Number.isFinite(hz)
          ? { name: monitor.trim(), hz }
          : { name: monitor.trim() };
      }
      if (headset.trim())  devices.headset  = { name: headset.trim() };

      const reduxName = pickedRedux?.name
        ?? (reduxExtLink.trim() ? `[link] ${reduxExtLink.trim()}` : '');
      const packName  = pickedPack?.name
        ?? (packExtLink.trim()  ? `[link] ${packExtLink.trim()}`  : '');

      const saved = await submitBuild({
        name:                  name.trim(),
        author:                auth?.username ?? 'guest',
        authorUserId:          userId,
        hntCode:               generateHntCode(),

        reduxId:               reduxPickedId || `ext:${reduxExtLink.trim()}`,
        gunpackId:             packPickedId  || `ext:${packExtLink.trim()}`,
        reduxNameSnapshot:     reduxName,
        gunpackNameSnapshot:   packName,
        gunSlots:              gunSlots,
        armor:                 null,
        arena:                 null,
        minimap:               null,

        reticle:               null,
        sounds:                null,
        devices:               devices as Record<string, never>,

        sensitivity:           parseFiniteOrNull(sensitivity),
        dpi:                   parseFiniteOrNull(dpi),
        resolution:            resolution.trim() || null,
        videoUrl:              videoUrl.trim()   || null,
        settingsXmlUrl:        null,
        description:           description.trim(),
      });

      if (localXmlPath) {
        try {
          const url = await uploadSettingsXml(saved.id, localXmlPath);
          await useUserBuildsStore.getState().update(saved.id, { settingsXmlUrl: url });
        } catch (e) {
          console.warn('[submit-profile] settings.xml upload failed', e);
          setToast({ tone: 'warning', message: t('players.submit.xmlUploadFailed', 'Сборка отправлена, но settings.xml не загрузился. Админ попросит приложить заново.') });
        }
      }

      setDoneId(saved.id);
      onSubmitted?.(saved.id);

      draft.reset();
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : t('players.submit.submitFailed', 'Не удалось отправить сборку.') });
    } finally {
      setSubmitting(false);
    }
  };

  if (doneId) {
    return (
      <div className="h-full overflow-y-auto">
        <div className="max-w-[640px] mx-auto px-12 py-20">
          <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-10 text-center space-y-4">
            <div className="inline-flex items-center justify-center w-14 h-14 rounded-2xl
                            bg-status-success/20 text-status-success">
              <Check size={26} />
            </div>
            <h1 className="text-[24px] font-semibold tracking-tight text-text-primary">
              {t('players.submit.doneTitle', 'Сборка отправлена на одобрение')}
            </h1>
            <p className="text-[13px] text-text-secondary leading-relaxed">
              {t('players.submit.doneBody', 'Админ скоро посмотрит. Если что-то не хватает - он напишет тебе с просьбой дополнить; увидишь ответ в своём списке заявок.')}
            </p>
            <div className="pt-2">
              <button
                type="button"
                onClick={onClose}
                className="inline-flex items-center gap-2 px-5 h-10 rounded-xl
                           bg-accent text-text-on-accent hover:bg-accent-hover
                           text-[13px] font-medium transition-colors"
              >
                <ArrowLeft size={14} /> {t('players.submit.backToList', 'Вернуться к списку')}
              </button>
            </div>
          </GlassPanel>
        </div>
        <Toast
          open={!!toast}
          tone={toast?.tone ?? 'info'}
          message={toast?.message ?? ''}
          onClose={() => setToast(null)}
        />
      </div>
    );
  }

  return (
    <motion.div
      className="h-full overflow-y-auto"
      variants={containerV} initial="hidden" animate="visible"
    >
      <div className="max-w-[860px] mx-auto px-12 py-10 space-y-6">

        {}
        <motion.div variants={itemV} className="flex items-center gap-4">
          <BackButton onClick={onClose} label={t('common.back') || 'Назад'} />
          <div className="flex-1 min-w-0">
            <h1 className="text-[24px] font-semibold tracking-tight text-text-primary">
              {t('players.submit.title', 'Заявка на сборку')}
            </h1>
            <p className="mt-1 text-[13px] text-text-muted">
              {t('players.submit.subtitle', 'Заполни как можно больше - твою сборку добавят на страницу игроков, когда админ её одобрит.')}
            </p>
          </div>
        </motion.div>

        {!userId && (
          <motion.div variants={itemV}>
            <GlassPanel depth="z1" tint="soft" rounded="2xl" className="p-4 flex items-start gap-3">
              <AlertTriangle size={16} className="text-status-warning shrink-0 mt-0.5" />
              <div className="text-[13px] text-text-secondary leading-relaxed">
                {t('players.submit.signInFirst', 'Чтобы отправить заявку, войди в аккаунт сверху-слева.')}
              </div>
            </GlassPanel>
          </motion.div>
        )}

        {}
        <motion.section variants={itemV}>
          <SectionHeader title={t('players.submit.sectionBasics', 'Основное')} />
          <FieldStack>
            <Field label={t('players.submit.nameLabel', 'Название сборки')} hint={t('players.submit.nameHint', 'Например: «PRO COMP TURBO» или «Night Drive - minimal HUD».')}>
              <TextInput value={name} onChange={setName} placeholder="…" />
            </Field>
          </FieldStack>
        </motion.section>

        {}
        <motion.section variants={itemV}>
          <SectionHeader title={t('players.submit.sectionBuild', 'Сборка')} hint={t('players.submit.sectionBuildHint', 'Открой каталог, выбери и (для редукса) при желании кастомизируй. Если в каталоге нет - кинь ссылку, админ добавит при одобрении.')} />
          <FieldStack>
            <CatalogPicker
              label={t('players.submit.reduxLabel', 'Редукс')}
              icon={<Layers size={13} />}
              pickedName={pickedRedux?.name ?? null}
              extLink={reduxExtLink}
              onExtLink={setReduxExtLink}
              extPlaceholder="https://..."
              onOpenCatalog={() => {
                draft.startPick('redux');
                requestNavigate('redux');
              }}
            />
            <CatalogPicker
              label={t('players.submit.gunpackLabel', 'Ган-пак')}
              icon={<Crosshair size={13} />}
              pickedName={pickedPack?.name ?? null}
              extLink={packExtLink}
              onExtLink={setPackExtLink}
              extPlaceholder="https://..."
              onOpenCatalog={() => {
                draft.startPick('gunpack');
                requestNavigate('guns');
              }}
            />
            {packPickedId && (
              <Field
                label={t('players.submit.gunsPickLabel', 'Какие пушки взять из пака')}
                hint={t('players.submit.gunsPickHint', 'По умолчанию все. Сними галочку чтобы не ставить эту пушку - она останется ванильной.')}
              >
                <PerGunSelector
                  gunpackId={packPickedId}
                  gunSlots={gunSlots}
                  onChange={setGunSlots}
                />
              </Field>
            )}
          </FieldStack>
        </motion.section>

        {}
        <motion.section variants={itemV}>
          <SectionHeader title={t('players.submit.sectionXml', 'Файл settings.xml')} hint={t('players.submit.sectionXmlHint', 'Опционально, но круто иметь - другие игроки смогут поставить твои настройки графики одной кнопкой.')} />
          <FieldStack>
            <Field label={t('players.submit.fileLabel', 'Файл')}>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={() => void onPickXml()}
                  className="inline-flex items-center gap-2 px-3.5 h-9 rounded-lg
                             bg-bg-elevated-soft hover:bg-bg-elevated
                             border border-border-subtle hover:border-border-strong
                             text-[13px] text-text-secondary hover:text-text-primary
                             transition-colors"
                >
                  <Upload size={13} />
                  {localXmlPath ? t('players.submit.replaceFile', 'Заменить файл') : t('players.submit.pickXml', 'Выбрать settings.xml')}
                </button>
                {localXmlPath && (
                  <span className="inline-flex items-center gap-1.5 text-[12px] text-text-muted truncate" title={localXmlPath}>
                    <FileText size={12} className="shrink-0" />
                    <span className="truncate">{localXmlPath.split(/[\\/]/).pop()}</span>
                  </span>
                )}
              </div>
            </Field>
          </FieldStack>
        </motion.section>

        {}
        <motion.section variants={itemV}>
          <SectionHeader title={t('players.submit.sectionDevices', 'Девайсы')} hint={t('players.submit.sectionDevicesHint', 'Опционально. То что увидят другие игроки на твоей карточке.')} />
          <FieldStack>
            <Field label={t('players.submit.mouse', 'Мышь')} inline>
              <TextInput value={mouse} onChange={setMouse} placeholder="Logitech G Pro X Superlight" icon={<Mouse size={12} />} />
            </Field>
            <Field label={t('players.submit.keyboard', 'Клавиатура')} inline>
              <TextInput value={keyboard} onChange={setKeyboard} placeholder="Wooting 60HE" icon={<Keyboard size={12} />} />
            </Field>
            <Field label={t('players.submit.monitor', 'Монитор')} inline>
              <div className="flex items-center gap-2 w-full">
                <TextInput value={monitor} onChange={setMonitor} placeholder="Dell AW2521HF" icon={<Monitor size={12} />} className="flex-1" />
                <NumberInput value={monitorHz} onChange={setMonitorHz} placeholder="240" suffix="Hz" className="w-20" />
              </div>
            </Field>
            <Field label={t('players.submit.headset', 'Гарнитура')} inline>
              <TextInput value={headset} onChange={setHeadset} placeholder="HyperX Cloud II" icon={<Headphones size={12} />} />
            </Field>
          </FieldStack>
        </motion.section>

        {}
        <motion.section variants={itemV}>
          <SectionHeader title={t('players.submit.sectionGameSettings', 'Игровые настройки')} />
          <FieldStack>
            <Field label={t('players.submit.sensitivity', 'Чувствительность')} inline>
              <NumberInput value={sensitivity} onChange={setSensitivity} placeholder="0.85" icon={<Gauge size={12} />} step="0.01" />
            </Field>
            <Field label={t('players.submit.dpi', 'DPI мыши')} inline>
              <NumberInput value={dpi} onChange={setDpi} placeholder="800" />
            </Field>
            <Field label={t('players.submit.resolution', 'Разрешение')} inline>
              <ResolutionPicker value={resolution} onChange={setResolution} />
            </Field>
          </FieldStack>
        </motion.section>

        {}
        <motion.section variants={itemV}>
          <SectionHeader title={t('players.submit.sectionMedia', 'Ролик и описание')} />
          <FieldStack>
            <Field label={t('players.submit.videoLabel', 'Ссылка на ролик')} hint={t('players.submit.videoHint', 'YouTube / Vimeo / прямой mp4 - встроится в карточку.')}>
              <TextInput value={videoUrl} onChange={setVideoUrl} placeholder="https://youtu.be/..." icon={<Video size={12} />} />
            </Field>
            <Field label={t('common.description', 'Описание')}>
              <TextArea value={description} onChange={setDescription}
                placeholder={t('players.submit.descriptionPlaceholder', 'Расскажи о своём билде, своей школе, что особенного в твоей настройке.')} />
            </Field>
          </FieldStack>
        </motion.section>

        {}
        <motion.div variants={itemV} className="pt-2 flex items-center justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className="px-4 h-10 rounded-xl text-[13px] text-text-muted
                       hover:text-text-primary hover:bg-glass transition-colors"
          >
            {t('common.cancel', 'Отмена')}
          </button>
          <button
            type="button"
            onClick={() => void onSubmit()}
            disabled={!canSubmit || submitting}
            title={!canSubmit ? t('players.submit.fillRequiredHint', 'Заполни название, редукс и ган-пак') : undefined}
            className="inline-flex items-center gap-2 px-5 h-10 rounded-xl text-[13px] font-medium
                       bg-accent text-text-on-accent
                       hover:bg-accent-hover
                       disabled:opacity-40 disabled:cursor-not-allowed
                       transition-colors"
          >
            {submitting ? <Loader2 size={13} className="animate-spin" /> : <Check size={13} />}
            <span>{submitting ? t('players.submit.submitting', 'Отправляем…') : t('players.submit.submitButton', 'Отправить на одобрение')}</span>
          </button>
        </motion.div>
      </div>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'info'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={6000}
      />
    </motion.div>
  );
}

function parseFiniteOrNull(raw: string): number | null {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  const n = Number(trimmed);
  return Number.isFinite(n) ? n : null;
}

function SectionHeader({ title, hint }: { title: string; hint?: string }) {
  return (
    <header className="mb-3">
      <h2 className="text-[15px] font-semibold tracking-tight text-text-primary">{title}</h2>
      {hint && <p className="mt-0.5 text-[12px] text-text-muted leading-relaxed max-w-[560px]">{hint}</p>}
    </header>
  );
}

function FieldStack({ children }: { children: React.ReactNode }) {
  return (
    <GlassPanel depth="z1" tint="soft" rounded="2xl" className="px-5 py-1 divide-y divide-border-subtle">
      {children}
    </GlassPanel>
  );
}

function Field({ label, hint, children, inline = false }: {
  label: string; hint?: string; children: React.ReactNode; inline?: boolean;
}) {
  if (inline) {
    return (
      <div className="grid grid-cols-[180px_1fr] gap-6 items-start py-3">
        <div className="min-w-0">
          <div className="text-[13px] text-text-primary">{label}</div>
          {hint && <p className="mt-0.5 text-[11.5px] text-text-muted leading-relaxed">{hint}</p>}
        </div>
        <div className="min-w-0">{children}</div>
      </div>
    );
  }
  return (
    <div className="py-3 space-y-1.5">
      <div>
        <div className="text-[13px] text-text-primary">{label}</div>
        {hint && <p className="mt-0.5 text-[11.5px] text-text-muted leading-relaxed">{hint}</p>}
      </div>
      <div>{children}</div>
    </div>
  );
}

function TextInput({ value, onChange, placeholder, icon, className = '' }: {
  value: string; onChange: (v: string) => void;
  placeholder?: string; icon?: React.ReactNode; className?: string;
}) {
  return (
    <div className={'relative ' + className}>
      {icon && (
        <span className="absolute left-3 top-1/2 -translate-y-1/2 text-text-muted pointer-events-none">
          {icon}
        </span>
      )}
      <input
        type="text"
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={placeholder}
        className={
          'w-full h-9 rounded-lg ' +
          (icon ? 'pl-8 pr-3 ' : 'px-3 ') +
          'bg-bg-elevated-soft hover:bg-bg-elevated ' +
          'border border-border-subtle hover:border-border-strong ' +
          'focus:border-accent focus:bg-bg-elevated ' +
          'text-[13px] text-text-primary placeholder:text-text-muted ' +
          'outline-none transition-colors'
        }
      />
    </div>
  );
}

function NumberInput({ value, onChange, placeholder, icon, suffix, step, className = '' }: {
  value: string; onChange: (v: string) => void;
  placeholder?: string; icon?: React.ReactNode; suffix?: string; step?: string; className?: string;
}) {
  return (
    <div className={'relative ' + className}>
      {icon && (
        <span className="absolute left-3 top-1/2 -translate-y-1/2 text-text-muted pointer-events-none">
          {icon}
        </span>
      )}
      <input
        type="number"
        inputMode="decimal"
        step={step}
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={placeholder}
        className={
          'w-full h-9 rounded-lg tabular-nums ' +
          (icon   ? 'pl-8 ' : 'pl-3 ') +
          (suffix ? 'pr-12 ' : 'pr-3 ') +
          'bg-bg-elevated-soft hover:bg-bg-elevated ' +
          'border border-border-subtle hover:border-border-strong ' +
          'focus:border-accent focus:bg-bg-elevated ' +
          'text-[13px] text-text-primary placeholder:text-text-muted ' +
          'outline-none transition-colors ' +

          '[appearance:textfield] [&::-webkit-outer-spin-button]:appearance-none ' +
          '[&::-webkit-outer-spin-button]:m-0 [&::-webkit-inner-spin-button]:appearance-none ' +
          '[&::-webkit-inner-spin-button]:m-0'
        }
      />
      {suffix && (
        <span className="absolute right-3 top-1/2 -translate-y-1/2 text-[10px] uppercase tracking-[0.14em] text-text-muted pointer-events-none">
          {suffix}
        </span>
      )}
    </div>
  );
}

function TextArea({ value, onChange, placeholder }: {
  value: string; onChange: (v: string) => void; placeholder?: string;
}) {
  return (
    <textarea
      rows={4}
      value={value}
      onChange={e => onChange(e.target.value)}
      placeholder={placeholder}
      className="w-full px-3 py-2 rounded-lg
                 bg-bg-elevated-soft hover:bg-bg-elevated
                 border border-border-subtle hover:border-border-strong
                 focus:border-accent focus:bg-bg-elevated
                 text-[13px] text-text-primary placeholder:text-text-muted
                 outline-none transition-colors leading-relaxed resize-none"
    />
  );
}

function CatalogPicker({
  label, icon, pickedName, extLink, onExtLink, extPlaceholder, onOpenCatalog,
}: {
  label: string;
  icon: React.ReactNode;
  pickedName: string | null;
  extLink: string;
  onExtLink: (s: string) => void;
  extPlaceholder?: string;
  onOpenCatalog: () => void;
}) {
  const { t } = useTranslation();
  const [mode, setMode] = useState<'catalog' | 'link'>(extLink ? 'link' : 'catalog');
  return (
    <Field label={label}>
      <div className="space-y-2">
        <div className="inline-flex items-center rounded-md bg-bg-elevated-soft border border-border-subtle p-0.5">
          <ModeBtn active={mode === 'catalog'} onClick={() => setMode('catalog')}>
            {icon}
            <span className="ml-1.5">{t('players.submit.modeCatalog', 'Из каталога')}</span>
          </ModeBtn>
          <ModeBtn active={mode === 'link'} onClick={() => setMode('link')}>
            <Link2 size={11} />
            <span className="ml-1.5">{t('players.submit.modeLink', 'Ссылка')}</span>
          </ModeBtn>
        </div>
        {mode === 'catalog' ? (
          <button
            type="button"
            onClick={onOpenCatalog}
            className="w-full h-10 px-3 rounded-lg
                       bg-bg-elevated-soft hover:bg-bg-elevated
                       border border-border-subtle hover:border-accent/40
                       text-[13px] text-left flex items-center gap-2
                       transition-colors"
            style={{ outline: 'none' }}
          >
            <span className={'flex-1 truncate ' + (pickedName ? 'text-text-primary font-semibold' : 'text-text-muted')}>
              {pickedName ?? t('players.submit.openCatalogPlaceholder', '- Открыть каталог и выбрать -')}
            </span>
            <span className="text-[10px] uppercase tracking-[0.18em] text-accent shrink-0">
              {pickedName ? t('players.submit.changeArrow', 'Сменить →') : t('players.submit.pickArrow', 'Выбрать →')}
            </span>
          </button>
        ) : (
          <TextInput value={extLink} onChange={onExtLink} placeholder={extPlaceholder} icon={<Link2 size={12} />} />
        )}
      </div>
    </Field>
  );
}

function ModeBtn({ active, onClick, children }: {
  active: boolean; onClick: () => void; children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        'inline-flex items-center px-2.5 h-7 rounded-[5px] text-[12px] transition-colors ' +
        (active
          ? 'bg-bg-base text-text-primary shadow-sm'
          : 'text-text-muted hover:text-text-secondary')
      }
    >
      {children}
    </button>
  );
}

function ResolutionPicker({ value, onChange }: {
  value: string; onChange: (v: string) => void;
}) {
  const { t } = useTranslation();
  const isPreset = RESOLUTION_PRESETS.includes(value);
  const [custom, setCustom] = useState(isPreset ? '' : value);
  return (
    <div className="flex items-center gap-2 w-full">
      <select
        value={isPreset ? value : '__custom'}
        onChange={e => {
          if (e.target.value === '__custom') {

            onChange(custom || '');
          } else {
            onChange(e.target.value);
          }
        }}
        className="h-9 px-3 rounded-lg
                   bg-bg-elevated-soft hover:bg-bg-elevated
                   border border-border-subtle hover:border-border-strong
                   focus:border-accent focus:bg-bg-elevated
                   text-[13px] text-text-primary
                   outline-none transition-colors"
      >
        {RESOLUTION_PRESETS.map(r => (
          <option key={r} value={r}>{r}</option>
        ))}
        <option value="__custom">{t('players.submit.resolutionCustom', 'Custom…')}</option>
      </select>
      {!isPreset && (
        <TextInput
          value={custom}
          onChange={v => { setCustom(v); onChange(v); }}
          placeholder="2560x1080"
          className="flex-1"
        />
      )}
    </div>
  );
}

export function PerGunSelector({
  gunpackId, gunSlots, onChange,
}: {
  gunpackId: string;
  gunSlots: Record<string, GunSlotState>;
  onChange: (next: Record<string, GunSlotState>) => void;
}) {
  const { t } = useTranslation();
  const allGuns       = useGunpackStore(s => s.allGuns);
  const loadAllGuns   = useGunpackStore(s => s.loadAllGuns);
  const loadingAllGuns = useGunpackStore(s => s.loadingAllGuns);

  useEffect(() => { void loadAllGuns(); }, [loadAllGuns]);

  const packGuns = useMemo(
    () => allGuns.filter(g => g.packId === gunpackId),
    [allGuns, gunpackId]);

  const isExcluded = (internalName: string): boolean =>
    gunSlots[internalName]?.kind === 'vanilla';

  const togglePresent = (internalName: string, gunId: string) => {
    const next = { ...gunSlots };
    if (isExcluded(internalName)) {

      delete next[internalName];
    } else {

      next[internalName] = { kind: 'vanilla' };
    }
    void gunId;
    onChange(next);
  };

  if (loadingAllGuns && packGuns.length === 0) {
    return (
      <div className="px-3 py-2 text-[12px] text-text-muted flex items-center gap-2">
        <Loader2 size={12} className="animate-spin" />
        <span>{t('players.submit.gunsLoading', 'Загружаем пушки пака…')}</span>
      </div>
    );
  }

  if (packGuns.length === 0) {
    return (
      <div className="px-3 py-2 text-[12px] text-text-muted">
        {t('players.submit.gunsEmpty', 'В этом паке нет пушек.')}
      </div>
    );
  }

  const includedCount = packGuns.filter(g => !isExcluded(g.weaponPrefix + g.baseName)).length;

  return (
    <div className="rounded-lg bg-bg-elevated-soft border border-border-subtle">
      <div className="flex items-center gap-2 px-3 py-2 text-[11px] font-bold uppercase tracking-[0.18em]
                      text-text-muted border-b border-border-subtle">
        <span>{t('players.submit.gunsInBuild', 'Пушки в сборке')}</span>
        <span className="ml-auto tabular-nums text-text-primary">
          {includedCount} / {packGuns.length}
        </span>
        <button
          type="button"
          onClick={() => onChange({})}
          className="text-[10px] font-bold uppercase tracking-[0.16em] text-accent hover:underline"
          style={{ outline: 'none' }}
        >
          {t('players.submit.gunsSelectAll', 'Все')}
        </button>
        <button
          type="button"
          onClick={() => {
            const next: Record<string, GunSlotState> = {};
            for (const g of packGuns) next[g.weaponPrefix + g.baseName] = { kind: 'vanilla' };
            onChange(next);
          }}
          className="text-[10px] font-bold uppercase tracking-[0.16em] text-text-muted hover:underline"
          style={{ outline: 'none' }}
        >
          {t('players.submit.gunsSelectNone', 'Никого')}
        </button>
      </div>
      <div className="max-h-[260px] overflow-y-auto p-1.5 grid grid-cols-2 gap-1">
        {packGuns.map(g => {
          const internalName = g.weaponPrefix + g.baseName;
          const excluded = isExcluded(internalName);
          return (
            <button
              key={internalName}
              type="button"
              onClick={() => togglePresent(internalName, g.gunId)}
              className={
                'flex items-center gap-2 px-2 h-8 rounded-md text-[12px] transition-colors ' +
                (excluded
                  ? 'bg-transparent text-text-muted hover:bg-bg-elevated'
                  : 'bg-accent-soft text-text-primary hover:bg-accent/15')
              }
              style={{ outline: 'none' }}
            >
              <span className={
                'shrink-0 w-4 h-4 rounded-sm border flex items-center justify-center ' +
                (excluded ? 'border-white/[0.18]' : 'bg-accent border-accent text-text-on-accent')
              }>
                {!excluded && <Check size={10} strokeWidth={3} />}
              </span>
              <span className="truncate">{g.baseName}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

declare module '@/bridge/IAppBridge' {

  interface IAppBridge {
    pickGtaSettingsXmlForUpload?(): Promise<string | null>;
  }
}
