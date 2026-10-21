import { createContext, useContext, useEffect, useState } from "react";

import { isDarkTheme, normalizeTheme as normalize, THEMES, type Theme } from "@/lib/themes";
const Context = createContext<{
  theme: Theme;
  resolvedTheme: Exclude<Theme, "system">;
  setTheme: (theme: Theme) => void;
  previewTheme: (theme: Theme | null) => void;
} | null>(null);
export function ThemeProvider({
  children,
  defaultTheme = "porcelain",
  storageKey = "starlights-ui-theme",
}: {
  children: React.ReactNode;
  defaultTheme?: Theme | "light" | "dark";
  storageKey?: string;
}) {
  const fallback = normalize(defaultTheme, "porcelain");
  const [theme, update] = useState<Theme>(() => {
    try {
      return normalize(localStorage.getItem(storageKey), fallback);
    } catch {
      return fallback;
    }
  });
  const [preview, previewTheme] = useState<Theme | null>(null);
  const [dark, setDark] = useState(() => window.matchMedia("(prefers-color-scheme: dark)").matches);
  useEffect(() => {
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const changed = () => setDark(media.matches);
    media.addEventListener("change", changed);
    return () => media.removeEventListener("change", changed);
  }, []);
  useEffect(() => {
    const changed = (event: StorageEvent) => {
      if (event.key === storageKey) update(normalize(event.newValue, fallback));
    };
    window.addEventListener("storage", changed);
    return () => window.removeEventListener("storage", changed);
  }, [storageKey, fallback]);
  const chosen = preview ?? theme;
  const resolvedTheme = chosen === "system" ? (dark ? "midnight" : "porcelain") : chosen;
  useEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = resolvedTheme;
    root.classList.toggle("dark", isDarkTheme(resolvedTheme));
    root.classList.toggle("light", !isDarkTheme(resolvedTheme));
    root.style.colorScheme = isDarkTheme(resolvedTheme) ? "dark" : "light";
    // the browser bar and an installed app's title bar follow the theme's background
    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", THEMES.find((t) => t.id === resolvedTheme)?.colors[0] ?? "#f4f0e8");
  }, [resolvedTheme]);
  function setTheme(next: Theme) {
    update(next);
    previewTheme(null);
    try {
      localStorage.setItem(storageKey, next);
    } catch {
      /* Still usable when storage is unavailable. */
    }
  }
  return <Context.Provider value={{ theme, resolvedTheme, setTheme, previewTheme }}>{children}</Context.Provider>;
}
// eslint-disable-next-line react-refresh/only-export-components -- Public provider hook; keep existing imports stable.
export function useTheme() {
  const context = useContext(Context);
  if (!context) throw new Error("useTheme must be used within ThemeProvider");
  return context;
}
