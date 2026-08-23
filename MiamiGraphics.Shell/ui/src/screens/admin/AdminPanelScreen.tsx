import { useEffect, useState } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { AdminSidebar, type AdminSectionId } from './AdminSidebar';
import { SettingsSection } from './sections/SettingsSection';
import { ReduxSection } from './sections/ReduxSection';
import { DatabaseSection } from './sections/DatabaseSection';
import { InjectorSection } from './sections/InjectorSection';
import { GtaVersionsSection } from './sections/GtaVersionsSection';
import { LibrarySection } from './sections/LibrarySection';
import { PopularitySection } from './sections/PopularitySection';
import { GunsSection } from './sections/GunsSection';
import { ArmorVisibilitySection } from './sections/ArmorVisibilitySection';
import { DlcArmorImportSection } from './sections/DlcArmorImportSection';
import { GtaPresetsSection } from './sections/GtaPresetsSection';
import { PendingBuildsSection } from './sections/PendingBuildsSection';
import { CustomGunsReviewSection } from './sections/CustomGunsReviewSection';
import { CustomGunsManageSection } from './sections/CustomGunsManageSection';
import { ProPlayersSection } from './sections/ProPlayersSection';
import { BigMapSection } from './sections/BigMapSection';
import { useAdminStore } from '@/store/adminStore';
import { useAdminDirtyStore } from '@/store/adminDirtyStore';
import { EASE_DEPTH } from '@/design';

function renderSection(id: AdminSectionId) {
  switch (id) {
    case 'settings':      return <SettingsSection />;
    case 'redux':         return <ReduxSection />;
    case 'database':      return <DatabaseSection />;
    case 'guns':          return <GunsSection />;
    case 'armor':         return <ArmorVisibilitySection />;
    case 'armorImport':   return <DlcArmorImportSection />;
    case 'injector':      return <InjectorSection />;
    case 'gtaVersions':   return <GtaVersionsSection />;
    case 'library':       return <LibrarySection />;
    case 'gtaPresets':    return <GtaPresetsSection />;
    case 'pendingBuilds':    return <PendingBuildsSection />;
    case 'customGunsReview': return <CustomGunsReviewSection />;
    case 'customGunsManage': return <CustomGunsManageSection />;
    case 'proPlayers':    return <ProPlayersSection />;
    case 'popularity':    return <PopularitySection />;
    case 'bigmap':        return <BigMapSection />;
  }
}

export function AdminPanelScreen() {
  const [active, setActive] = useState<AdminSectionId>('settings');

  const dirty = useAdminDirtyStore(s => s.dirty);
  const setDirty = useAdminDirtyStore(s => s.setDirty);

  const guardedSetActive = (id: AdminSectionId) => {
    if (active === 'settings' && id !== 'settings' && dirty) {
      const ok = window.confirm(
        'В настройках есть несохранённые изменения. Уйти без сохранения? Изменения будут потеряны.',
      );
      if (!ok) return;
      setDirty(false);
    }
    setActive(id);
  };

  const requestSection = useAdminStore(s => s.requestSection);
  const consumeSectionRequest = useAdminStore(s => s.consumeSectionRequest);
  useEffect(() => {
    if (!requestSection) return;
    guardedSetActive(requestSection);
    consumeSectionRequest();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestSection, consumeSectionRequest]);

  return (
    <div className="flex h-full">
      <AdminSidebar active={active} onChange={guardedSetActive} />
      {}
      <main className="flex-1 overflow-hidden relative">
        <AnimatePresence mode="wait" initial={false}>
          <motion.div
            key={active}
            initial={{ opacity: 0, y: 12, scale: 0.985 }}
            animate={{ opacity: 1, y: 0,  scale: 1 }}
            exit   ={{ opacity: 0, y: -8, scale: 0.99 }}
            transition={{ duration: 0.32, ease: EASE_DEPTH }}
            className="absolute inset-0"
          >
            {renderSection(active)}
          </motion.div>
        </AnimatePresence>
      </main>
    </div>
  );
}
