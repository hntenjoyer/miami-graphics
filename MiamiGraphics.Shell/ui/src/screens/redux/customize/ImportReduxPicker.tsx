import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AnimatePresence, motion } from 'framer-motion';
import { Search, PackageOpen, X, Flame, Boxes } from 'lucide-react';
import { BackButton } from '@/components/BackButton';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';
import { bridge } from '@/bridge';
import type { ReduxItem, ComponentInfo } from '@/bridge/types';
import { useReduxVersionsStore } from '@/store/reduxVersionsStore';
import {
  useCustomizeStore,
  type CustomizeComponentName,
} from '@/store/customizeStore';
import { AccentLoader, EASE_DEPTH } from '@/design';
import { GlbViewerModal } from '@/screens/guns/GlbViewerModal';
import { ArmorCard, type ArmorListItem } from '@/screens/armor/ArmorCard';
import type { ArmorLibraryItem } from '@/bridge/types';
import { SlideIntro } from './SlideIntro';

interface Props { forComponent: CustomizeComponentName }

const COMPONENT_LABEL_RU: Record<CustomizeComponentName, string> = {
  minimap:   'минимапа',
  crosshair: 'прицел',
  tracers:   'трейсера',
  bloodfx:   'эффекты',
  timecycle: 'таймциклы',
  armor:     'броник',
  arena:     'арена',
};

