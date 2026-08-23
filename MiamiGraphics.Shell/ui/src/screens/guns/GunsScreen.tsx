import { useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useGunpackStore } from '@/store/gunpackStore';
import { useCustomGunStore } from '@/store/customGunStore';
import { GunsBrowse } from './GunsBrowse';
import { GunpackDetail } from './GunpackDetail';
import { WhitelistGunDetail } from './WhitelistGunDetail';
import { WorkshopScreen } from './workshop/WorkshopScreen';
import { useCanSeeCustomGuns } from '@/store/sessionStore';
import { EASE_DEPTH } from '@/design';

export function GunsScreen() {
  const selectedId            = useGunpackStore(s => s.selectedId);
  const selectedWhitelistName = useGunpackStore(s => s.selectedWhitelistName);
  const selectPack            = useGunpackStore(s => s.selectPack);
  const selectWhitelistGun    = useGunpackStore(s => s.selectWhitelistGun);
  const workshopReq           = useCustomGunStore(s => s.workshopReq);
  const closeWorkshop         = useCustomGunStore(s => s.closeWorkshop);
  const canSeeCustomGuns      = useCanSeeCustomGuns();
  const isEditRequest         = canSeeCustomGuns && !!workshopReq?.customGunId;

  useEffect(() => () => closeWorkshop(), [closeWorkshop]);

  const onOpenPack     = (id: string) => { selectWhitelistGun(null); void selectPack(id); };
  const onBackFromGun  = () => { selectWhitelistGun(null); };
  const onBackFromPack = () => { void selectPack(null); };

  const viewKey = isEditRequest
    ? 'workshop-edit'
    : selectedWhitelistName
      ? `gun:${selectedWhitelistName}`
      : selectedId
        ? `detail:${selectedId}`
        : 'browse';

  return (
    <AnimatePresence mode="wait" initial={false}>
      {isEditRequest ? (
        <motion.div
          key={viewKey}
          initial={{ opacity: 0, scale: 0.99 }}
          animate={{ opacity: 1, scale: 1 }}
          exit={{ opacity: 0, scale: 0.99 }}
          transition={{ duration: 0.28, ease: EASE_DEPTH }}
          className="h-full"
        >
          <WorkshopScreen req={workshopReq!} />
        </motion.div>
      ) : selectedWhitelistName ? (
        <motion.div
          key={viewKey}
          initial={{ opacity: 0, scale: 0.99 }}
          animate={{ opacity: 1, scale: 1 }}
          exit={{ opacity: 0, scale: 0.99 }}
          transition={{ duration: 0.28, ease: EASE_DEPTH }}
          className="h-full"
        >
          <WhitelistGunDetail
            internalName={selectedWhitelistName}
            onBack={onBackFromGun}
            onOpenPack={onOpenPack}
          />
        </motion.div>
      ) : selectedId ? (
        <motion.div
          key={viewKey}
          initial={{ opacity: 0, scale: 0.99 }}
          animate={{ opacity: 1, scale: 1 }}
          exit={{ opacity: 0, scale: 0.99 }}
          transition={{ duration: 0.28, ease: EASE_DEPTH }}
          className="h-full"
        >
          <GunpackDetail packId={selectedId} onBack={onBackFromPack} />
        </motion.div>
      ) : (
        <motion.div
          key={viewKey}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.22, ease: EASE_DEPTH }}
          className="h-full"
        >
          <GunsBrowse onOpenPack={onOpenPack} onOpenWhitelistGun={(name) => selectWhitelistGun(name)} />
        </motion.div>
      )}
    </AnimatePresence>
  );
}
