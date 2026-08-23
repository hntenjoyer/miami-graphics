import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import {
  Eye, Youtube, Twitch, MessageCircle, Trophy,
  MousePointer2, Keyboard, Headphones, Monitor, RectangleHorizontal,
  Mic, CircuitBoard, Cpu, Play, X, Archive, FileJson, Download,
  Loader2, CheckCircle2, Crosshair, ArrowLeft, ArrowUpRight,
  type LucideIcon,
} from 'lucide-react';
import type { ProPlayer } from './playersApi';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useNavStore } from '@/store/navStore';
import { bridge } from '@/bridge';

interface Props {
  player: ProPlayer;
  isRu:   boolean;
  onBack: () => void;
}

const detailContainer: Variants = {
  hidden: { opacity: 1 },
  visible: { opacity: 1, transition: { delayChildren: 0.05, staggerChildren: 0.07 } },
};
const detailItem: Variants = {
  hidden: { opacity: 0, y: 14 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.45, ease: [0.22, 1, 0.36, 1] } },
};

export function PlayerDetailScreen({ player, isRu, onBack }: Props) {
  const { t, i18n } = useTranslation();
  const role = (isRu ? player.roleRu : player.roleEn) ?? player.roleEn ?? player.roleRu ?? '';
  const description = (isRu ? player.descriptionRu : player.descriptionEn)
    ?? player.descriptionEn ?? player.descriptionRu ?? '';

  const [imgFailed, setImgFailed] = useState(false);
  const [activeIdx, setActiveIdx] = useState(0);
  const [videoPlaying, setVideoPlaying] = useState(false);

  const reduxInstall        = useReduxStore(s => s.install);
  const installedReduxId    = useReduxStore(s => s.installedReduxId);
  const selectRedux         = useReduxStore(s => s.select);
  const installedGunpack    = useGunpackStore(s => s.installedGunpack);
  const selectGunpack       = useGunpackStore(s => s.selectPack);
  const selectWhitelistGun  = useGunpackStore(s => s.selectWhitelistGun);
  const setGunsSubTab       = useGunpackStore(s => s.setGunsSubTab);
  const requestNavigate     = useNavStore(s => s.requestNavigate);

  const [installingRedux, setInstallingRedux] = useState(false);
  const [installingPack,  setInstallingPack]  = useState(false);
  const [applyingSettings, setApplyingSettings] = useState(false);
  const [applyingAll, setApplyingAll]         = useState(false);

  const isReduxInstalled = !!player.reduxId && installedReduxId === player.reduxId;
  const isPackInstalled  = !!player.gunpackId && installedGunpack.activeGunpackId === player.gunpackId;

  const onOpenReduxCard = () => {
    if (!player.reduxId) return;
    selectRedux(player.reduxId);
    requestNavigate('redux');
  };

  const onOpenGunpackCard = () => {
    if (!player.gunpackId) return;
    selectWhitelistGun(null);
    setGunsSubTab('gunpacks');
    void selectGunpack(player.gunpackId);
    requestNavigate('guns');
  };

  const onInstallRedux = async () => {
    if (!player.reduxId || installingRedux) return;
    setInstallingRedux(true);
    try { await reduxInstall(player.reduxId); }
    finally { setInstallingRedux(false); }
  };
  const onInstallPack = async () => {
    if (!player.gunpackId || installingPack) return;
    setInstallingPack(true);
    try {
      await bridge.gunpackInstallAll(player.gunpackId);
      void bridge.activityLog('gunpack_install', `ганпак «${player.gunpackName ?? player.name}»`);
    }
    finally { setInstallingPack(false); }
  };
  const onApplySettings = async () => {
    if (!player.settingsLink || applyingSettings) return;
    setApplyingSettings(true);
    try { await bridge.gtaSettingsApplyFromUrl(player.settingsLink); }
    finally { setApplyingSettings(false); }
  };

  const hasAnyComponent = !!player.reduxId || !!player.gunpackId || !!player.settingsLink;
  const onInstallAll = async () => {
    if (applyingAll || !hasAnyComponent) return;
    setApplyingAll(true);
    try {
      if (player.reduxId)      { try { await reduxInstall(player.reduxId); } catch {  } }
      if (player.gunpackId)    { try { await bridge.gunpackInstallAll(player.gunpackId); } catch {  } }
      if (player.settingsLink) { try { await bridge.gtaSettingsApplyFromUrl(player.settingsLink); } catch {  } }
    } finally {
      setApplyingAll(false);
    }
  };

  const dpi  = readSpec(player.specs, ['dpi', 'DPI']) || 'N/A';
  const sens = readSpec(player.specs, ['inGameSens', 'inGame', 'sens', 'sensitivity']) || 'N/A';
  const hz   = readSpec(player.specs, ['refreshRate', 'hz', 'Hz', 'monitorHz', 'frequency']) || 'N/A';

  const devices = useMemo(() => DEVICE_SLOTS.map(slot => ({
    ...slot,
    value: readSpec(player.devices, slot.keys),
  })), [player.devices]);
  const installLabel   = t('players.detail.install', 'Установить');
  const installedLabel = t('players.detail.installed', 'Установлено');
  const openCardLabel  = t('players.detail.openCard', 'К карточке');

  return (
    <motion.div
      className="min-h-full"
      variants={detailContainer}
      initial="hidden"
      animate="visible"
    >
      <div className="max-w-[1660px] mx-auto px-4 md:px-8 py-4 flex flex-col gap-5 relative">
        {}
        <motion.div
          className="grid grid-cols-1 lg:grid-cols-12 gap-6 lg:gap-8 relative z-10"
          variants={detailItem}
        >
          {}
          <div className="lg:col-span-4 flex flex-col gap-4 lg:pt-2">
            <div className="relative aspect-[6/7] rounded-2xl overflow-hidden
                            bg-bg-elevated border border-transparent shadow-z3 group">
              <button
                type="button"
                onClick={onBack}
                title={t('players.detail.backToList')}
                aria-label={t('players.detail.backToList')}
                className="absolute left-4 top-4 z-20 h-10 w-10 rounded-xl
                           bg-black/45 backdrop-blur-md border border-white/10
                           text-white/80 hover:text-white hover:bg-black/65
                           hover:border-white/20 transition-colors
                           flex items-center justify-center"
              >
                <ArrowLeft size={18} />
              </button>

              {player.image && !imgFailed ? (
                <img
                  src={player.image}
                  alt={player.name}
                  draggable={false}
                  className="absolute inset-0 w-full h-full object-cover
                             transform-gpu transition-transform duration-700 group-hover:scale-[1.04]"
                  onError={() => setImgFailed(true)}
                />
              ) : (
                <div className="absolute inset-0 flex items-center justify-center text-text-muted
                                bg-gradient-to-br from-bg-elevated to-bg-base">
                  <Trophy size={64} className="opacity-25" />
                </div>
              )}

              {}
              {}
              <div style={{ height: '32%' }} className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/40 to-transparent pointer-events-none" />

              {}
              <div className="absolute inset-x-0 bottom-0 p-5 z-10 flex flex-col items-start text-left">
                {role && (
                  <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full
                                  bg-white/10 backdrop-blur-md border border-transparent
                                  text-white text-[10px] font-bold uppercase tracking-[0.22em]
                                  mb-3 shadow-lg">
                    <Trophy size={12} className="text-accent" />
                    <span>{role}</span>
                  </div>
                )}

                <div className="w-full flex items-end justify-between gap-3 mb-4 min-w-0">
                  <h1 className="font-display font-black text-white uppercase tracking-tight leading-[0.95]
                                 text-3xl lg:text-4xl
                                 drop-shadow-[0_2px_10px_rgba(0,0,0,0.55)] truncate flex-1 min-w-0">
                    {player.name}
                  </h1>
                  <div className="flex gap-1.5 shrink-0">
                    <SocialLink href={player.youtube} icon={Youtube} title="YouTube" />
                    <SocialLink href={player.twitch}  icon={Twitch}  title="Twitch" />
                    <SocialLink href={player.discord} icon={MessageCircle} title="Discord" />
                  </div>
                </div>

                {}
                <div className="w-full bg-white/5 backdrop-blur-md rounded-2xl px-4 py-3
                                border border-transparent flex justify-between items-center
                                ">
                  <StatBadge label={t('players.detail.dpi')}  value={dpi} />
                  <span className="w-px h-8 bg-white/10" />
                  <StatBadge label={t('players.detail.sens')} value={sens} />
                  <span className="w-px h-8 bg-white/10" />
                  <StatBadge label={t('players.detail.hz')}   value={hz} />
                </div>
              </div>
            </div>

            {}
            {description && (
              <div className="rounded-2xl bg-bg-surface border border-transparent p-4
                              text-sm text-text-secondary leading-relaxed whitespace-pre-line">
                {description}
              </div>
            )}

            {hasAnyComponent && (
              <button
                type="button"
                onClick={() => void onInstallAll()}
                disabled={applyingAll}
                style={{ outline: 'none' }}
                className="group w-full h-[70px] mt-5 rounded-2xl
                           bg-bg-elevated border border-transparent hover:border-accent/30
                           disabled:opacity-60 disabled:cursor-wait
                           transition-colors duration-200
                           flex items-center gap-2 p-3 text-left overflow-hidden"
              >
                <div className="w-9 h-9 rounded-xl flex items-center justify-center shrink-0 bg-accent/15 text-accent">
                  <Download size={16} />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
                    {t('players.detail.fullBuildKicker', 'Full Build')}
                  </div>
                  <div className="font-display font-bold text-sm text-text-primary truncate">
                    {applyingAll
                      ? t('players.detail.installingAllShort', 'Устанавливаем')
                      : t('players.detail.installAll', 'Установить всё')}
                  </div>
                </div>
                <div className="shrink-0 h-9 w-9 rounded-xl bg-accent text-text-on-accent flex items-center justify-center">
                  {applyingAll ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
                </div>
              </button>
            )}
          </div>

          {}
          <div className="lg:col-span-8">
            {}
            <section className="relative">
              {!videoPlaying && (
                <>
                  <div className="absolute left-4 top-4 z-20 inline-flex items-center gap-2
                                  rounded-full bg-black/45 backdrop-blur-md px-3 py-1.5
                                  border border-white/10 text-[10px] font-bold uppercase
                                  tracking-[0.22em] text-white/70">
                    <span className="w-1.5 h-1.5 rounded-full bg-accent" />
                    {t('players.detail.highlights')}
                  </div>
                  <div className="absolute right-4 top-4 z-20 inline-flex items-center gap-2
                                  rounded-full bg-black/45 backdrop-blur-md px-3 py-1.5
                                  border border-white/10">
                    <Eye size={12} className="text-accent" />
                    <span className="text-xs font-mono font-bold text-white tabular-nums">
                      {player.views.toLocaleString(i18n.language)}
                    </span>
                  </div>
                </>
              )}

              <VideoGallery
                videoIds={player.videoIds}
                activeIdx={activeIdx}
                onSelect={(idx) => { setActiveIdx(idx); setVideoPlaying(false); }}
                playing={videoPlaying}
                onPlay={() => setVideoPlaying(true)}
                onClose={() => setVideoPlaying(false)}
              />
            </section>

            {}
            {hasAnyComponent && (
              <section className="hidden">
                <button
                  type="button"
                  onClick={() => void onInstallAll()}
                  disabled={applyingAll}
                  style={{ outline: 'none' }}
                  className="w-full inline-flex items-center justify-center gap-3 h-12 rounded-xl
                             bg-accent text-text-on-accent
                             border border-accent
                             hover:opacity-90 disabled:opacity-60 disabled:cursor-wait
                             transition-opacity text-sm font-bold uppercase tracking-[0.18em]
                             shadow-[0_8px_28px_-12px_color-mix(in_srgb,var(--accent)_70%,transparent)]"
                >
                  {applyingAll ? <Loader2 size={18} className="animate-spin" /> : <Download size={18} />}
                  <span>{applyingAll
                    ? t('players.detail.installingAll', 'Устанавливаем…')
                    : t('players.detail.installAllFullBuild', 'Установить всё (Full Build)')}</span>
                </button>
              </section>
            )}

            {}
            <section className="mt-4">
              <div className="h-7" aria-hidden="true" />
              <div className="grid grid-cols-1 xl:grid-cols-3 gap-2">
                {}
                {player.reduxId ? (
                  <ComponentRow
                    icon={Archive}
                    kicker="Redux"
                    title={player.reduxName ?? player.reduxId}
                    actionLabel={isReduxInstalled ? installedLabel : installLabel}
                    busy={installingRedux}
                    done={isReduxInstalled}
                    onClick={() => void onInstallRedux()}
                    onOpen={onOpenReduxCard}
                    openLabel={openCardLabel}
                  />
                ) : player.reduxLink ? (

                  <BigDownloadButton
                    href={player.reduxLink}
                    label={t('players.detail.downloadRedux')}
                    subLabel={t('players.detail.downloadReduxSub')}
                    icon={Archive}
                    primary
                  />
                ) : null}

                {}
                {player.gunpackId && (
                  <ComponentRow
                    icon={Crosshair}
                    kicker="Gunpack"
                    title={player.gunpackName ?? player.gunpackId}
                    actionLabel={isPackInstalled ? installedLabel : installLabel}
                    busy={installingPack}
                    done={isPackInstalled}
                    onClick={() => void onInstallPack()}
                    onOpen={onOpenGunpackCard}
                    openLabel={openCardLabel}
                  />
                )}

                {}
                {player.settingsLink && (
                  <ComponentRow
                    icon={FileJson}
                    kicker="GTA Settings"
                    title={t('players.detail.downloadSettingsSub', 'GTA V SETTINGS.XML')}
                    actionLabel={t('common.apply', 'Применить')}
                    busy={applyingSettings}
                    done={false}
                    onClick={() => void onApplySettings()}
                  />
                )}
              </div>
            </section>
          </div>
        </motion.div>

        {}
        <motion.section variants={detailItem}>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
            {devices.map(d => (
              <GearCard
                key={d.labelKey}
                label={t(`players.detail.devicesGrid.${d.labelKey}`, d.defaultLabel)}
                value={d.value}
                icon={d.icon}
              />
            ))}
          </div>
        </motion.section>
      </div>
    </motion.div>
  );
}

function StatBadge({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col items-center text-center flex-1 min-w-0">
      <span className="text-[9px] font-bold text-white/35 uppercase tracking-[0.22em] mb-1.5">
        {label}
      </span>
      <span className="text-white font-bold text-base tracking-tight tabular-nums truncate max-w-full">
        {value}
      </span>
    </div>
  );
}

function SocialLink({
  href, icon: Icon, title,
}: {
  href: string | null;
  icon: LucideIcon;
  title: string;
}) {
  if (!href) return null;
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      title={title}
      onClick={e => e.stopPropagation()}
      className="w-9 h-9 flex items-center justify-center rounded-lg
                 bg-white/5 border border-transparent text-white/55
                 hover:text-text-on-accent hover:bg-accent hover:border-accent
                 hover:scale-110 transition-all duration-300"
      style={{ outline: 'none' }}
    >
      <Icon size={15} />
    </a>
  );
}

