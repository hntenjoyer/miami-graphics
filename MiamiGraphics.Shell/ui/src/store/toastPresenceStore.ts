import { create } from 'zustand';

interface ToastPresenceState {
  count: number;
  enter: () => void;
  leave: () => void;
}

export const useToastPresenceStore = create<ToastPresenceState>(set => ({
  count: 0,
  enter: () => set(s => ({ count: s.count + 1 })),
  leave: () => set(s => ({ count: Math.max(0, s.count - 1) })),
}));
