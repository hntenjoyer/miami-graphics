import { create } from 'zustand';

interface Ru2QueueState {
  active: boolean;
  position: number;
  etaSec: number;
  set: (s: { active: boolean; position?: number; etaSec?: number }) => void;
}

export const useRu2QueueStore = create<Ru2QueueState>((set) => ({
  active: false,
  position: 0,
  etaSec: 0,
  set: ({ active, position, etaSec }) =>
    set({ active, position: position ?? 0, etaSec: etaSec ?? 0 }),
}));
