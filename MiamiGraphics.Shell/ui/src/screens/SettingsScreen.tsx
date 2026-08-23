import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Palette, Languages, Trash2, Database, RotateCcw, Gamepad2, Wrench, FileCheck2, Loader2, HelpCircle, Globe2, PackageX, ScrollText, ShieldCheck, FolderOpen } from 'lucide-react';
import { SettingsSection } from '@/components/settings/SettingsSection';
import { TermsModal } from '@/components/TermsModal';
import { PrivacyModal } from '@/components/PrivacyModal';
import { SettingsRow } from '@/components/settings/SettingsRow';
import { LanguageSwitcher } from '@/components/settings/LanguageSwitcher';
import { ServerRegionSwitcher } from '@/components/settings/ServerRegionSwitcher';
import { DownloadSourceSwitcher } from '@/components/settings/DownloadSourceSwitcher';
import { BackgroundSwitcher } from '@/components/settings/BackgroundSwitcher';
import { ZapretSection } from '@/components/settings/ZapretSection';
import { NetworkDoctorSection } from '@/components/settings/NetworkDoctorSection';
import { GtaPathSection } from '@/components/settings/GtaPathSection';
import { RockstarModeSection } from '@/components/settings/RockstarModeSection';
import { FeatureLogSection } from '@/components/settings/FeatureLogSection';
import { DownloadLogSection } from '@/components/settings/DownloadLogSection';
import { CacheSection } from '@/components/settings/CacheSection';
import { ConfirmModal } from '@/components/ConfirmModal';
import { Toast, type ToastTone } from '@/components/Toast';
import { resetOnboarding } from '@/screens/OnboardingScreen';
import { bridge } from '@/bridge';
import { useGunpackStore } from '@/store/gunpackStore';
import { useReduxStore } from '@/store/reduxStore';
import { useGtaSettingsStore } from '@/store/gtaSettingsStore';
import { useInstallProgressStore } from '@/store/installProgressStore';
import { useAppVersion } from '@/appVersion';

interface SettingsScreenProps {
  onNavigate?: (id: string) => void;
}

