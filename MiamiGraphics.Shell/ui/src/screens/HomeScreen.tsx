import { useEffect, useRef, useState, lazy, Suspense } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { EASE_DEPTH } from '@/design/tokens';
import { Sidebar } from '@/components/Sidebar';
import { ConfirmModal } from '@/components/ConfirmModal';
import { KeepOverlaysModal } from '@/components/KeepOverlaysModal';
import {
  MODIFICATIONS_DEFAULT_ID, MODIFICATION_IDS, type NavItemId,
} from '@/data/navigation';
import { ADMIN_BUILD } from '@/buildFlags';
import { ModificationsHub } from '@/screens/ModificationsHub';
import { HomeTab } from '@/screens/home/HomeTab';
import { ComingSoonTab } from '@/screens/home/ComingSoonTab';
const AdminEntry = ADMIN_BUILD
  ? lazy(() => import('@/screens/admin/adminEntry'))
  : null;
const SecurityEntry = lazy(() =>
  import('@/screens/security/SecurityScreen').then(m => ({ default: m.SecurityScreen })));
import { ReduxScreen } from '@/screens/redux/ReduxScreen';
import { GunsScreen } from '@/screens/guns/GunsScreen';
import { WorkshopScreen, CREATE_WORKSHOP_REQ } from '@/screens/guns/workshop/WorkshopScreen';
import { useCustomGunStore } from '@/store/customGunStore';
import { ArmorScreen } from '@/screens/armor/ArmorScreen';
import { MinimapsScreen } from '@/screens/minimaps/MinimapsScreen';
import { BigMapScreen } from '@/screens/bigmap/BigMapScreen';
import { ImprovementsScreen } from '@/screens/improvements/ImprovementsScreen';
import { Shapes } from 'lucide-react';
import { ReticlesScreen } from '@/screens/reticles/ReticlesScreen';
import { WorkshopHubScreen } from '@/screens/workshop/WorkshopHubScreen';
import { SoundsScreen } from '@/screens/sounds/SoundsScreen';
import { OtherScreen } from '@/screens/other/OtherScreen';
import { InstalledModsScreen } from '@/screens/installed/InstalledModsScreen';
import { SettingsScreen } from '@/screens/SettingsScreen';
import { GtaSettingsScreen } from '@/screens/settings/GtaSettingsScreen';
import { PcDiagScreen } from '@/screens/pcdiag/PcDiagScreen';
import { ProfileScreen } from '@/screens/ProfileScreen';
import { PlayersScreen } from '@/screens/players/PlayersScreen';
import { AuthorsScreen } from '@/screens/authors/AuthorsScreen';
import { FaqScreen } from '@/screens/faq/FaqScreen';
import { CustomizeScreen } from '@/screens/redux/customize/CustomizeScreen';
import { DownloadsScreen } from '@/screens/downloads/DownloadsScreen';
import { CustomBuildsScreen } from '@/screens/userBuilds/CustomBuildsScreen';
import { BuildsHubScreen } from '@/screens/userBuilds/BuildsHubScreen';
import { EnvironmentGalleryScreen } from '@/screens/environment/EnvironmentGalleryScreen';
import { TimecyclesScreen } from '@/screens/environment/TimecyclesScreen';
import { TreesScreen } from '@/screens/environment/TreesScreen';
import { RoadsScreen } from '@/screens/environment/RoadsScreen';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useBigMapStore } from '@/store/bigMapStore';
import { useCustomizeStore } from '@/store/customizeStore';
import { useNavStore } from '@/store/navStore';
import { useCanSeeCustomGuns } from '@/store/sessionStore';
import { useLeaveGuardStore } from '@/store/leaveGuardStore';
import { bridge } from '@/bridge';
import { preloadGltf } from '@/utils/preloadGltf';

const ROUTED: ReadonlySet<NavItemId> = new Set([
  'home', 'redux', 'redux-customize', 'guns', 'armor', 'installed', 'downloads',
  'admin', 'app-settings', 'profile', 'players', 'faq', 'settings', 'security', 'pcdiag',
  'user-builds', 'builds-hub', 'minimaps', 'bigmap', 'reticles', 'sounds', 'improvements', 'improvements-misc',
  'environment', 'timecycles', 'env-roads', 'env-trees', 'authors', 'workshop',
]);