function BigDownloadButton({
  href, label, subLabel, icon: Icon, primary,
}: {
  href: string | null;
  label: string;
  subLabel: string;
  icon: LucideIcon;
  primary?: boolean;
}) {
  const disabled = !href;

  const baseShell = 'group relative flex items-center gap-4 p-4 rounded-xl w-full overflow-hidden transition-all duration-300';
  const noFocusStyle = { outline: 'none', boxShadow: 'none' as const };
  const cls = primary
    ? 'bg-accent-soft/40 hover:bg-accent-soft/60'
    : 'bg-bg-elevated border border-transparent hover:border-text-secondary/30 hover:bg-glass-strong';

  const inner = (
    <>
      <div className={
        'w-12 h-12 rounded-lg flex items-center justify-center shrink-0 transition-colors ' +
        (primary
          ? 'bg-accent text-text-on-accent shadow-glow-accent'
          : 'bg-glass-strong text-text-secondary group-hover:bg-glass group-hover:text-text-primary')
      }>
        <Icon size={22} />
      </div>
      <div className="flex flex-col min-w-0 z-10">
        <span className={
          'text-sm font-bold uppercase tracking-wider leading-none transition-colors truncate ' +
          (primary ? 'text-accent' : 'text-text-primary')
        }>
          {label}
        </span>
        <span className="text-[10px] font-medium text-text-muted uppercase tracking-[0.22em] mt-1.5 truncate">
          {subLabel}
        </span>
      </div>
      <div className="ml-auto opacity-0 -translate-x-2 group-hover:opacity-100 group-hover:translate-x-0 transition-all duration-300">
        <Download size={14} className={primary ? 'text-accent' : 'text-text-muted'} />
      </div>
    </>
  );

  if (disabled) {
    return (
      <div className={baseShell + ' ' + cls + ' opacity-40 cursor-not-allowed'} style={noFocusStyle}>
        {inner}
      </div>
    );
  }
  return (
    <a
      href={href!}
      target="_blank"
      rel="noopener noreferrer"
      className={baseShell + ' ' + cls}
      style={noFocusStyle}
    >
      {inner}
    </a>
  );
}

