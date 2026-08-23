import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AnimatePresence, motion } from 'framer-motion';
import { X, Search, Boxes, CloudSun, Eye, Download } from 'lucide-react';
import { bridge } from '@/bridge';
import type { ReduxItem, ComponentInfo } from '@/bridge/types';
import { useReduxVersionsStore } from '@/store/reduxVersionsStore';
import { useNavStore } from '@/store/navStore';
import { BackButton } from '@/components/BackButton';
import { AccentLoader, EASE_DEPTH, ENV_CARD_H } from '@/design';
import { ensureBackupOrGate } from '@/store/installGate';
import { Toast } from '@/components/Toast';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';

function isTimecycleDonor(info: ComponentInfo | undefined): boolean {
  if (!info?.isFound) return false;
  if ((info.internalPaths?.length ?? 0) === 0) return false;
  return true;
}

const FEATURED: ReadonlyArray<{ id: string; expandVersions?: boolean }> = [
  { id: 'deku_v1' },
  { id: 'invicta' },
  { id: 'glow' },
  { id: 'sadovskyy', expandVersions: true },
  { id: 'kiroki' },
  { id: 'homixide', expandVersions: true },
  { id: 'zdclaguna' },
  { id: 'gucci_v2' },
  { id: 'allegri_v3' },
  { id: 'pain_v1' },
  { id: 'ashes' },
];
const ALLOWED_REDUX_IDS: ReadonlySet<string> = new Set(FEATURED.map(f => f.id));
const EXPAND_VERSION_IDS: ReadonlySet<string> = new Set(
  FEATURED.filter(f => f.expandVersions).map(f => f.id));

interface TcEntry {
  item: ReduxItem;
  versionId: string | null;
  versionLabel: string | null;
  slot: number | null;
}
const entryKey = (e: TcEntry) => `${e.item.id}:${e.versionId ?? 'base'}`;

const TC_INSTALL_NS = 'timecycle_install';
const installKeyFor = (reduxId: string, slot: number | null) =>
  slot !== null ? `${reduxId}#${slot}` : reduxId;
const installKey = (e: TcEntry) => installKeyFor(e.item.id, e.slot);

const TC_SHOT_BASE = 'https://miamigraphicsstorage.uk/environment/timecycles';
const TC_SHOT_KEYS: ReadonlySet<string> = new Set([
  'allegri_v3', 'deku_v1', 'glow', 'invicta', 'kiroki', 'zdclaguna',
  'gucci_v2', 'sadovskyy_v1', 'sadovskyy_v2', 'homixide_v1', 'homixide_v2',
  'pain_v1', 'ashes',
]);
function customShotFor(e: TcEntry): string | null {
  const key = e.slot !== null ? `${e.item.id}_v${e.slot}` : e.item.id;
  return TC_SHOT_KEYS.has(key) ? `${TC_SHOT_BASE}/${key}.webp` : null;
}

