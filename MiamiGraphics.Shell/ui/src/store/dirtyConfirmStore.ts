import { create } from 'zustand';

export interface DirtyConfirmAction {
  label: string;
  hint?: string;
  kind?: 'accent' | 'neutral' | 'danger';
  run: () => void | Promise<void>;
}

export interface DirtyConfirmRequest {
  title: string;
  message: string;
  actions: DirtyConfirmAction[];
  cancelLabel: string;
  onCancel?: () => void;
}

interface DirtyConfirmState {
  pending: DirtyConfirmRequest | null;
  open: (req: DirtyConfirmRequest) => void;
  close: () => void;
}

export const useDirtyConfirmStore = create<DirtyConfirmState>(set => ({
  pending: null,
  open: req => set({ pending: req }),
  close: () => set({ pending: null }),
}));

if (import.meta.env.DEV) {
  (window as unknown as Record<string, unknown>).__dirtyConfirm = useDirtyConfirmStore;
}