function ComponentRow({
  icon: Icon, kicker, title, actionLabel, busy, done, onClick, onOpen, openLabel,
}: {
  icon:        LucideIcon;
  kicker:      string;
  title:       string;
  actionLabel: string;
  busy:        boolean;
  done:        boolean;
  onClick:     () => void;
  onOpen?:     () => void;
  openLabel?:  string;
}) {
  const { t } = useTranslation();
  return (
    <div className={
      'h-[70px] flex items-center gap-2 p-3 rounded-2xl bg-bg-elevated border border-transparent ' +
      'transition-colors duration-200 ' +
      (done ? 'border-green-500/30 bg-green-500/5' : 'hover:border-accent/30')
    }>
      <div className={
        'w-9 h-9 rounded-xl flex items-center justify-center shrink-0 ' +
        (done ? 'bg-green-500/15 text-green-300' : 'bg-accent/15 text-accent')
      }>
        <Icon size={16} />
      </div>
      <div className="flex-1 min-w-0">
        <div className="text-[10px] uppercase tracking-[0.22em] text-text-muted mb-0.5">
          {kicker}
        </div>
        <div className="font-display font-bold text-sm text-text-primary truncate">
          {title}
        </div>
      </div>
      <div className="flex items-center gap-1.5 shrink-0">
        {onOpen && (
          <button
            type="button"
            onClick={onOpen}
            title={openLabel}
            style={{ outline: 'none' }}
            className="inline-flex items-center justify-center w-9 h-9 rounded-xl
                       bg-glass border border-glass-border text-xs font-semibold text-text-secondary
                       hover:text-text-primary hover:bg-glass-strong hover:border-accent/40
                       transition-colors duration-200"
          >
            <ArrowUpRight size={13} />
          </button>
        )}
        <button
          type="button"
          onClick={onClick}
          disabled={busy || done}
          title={busy ? t('players.detail.installingShort', 'Ставим...') : actionLabel}
          style={{ outline: 'none' }}
          className={
            'inline-flex items-center justify-center w-9 h-9 rounded-xl text-xs font-semibold ' +
            'transition-colors duration-200 ' +
            (done
              ? 'bg-green-500/15 text-green-300 cursor-default'
              : busy
                ? 'bg-glass text-text-muted cursor-wait'
                : 'bg-accent text-text-on-accent hover:opacity-90')
          }
        >
          {busy
            ? <Loader2 size={13} className="animate-spin" />
            : done
              ? <CheckCircle2 size={13} />
              : <Download size={13} />}
        </button>
      </div>
    </div>
  );
}

