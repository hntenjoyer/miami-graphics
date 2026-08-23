import { create } from 'zustand';

interface LeaveGuardState {
  blocked: boolean;
  setBlocked: (b: boolean) => void;
  pending: (() => void) | null;
  attempt: (action: () => void) => void;
  confirm: () => void;
  cancel: () => void;
}

export const useLeaveGuardStore = create<LeaveGuardState>((set, get) => ({
  blocked: false,
  setBlocked: (blocked) => set({ blocked }),
  pending: null,
  attempt: (action) => {
    if (!get().blocked) { action(); return; }
    set({ pending: () => action() });
  },
  confirm: () => {
    const run = get().pending;
    set({ pending: null, blocked: false });
    run?.();
  },
  cancel: () => set({ pending: null }),
}));
