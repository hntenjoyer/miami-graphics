import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { Shield, Sparkles, Download as DownloadIcon } from 'lucide-react';
import { clsx } from 'clsx';
import { GlassPanel, GlassDropdown, type GlassDropdownOption } from '@/design';
import { ScreenHero } from '@/screens/ScreenHero';
import { Toast } from '@/components/Toast';
import { useReduxStore } from '@/store/reduxStore';
import { bridge } from '@/bridge';
import { ensureBackupOrGate } from '@/store/installGate';
import { useDirtyConfirmStore } from '@/store/dirtyConfirmStore';
import { GlbViewerModal } from '../guns/GlbViewerModal';
import { ArmorCard, type ArmorListItem } from './ArmorCard';
import { ArmorReplaceConfirmModal } from './ArmorReplaceConfirmModal';
import type { CurrentArmorInfo, ArmorLibraryItem } from '@/bridge/types';
import { getArmorLibraryCache, setArmorLibraryCache } from '@/store/armorLibraryCache';
import gta5rpLogo from '@/assets/logo/gta5rp.svg';
import majesticLogo from '@/assets/logo/majesticlogo.png';
import { shuffled } from '@/utils/shuffle';

type ArmorSort = 'default' | 'downloads';

let cachedArmorOrder: { signature: string; entries: ArmorListItem[] } | null = null;

interface Props {
  onOpenRedux: (reduxId: string) => void;
}