interface DeviceSlot {
  labelKey:     string;
  defaultLabel: string;
  keys:  string[];
  icon:  LucideIcon;
}

const DEVICE_SLOTS: DeviceSlot[] = [
  { labelKey: 'mouse',     defaultLabel: 'Mouse',       keys: ['mouse', 'Mouse', 'mice'],                       icon: MousePointer2 },
  { labelKey: 'keyboard',  defaultLabel: 'Keyboard',    keys: ['keyboard', 'Keyboard'],                         icon: Keyboard },
  { labelKey: 'headset',   defaultLabel: 'Headset',     keys: ['headset', 'Headset', 'headphones'],             icon: Headphones },
  { labelKey: 'display',   defaultLabel: 'Display',     keys: ['monitor', 'display', 'Display', 'Monitor'],     icon: Monitor },
  { labelKey: 'surface',   defaultLabel: 'Surface',     keys: ['mousepad', 'surface', 'Mousepad', 'pad'],       icon: RectangleHorizontal },
  { labelKey: 'audio',     defaultLabel: 'Audio Input', keys: ['microphone', 'mic', 'audioInput', 'audio_input'], icon: Mic },
  { labelKey: 'graphics',  defaultLabel: 'Graphics',    keys: ['gpu', 'graphics', 'GPU', 'videoCard'],          icon: CircuitBoard },
  { labelKey: 'processor', defaultLabel: 'Processor',   keys: ['cpu', 'processor', 'CPU'],                      icon: Cpu },
];

