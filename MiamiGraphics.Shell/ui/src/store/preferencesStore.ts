import { create } from 'zustand';

const STORAGE_KEY = 'hntgraph.uiPreferences';

interface UiPreferences {
  hidePlayersFromNav: boolean;
}

interface PreferencesState extends UiPreferences {
  setHidePlayersFromNav: (v: boolean) => void;
}

const DEFAULTS: UiPreferences = {
  hidePlayersFromNav: false,
};

const MIGRATION_KEY = 'hntgraph.uiPreferences.playersAlwaysVisible.v1';

function loadFromStorage(): UiPreferences {
  try {

    const migrated = window.localStorage.getItem(MIGRATION_KEY) === '1';
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      if (!migrated) window.localStorage.setItem(MIGRATION_KEY, '1');
      return DEFAULTS;
    }
    const parsed = JSON.parse(raw) as Partial<UiPreferences>;
    let next: UiPreferences = { ...DEFAULTS, ...parsed };
    if (!migrated) {
      next = { ...next, hidePlayersFromNav: false };
      try {
        window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
        window.localStorage.setItem(MIGRATION_KEY, '1');
      } catch {  }
    }
    return next;
  } catch {
    return DEFAULTS;
  }
}

function saveToStorage(prefs: UiPreferences) {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs));
  } catch {  }
}

export const usePreferencesStore = create<PreferencesState>((set, get) => ({
  ...loadFromStorage(),

  setHidePlayersFromNav: (v) => {
    const next = { ...stateOf(get()), hidePlayersFromNav: v };
    saveToStorage(next);
    set({ hidePlayersFromNav: v });
  },
}));

function stateOf(s: PreferencesState): UiPreferences {
  return {
    hidePlayersFromNav: s.hidePlayersFromNav,
  };
}