export function HomeScreen() {
  const { t } = useTranslation();
  const [activeId, setActiveIdRaw] = useState<NavItemId>('home');

  const workshopReq         = useCustomGunStore(s => s.workshopReq);
  const canSeeCustomGuns    = useCanSeeCustomGuns();
  const isCreateWorkshopReq = canSeeCustomGuns && !!workshopReq && !workshopReq.customGunId;
  const showCreateWorkshop  =
    (activeId === 'guns' || activeId === 'workshop') && isCreateWorkshopReq;

  const setNavActive = useNavStore(s => s.setActiveId);
  useEffect(() => { setNavActive(activeId); }, [activeId, setNavActive]);

  const activeIdRef = useRef(activeId);
  activeIdRef.current = activeId;
  const attemptLeave  = useLeaveGuardStore(s => s.attempt);
  const pendingLeave  = useLeaveGuardStore(s => s.pending);
  const confirmLeave  = useLeaveGuardStore(s => s.confirm);
  const cancelLeave   = useLeaveGuardStore(s => s.cancel);

  const setNavigator = useNavStore(s => s.setNavigator);

  const customizeActive = useCustomizeStore(s => s.activeReduxId !== null);

  const setActiveId = (id: NavItemId) => {
    setActiveIdRaw(prev => {
      const target = id === 'modifications' ? MODIFICATIONS_DEFAULT_ID : id;
      if (prev !== target) {
        if (prev === 'redux' && target !== 'redux') {
          useReduxStore.getState().select(null);
        }
        if (prev === 'guns' && target !== 'guns') {
          const gs = useGunpackStore.getState();
          gs.selectWhitelistGun(null);
          void gs.selectPack(null);
        }
        if (prev === 'bigmap' && target !== 'bigmap') {
          useBigMapStore.getState().select(null);
        }
      }
      return target;
    });
  };

  const guardedNavigate = (id: NavItemId) => {
    if (id === activeIdRef.current) { setActiveId(id); return; }
    attemptLeave(() => setActiveId(id));
  };

  useEffect(() => {
    setNavigator(guardedNavigate);
    return () => setNavigator(null);

  }, [setNavigator]);

  const prevCustomizeActive = useRef(customizeActive);
  useEffect(() => {
    const was = prevCustomizeActive.current;
    prevCustomizeActive.current = customizeActive;

    if (!was && customizeActive) {
      setActiveIdRaw('redux-customize');
      return;
    }

    if (was && !customizeActive) {
      setActiveIdRaw(prev => prev === 'redux-customize' ? 'redux' : prev);
    }
  }, [customizeActive]);

  const selectMod = useReduxStore(s => s.select);
  const openMod = (modId: string) => {
    selectMod(modId);
    setActiveId('redux');
  };

  const selectGunpack = useGunpackStore(s => s.selectPack);
  const openGunpack = (packId: string) => {
    void selectGunpack(packId);
    setActiveId('guns');
  };

  const reduxItems = useReduxStore(s => s.items);
  const reduxLoad  = useReduxStore(s => s.load);
  const armorPreloadFired = useRef(false);
  useEffect(() => {
    if (armorPreloadFired.current) return;
    if (reduxItems.length === 0) {

      void reduxLoad();
      return;
    }
    armorPreloadFired.current = true;
    const urls = new Set<string>();
    for (const it of reduxItems) {
      const url = it.components?.armor?.glbUrl;
      if (url) urls.add(url);
    }

    const warm = (target: Set<string>) => {
      for (const u of target) {
        preloadGltf(u);
      }
    };
    bridge.armorLibraryList()
      .then(rows => {
        for (const r of rows ?? []) if (r.glbUrl) urls.add(r.glbUrl);
        warm(urls);
      })
      .catch(() => warm(urls));
  }, [reduxItems, reduxLoad]);

  return (
    <div className="relative flex h-full">
      <Sidebar activeId={activeId} onNavigate={guardedNavigate} />
      {}
      <main className="flex-1 overflow-hidden relative">
        {}
        <motion.div

          key={MODIFICATION_IDS.includes(activeId) ? 'modifications' : activeId}
          className="absolute inset-0"

          initial={activeId === 'armor' ? false : { opacity: 0, y: 16, scale: 0.978 }}
          animate={activeId === 'armor' ? undefined : { opacity: 1, y: 0,  scale: 1 }}
          transition={{ duration: 0.48, ease: EASE_DEPTH, delay: 0.06 }}
        >
          {activeId === 'home' && (
            <HomeTab
              onNavigateToCategory={(id) => setActiveId(id as NavItemId)}
              onOpenMod={openMod}
            />
          )}
          {}
          {MODIFICATION_IDS.includes(activeId) && (
            <ModificationsHub
              activeSubId={activeId}
              onSubTabChange={(id) => setActiveId(id)}
            >
              {activeId === 'redux'    && <ReduxScreen />}
              {activeId === 'guns'     && <GunsScreen />}
              {activeId === 'armor'    && <ArmorScreen onOpenRedux={openMod} />}
              {activeId === 'minimaps' && <MinimapsScreen onOpenRedux={openMod} />}
              {activeId === 'bigmap'   && <BigMapScreen />}
              {activeId === 'reticles' && <ReticlesScreen onOpenRedux={openMod} />}
              {activeId === 'sounds'   && <SoundsScreen />}
              {activeId === 'other'    && <OtherScreen />}
              {activeId === 'settings' && <GtaSettingsScreen />}
            </ModificationsHub>
          )}
          {activeId === 'workshop'        && <WorkshopHubScreen />}
          {activeId === 'pcdiag'          && <PcDiagScreen />}
          {activeId === 'security' && (
            <Suspense fallback={null}><SecurityEntry /></Suspense>
          )}
          {activeId === 'redux-customize' && <CustomizeScreen />}
          {activeId === 'downloads'       && <DownloadsScreen />}
          {activeId === 'user-builds'  && <CustomBuildsScreen />}
          {activeId === 'installed'    && <InstalledModsScreen onNavigateTo={(id) => setActiveId(id as NavItemId)} />}
          {activeId === 'admin' && AdminEntry && (
            <Suspense fallback={null}><AdminEntry /></Suspense>
          )}
          {activeId === 'app-settings' && <SettingsScreen onNavigate={(id) => setActiveId(id as NavItemId)} />}
          {activeId === 'profile'      && <ProfileScreen onOpenMod={openMod} onOpenGunpack={openGunpack} />}
          {activeId === 'players'      && <PlayersScreen />}
          {activeId === 'builds-hub'   && <BuildsHubScreen onNavigate={(id) => setActiveId(id as NavItemId)} />}
          {activeId === 'authors'      && <AuthorsScreen onOpenMod={openMod} onOpenGunpack={openGunpack} />}
          {activeId === 'faq'          && <FaqScreen />}
          {activeId === 'environment'  && <EnvironmentGalleryScreen />}
          {activeId === 'timecycles'   && <TimecyclesScreen />}
          {activeId === 'env-roads'    && <RoadsScreen />}
          {activeId === 'env-trees'    && <TreesScreen />}
          {activeId === 'improvements' && (
            <ImprovementsScreen category="gas_stations" title={t('environment.improvements', 'Заправки')} />
          )}
          {activeId === 'improvements-misc' && (
            <ImprovementsScreen category="misc" title={t('environment.miscTitle', 'Разное')} icon={Shapes} />
          )}
          {!ROUTED.has(activeId)       && <ComingSoonTab sectionId={activeId} />}
        </motion.div>

      </main>

      {canSeeCustomGuns && (
        <div
          aria-hidden={!showCreateWorkshop}
          className={
            'absolute inset-0 transition-opacity duration-300 ease-smooth ' +
            (showCreateWorkshop ? 'opacity-100 pointer-events-auto z-40' : 'opacity-0 pointer-events-none')
          }
        >
          <WorkshopScreen req={CREATE_WORKSHOP_REQ} visible={showCreateWorkshop} />
        </div>
      )}

      <ConfirmModal
        open={pendingLeave !== null}
        title={t('userBuilds.leaveTitle', 'Покинуть конструктор?')}
        message={t('userBuilds.leaveMessage', 'Несохранённая сборка будет сброшена. Уйти и потерять её?')}
        confirmLabel={t('userBuilds.leaveConfirm', 'Сбросить')}
        cancelLabel={t('userBuilds.leaveCancel', 'Остаться')}
        onConfirm={confirmLeave}
        onCancel={cancelLeave}
      />

      <KeepOverlaysModal />
    </div>
  );
}