export function TimecyclesScreen() {
  const { t } = useTranslation();
  const navigate = useNavStore(s => s.requestNavigate);

  const [items, setItems] = useState<ReduxItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [query, setQuery] = useState('');
  const [picks, setPicks] = useState<Record<string, number>>({});
  const [lightbox, setLightbox] = useState<{ url: string; name: string; donorId: string; versionId: string | null; installKey: string } | null>(null);
  const [installingId, setInstallingId] = useState<string | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error' | 'info'; message: string } | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    bridge.reduxList(query || undefined, undefined)
      .then(list => { if (!cancelled) setItems(list); })
      .catch(() => {  })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [query]);

  useEffect(() => {
    let cancelled = false;
    if (typeof bridge.donorPickCounts !== 'function') return;
    bridge.donorPickCounts(TC_INSTALL_NS)
      .then(m => { if (!cancelled) setPicks(m ?? {}); })
      .catch(() => {  });
    return () => { cancelled = true; };
  }, []);

  const donors = useMemo(() => {
    return items.filter(it => {
      if (!ALLOWED_REDUX_IDS.has(it.id)) return false;
      const info = it.components?.timecycle;
      if (!isTimecycleDonor(info)) return false;
      const flags = info!.flags ?? [];
      if (flags.includes('exportBlocked')) return false;
      if (!(flags.includes('importable') || flags.includes('replaceable'))) return false;
      const shot = it.componentScreenshots?.timecycle;
      if (!shot || shot.length === 0) return false;
      return true;
    });
  }, [items]);

  const versionsByRedux = useReduxVersionsStore(s => s.byRedux);
  const loadVersions    = useReduxVersionsStore(s => s.load);
  useEffect(() => {
    for (const it of donors) {
      if (!EXPAND_VERSION_IDS.has(it.id)) continue;
      if (versionsByRedux[it.id] !== undefined) continue;
      void loadVersions(it.id);
    }
  }, [donors, versionsByRedux, loadVersions]);

  const entries = useMemo<TcEntry[]>(() => {
    const out: TcEntry[] = [];
    for (const it of donors) {
      const versions = EXPAND_VERSION_IDS.has(it.id) ? (versionsByRedux[it.id] ?? []) : [];
      const tcVersions = versions
        .filter(v => isTimecycleDonor(v.components?.timecycle))
        .sort((a, b) => a.slot - b.slot);
      if (tcVersions.length > 1) {
        for (const v of tcVersions)
          out.push({ item: it, versionId: v.id, versionLabel: v.label || `V${v.slot}`, slot: v.slot });
      } else {
        out.push({ item: it, versionId: null, versionLabel: null, slot: null });
      }
    }
    return out;
  }, [donors, versionsByRedux]);

  const openDonor = (e: TcEntry) => {
    const it = e.item;
    const base = it.name || it.id;
    const name = e.versionLabel ? `${base} · ${e.versionLabel}` : base;
    const url = customShotFor(e) || it.componentScreenshots?.timecycle;
    if (url) setLightbox({ url, name, donorId: it.id, versionId: e.versionId, installKey: installKey(e) });
  };

  const dispatchInstall = async (donorId: string, name: string, versionId: string | null, ik: string) => {
    if (installingId) return;
    if (!ensureBackupOrGate()) return;
    setInstallingId(`${donorId}:${versionId ?? 'base'}`);
    try {
      const r = await bridge.timecycleInstall(donorId, name, versionId);
      if (r.success) {
        setToast({ tone: 'success', message: t('environment.installedToast', 'Таймцикл «{{name}}» установлен.', { name }) });
        void bridge.activityLog('timecycle_install', `таймцикл «${name}»`);
        setPicks(p => ({ ...p, [ik]: (p[ik] ?? 0) + 1 }));
        if (typeof bridge.donorPickIncrement === 'function')
          void bridge.donorPickIncrement(ik, TC_INSTALL_NS).catch(() => {  });
        setLightbox(null);
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setInstallingId(null);
    }
  };

  return (
    <div className="relative h-full flex flex-col">
      <header className="px-8 pt-6 pb-3 shrink-0">
        <div className="max-w-[1280px] mx-auto flex items-center gap-3 flex-wrap">
          <BackButton onClick={() => navigate('environment')} label={t('environment.back', 'К окружению')} />
          <div className="min-w-0">
            <h1 className="text-[20px] font-semibold tracking-tight text-text-primary flex items-center gap-2">
              <CloudSun size={18} className="text-accent" />
              {t('environment.timecycles', 'Небо')}
            </h1>
            <p className="text-[12px] text-text-muted">
              {t('environment.timecyclesSub', 'Небо, погода и атмосфера')}
            </p>
          </div>

          <label
            className="group relative flex items-center flex-1 min-w-[180px] max-w-[300px] h-10 ml-2 rounded-xl
                       bg-white/[0.04] border border-white/[0.06]
                       transition-[background-color,border-color] duration-300 ease-smooth
                       hover:bg-white/[0.07] hover:border-white/[0.12]
                       focus-within:bg-white/[0.07] focus-within:border-white/[0.30]"
          >
            <Search size={14} strokeWidth={2} className="absolute left-3.5 text-text-muted pointer-events-none" />
            <input
              type="text"
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder={t('environment.searchPlaceholder', 'Поиск по названию')}
              className="w-full h-full pl-10 pr-9 bg-transparent rounded-xl text-[13px]
                         text-text-primary placeholder:text-text-muted outline-none"
            />
            {query && (
              <button
                type="button"
                onClick={() => setQuery('')}
                aria-label={t('environment.searchClear', 'Очистить поиск')}
                style={{ outline: 'none' }}
                className="absolute right-1.5 w-7 h-7 rounded-md flex items-center justify-center
                           text-text-muted hover:text-text-primary hover:bg-white/[0.10] transition-colors"
              >
                <X size={12} />
              </button>
            )}
          </label>

          <span
            className="ml-auto inline-flex items-center gap-2 h-9 px-3 rounded-xl
                       bg-white/[0.03] border border-white/[0.07] text-text-muted"
          >
            <Boxes size={13} className="text-accent" strokeWidth={2} />
            <span className="text-[11px] uppercase tracking-[0.16em] font-bold">
              {t('environment.count', 'Доноров')}
            </span>
            <span className="text-sm font-bold tabular-nums text-text-primary">
              {loading ? '…' : entries.length}
            </span>
          </span>
        </div>
      </header>

      <div className="flex-1 overflow-y-auto px-8 pb-12">
        <div className="max-w-[1280px] mx-auto pt-2">
          {loading ? (
            <div className="flex items-center justify-center py-16 gap-3 text-sm text-text-muted">
              <AccentLoader size={20} />
              <span>{t('environment.loading', 'Загрузка…')}</span>
            </div>
          ) : entries.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-20 gap-3 text-text-muted">
              <div className="w-14 h-14 rounded-2xl bg-white/[0.04] flex items-center justify-center">
                <CloudSun size={24} strokeWidth={1.6} />
              </div>
              <p className="text-sm text-center max-w-sm">
                {t('environment.empty', 'Пока нет редуксов с таймциклом.')}
              </p>
            </div>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 2xl:grid-cols-3 gap-5">
              {entries.map(e => (
                <TimecycleCard
                  key={entryKey(e)}
                  item={e.item}
                  versionLabel={e.versionLabel}
                  customShot={customShotFor(e)}
                  installCount={picks[installKey(e)] ?? 0}
                  onClick={() => openDonor(e)}
                />
              ))}
            </div>
          )}
        </div>
      </div>

      <AnimatePresence>
        {lightbox && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-50 flex items-center justify-center bg-black/85 backdrop-blur-sm p-6"
            onClick={() => setLightbox(null)}
          >
            <button
              type="button"
              onClick={() => setLightbox(null)}
              aria-label={t('environment.close', 'Закрыть')}
              className="absolute top-4 right-4 text-white/80 hover:text-white transition-colors"
              style={{ outline: 'none' }}
            >
              <X size={26} />
            </button>
            <motion.img
              key={lightbox.url}
              src={lightbox.url}
              alt={lightbox.name}
              onClick={e => e.stopPropagation()}
              initial={{ opacity: 0, scale: 0.9 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.9 }}
              transition={{ duration: 0.28, ease: EASE_DEPTH }}
              className="max-w-[92vw] max-h-[88vh] object-contain rounded-xl shadow-2xl"
            />
            <div
              onClick={e => e.stopPropagation()}
              className="absolute bottom-5 left-1/2 -translate-x-1/2 flex items-center gap-3
                         px-4 py-2 rounded-xl bg-white/10 backdrop-blur-sm"
            >
              <span className="text-white text-sm font-semibold">{lightbox.name}</span>
              <button
                type="button"
                disabled={installingId !== null}
                onClick={() => void dispatchInstall(lightbox.donorId, lightbox.name, lightbox.versionId, lightbox.installKey)}
                className="inline-flex items-center gap-2 h-9 px-4 rounded-lg
                           bg-bg-elevated/70 text-text-primary border border-white/[0.10]
                           hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)] hover:shadow-glow-accent
                           text-[12px] font-bold uppercase tracking-wider transition-all
                           disabled:opacity-50 disabled:cursor-not-allowed"
                style={{ outline: 'none' }}
              >
                {installingId === `${lightbox.donorId}:${lightbox.versionId ?? 'base'}`
                  ? <AccentLoader size={12} />
                  : <Download size={12} className="text-accent" />}
                {installingId === `${lightbox.donorId}:${lightbox.versionId ?? 'base'}`
                  ? t('environment.installing', 'Устанавливаю…')
                  : t('environment.install', 'Установить')}
              </button>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 3500}
      />
    </div>
  );
}