function GearCard({
  label, value, icon: Icon,
}: {
  label: string;
  value: string;
  icon: LucideIcon;
}) {
  const empty = !value || value === '-';
  return (
    <div
      className="group relative rounded-2xl p-5 h-[180px] flex flex-col justify-between
                 overflow-hidden border border-transparent transform-gpu
                 shadow-[0_0_30px_-10px_rgba(0,0,0,0.5)]
                 transition-[transform,border-color,box-shadow] duration-300 ease-out
                 hover:border-glass-border-strong hover:shadow-[0_10px_40px_-10px_rgba(0,0,0,0.6)]
                 hover:-translate-y-1 hover:scale-[1.015]"
      style={{ backgroundColor: '#0a0a0c', willChange: 'transform' }}
    >
      {}
      <div
        aria-hidden="true"
        className="absolute inset-0 z-0 opacity-[0.04] pointer-events-none"
        style={{ backgroundImage: 'radial-gradient(circle at center, white 1px, transparent 1px)', backgroundSize: '16px 16px' }}
      />

      {}
      <div className="flex justify-between items-start z-10 relative">
        <span className="bg-black/50 backdrop-blur-md text-white/55 text-[10px] font-black px-3 py-1.5
                         rounded-full uppercase tracking-[0.22em] border border-transparent
                         group-hover:border-accent/40 group-hover:text-white
                         transition-colors duration-300
                         shadow-[inset_0_1px_1px_rgba(255,255,255,0.05)]">
          {label}
        </span>
        <div className="w-8 h-8 rounded-full bg-white/5 border border-transparent
                        flex items-center justify-center backdrop-blur-sm
                        group-hover:bg-accent-soft group-hover:border-accent/40
                        transition-colors duration-300">
          <Icon size={16} className="text-white/45 group-hover:text-accent transition-colors" />
        </div>
      </div>

      {}
      <div className="absolute inset-0 flex items-center justify-center pointer-events-none z-0">
        <motion.div
          animate={{ y: [-4, 4, -4] }}
          transition={{ duration: 4, repeat: Infinity, ease: 'easeInOut' }}
          className="relative w-24 h-24 flex items-center justify-center transform-gpu"
        >
          <Icon
            size={78}
            strokeWidth={1.25}
            className="text-white/15 group-hover:text-white
                       drop-shadow-[0_0_0_rgba(0,0,0,0)]
                       group-hover:drop-shadow-[0_0_18px_rgba(255,255,255,0.18)]
                       transition-[color,filter] duration-500 ease-out"
          />
        </motion.div>
      </div>

      {}
      <div className="z-10 relative mt-auto transition-transform duration-500 group-hover:-translate-y-1">
        <div className="w-8 h-[2px] bg-white/10 mb-3
                        group-hover:bg-accent group-hover:w-16 group-hover:shadow-[0_0_10px_var(--accent)]
                        transition-[background-color,width,box-shadow] duration-500" />
        <h3 className={
          'font-black text-base leading-tight tracking-tight pr-2 truncate ' +
          (empty ? 'text-text-muted/50 italic' : 'text-white drop-shadow-md')
        }>
          {empty ? '-' : value}
        </h3>
      </div>

      {}
      <div className="absolute inset-0 rounded-2xl pointer-events-none border border-transparent group-hover:border-glass-border-strong transition-colors duration-500" />
      <div className="absolute top-0 left-0 right-0 h-px bg-gradient-to-r from-transparent via-white/20 to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-500" />
    </div>
  );
}

