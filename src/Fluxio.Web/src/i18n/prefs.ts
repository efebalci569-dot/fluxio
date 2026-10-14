import { createContext, useContext } from 'react';
import { translate, type Lang, type StringKey } from './strings';

export type ThemeChoice = 'system' | 'light' | 'dark';

const LANG_KEY = 'fluxio.lang';
const THEME_KEY = 'fluxio.theme';

export function initialLang(): Lang {
  try {
    const saved = localStorage.getItem(LANG_KEY);
    if (saved === 'en' || saved === 'tr') {
      return saved;
    }
  } catch {
    // ignore
  }

  return (navigator.language || 'en').toLowerCase().startsWith('tr') ? 'tr' : 'en';
}

export function saveLang(lang: Lang) {
  try {
    localStorage.setItem(LANG_KEY, lang);
  } catch {
    // ignore
  }
}

export function initialTheme(): ThemeChoice {
  try {
    const saved = localStorage.getItem(THEME_KEY);
    if (saved === 'light' || saved === 'dark' || saved === 'system') {
      return saved;
    }
  } catch {
    // ignore
  }

  return 'system';
}

export function applyTheme(choice: ThemeChoice) {
  const root = document.documentElement;
  if (choice === 'system') {
    root.removeAttribute('data-theme');
  } else {
    root.setAttribute('data-theme', choice);
  }

  try {
    localStorage.setItem(THEME_KEY, choice);
  } catch {
    // ignore
  }
}

export interface Prefs {
  lang: Lang;
  setLang: (lang: Lang) => void;
  theme: ThemeChoice;
  cycleTheme: () => void;
  t: (key: StringKey, params?: Record<string, string | number>) => string;
}

export const PrefsContext = createContext<Prefs>({
  lang: 'en',
  setLang: () => undefined,
  theme: 'system',
  cycleTheme: () => undefined,
  t: (key, params) => translate('en', key, params),
});

export const usePrefs = () => useContext(PrefsContext);