export function ArmorScreen({ onOpenRedux }: Props) {
  const { t } = useTranslation();
  const reduxItems = useReduxStore(s => s.items);
  const loading    = useReduxStore(s => s.loading);
  const load       = useReduxStore(s => s.load);

  const [currentArmorId, setCurrentArmorId] = useState<string | null>(null);
  const refreshCurrentArmor = async () => {
    try {
      const info = await bridge.getCurrentArmorInfo();
      setCurrentArmorId(info?.id ?? null);
    } catch { setCurrentArmorId(null); }
  };
  useEffect(() => { void refreshCurrentArmor(); }, []);

  useEffect(() => {
    if (reduxItems.length === 0 && !loading) void load();

  }, [load]);

  const [libraryItems, setLibraryItems] = useState<ArmorLibraryItem[]>(() => getArmorLibraryCache());
  useEffect(() => {
    let alive = true;
    bridge.armorLibraryList()
      .then(rows => {
        if (!alive) return;
        const next = rows ?? [];
        setArmorLibraryCache(next);
        setLibraryItems(next);
      })
      .catch(e => { console.warn('[armor.libraryList] fail:', e); });
    return () => { alive = false; };
  }, []);

  const [search, setSearch] = useState('');
  const [server, setServer] = useState<'' | 'majestic' | 'gta5rp'>('');
  const [sort, setSort] = useState<ArmorSort>('default');
  const [previewItem, setPreviewItem] = useState<ArmorListItem | null>(null);
  const [installingId, setInstallingId] = useState<string | null>(null);

  const [replacePrompt, setReplacePrompt] = useState<{
    current:  CurrentArmorInfo;
    incoming: ArmorListItem;
  } | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error' | 'info'; message: string } | null>(null);

  const handleInstall = async (item: ArmorListItem) => {
    if (installingId || replacePrompt) return;

    if (item.installHidden) return;
    let current: CurrentArmorInfo | null = null;
    try { current = await bridge.getCurrentArmorInfo(); }
    catch (e) { console.warn('[armor.install] preflight failed:', e); }

    const sameSource = current !== null && current.id === item.id;
    if (current && !sameSource) {
      setReplacePrompt({ current, incoming: item });
      return;
    }
    void dispatchInstall(item);
  };

  const dispatchInstall = async (item: ArmorListItem, force = false, confirmWipe = false) => {

    if (!ensureBackupOrGate()) return;
    setInstallingId(item.id);
    try {

      const r = item.kind === 'library'
        ? await bridge.armorLibraryInstall(item.id, true, force, confirmWipe)
        : await bridge.armorInstallStandalone(item.id, undefined, force, confirmWipe);
      if (r.success) {
        setToast({ tone: 'success', message: t('armor.installToastDone', { name: item.name }) });
        void bridge.activityLog('armor_install', `броню «${item.name}»`);
        void refreshCurrentArmor();
      } else if (r.errorMessage === 'DIRTY_FILES_NEED_CONFIRM') {
        useDirtyConfirmStore.getState().open({
          title: t('armor.dirtyTitle'),
          message: t('armor.dirtyMessage'),
          cancelLabel: t('armor.dirtyCancel'),
          actions: [{
            label: t('armor.dirtyConfirm'),
            kind: 'neutral',
            run: () => dispatchInstall(item, false, true),
          }],
        });
      } else if (r.errorMessage) {
        setToast({ tone: 'error', message: r.errorMessage });
      }
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setInstallingId(null);
    }
  };

  const handleReplaceKeep = () => {
    setReplacePrompt(null);
    setToast({ tone: 'info', message: t('armor.replaceToastKeep') });
  };

  const handleReplaceInstall = () => {
    if (!replacePrompt) return;
    const item = replacePrompt.incoming;
    setReplacePrompt(null);
    void dispatchInstall(item);
  };

  const items = useMemo<ArmorListItem[]>(() => {
    const out: ArmorListItem[] = [];
    for (const r of reduxItems) {
      const armor = r.components?.armor;
      if (!armor?.isFound) continue;

      if (r.armorStandaloneInstallHidden) continue;
      out.push({
        id: r.id,
        kind: 'redux',
        name: r.name || r.id,
        author: r.author || '',
        glbUrl: armor.glbUrl ?? null,
        previewUrl: r.componentScreenshots?.armor ?? null,
        downloadCount: r.downloadCount ?? 0,
        installHidden: false,

        supportedServers: r.supportedServers ?? [],
        hasMale: true,
        hasFemale: true,
      });
    }

    for (const a of libraryItems) {
      out.push({
        id:           a.id,
        kind:         'library',
        name:         a.name || a.id,
        author:       a.author || '',
        glbUrl:       a.glbUrl || null,
        previewUrl:   a.previewUrl || null,
        downloadCount: a.downloadCount ?? 0,
        installHidden: false,

        supportedServers: Array.isArray(a.supportedServers) ? a.supportedServers : [],
        hasMale:   a.hasMale   ?? true,
        hasFemale: a.hasFemale ?? true,
      });
    }

    const signature = out.map(i => i.id).sort().join('|');
    if (cachedArmorOrder?.signature === signature) {
      return cachedArmorOrder.entries;
    }
    const withPreview = out.filter(i => i.previewUrl || i.glbUrl);
    const noPreview   = out.filter(i => !(i.previewUrl || i.glbUrl));
    const entries = [...shuffled(withPreview), ...noPreview];
    cachedArmorOrder = { signature, entries };
    return entries;
  }, [reduxItems, libraryItems]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    let out = items;
    if (server) out = out.filter(i => i.supportedServers.includes(server));
    if (q) out = out.filter(i =>
      i.name.toLowerCase().includes(q) || i.author.toLowerCase().includes(q),
    );
    if (sort === 'downloads') {
      out = [...out].sort((a, b) => b.downloadCount - a.downloadCount);
    }
    return out;
  }, [items, search, server, sort]);

  return (
    <div className="h-full overflow-y-auto">
      <div className="max-w-7xl 2xl:max-w-[1700px] mx-auto px-5 pt-3 pb-6 flex flex-col gap-3">
        <ScreenHero
          title={t('armor.screenTitle')}
          subtitle={t('armor.screenSubtitle')}
          search={{
            value: search,
            onChange: setSearch,
            placeholder: t('armor.searchPlaceholder'),
            clearLabel: t('armor.searchClear'),
          }}
          trailing={
            <>
              <ArmorServerPill
                active={server === 'majestic'}
                label="Majestic"
                logo={majesticLogo}
                onClick={() => setServer(server === 'majestic' ? '' : 'majestic')}
              />
              <ArmorServerPill
                active={server === 'gta5rp'}
                label="GTA5RP"
                logo={gta5rpLogo}
                onClick={() => setServer(server === 'gta5rp' ? '' : 'gta5rp')}
              />
              <span
                aria-hidden
                className="hidden sm:block h-7 w-px self-center
                           bg-gradient-to-b from-transparent via-white/15 to-transparent"
              />
              <ArmorSortSelect value={sort} onChange={setSort} />
              <span className="inline-flex items-center gap-1.5 px-3 h-9 rounded-xl
                               bg-white/[0.04] border border-white/[0.06]
                               text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted shrink-0">
                <span className="tabular-nums text-text-primary">{filtered.length}</span>
                <span>{t('redux.count', { count: filtered.length }).replace(/^\d+\s*/, '')}</span>
              </span>
            </>
          }
        />

        {}
        {loading && items.length === 0 ? (
          <GlassPanel depth="z1" tint="soft" rounded="3xl" className="p-10 text-center text-text-muted">
            {t('armor.loading')}
          </GlassPanel>
        ) : filtered.length === 0 ? (
          <GlassPanel
            depth="z1" tint="soft" rounded="3xl"
            className="p-10 flex flex-col items-center gap-3 text-center"
          >
            <Shield size={42} className="text-text-muted" strokeWidth={1.4} />
            <h2 className="text-base font-semibold text-text-primary">
              {items.length === 0 ? t('armor.emptyTitle') : t('armor.noMatchTitle')}
            </h2>
            <p className="text-sm text-text-secondary max-w-md">
              {items.length === 0 ? t('armor.emptyHint') : t('armor.noMatchHint')}
            </p>
          </GlassPanel>
        ) : (

          <motion.div layout className="grid gap-4 grid-cols-[repeat(auto-fill,minmax(260px,1fr))]">
            <AnimatePresence mode="popLayout">
              {filtered.map((item, i) => (
                <motion.div
                  key={item.id}
                  layout
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{
                    duration: 0.32,
                    ease: [0.22, 1, 0.36, 1],
                    delay: Math.min(i, 24) * 0.04,
                  }}
                >
                  <ArmorCard
                    item={item}
                    isInstalled={item.id === currentArmorId}
                    installingId={installingId}
                    onView3D={setPreviewItem}
                    onInstall={(it) => void handleInstall(it)}
                    onOpenRedux={onOpenRedux}
                  />
                </motion.div>
              ))}
            </AnimatePresence>
          </motion.div>
        )}
      </div>

      {previewItem && (
        <GlbViewerModal
          glbUrl={previewItem.glbUrl}
          title={previewItem.name}
          subjectKind="armor"
          onClose={() => setPreviewItem(null)}
        />
      )}

      <ArmorReplaceConfirmModal
        open={replacePrompt !== null}
        current={{
          name:       replacePrompt?.current.name ?? '',
          screenshot: (replacePrompt && items.find(i => i.id === replacePrompt.current.id)?.previewUrl) || null,
          kindLabel:  replacePrompt?.current.kind === 'library' ? t('catalog.kindCustom') : t('catalog.kindRedux'),
        }}
        incoming={{
          name:       replacePrompt?.incoming.name ?? '',
          screenshot: replacePrompt?.incoming.previewUrl ?? null,
          kindLabel:  replacePrompt?.incoming.kind === 'library' ? t('catalog.kindCustom') : t('catalog.kindRedux'),
        }}
        busy={installingId !== null}
        onKeepCurrent={handleReplaceKeep}
        onInstallNew={handleReplaceInstall}
        onCancel={() => setReplacePrompt(null)}
      />

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 12000 : 5000}
      />
    </div>
  );
}

