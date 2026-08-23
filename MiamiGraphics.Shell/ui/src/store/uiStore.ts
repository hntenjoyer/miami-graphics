import { create } from 'zustand';
import type { AppSettings, AccentColor, Background, Language } from '@/bridge/types';
import { bridge } from '@/bridge';
import i18n from '@/i18n';

interface UiState {
  settings: AppSettings;
  initialized: boolean;
  initialize: () => Promise<void>;
  setLanguage: (lang: Language) => Promise<void>;
  setAccent: (accent: AccentColor) => Promise<void>;
  setBackground: (bg: Background) => Promise<void>;
  setPolygonsEnabled: (enabled: boolean) => Promise<void>;
  setSidebarCollapsed: (collapsed: boolean) => Promise<void>;
}

const LOCKED_ACCENT: AccentColor = 'slate';
const applySettingsToDom = (_settings: AppSettings) => {
  document.documentElement.setAttribute('data-accent', LOCKED_ACCENT);
};

const pushLanguageToBackend = (lang: Language) => {
  try {
    void bridge.setUiLanguage?.(lang)?.catch(() => {});
  } catch {}
};

export const useUiStore = create<UiState>((set, get) => ({
  settings: {
    language: 'ru',
    accentColor: 'slate',
    background: 'cubes',
    polygonsEnabled: true,
    sidebarCollapsed: false,
  },
  initialized: false,
  initialize: async () => {
    const fallback = get().settings;
    try {
      const s = await bridge.getAppSettings();
      if (s.accentColor !== LOCKED_ACCENT) {
        s.accentColor = LOCKED_ACCENT;
        try { await bridge.saveAppSettings(s); } catch {  }
      }
      if ((s.language as string) === 'de') {
        s.language = 'en';
        try { await bridge.saveAppSettings(s); } catch {  }
      }
      applySettingsToDom(s);
      await i18n.changeLanguage(s.language);
      pushLanguageToBackend(s.language);
      set({ settings: s, initialized: true });
    } catch (e) {
      console.warn('[ui] settings load failed, using defaults', e);
      applySettingsToDom(fallback);
      await i18n.changeLanguage(fallback.language);
      pushLanguageToBackend(fallback.language);
      set({ settings: fallback, initialized: true });
    }
  },
  setLanguage: async (lang) => {
    const s = { ...get().settings, language: lang };
    set({ settings: s });
    await i18n.changeLanguage(lang);
    pushLanguageToBackend(lang);
    await bridge.saveAppSettings(s);
  },
  setAccent: async (accent) => {
    const s = { ...get().settings, accentColor: accent };
    applySettingsToDom(s);
    set({ settings: s });
    await bridge.saveAppSettings(s);
  },
  setBackground: async (bg) => {
    const s = { ...get().settings, background: bg };
    set({ settings: s });
    await bridge.saveAppSettings(s);
  },
  setPolygonsEnabled: async (enabled) => {
    const s = { ...get().settings, polygonsEnabled: enabled };
    set({ settings: s });
    await bridge.saveAppSettings(s);
  },
  setSidebarCollapsed: async (collapsed) => {
    const s = { ...get().settings, sidebarCollapsed: collapsed };
    set({ settings: s });
    await bridge.saveAppSettings(s);
  },
}));
