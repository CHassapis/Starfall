/**
 * Installing Starfall as an app (a Progressive Web App: public/manifest.webmanifest, no service worker). Chromium
 * browsers offer an install prompt (`beforeinstallprompt`); it is kept here, from the first script on, so the Install
 * Starfall button can show it on a real click and the browser's own mini banner does not pop up by itself. Safari
 * (iPhone/iPad) has no prompt: the dialog explains Share > Add to Home Screen.
 */
import { useSyncExternalStore } from "react";

type InstallPrompt = Event & { prompt: () => Promise<void>; userChoice: Promise<{ outcome: "accepted" | "dismissed" }> };

let deferred: InstallPrompt | null = null;
let installed = false;
const listeners = new Set<() => void>();
const changed = () => listeners.forEach((l) => l());

if (typeof window !== "undefined") {
  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    deferred = event as InstallPrompt;
    changed();
  });
  window.addEventListener("appinstalled", () => {
    installed = true;
    deferred = null;
    changed();
  });
}

export type Platform = "ios" | "android" | "desktop";

export function platform(): Platform {
  const ua = navigator.userAgent;
  // iPadOS reports itself as a Mac: tell it by touch
  if (/iPad|iPhone|iPod/.test(ua) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1)) return "ios";
  return /Android/.test(ua) ? "android" : "desktop";
}

/** running as the installed app (its own window, no address bar) */
export function standalone(): boolean {
  return window.matchMedia("(display-mode: standalone)").matches || (navigator as Navigator & { standalone?: boolean }).standalone === true;
}

/** shows the browser's install prompt (Chromium); the reader's answer */
export async function promptInstall(): Promise<"accepted" | "dismissed" | "unavailable"> {
  const event = deferred;
  if (!event) return "unavailable";
  deferred = null; // a prompt can be shown once
  changed();
  await event.prompt();
  return (await event.userChoice).outcome;
}

type State = { canPrompt: boolean; installed: boolean; platform: Platform; secure: boolean };
let snapshot: State | null = null;
function read(): State {
  const next = { canPrompt: !!deferred, installed: installed || standalone(), platform: platform(), secure: window.isSecureContext };
  if (!snapshot || snapshot.canPrompt !== next.canPrompt || snapshot.installed !== next.installed) snapshot = next;
  return snapshot;
}

/** what installing looks like here: the browser's prompt, Safari's steps, or nothing (already the app) */
export function useInstall(): State {
  return useSyncExternalStore((listener) => {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }, read);
}