export function SettingsScreen({ onNavigate }: SettingsScreenProps = {}) {
  const { t } = useTranslation();
  const appVersion = useAppVersion();
  const [docOpen, setDocOpen] = useState<'terms' | 'privacy' | null>(null);

  const dangerOutline = 'border-2 border-red-500/50';

  type ConfirmKind = null | 'reset' | 'removeMods' | 'restoreGame' | 'uninstall';
  const [confirm, setConfirm]   = useState<ConfirmKind>(null);
  const [busyKind, setBusyKind] = useState<Exclude<ConfirmKind, null> | null>(null);
  const [toast, setToast]       = useState<{ tone: ToastTone; message: string } | null>(null);

  const reloadInstallState = useGunpackStore(s => s.loadInstallState);

  const clearInstalledUiState = () => {
    useReduxStore.getState().clearInstalled();
    useGtaSettingsStore.getState().setInstalledPreset(null);
    useGunpackStore.getState().setInstalledGunpack({
      activeGunpackId: null,
      activeGunpackName: null,
      weaponsRpfSha256: null,
      installedAt: null,
    });
    useGunpackStore.getState().setInstalledSelectedGuns([]);
  };

  const handleRemoveMods = async () => {
    setBusyKind('removeMods');
    try { await reloadInstallState(); } catch {  }

    const installedReduxId = useReduxStore.getState().installedReduxId;
    const gunpackNow = useGunpackStore.getState().installedGunpack;
    const selgunsNow = useGunpackStore.getState().installedSelectedGuns;
    const hasRedux   = !!installedReduxId;
    const hasGunpack = !!gunpackNow.activeGunpackId;
    const hasGuns    = selgunsNow.length > 0;

    const reportOp  = useInstallProgressStore.getState().reportLocalOp;
    const silence   = useInstallProgressStore.getState().silenceProgress;
    const unsilence = useInstallProgressStore.getState().unsilenceProgress;
    const opId      = 'system:removeAll';
    const opName    = t('settings.game.removeModsOpName', 'Удаление всех модов');
    const silencedIds: string[] = [
      'gunpack:uninstall',
      'selectedguns:_',
    ];
    if (hasRedux && installedReduxId) silencedIds.push(`redux:${installedReduxId}`);

    reportOp(opId, opName, 'starting',
      t('settings.game.removeModsOpStarting', 'Удаляем моды и возвращаем чистую GTA...'));
    silencedIds.forEach(silence);

    try {

      if (hasRedux) {
        try { await bridge.reduxUninstallForceClean(); } catch {  }
      }
      if (hasGunpack) {
        try { await bridge.gunpackUninstall(); } catch {  }
      }
      if (hasGuns) {
        try { await bridge.selectedGunsUninstallAll(); } catch {  }
      }

      const restored = await bridge.backupRestoreClean();
      clearInstalledUiState();
      try { await reloadInstallState(); } catch {  }

      if (restored) {
        reportOp(opId, opName, 'done',
          t('settings.game.removeModsOpDone', 'Готово - игра в чистом состоянии.'));
        setToast({ tone: 'success', message: t('settings.game.removeModsDone') });
      } else {

        reportOp(opId, opName, 'done',
          t('settings.game.removeModsOpPartial', 'Свои моды сняты, чистого update.rpf в кеше нет - сделай Backup для полного отката.'));
        setToast({ tone: 'info', message: t('settings.game.removeModsPartial', 'Свои моды сняты. Чтобы вернуть чистый update.rpf, сделай Backup и нажми снова.') });
      }
      setConfirm(null);
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      reportOp(opId, opName, 'error', null, msg);
      setConfirm(null);
      setToast({ tone: 'error', message: msg });
    } finally {
      silencedIds.forEach(unsilence);
      setBusyKind(null);
    }
  };

  const handleRestoreGame = () => {
    setConfirm(null);
    const reportOp  = useInstallProgressStore.getState().reportLocalOp;
    const opId      = 'system:restoreSnapshot';
    const opName    = t('settings.game.restoreGameOpName', 'Возвращение твоего бекапа');
    reportOp(opId, opName, 'starting',
      t('settings.game.restoreGameOpStarting', 'Возвращаю снимок до установки...'));
    void (async () => {
      try {
        const ok = await bridge.backupRestoreSnapshot();
        if (ok) {

          clearInstalledUiState();
          try { await reloadInstallState(); } catch {  }
          reportOp(opId, opName, 'done',
            t('settings.game.restoreGameOpDoneDetail', 'GTA возвращена к состоянию до установки лаунчера.'));
          setToast({ tone: 'success', message: t('settings.game.restoreGameDone') });
        } else {
          reportOp(opId, opName, 'error',
            null,
            t('settings.game.restoreGameMissing'));
          setToast({ tone: 'error', message: t('settings.game.restoreGameMissing') });
        }
      } catch (e) {
        const msg = e instanceof Error ? e.message : String(e);
        reportOp(opId, opName, 'error', null, msg);
        setToast({ tone: 'error', message: msg });
      }
    })();
  };

  const handleResetCache = async () => {
    setBusyKind('reset');
    try {

      resetOnboarding();
      try { window.localStorage.clear(); } catch {  }
      try { window.sessionStorage.clear(); } catch {  }

      await bridge.factoryResetAndRestart();

      setConfirm(null);
      setToast({ tone: 'success', message: t('settings.cache.cleared') });
    } catch (e) {
      setConfirm(null);
      setBusyKind(null);
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    }
  };

  const handleOpenLogs = async () => {
    try {
      await bridge.openLogsFolder();
    } catch (e) {
      setToast({ tone: 'error', message: e instanceof Error ? e.message : String(e) });
    }
  };

  const handleUninstall = async () => {
    setBusyKind('uninstall');
    try {
      await bridge.launcherUninstall();
      setConfirm(null);
    } catch (e) {
      setConfirm(null);
      setBusyKind(null);
      setToast({ tone: 'error', message: e instanceof Error ? e.message : t('settings.cache.uninstallError') });
    }
  };

  return (
    <div className="h-full overflow-y-auto">
      <div className="max-w-5xl mx-auto px-8 py-10">
        <div className="flex flex-col gap-5">
          {}
          <SettingsSection
            icon={Palette}
            title={t('settings.appearance.title')}
            description={t('settings.appearance.description')}
          >
            <SettingsRow
              label={t('settings.appearance.backgroundLabel')}
              description={t('settings.appearance.backgroundDescription')}
              control={<BackgroundSwitcher />}
            />
          </SettingsSection>

          {}
          <SettingsSection
            icon={Languages}
            title={t('settings.languageRegion.title')}
            description={t('settings.languageRegion.description')}
          >
            <SettingsRow
              label={t('settings.languageRegion.languageLabel')}
              description={t('settings.languageRegion.languageDescription')}
              control={<LanguageSwitcher />}
            />
          </SettingsSection>

          {}
          <SettingsSection
            icon={Globe2}
            title={t('settings.serverRegion.title')}
            description={t('settings.serverRegion.description')}
          >
            <SettingsRow
              label={t('settings.serverRegion.label')}
              description={t('settings.serverRegion.rowDescription')}
              control={<ServerRegionSwitcher />}
            />
            <SettingsRow
              label={t('settings.downloadSource.label')}
              description={t('settings.downloadSource.rowDescription')}
              control={<DownloadSourceSwitcher />}
            />
          </SettingsSection>

          {}
          <GtaPathSection onToast={setToast} />

          <RockstarModeSection onToast={setToast} />

          <FeatureLogSection />

          <ZapretSection />

          <NetworkDoctorSection />

          <DownloadLogSection onToast={setToast} />

          {}
          <SettingsSection
            icon={HelpCircle}
            title={t('settings.help.title')}
            description={t('settings.help.description')}
          >
            <SettingsRow
              label={t('settings.help.aboutLabel')}
              description={t('settings.help.aboutDescription')}
              control={
                <button
                  type="button"
                  onClick={() => onNavigate?.('faq')}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-white/[0.04] text-text-primary border border-white/[0.08]
                             hover:bg-white/[0.08] hover:border-white/[0.18]
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {t('settings.help.aboutButton')}
                </button>
              }
            />
            <SettingsRow
              label={t('settings.help.logsLabel', 'Папка логов')}
              description={t('settings.help.logsDescription', 'Лог каждого запуска и установки. Открой папку, чтобы отправить файл в поддержку при проблеме.')}
              control={
                <button
                  type="button"
                  onClick={handleOpenLogs}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-white/[0.04] text-text-primary border border-white/[0.08]
                             hover:bg-white/[0.08] hover:border-white/[0.18]
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  <FolderOpen size={14} />
                  <span>{t('common.open', 'Открыть')}</span>
                </button>
              }
            />
          </SettingsSection>

          <SettingsSection
            icon={Gamepad2}
            title={t('settings.game.title')}
            description={t('settings.game.description')}
            className={dangerOutline}
          >
            <SettingsRow
              label={t('settings.game.removeModsLabel')}
              description={t('settings.game.removeModsDescription')}
              control={
                <button
                  type="button"
                  onClick={() => setConfirm('removeMods')}
                  disabled={busyKind !== null}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-white/[0.04] text-text-primary border border-white/[0.08]
                             hover:bg-white/[0.08] hover:border-white/[0.18]
                             disabled:opacity-50 disabled:cursor-wait
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {busyKind === 'removeMods' ? <Loader2 size={14} className="animate-spin" /> : <Wrench size={14} />}
                  <span>{busyKind === 'removeMods' ? t('settings.game.working') : t('settings.game.removeModsButton')}</span>
                </button>
              }
            />
            <SettingsRow
              label={t('settings.game.restoreGameLabel')}
              description={t('settings.game.restoreGameDescription')}
              control={
                <button
                  type="button"
                  onClick={() => setConfirm('restoreGame')}
                  disabled={busyKind !== null}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-white/[0.04] text-text-primary border border-white/[0.08]
                             hover:bg-white/[0.08] hover:border-white/[0.18]
                             disabled:opacity-50 disabled:cursor-wait
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {busyKind === 'restoreGame' ? <Loader2 size={14} className="animate-spin" /> : <FileCheck2 size={14} />}
                  <span>{busyKind === 'restoreGame' ? t('settings.game.working') : t('settings.game.restoreGameButton')}</span>
                </button>
              }
            />
          </SettingsSection>

          <CacheSection onToast={setToast} />

          <SettingsSection
            icon={Database}
            title={t('settings.cache.title')}
            description={t('settings.cache.description')}
            className={dangerOutline}
          >
            <SettingsRow
              label={t('settings.cache.resetLabel')}
              description={t('settings.cache.resetDescription')}
              control={
                <button
                  type="button"
                  onClick={() => setConfirm('reset')}
                  disabled={busyKind !== null}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-red-500/20 text-red-200
                             hover:bg-red-500/25 hover:border-red-500/50
                             disabled:opacity-50 disabled:cursor-wait
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  <Trash2 size={14} />
                  <span>{busyKind === 'reset' ? t('settings.cache.resetting') : t('settings.cache.resetButton')}</span>
                </button>
              }
            />
            <SettingsRow
              label={t('settings.cache.uninstallLabel')}
              description={t('settings.cache.uninstallDescription')}
              control={
                <button
                  type="button"
                  onClick={() => setConfirm('uninstall')}
                  disabled={busyKind !== null}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-red-500/20 text-red-200
                             hover:bg-red-500/25 hover:border-red-500/50
                             disabled:opacity-50 disabled:cursor-wait
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  {busyKind === 'uninstall' ? <Loader2 size={14} className="animate-spin" /> : <PackageX size={14} />}
                  <span>{busyKind === 'uninstall' ? t('settings.cache.uninstalling') : t('settings.cache.uninstallButton')}</span>
                </button>
              }
            />
          </SettingsSection>

          <SettingsSection
            icon={ScrollText}
            title={t('settings.docs.title', 'Документы')}
            description={t('settings.docs.description', 'Условия использования и политика конфиденциальности - те же, что при регистрации.')}
          >
            <SettingsRow
              label={t('settings.docs.termsLabel', 'Условия использования')}
              description={t('settings.docs.termsDescription', 'Правила пользования лаунчером и каталогом модов.')}
              control={
                <button
                  type="button"
                  onClick={() => setDocOpen('terms')}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-white/[0.04] text-text-primary border border-white/[0.08]
                             hover:bg-white/[0.08] hover:border-white/[0.18]
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  <ScrollText size={14} />
                  <span>{t('common.open', 'Открыть')}</span>
                </button>
              }
            />
            <SettingsRow
              label={t('settings.docs.privacyLabel', 'Политика конфиденциальности')}
              description={t('settings.docs.privacyDescription', 'Какие данные обрабатываются и как они используются.')}
              control={
                <button
                  type="button"
                  onClick={() => setDocOpen('privacy')}
                  className="inline-flex items-center gap-2 px-4 py-2 rounded-xl
                             bg-white/[0.04] text-text-primary border border-white/[0.08]
                             hover:bg-white/[0.08] hover:border-white/[0.18]
                             transition-colors text-sm font-bold uppercase tracking-wider"
                  style={{ outline: 'none' }}
                >
                  <ShieldCheck size={14} />
                  <span>{t('common.open', 'Открыть')}</span>
                </button>
              }
            />
          </SettingsSection>

          <div className="pt-4 pb-2 flex justify-center">
            <span
              className="text-[11px] uppercase tracking-[0.22em] text-text-muted/70 font-semibold select-text"
              title="Miami Graphics"
            >
              {t('settings.betaVersion', { defaultValue: 'БЕТА {{version}}', version: appVersion })}
            </span>
          </div>
        </div>
      </div>

      <ConfirmModal
        open={confirm === 'reset'}
        title={t('settings.cache.confirmTitle')}
        message={t('settings.cache.confirmMessage')}
        confirmLabel={t('settings.cache.resetButton')}
        cancelLabel={t('settings.cache.cancel')}
        destructive
        onConfirm={handleResetCache}
        onCancel={() => setConfirm(null)}
      />
      <ConfirmModal
        open={confirm === 'removeMods'}
        title={t('settings.game.removeModsConfirmTitle')}
        message={t('settings.game.removeModsConfirmMessage')}
        confirmLabel={t('settings.game.removeModsButton')}
        cancelLabel={t('settings.cache.cancel')}
        onConfirm={handleRemoveMods}
        onCancel={() => setConfirm(null)}
      />
      <ConfirmModal
        open={confirm === 'restoreGame'}
        title={t('settings.game.restoreGameConfirmTitle')}
        message={t('settings.game.restoreGameConfirmMessage')}
        confirmLabel={t('settings.game.restoreGameButton')}
        cancelLabel={t('settings.cache.cancel')}
        onConfirm={handleRestoreGame}
        onCancel={() => setConfirm(null)}
      />
      <ConfirmModal
        open={confirm === 'uninstall'}
        title={t('settings.cache.uninstallConfirmTitle')}
        message={t('settings.cache.uninstallConfirmMessage')}
        confirmLabel={t('settings.cache.uninstallButton')}
        cancelLabel={t('settings.cache.cancel')}
        destructive
        onConfirm={handleUninstall}
        onCancel={() => setConfirm(null)}
      />

      <TermsModal open={docOpen === 'terms'} onClose={() => setDocOpen(null)} />
      <PrivacyModal open={docOpen === 'privacy'} onClose={() => setDocOpen(null)} />

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 3500}
      />

      {}
      <span className="sr-only"><RotateCcw size={1} /></span>
    </div>
  );
}
