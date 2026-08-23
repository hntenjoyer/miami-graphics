import { create } from 'zustand';

interface AdminGateState {
  unlocked: boolean;
  setUnlocked: (v: boolean) => void;
}

export const useAdminGate = create<AdminGateState>((set) => ({
  unlocked: false,
  setUnlocked: (v) => set({ unlocked: v }),
}));