export function ImportReduxPicker({ forComponent }: Props) {
  const { t } = useTranslation();
  const goManage           = useCustomizeStore(s => s.goManage);
  const openImportPreview  = useCustomizeStore(s => s.openImportPreview);
  const openArmorLibraryPreview = useCustomizeStore(s => s.openArmorLibraryPreview);
  const activeReduxId      = useCustomizeStore(s => s.activeReduxId);
  const isArmor            = forComponent === 'armor';

  const [items, setItems] = useState<ReduxItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [query, setQuery] = useState('');
  const [server, setServer] = useState<'' | 'majestic' | 'gta5rp'>('');

  const [picks, setPicks] = useState<Record<string, number>>({});
  const [popularFirst, setPopularFirst] = useState(false);
  useEffect(() => {
    let cancelled = false;
    if (typeof bridge.donorPickCounts !== 'function') return;
    bridge.donorPickCounts(forComponent)
      .then(m => { if (!cancelled) setPicks(m ?? {}); })
      .catch(() => {  });
    return () => { cancelled = true; };
  }, [forComponent]);

  const versionsByRedux = useReduxVersionsStore(s => s.byRedux);
  const loadVersions    = useReduxVersionsStore(s => s.load);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    bridge.reduxList(query || undefined, server || undefined)
      .then(list => { if (!cancelled) setItems(list); })
      .catch(() => {  })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [query, server]);

  useEffect(() => {
    for (const it of items) {
      if (it.id === activeReduxId) continue;
      const info = it.components?.[forComponent];
      if (!info?.isFound) continue;
      if (versionsByRedux[it.id] !== undefined) continue;
      void loadVersions(it.id);
    }
  }, [items, activeReduxId, forComponent, versionsByRedux, loadVersions]);

  const isTransferable = forComponent === 'armor' || forComponent === 'arena';

  const importableComponent = (info: ComponentInfo | undefined): boolean => {
    if (!info?.isFound) return false;
    if (!isTransferable && (info.internalPaths?.length ?? 0) === 0) return false;
    return true;
  };

  const candidates = useMemo(() => {
    return items.filter(it => {
      if (it.id === activeReduxId) return false;
      const info = it.components?.[forComponent];
      if (!importableComponent(info)) return false;
      const flags = info!.flags ?? [];
      if (flags.includes('exportBlocked')) return false;
      const flagOk = isTransferable
        ? flags.includes('transferable')
        : (flags.includes('importable') || flags.includes('replaceable'));
      if (!flagOk) return false;

      const shot = it.componentScreenshots?.[forComponent];
      if (!shot || shot.length === 0) return false;

      const versions = versionsByRedux[it.id];
      if (versions === undefined) return true;
      if (versions.length === 0) return true;
      return versions.some(v => importableComponent(v.components?.[forComponent]));
    });
  }, [items, forComponent, activeReduxId, versionsByRedux, isTransferable]);

  const sortedCandidates = useMemo(() => {
    if (!popularFirst) return candidates;
    return [...candidates].sort((a, b) =>
      (picks[b.id] ?? 0) - (picks[a.id] ?? 0)
      || (a.name || a.id).localeCompare(b.name || b.id));
  }, [candidates, popularFirst, picks]);

  const onPick = (donor: ReduxItem) => {
    openImportPreview(forComponent, donor.id);
  };

  const [libraryArmors, setLibraryArmors] = useState<ArmorLibraryItem[]>([]);
  useEffect(() => {
    if (!isArmor) return;
    let cancelled = false;
    bridge.armorLibraryList()
      .then(list => { if (!cancelled) setLibraryArmors(list); })
      .catch(() => {  });
    return () => { cancelled = true; };
  }, [isArmor]);

  const armorItems = useMemo<ArmorListItem[]>(() => {
    if (!isArmor) return [];
    const out: ArmorListItem[] = [];
    for (const r of candidates) {
      if (r.armorStandaloneInstallHidden) continue;
      out.push({
        id: r.id, kind: 'redux', name: r.name || r.id, author: r.author || '',
        glbUrl: r.components?.armor?.glbUrl ?? null,
        previewUrl: r.componentScreenshots?.armor ?? null,
        downloadCount: r.downloadCount ?? 0, installHidden: false,
        supportedServers: r.supportedServers ?? [],
        hasMale: true, hasFemale: true,
      });
    }
    for (const a of libraryArmors) {
      out.push({
        id: a.id, kind: 'library', name: a.name || a.id, author: a.author || '',
        glbUrl: a.glbUrl || null, previewUrl: a.previewUrl || null,
        downloadCount: a.downloadCount ?? 0, installHidden: false,
        supportedServers: Array.isArray(a.supportedServers) ? a.supportedServers : [],
        hasMale: a.hasMale ?? true, hasFemale: a.hasFemale ?? true,
      });
    }
    const q = query.trim().toLowerCase();
    let res = out;
    if (server) res = res.filter(i => i.supportedServers.includes(server));
    if (q) res = res.filter(i => i.name.toLowerCase().includes(q) || i.author.toLowerCase().includes(q));
    return res;
  }, [isArmor, candidates, libraryArmors, query, server]);

  const sortedArmorItems = useMemo(() => {
    if (!popularFirst) return armorItems;
    return [...armorItems].sort((a, b) => (picks[b.id] ?? 0) - (picks[a.id] ?? 0));
  }, [armorItems, popularFirst, picks]);

  const onPickArmor = (it: ArmorListItem) => {
    if (it.kind === 'library') {
      openArmorLibraryPreview({ id: it.id, name: it.name, previewUrl: it.previewUrl, glbUrl: it.glbUrl });
    } else {
      openImportPreview('armor', it.id);
    }
  };

  const draftArmor = useCustomizeStore(s => s.draft?.armor ?? null);
  const selectedArmorId =
    draftArmor?.kind === 'armorLibrary' ? draftArmor.armorLibraryId
    : draftArmor?.kind === 'import'     ? draftArmor.donorReduxId
    : null;

  const [armorViewer, setArmorViewer] = useState<{ glbUrl: string | null; name: string } | null>(null);
  const armorViewerGlbUrl = armorViewer?.glbUrl ?? null;

  return (
    <SlideIntro
      resetKey={`import-picker:${forComponent}`}
      title={t('customize.import.pickerSlideTitle', 'Из какой сборки забрать компонент?')}
      subtitle={
        <span className="inline-flex items-center gap-2">
          <PackageOpen size={12} className="text-accent" />
          <span className="text-accent font-bold tracking-wider uppercase text-xs">
            {t(`customize.componentNamesLower.${forComponent}`, COMPONENT_LABEL_RU[forComponent])}
          </span>
        </span>
      }
    >
    <div className="relative h-full flex flex-col">
      <header className="px-8 pt-6 pb-3 shrink-0">
        <div className="max-w-[1280px] 2xl:max-w-[1600px] mx-auto flex items-center gap-3 flex-wrap">
          <BackButton onClick={goManage} label={t('customize.back')} />
          <div className="group relative flex-1 min-w-[220px] max-w-md">
            <span
              aria-hidden
              className="pointer-events-none absolute inset-x-4 top-0 h-px
                         bg-gradient-to-r from-transparent via-white/25 to-transparent
                         group-focus-within:via-[color-mix(in_srgb,var(--accent)_70%,transparent)]
                         transition-colors duration-300"
            />
            <Search
              size={14}
              className="absolute left-4 top-1/2 -translate-y-1/2 text-text-muted
                         group-focus-within:text-accent transition-colors duration-300"
            />
            <input
              type="text"
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder={t('customize.import.searchPlaceholder')}
              className="w-full h-11 pl-10 pr-10 rounded-2xl
                         bg-glass-strong border border-glass-border
                         backdrop-blur-glass text-sm text-text-primary
                         placeholder:text-text-muted/70
                         shadow-[inset_0_1px_0_rgba(255,255,255,0.06)]
                         outline-none hover:border-white/20
                         focus:border-accent/60 focus:shadow-glow-accent
                         transition-[border-color,box-shadow] duration-300 ease-depth"
            />
            {query && (
              <button
                type="button"
                onClick={() => setQuery('')}
                aria-label={t('customize.import.clearSearch', 'Очистить поиск')}
                style={{ outline: 'none' }}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 w-6 h-6 rounded-lg
                           flex items-center justify-center text-text-muted
                           hover:text-text-primary hover:bg-white/10 transition-colors"
              >
                <X size={12} />
              </button>
            )}
          </div>
          <ServerToggle value={server} onChange={setServer} />
          <button
            type="button"
            onClick={() => setPopularFirst(p => !p)}
            title={t('customize.import.popularHint', 'Сначала те, кого чаще выбирают донором')}
            className={
              'inline-flex items-center gap-2 px-3 py-2 rounded-xl text-xs uppercase tracking-wider border ' +
              'transition-[border-color,background-color,box-shadow,color] duration-300 ease-depth ' +
              (popularFirst
                ? 'bg-accent-soft text-accent border-[color-mix(in_srgb,var(--accent)_60%,transparent)] ' +
                  'shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_30%,transparent),0_6px_18px_-6px_color-mix(in_srgb,var(--accent)_55%,transparent)]'
                : 'border-transparent text-text-muted hover:border-[color-mix(in_srgb,var(--accent)_35%,transparent)] hover:text-text-primary')
            }
          >
            <Flame size={13} strokeWidth={2} />
            <span>{t('customize.import.popular', 'Популярные')}</span>
          </button>
          <span
            className="ml-auto inline-flex items-center gap-2 h-9 px-3 rounded-xl
                       bg-glass-strong border border-glass-border backdrop-blur-glass
                       text-text-secondary"
            title={t('customize.import.donorCountHint', 'Доступно доноров с этим компонентом')}
          >
            <Boxes size={13} className="text-accent" strokeWidth={2} />
            <span className="text-[11px] uppercase tracking-[0.16em] font-bold">
              {t('customize.import.donorCount', 'Доноров')}
            </span>
            <span className="text-sm font-bold tabular-nums text-text-primary">
              {loading ? '…' : (isArmor ? armorItems.length : candidates.length)}
            </span>
          </span>
        </div>
      </header>

      {}
      <div className="flex-1 overflow-y-auto px-8 pb-12">
        <div className="max-w-[1280px] 2xl:max-w-[1600px] mx-auto flex flex-col gap-6 pt-2">

          {}
          {loading ? (
            <div className="flex items-center justify-center py-16 gap-3 text-sm text-text-muted">
              <AccentLoader size={20} />
              <span>{t('customize.import.loading')}</span>
            </div>
          ) : isArmor ? (
            armorItems.length === 0 ? (
              <EmptyDonorList />
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-4">
                {sortedArmorItems.map((it, i) => (
                  <motion.div
                    key={`${it.kind}:${it.id}`}
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ duration: 0.32, delay: 0.04 + i * 0.02, ease: EASE_DEPTH }}
                  >
                    <ArmorCard
                      item={it}
                      isInstalled={false}
                      installingId={null}
                      onView3D={() => setArmorViewer({ glbUrl: it.glbUrl, name: it.name })}
                      onInstall={() => onPickArmor(it)}
                      pick={{ selected: selectedArmorId === it.id, onPick: () => onPickArmor(it) }}
                    />
                  </motion.div>
                ))}
              </div>
            )
          ) : candidates.length === 0 ? (
            <EmptyDonorList />
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-4">
              {sortedCandidates.map((it, i) => (
                <motion.div
                  key={it.id}
                  initial={{ opacity: 0, y: 8 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.32, delay: 0.04 + i * 0.025, ease: EASE_DEPTH }}
                  className="flex flex-col gap-2"
                >
                  <DonorCard item={it} onPick={() => onPick(it)} forComponent={forComponent} pickCount={picks[it.id] ?? 0} />
                </motion.div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>

    {}
    <AnimatePresence>
      {armorViewer && (
        <GlbViewerModal
          glbUrl={armorViewerGlbUrl}
          title={t('customize.armorViewerDonorTitle', 'Броник «{{name}}»', {
            name: armorViewer.name,
          })}
          subjectKind="armor"
          onClose={() => setArmorViewer(null)}
        />
      )}
    </AnimatePresence>
    </SlideIntro>
  );
}

function EmptyDonorList() {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col items-center justify-center py-20 gap-3 text-text-muted">
      <div className="w-14 h-14 rounded-2xl bg-glass-strong
                      flex items-center justify-center text-text-muted">
        <PackageOpen size={22} strokeWidth={1.6} />
      </div>
      <p className="text-sm text-center max-w-sm">{t('customize.import.empty')}</p>
    </div>
  );
}

function ServerToggle({
  value, onChange,
}: { value: '' | 'majestic' | 'gta5rp'; onChange: (next: '' | 'majestic' | 'gta5rp') => void }) {
  return (
    <div className="flex items-center gap-2">
      <ServerChip
        active={value === 'gta5rp'}
        logo={gta5rpLogo}
        alt="GTA5RP"
        onClick={() => onChange(value === 'gta5rp' ? '' : 'gta5rp')}
      />
      <ServerChip
        active={value === 'majestic'}
        logo={majesticLogo}
        alt="Majestic"
        onClick={() => onChange(value === 'majestic' ? '' : 'majestic')}
      />
    </div>
  );
}

function ServerChip({
  active, logo, alt, onClick,
}: { active: boolean; logo: string; alt: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={alt}
      className={
        'inline-flex items-center gap-2 px-3 py-2 rounded-xl text-xs uppercase tracking-wider border ' +
        'transition-[border-color,background-color,box-shadow,color] duration-300 ease-depth ' +
        (active
          ? 'bg-accent-soft text-accent border-[color-mix(in_srgb,var(--accent)_60%,transparent)] ' +
            'shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_30%,transparent),0_6px_18px_-6px_color-mix(in_srgb,var(--accent)_55%,transparent)]'
          : 'border-transparent text-text-muted hover:border-[color-mix(in_srgb,var(--accent)_35%,transparent)] hover:text-text-primary')
      }
    >
      <img src={logo} alt={alt} className="w-4 h-4 object-contain" />
      <span>{alt}</span>
    </button>
  );
}

export function DonorCard({ item, onPick, forComponent, pickCount = 0 }: {
  item: ReduxItem;
  onPick: () => void;
  forComponent: CustomizeComponentName;
  pickCount?: number;
}) {
  const { t } = useTranslation();
  const showsMajestic = item.supportedServers.includes('majestic');
  const showsGta5rp   = item.supportedServers.includes('gta5rp');

  const componentShot = item.componentScreenshots?.[forComponent];
  const fallback      = item.galleryUrls?.[0] ?? item.previewUrl;
  const tileImage     = componentShot || fallback;

  const useContain = !!componentShot;
  return (
    <button
      type="button"
      onClick={onPick}
      className="group relative h-48 w-full overflow-hidden rounded-2xl
                 bg-bg-elevated text-left
                 transform-gpu will-change-transform
                 border border-transparent hover:border-[color-mix(in_srgb,var(--accent)_60%,transparent)]
                 shadow-z2 hover:shadow-glow-accent
                 transition-[transform,box-shadow,border-color] duration-[500ms] ease-smooth
                 hover:-translate-y-1
                 focus-visible:outline-none focus-visible:shadow-glow-accent focus-visible:border-[color-mix(in_srgb,var(--accent)_60%,transparent)]"
    >
      {}
      {tileImage ? useContain ? (
        <>
          <img
            src={tileImage}
            alt=""
            aria-hidden="true"
            draggable={false}
            className="absolute inset-0 w-full h-full object-cover"
            style={{
              filter: 'blur(20px) saturate(115%) brightness(0.6)',
              transform: 'scale(1.18)',
            }}
          />
          <img
            src={tileImage}
            alt=""
            aria-hidden="true"
            draggable={false}
            loading="lazy"
            className="absolute inset-0 w-full h-full object-contain object-center select-none
                       transform-gpu transition-transform duration-[1100ms] ease-smooth
                       group-hover:scale-[1.03]"
            style={{ willChange: 'transform', backfaceVisibility: 'hidden' }}
          />
        </>
      ) : (
        <img
          src={tileImage}
          alt=""
          aria-hidden="true"
          draggable={false}
          loading="lazy"
          className="absolute inset-0 w-full h-full object-cover object-center select-none
                     transform-gpu transition-transform duration-[1100ms] ease-smooth
                     group-hover:scale-[1.04]"
          style={{ willChange: 'transform', backfaceVisibility: 'hidden' }}
        />
      ) : (
        <div className="absolute inset-0 bg-gradient-to-br from-bg-elevated to-bg-base" />
      )}

      {}
      <div style={{ height: '32%' }} className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/40 to-transparent pointer-events-none opacity-0 group-hover:opacity-100 transition-opacity duration-300" />

      <div className="absolute top-3 right-3 z-10 flex items-center gap-2">
        {showsGta5rp && <Badge logo={gta5rpLogo} alt="GTA5RP" />}
        {showsMajestic && <Badge logo={majesticLogo} alt="Majestic" />}
      </div>

      {pickCount > 0 && (
        <div
          className="absolute top-3 left-3 z-10 inline-flex items-center gap-1.5
                     px-2 py-1 rounded-md bg-base-70 backdrop-blur-sm
                     text-[11px] font-bold tabular-nums text-white/90"
          title={t('customize.import.pickCountTitle', { n: pickCount, defaultValue: 'Выбирали донором: {{n}}' })}
        >
          <Flame size={11} className="text-accent" strokeWidth={2.2} />
          <span>{pickCount}</span>
        </div>
      )}

      <div className="absolute bottom-0 left-0 right-0 p-4 z-10 opacity-0 group-hover:opacity-100 transition-opacity duration-300">
        <div className="font-display font-bold text-white text-lg uppercase tracking-wide truncate
                        drop-shadow-[0_2px_8px_rgba(0,0,0,0.6)]">
          {item.name || item.id}
        </div>
        {item.author && (
          <div className="text-xs text-white/75 truncate mt-0.5">
            {t('customize.import.byAuthor', { author: item.author, defaultValue: 'by {{author}}' })}
          </div>
        )}
      </div>
    </button>
  );
}

function Badge({ logo, alt }: { logo: string; alt: string }) {
  return (
    <div className="px-1.5 py-1 rounded-md bg-base-70 backdrop-blur-sm flex items-center" title={alt}>
      <img src={logo} alt={alt} className="w-3.5 h-3.5 object-contain" />
    </div>
  );
}
