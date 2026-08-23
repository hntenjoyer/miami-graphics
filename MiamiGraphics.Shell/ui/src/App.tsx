import { useEffect, useState, useMemo, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useUiStore } from '@/store/uiStore';
import { useSessionStore } from '@/store/sessionStore';
import { useBackupStore } from '@/store/backupStore';
import { ensureInstallProgressBooted } from '@/store/installProgressStore';
import { useGlobalToastStore } from '@/store/globalToastStore';
import { useAppScreenStore } from '@/store/appScreenStore';
import { useNavStore } from '@/store/navStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { bridge } from '@/bridge';
import { WelcomeScreen } from '@/screens/WelcomeScreen';
import { FirstRunScreen } from '@/screens/FirstRunScreen';
import { OnboardingScreen, isOnboardingDone } from '@/screens/OnboardingScreen';
import { HomeScreen } from '@/screens/HomeScreen';
import { BackupScreen } from '@/screens/BackupScreen';
import { WarmupScreen } from '@/screens/WarmupScreen';
import { BetaGateScreen } from '@/screens/BetaGateScreen';
import { Titlebar } from '@/components/Titlebar';
import { Toast } from '@/components/Toast';

import { AppUpdatePrompt } from '@/components/AppUpdatePrompt';
import { BackupProgressToast } from '@/components/BackupProgressToast';
import { InstallProgressToast } from '@/components/InstallProgressToast';
import { GameRunningModal } from '@/components/GameRunningModal';
import { ServerUnreachableBanner } from '@/components/ServerUnreachableBanner';
import { ExitBlockedModal } from '@/components/ExitBlockedModal';
import { DirtyConfirmModal } from '@/components/DirtyConfirmModal';
import { RegionPicker } from '@/components/RegionPicker';
import { ZAPRET_PATH_KEY } from '@/components/settings/ZapretSection';
import { AmbientScene } from '@/design';
import { EASE_DEPTH } from '@/design/tokens';
import { AnimatePresence, motion } from 'framer-motion';

type Screen = 'onboarding' | 'firstrun' | 'welcome' | 'backup' | 'warmup' | 'home';

let warmupDoneThisSession = false;

function pickPostWarmupScreen(): Screen {
  if (!isOnboardingDone()) return 'onboarding';
  const auth = useSessionStore.getState().auth;
  if (!auth || auth.role === 'Guest') return 'firstrun';
  const status = useBackupStore.getState().status;
  return status?.cleanUpdatePresent ? 'home' : 'backup';
}

