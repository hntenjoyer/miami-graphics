import { create } from 'zustand';
import type { GunSlotState } from '@/store/userBuildsStore';

export interface SubmitDraft {
  pickingFor: null | 'redux' | 'gunpack';

  reduxId:   string | null;
  reduxName: string | null;

  gunpackId:   string | null;
  gunpackName: string | null;

  gunSlots: Record<string, GunSlotState>;

  customizeApplied: boolean;

  returnTo: 'players' | 'admin';
}

interface SubmitDraftState extends SubmitDraft {
  startPick: (target: 'redux' | 'gunpack', returnTo?: 'players' | 'admin') => void;
  finishPick: (target: 'redux' | 'gunpack', id: string, name: string) => void;
  cancelPick: () => void;
  setGunSlots: (next: Record<string, GunSlotState>) => void;
  reset: () => void;
}

const INITIAL: SubmitDraft = {
  pickingFor:       null,
  reduxId:          null,
  reduxName:        null,
  gunpackId:        null,
  gunpackName:      null,
  gunSlots:         {},
  customizeApplied: false,
  returnTo:         'players',
};

export const useSubmitDraftStore = create<SubmitDraftState>((set) => ({
  ...INITIAL,

  startPick: (target, returnTo = 'players') => set({ pickingFor: target, returnTo }),
  finishPick: (target, id, name) => set(state => {
    if (target === 'redux') {

      return {
        ...state,
        pickingFor: null,
        reduxId:   id,
        reduxName: name,
        customizeApplied: false,
      };
    }

    return {
      ...state,
      pickingFor: null,
      gunpackId:   id,
      gunpackName: name,
      gunSlots:    {},
    };
  }),
  cancelPick: () => set({ pickingFor: null }),
  setGunSlots: (gunSlots) => set({ gunSlots }),
  reset: () => set(INITIAL),
}));
