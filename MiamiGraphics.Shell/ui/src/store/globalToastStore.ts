import { create } from 'zustand';
import type { ToastTone } from '@/components/Toast';

interface GlobalToastEntry {
  seq: number;
  tone: ToastTone;
  message: string;
}

interface GlobalToastState {
  current: GlobalToastEntry | null;
  push: (tone: ToastTone, message: string) => void;
  dismiss: () => void;
}

let nextSeq = 1;

export const useGlobalToastStore = create<GlobalToastState>(set => ({
  current: null,
  push: (tone, message) => set({ current: { seq: nextSeq++, tone, message } }),
  dismiss: () => set({ current: null }),
}));