export default function App() {
  const { t } = useTranslation();

  const initialize = useUiStore(s => s.initialize);
  const initialized = useUiStore(s => s.initialized);
  const background = useUiStore(s => s.settings.background);
  const auth = useSessionStore(s => s.auth);
  const beta = useSessionStore(s => s.beta);
  const loadStatus = useBackupStore(s => s.loadStatus);

  const [screen, setScreen] = useState<Screen>(() =>
    warmupDoneThisSession ? pickPostWarmupScreen() : 'warmup'
  );

  const [regionConfigured, setRegionConfigured] = useState<boolean | null>(null);
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const status = await bridge.serverRegionGet();
        if (cancelled) return;
        setRegionConfigured(status.isConfigured);
      } catch (err) {
        console.warn('[App] serverRegionGet failed, falling back to "configured"', err);
        if (!cancelled) setRegionConfigured(true);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const onRegionChosen = (_region: string) => setRegionConfigured(true);

  useEffect(() => { void initialize(); }, [initialize]);

  const appReadySent = useRef(false);
  useEffect(() => {
    if (appReadySent.current) return;
    appReadySent.current = true;
    requestAnimationFrame(() => {
      try {
        const wv = (window as unknown as { chrome?: { webview?: { postMessage?: (msg: string) => void } } }).chrome?.webview;
        wv?.postMessage?.('app-ready');
      } catch {  }
    });
  }, []);

  useEffect(() => { ensureInstallProgressBooted(); }, []);

  useEffect(() => {
    try {
      const zpath = localStorage.getItem(ZAPRET_PATH_KEY) ?? '';
      void bridge.zapretDetect(zpath || null)
        .then(res => {
          if (!res?.installed || !res.detectedRoot) return;
          if (res.detectedRoot !== zpath) {
            try { localStorage.setItem(ZAPRET_PATH_KEY, res.detectedRoot); } catch {  }
          }
          if (res.configuredForUs) return;
          void bridge.zapretApplyWhitelist(res.detectedRoot)
            .catch(err => console.warn('[App] zapretApplyWhitelist failed', err));
        })
        .catch(err => console.warn('[App] zapretDetect failed', err));
    } catch (err) {
      console.warn('[App] zapretDetect skipped', err);
    }
  }, []);

  useEffect(() => {
    (async () => {
      try { await bridge.reconcileInstallState(); }
      catch (e) { console.warn('[boot] reconcileInstallState failed', e); }
      void useGunpackStore.getState().loadInstallState();
    })();
  }, []);

  const isFirstAuthCheck = useRef(true);
  useEffect(() => {
    if (isFirstAuthCheck.current) {
      isFirstAuthCheck.current = false;
      return;
    }
    if (!auth) return;
    if (!isOnboardingDone()) return;
    if (screen === 'warmup') return;
    let cancelled = false;
    (async () => {
      try {
        await loadStatus();
      } catch (e) {
        console.warn('[app] backup status load failed', e);
      }
      if (cancelled) return;
      const status = useBackupStore.getState().status;

      if (status?.cleanUpdatePresent) {
        setScreen(warmupDoneThisSession ? 'home' : 'warmup');
      } else {
        const phase = useBackupStore.getState().progress?.phase;
        const downloadStarted = phase === 'downloading_clean_update'
          || phase === 'writing_working_update'
          || phase === 'snapshot_dlc'
          || phase === 'downloading_clean_dlc'
          || phase === 'writing_working_dlc'
          || phase === 'writing_manifest'
          || phase === 'done';
        if (!downloadStarted) setScreen('backup');
      }
    })();
    return () => { cancelled = true; };
  }, [auth, loadStatus]);

  const requestedScreen = useAppScreenStore(s => s.requested);
  const clearRequestedScreen = useAppScreenStore(s => s.clear);
  useEffect(() => {
    if (!auth) {
      useNavStore.getState().setActiveId(null);
      if (screen !== 'welcome' && screen !== 'warmup' && screen !== 'onboarding' && screen !== 'firstrun') {
        setScreen('welcome');
        useAppScreenStore.getState().clear();
      }
    }
  }, [auth, screen]);
  useEffect(() => {
    if (!requestedScreen) return;
    if (screen === 'warmup') return;
    if (screen !== requestedScreen) setScreen(requestedScreen);
    clearRequestedScreen();
  }, [requestedScreen, screen, clearRequestedScreen]);

  const backupGateOpen = useBackupStore(s => s.backupGateOpen);
  const closeBackupGate = useBackupStore(s => s.closeBackupGate);
  useEffect(() => {
    if (!backupGateOpen) return;
    if (screen === 'warmup') return;
    if (screen !== 'backup') setScreen('backup');
    closeBackupGate();
  }, [backupGateOpen, screen, closeBackupGate]);

  const handleBackupDone = (success: boolean) => {
    if (success) useGlobalToastStore.getState().push('success', t('backup.toastSuccess'));
    setScreen(warmupDoneThisSession ? 'home' : 'warmup');
  };

  const handleWarmupDone = () => {
    warmupDoneThisSession = true;
    setScreen(pickPostWarmupScreen());
  };

  const effectiveScreen: Screen =
    !auth && (screen === 'home' || screen === 'backup') ? 'welcome' : screen;

  const zbtEnabled = useSessionStore(s => s.zbtEnabled);
  const gateActive =
    zbtEnabled && initialized && screen !== 'warmup' && !!auth && auth.role !== 'Guest' && beta !== 'ok';

  const sceneTone = useMemo<'login' | 'home' | 'default'>(() => {
    if (gateActive)                    return 'login';
    if (effectiveScreen === 'welcome') return 'login';
    if (effectiveScreen === 'home')    return 'home';
    return 'default';
  }, [effectiveScreen, gateActive]);

  return (
    <div className="relative w-screen h-screen text-text-primary overflow-hidden flex flex-col">
      {}

      <div
        aria-hidden
        className="fixed top-0 left-0 w-px h-px opacity-[0.01] pointer-events-none backdrop-blur-sm"
      />

      {screen === 'warmup' && (
        <div className="absolute inset-0 z-[120]">
          <WarmupScreen onDone={handleWarmupDone} />
        </div>
      )}

      {!initialized ? null : (<>

      {}
      <div
        aria-hidden
        className="fixed inset-0 z-0 pointer-events-none"
        style={{
          background:
            'radial-gradient(ellipse at 18% 12%, rgba(255,255,255,0.06) 0%, transparent 45%),' +
            'radial-gradient(ellipse at 82% 88%, rgba(255,255,255,0.04) 0%, transparent 50%),' +
            'linear-gradient(180deg, #14141a 0%, #0e0e14 60%, #0a0a10 100%)',
        }}
      />

      {}
      {background !== 'off' && <AmbientScene tone={sceneTone} background={background} />}

      {}
      <div className="relative z-10 flex flex-col w-full h-full">
        <Titlebar />
        <main className="relative flex-1 overflow-hidden" style={{ perspective: 1600 }}>
          {}
          <AnimatePresence mode="wait">
            <motion.div
              key={gateActive ? 'beta-gate' : effectiveScreen}
              className="absolute inset-0"
              initial={{ opacity: 0, y: 14, scale: 0.985, filter: 'blur(12px)' }}
              animate={{
                opacity: 1, y: 0, scale: 1, filter: 'blur(0px)',
                transition: { duration: 0.5, ease: EASE_DEPTH },
              }}
              exit={{
                opacity: 0, y: -12, scale: 1.012, filter: 'blur(10px)',
                transition: { duration: 0.3, ease: EASE_DEPTH },
              }}
            >
              {gateActive ? <BetaGateScreen /> : (<>
              {effectiveScreen === 'onboarding' && <OnboardingScreen onContinue={() => setScreen('firstrun')} />}
              {effectiveScreen === 'firstrun'   && <FirstRunScreen onContinue={() => setScreen('welcome')} />}
              {effectiveScreen === 'welcome'    && <WelcomeScreen />}
              {effectiveScreen === 'backup'     && <BackupScreen onDone={handleBackupDone} />}
              {effectiveScreen === 'home'       && <HomeScreen />}
              </>)}
            </motion.div>
          </AnimatePresence>
        </main>

        {}
        <GlobalToastSlot />
        <AppUpdatePrompt />
        <BackupProgressToast />
        <InstallProgressToast />
        <ServerUnreachableBanner />
        <ExitBlockedModal />
        <GameRunningModal />
        <DirtyConfirmModal />

        {}
        {regionConfigured === false
          && screen !== 'onboarding'
          && screen !== 'warmup'
          && (
          <RegionPicker
            initialRegion=""
            onChosen={onRegionChosen}
          />
        )}

      </div>

      </>)}
    </div>
  );
}

function GlobalToastSlot() {
  const current = useGlobalToastStore(s => s.current);
  const dismiss = useGlobalToastStore(s => s.dismiss);
  return (
    <Toast
      key={current?.seq ?? 'idle'}
      open={current !== null}
      tone={current?.tone}
      message={current?.message ?? ''}
      onClose={dismiss}
    />
  );
}
