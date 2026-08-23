import {
  createContext, useContext, useState, useEffect, useLayoutEffect, useMemo, useCallback,
  type ReactNode,
} from 'react';
import i18n from '@/i18n';
import { useUiStore } from '@/store/uiStore';
import type { Language, Background } from '@/bridge/types';

export type ThemeMode = 'light' | 'dark';

export interface SettingsContextValue {
  theme:        ThemeMode;
  setTheme:     (mode: ThemeMode) => void;

  language:     Language;
  setLanguage:  (lang: Language) => Promise<void>;
  langTransitioning: boolean;

  background:    Background;
  setBackground: (bg: Background) => Promise<void>;
}

const VALID_THEMES: readonly ThemeMode[] = ['light', 'dark'] as const;
const DEFAULT_THEME: ThemeMode = 'dark';
void VALID_THEMES;

const readStoredTheme = (): ThemeMode => DEFAULT_THEME;

const applyThemeClass = (mode: ThemeMode) => {

  document.documentElement.classList.add('dark');
  void mode;
};

const SettingsContext = createContext<SettingsContextValue | null>(null);

export function SettingsProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemeMode>(() => readStoredTheme());

  useLayoutEffect(() => {
    applyThemeClass(theme);
  }, [theme]);

  useEffect(() => {
    const id = window.requestAnimationFrame(() => {
      document.documentElement.classList.add('theme-anim');
    });
    return () => window.cancelAnimationFrame(id);
  }, []);

  void setThemeState;
  const setTheme = useCallback((_mode: ThemeMode) => {  }, []);

  const language         = useUiStore(s => s.settings.language);
  const setStoreLanguage = useUiStore(s => s.setLanguage);
  const langTransitioning = false;

  const setLanguage = useCallback(async (lang: Language) => {

    if (i18n.language === lang) {
      await setStoreLanguage(lang);
      return;
    }

    await i18n.changeLanguage(lang);
    await setStoreLanguage(lang);
  }, [setStoreLanguage]);

  const background         = useUiStore(s => s.settings.background);
  const setStoreBackground = useUiStore(s => s.setBackground);
  const setBackground = useCallback(async (bg: Background) => {
    await setStoreBackground(bg);
  }, [setStoreBackground]);

  const value = useMemo<SettingsContextValue>(() => ({
    theme, setTheme,
    language, setLanguage, langTransitioning,
    background, setBackground,
  }), [theme, setTheme, language, setLanguage, langTransitioning, background, setBackground]);

  return (
    <SettingsContext.Provider value={value}>
      {children}
    </SettingsContext.Provider>
  );
}

function useSettings(): SettingsContextValue {
  const ctx = useContext(SettingsContext);
  if (ctx === null) throw new Error('useSettings must be used inside <SettingsProvider>');
  return ctx;
}

export function useTheme() {
  const { theme, setTheme } = useSettings();
  return { theme, setTheme };
}

export function useLanguage() {
  const { language, setLanguage } = useSettings();
  return { language, setLanguage };
}

export function useLanguageTransition() {
  return useSettings().langTransitioning;
}

export function useBackground() {
  const { background, setBackground } = useSettings();
  return { background, setBackground };
}