function TimecycleCard({
  item, versionLabel, customShot, installCount, onClick,
}: {
  item: ReduxItem;
  versionLabel: string | null;
  customShot: string | null;
  installCount: number;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  const shot = item.componentScreenshots?.timecycle;
  const fallback = item.galleryUrls?.[0] ?? item.previewUrl;
  const img = customShot || shot || fallback;
  const showsMajestic = item.supportedServers.includes('majestic');
  const showsGta5rp = item.supportedServers.includes('gta5rp');

  return (
    <button
      type="button"
      onClick={onClick}
      className={`group relative w-full ${ENV_CARD_H} overflow-hidden rounded-2xl bg-bg-elevated text-left
                 transform-gpu will-change-transform
                 border border-transparent hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                 shadow-z2 hover:shadow-glow-accent
                 transition-[transform,box-shadow,border-color] duration-500 ease-smooth
                 hover:-translate-y-1
                 focus-visible:outline-none focus-visible:shadow-glow-accent
                 focus-visible:border-[color-mix(in_srgb,var(--accent)_60%,transparent)]`}
    >
      {img ? (
        <img
          src={img}
          alt=""
          aria-hidden="true"
          draggable={false}
          loading="lazy"
          className="absolute inset-0 w-full h-full object-cover select-none
                     transform-gpu transition-transform duration-[1100ms] ease-smooth
                     group-hover:scale-[1.05]"
          style={{ backfaceVisibility: 'hidden' }}
        />
      ) : (
        <div className="absolute inset-0 bg-gradient-to-br from-bg-elevated to-bg-base flex items-center justify-center">
          <CloudSun size={48} strokeWidth={1.2} className="text-white/15" />
        </div>
      )}

      <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/10 to-black/20 pointer-events-none" />

      <div className="absolute top-3 right-3 z-10 flex items-center gap-2">
        {showsGta5rp && <TileBadge logo={gta5rpLogo} alt="GTA5RP" />}
        {showsMajestic && <TileBadge logo={majesticLogo} alt="Majestic" />}
      </div>

      {installCount > 0 && (
        <div
          className="absolute top-3 left-3 z-10 inline-flex items-center gap-1.5
                     px-2 py-1 rounded-md bg-base-70 backdrop-blur-sm
                     text-[11px] font-bold tabular-nums text-white/90"
          title={t('environment.installCountTitle', { defaultValue: 'Установок: {{n}}', n: installCount })}
        >
          <Download size={11} className="text-accent" strokeWidth={2.2} />
          <span>{installCount}</span>
        </div>
      )}

      <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
        <span
          className="w-14 h-14 rounded-full bg-black/45 backdrop-blur-md border border-white/25
                     flex items-center justify-center shadow-2xl
                     opacity-0 scale-90 group-hover:opacity-100 group-hover:scale-100
                     group-focus-visible:opacity-100 group-focus-visible:scale-100
                     transition-[opacity,transform,background-color,border-color] duration-300
                     group-hover:bg-accent group-hover:border-accent"
        >
          <Eye size={20} className="text-white group-hover:text-text-on-accent" />
        </span>
      </div>

      <div className="absolute inset-x-0 bottom-0 p-4 z-10">
        {versionLabel && (
          <div className="mb-1.5">
            <span className="inline-flex items-center px-2 py-0.5 rounded-md
                             bg-accent-soft text-accent
                             text-[10px] font-bold uppercase tracking-[0.14em]">
              {versionLabel}
            </span>
          </div>
        )}
        <div className="font-display font-bold text-white text-base uppercase tracking-wide truncate
                        drop-shadow-[0_2px_8px_rgba(0,0,0,0.6)]">
          {item.name || item.id}
        </div>
        {item.author && (
          <div className="text-xs text-white/75 truncate mt-0.5">by {item.author}</div>
        )}
      </div>
    </button>
  );
}

function TileBadge({ logo, alt }: { logo: string; alt: string }) {
  return (
    <div className="px-1.5 py-1 rounded-md bg-base-70 backdrop-blur-sm flex items-center" title={alt}>
      <img src={logo} alt={alt} className="w-3.5 h-3.5 object-contain" />
    </div>
  );
}
