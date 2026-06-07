import { useEffect, useState } from 'react';

export type ThemeMode = 'light' | 'dark' | 'system';
export type ResolvedTheme = 'light' | 'dark';

export const THEME_STORAGE_KEY = 'mystia-steward-companion-theme-mode';

const LEGACY_THEME_STORAGE_KEY = 'mystia-steward-theme-mode';
const THEME_CHANGE_EVENT = 'mystia-steward-companion-theme-change';
const THEME_MODES = new Set<ThemeMode>(['light', 'dark', 'system']);

function isBrowser() {
  return typeof window !== 'undefined' && typeof document !== 'undefined';
}

function normalizeThemeMode(value: unknown): ThemeMode {
  return typeof value === 'string' && THEME_MODES.has(value as ThemeMode)
    ? value as ThemeMode
    : 'system';
}

export function readThemeMode(): ThemeMode {
  if (!isBrowser()) return 'system';
  try {
    const value = window.localStorage.getItem(THEME_STORAGE_KEY);
    if (value !== null) return normalizeThemeMode(value);

    const legacyValue = window.localStorage.getItem(LEGACY_THEME_STORAGE_KEY);
    if (legacyValue !== null) {
      window.localStorage.setItem(THEME_STORAGE_KEY, legacyValue);
      window.localStorage.removeItem(LEGACY_THEME_STORAGE_KEY);
      return normalizeThemeMode(legacyValue);
    }

    return 'system';
  } catch {
    return 'system';
  }
}

function writeThemeMode(mode: ThemeMode) {
  if (!isBrowser()) return;
  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, mode);
  } catch {
    // Theme persistence is a convenience; rendering should continue if storage is blocked.
  }
}

export function resolveThemeMode(mode: ThemeMode): ResolvedTheme {
  if (!isBrowser() || mode !== 'system') return mode === 'dark' ? 'dark' : 'light';
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function applyThemeMode(mode: ThemeMode): ResolvedTheme {
  if (!isBrowser()) return 'light';
  const resolved = resolveThemeMode(mode);
  const root = document.documentElement;
  root.classList.toggle('dark', resolved === 'dark');
  root.dataset.theme = mode;
  root.style.colorScheme = resolved;
  return resolved;
}

export function setStoredThemeMode(mode: ThemeMode): ResolvedTheme {
  writeThemeMode(mode);
  const resolved = applyThemeMode(mode);
  if (isBrowser()) {
    window.dispatchEvent(new Event(THEME_CHANGE_EVENT));
  }
  return resolved;
}

export function useThemeMode() {
  const [mode, setModeState] = useState<ThemeMode>(() => readThemeMode());
  const [resolvedTheme, setResolvedTheme] = useState<ResolvedTheme>(() => resolveThemeMode(readThemeMode()));

  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)');

    const syncTheme = () => {
      const nextMode = readThemeMode();
      setModeState(nextMode);
      setResolvedTheme(applyThemeMode(nextMode));
    };

    syncTheme();
    window.addEventListener(THEME_CHANGE_EVENT, syncTheme);
    window.addEventListener('storage', syncTheme);
    media.addEventListener('change', syncTheme);
    return () => {
      window.removeEventListener(THEME_CHANGE_EVENT, syncTheme);
      window.removeEventListener('storage', syncTheme);
      media.removeEventListener('change', syncTheme);
    };
  }, []);

  const setMode = (nextMode: ThemeMode) => {
    setModeState(nextMode);
    setResolvedTheme(setStoredThemeMode(nextMode));
  };

  return { mode, resolvedTheme, setMode };
}
