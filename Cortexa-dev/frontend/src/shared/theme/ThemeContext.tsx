import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import './tokens.css';

export type ThemeMode = 'light' | 'dark';

const STORAGE_KEY = 'cortexa.theme';

interface ThemeContextValue {
  themeMode: ThemeMode;
  applyTheme: (mode: ThemeMode) => void;
  toggleTheme: () => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

function readStored(): ThemeMode | null {
  try {
    const t = localStorage.getItem(STORAGE_KEY);
    return t === 'dark' || t === 'light' ? t : null;
  } catch {
    return null;
  }
}

function getInitialTheme(): ThemeMode {
  const stored = readStored();
  if (stored) return stored;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [themeMode, setMode] = useState<ThemeMode>(getInitialTheme);

  useEffect(() => {
    document.documentElement.dataset.theme = themeMode;
  }, [themeMode]);

  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const handler = (e: MediaQueryListEvent) => {
      if (!readStored()) setMode(e.matches ? 'dark' : 'light');
    };
    mq.addEventListener('change', handler);
    return () => mq.removeEventListener('change', handler);
  }, []);

  function applyTheme(mode: ThemeMode) {
    try {
      localStorage.setItem(STORAGE_KEY, mode);
    } catch {
      /* storage may be unavailable — theme still applies for the session */
    }
    setMode(mode);
  }

  function toggleTheme() {
    applyTheme(themeMode === 'dark' ? 'light' : 'dark');
  }

  return (
    <ThemeContext.Provider value={{ themeMode, applyTheme, toggleTheme }}>{children}</ThemeContext.Provider>
  );
}

export function useThemeContext(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useThemeContext must be used inside ThemeProvider');
  return ctx;
}