function ArmorServerPill({ active, label, logo, onClick }: {
  active: boolean; label: string; logo: string; onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={label}
      aria-label={label}
      aria-pressed={active}
      style={{ outline: 'none' }}
      className={clsx(
        'group relative inline-flex items-center gap-2 h-10 pl-2.5 pr-3.5 rounded-xl',
        'text-[12px] font-bold uppercase tracking-[0.12em]',
        'border transition-all duration-300 ease-smooth overflow-hidden',
        active
          ? 'bg-white text-black border-white shadow-pill-active'
          : 'bg-white/[0.04] text-text-secondary border-white/[0.06] ' +
            'hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]',
      )}
    >
      <span
        className={clsx(
          'relative shrink-0 w-6 h-6 rounded-md flex items-center justify-center',
          active ? 'bg-black/[0.06]' : 'bg-white/[0.06]',
        )}
      >
        <img src={logo} alt="" className="w-4 h-4 object-contain" />
      </span>
      <span>{label}</span>
    </button>
  );
}

function ArmorSortSelect({ value, onChange }: { value: ArmorSort; onChange: (v: ArmorSort) => void }) {
  const { t } = useTranslation();
  const options: GlassDropdownOption<ArmorSort>[] = [
    { value: 'default',   label: t('redux.sort.default', 'По умолчанию'), icon: Sparkles },
    { value: 'downloads', label: t('redux.sort.downloads'), icon: DownloadIcon },
  ];
  return (
    <GlassDropdown<ArmorSort>
      value={value}
      options={options}
      onChange={onChange}
      ariaLabel={t('redux.sort.label')}
      title={t('redux.sort.label')}
      width={210}
    />
  );
}