function VideoGallery({
  videoIds, activeIdx, onSelect, playing, onPlay, onClose,
}: {
  videoIds: string[];
  activeIdx: number;
  onSelect: (idx: number) => void;
  playing: boolean;
  onPlay:  () => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  if (!videoIds || videoIds.length === 0) {
    return (
      <div className="aspect-video rounded-2xl bg-bg-elevated border border-transparent
                      flex items-center justify-center text-text-muted text-sm">
        <span>-</span>
      </div>
    );
  }
  const currentId = videoIds[activeIdx] ?? videoIds[0];

  return (
    <div className="flex flex-col gap-3 w-full">
      <div className="relative aspect-video rounded-2xl overflow-hidden bg-black border border-transparent
                      shadow-[0_0_40px_-10px_rgba(0,0,0,0.7)] group">
        <AnimatePresence mode="wait">
          {playing ? (
            <motion.div
              key={`iframe-${currentId}`}
              className="absolute inset-0"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.22 }}
            >
              <iframe
                src={`https://www.youtube-nocookie.com/embed/${currentId}?autoplay=1&rel=0&modestbranding=1`}
                className="absolute inset-0 w-full h-full"
                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                allowFullScreen
                title={t('players.detail.youtubeIframeTitle', 'YouTube video')}
              />
              <button
                type="button"
                onClick={onClose}
                className="absolute top-2 right-2 z-10 w-8 h-8 rounded-md
                           bg-black/60 backdrop-blur-md text-white hover:bg-black/85
                           flex items-center justify-center transition-colors"
                title={t('common.close', 'Закрыть')}
              >
                <X size={14} />
              </button>
            </motion.div>
          ) : (
            <motion.button
              key="thumb"
              type="button"
              onClick={onPlay}
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.18 }}
              className="absolute inset-0 cursor-pointer"
            >
              <img
                src={`https://img.youtube.com/vi/${currentId}/maxresdefault.jpg`}
                alt=""
                draggable={false}
                className="absolute inset-0 w-full h-full object-cover transition-transform duration-700 group-hover:scale-105"
                onError={e => {

                  const img = e.currentTarget;
                  if (!img.dataset.fallback) {
                    img.dataset.fallback = '1';
                    img.src = `https://img.youtube.com/vi/${currentId}/mqdefault.jpg`;
                  }
                }}
              />
              <div style={{ height: '32%' }} className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/40 to-transparent" />
              <div className="absolute inset-0 flex items-center justify-center">
                <div className="relative flex items-center justify-center w-20 h-20">
                  <span className="absolute inset-0 rounded-full bg-accent opacity-20 animate-ping" />
                  <span className="absolute inset-0 rounded-full bg-accent/20 blur-md" />
                  <span className="relative w-16 h-16 rounded-full bg-white/10 backdrop-blur-md
                                   border border-transparent flex items-center justify-center shadow-2xl
                                   transition-all group-hover:bg-accent group-hover:border-accent">
                    <Play size={22} className="text-white ml-1 fill-current group-hover:text-text-on-accent" />
                  </span>
                </div>
              </div>
            </motion.button>
          )}
        </AnimatePresence>
      </div>

      {videoIds.length > 1 && (
        <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 gap-3 px-1">
          {videoIds.map((vid, idx) => {
            const active = idx === activeIdx;
            return (
              <button
                key={vid}
                type="button"
                onClick={() => onSelect(idx)}
                className={
                  'relative aspect-video rounded-lg overflow-hidden border transition-all duration-300 ' +
                  (active
                    ? 'border-accent ring-1 ring-accent/40 shadow-[0_0_15px_-5px_var(--accent)]'
                    : 'border-glass-border opacity-55 hover:opacity-100 hover:border-accent/40')
                }
              >
                <img
                  src={`https://img.youtube.com/vi/${vid}/mqdefault.jpg`}
                  alt=""
                  draggable={false}
                  className="w-full h-full object-cover"
                />
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

function readSpec(blob: Record<string, unknown> | null, keys: string[]): string {
  if (!blob) return '';
  for (const k of keys) {
    const v = blob[k];
    if (v == null) continue;

    if (typeof v === 'object') {
      const obj = v as { name?: unknown };
      if (typeof obj.name === 'string' && obj.name.trim().length > 0) {
        return obj.name.trim();
      }
      continue;
    }
    const s = String(v).trim();
    if (s.length > 0) return s;
  }
  return '';
}
