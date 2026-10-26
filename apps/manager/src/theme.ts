export type Theme = 'dark' | 'light';
export const themeKey = 'pulse.manager.theme';

export function readTheme(): Theme {
  try { return localStorage.getItem(themeKey) === 'light' ? 'light' : 'dark'; }
  catch { return 'dark'; }
}

export function saveTheme(theme: Theme): void {
  // A blocked preference store must not stop the recording application.
  try { localStorage.setItem(themeKey, theme); } catch { /* use this session */ }
}
