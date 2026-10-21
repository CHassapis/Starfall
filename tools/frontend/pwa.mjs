/**
 * Installing Starfall as an app (PWA). Run against a local server of the built app (VITE_API_BASE=""); every API
 * request is answered by the synthetic fixture. Checks: the manifest and its icons load (JSON and PNGs, not the app's
 * index.html); Chrome finds nothing stopping an install (DevTools' installability check); no service worker is
 * registered (nothing is cached for offline use); the browser's install prompt is kept back and shown only from the
 * Install Starfall button, never by itself; an iPhone gets Safari's steps and an Android phone Chrome's, from the
 * phone menu; the entry is gone inside the installed app; the title bar colour follows the theme; and on a plain http
 * address the dialog says installing needs https. SCREENSHOT_DIR, when set, gets the phone menu and dialogs.
 */
import assert from "node:assert/strict";
import { mkdtemp } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { fixture } from "./fixtures.mjs";
const { chromium, devices } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : "playwright");
const base = process.env.FRONTEND_URL || "http://127.0.0.1:55052";
const local = new URL(base);
assert.ok(["localhost", "127.0.0.1"].includes(local.hostname), "Use a local test server");
const shots = process.env.SCREENSHOT_DIR;
let checks = 0;
const ok = (value, message) => { assert.ok(value, message); checks++; };
const answer = async (context) => {
  const data = fixture();
  await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const req = route.request();
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(data.resolve(req.url(), req.method(), req.postDataJSON())) });
  });
};
const signedIn = () => localStorage.setItem("starlights.player", "Test Player");
const errors = [];
const watch = (page) => page.on("pageerror", (e) => errors.push(e.message));
const shot = async (page, name) => { if (shots) await page.screenshot({ path: join(shots, `${name}.png`) }); };

// the manifest, its icons and Chrome's verdict, in a normal (not incognito) profile, which is the only kind Chrome
// installs from, and in full Chromium: the headless shell answers the installability check with nothing, always
const desktop = await chromium.launchPersistentContext(await mkdtemp(join(tmpdir(), "pwa-")), { channel: "chromium", headless: true, viewport: { width: 1280, height: 900 }, reducedMotion: "reduce" });
try {
  await desktop.addInitScript(signedIn);
  await answer(desktop);
  const page = desktop.pages()[0] ?? (await desktop.newPage());
  watch(page);
  await page.goto(`${base}/`);
  await page.getByRole("button", { name: "Choose theme and units", exact: true }).waitFor();

  const href = await page.locator('link[rel="manifest"]').getAttribute("href");
  ok(href === "/manifest.webmanifest", `manifest link ${href}`);
  const res = await page.request.get(`${base}/manifest.webmanifest`);
  ok(res.ok(), `manifest ${res.status()}`);
  const manifest = await res.json();
  ok(manifest.name === "Starfall" && manifest.display === "standalone" && manifest.start_url, "manifest: name, standalone, start_url");
  for (const icon of manifest.icons) {
    const img = await page.request.get(new URL(icon.src, `${base}/manifest.webmanifest`).href);
    ok(img.ok() && img.headers()["content-type"] === "image/png", `icon ${icon.src}: ${img.status()} ${img.headers()["content-type"]}`);
  }
  ok(manifest.icons.some((i) => i.purpose === "maskable" && i.sizes === "512x512"), "a 512 maskable icon");
  ok(manifest.icons.some((i) => i.purpose === "any" && i.sizes === "512x512"), "a 512 icon");
  const touch = await page.request.get(`${base}${await page.locator('link[rel="apple-touch-icon"]').getAttribute("href")}`);
  ok(touch.ok() && touch.headers()["content-type"] === "image/png", "apple-touch-icon is a PNG");

  const cdp = await desktop.newCDPSession(page);
  const parsed = await cdp.send("Page.getAppManifest");
  ok(parsed.errors.length === 0, `manifest parse errors: ${JSON.stringify(parsed.errors)}`);
  // installability is worked out once the icons have been fetched; ask until it settles
  let installability = [];
  for (let i = 0; i < 20; i++) {
    ({ installabilityErrors: installability } = await cdp.send("Page.getInstallabilityErrors"));
    if (installability.length === 0) break;
    await page.waitForTimeout(250);
  }
  ok(installability.length === 0, `Chrome installability: ${JSON.stringify(installability)}`);
  // and the check does tell: a page without the manifest (an icon on its own) is not installable
  const bare = await desktop.newPage();
  await bare.goto(`${base}/icons/icon-192.png`);
  const { installabilityErrors: bareErrors } = await (await desktop.newCDPSession(bare)).send("Page.getInstallabilityErrors");
  ok(bareErrors.some((e) => e.errorId === "no-manifest"), `control page: ${JSON.stringify(bareErrors)}`);
  await bare.close();
  ok((await page.evaluate(() => navigator.serviceWorker.getRegistrations())).length === 0, "no service worker");

  // the browser's prompt: kept back on arrival (no banner, no dialog), shown only from the button
  const outcome = await page.evaluate(() => {
    const event = new Event("beforeinstallprompt", { cancelable: true });
    window.__prompted = 0;
    Object.assign(event, { prompt: async () => { window.__prompted++; }, userChoice: Promise.resolve({ outcome: "dismissed" }) });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  });
  ok(outcome, "the install prompt is kept back (preventDefault)");
  await page.waitForTimeout(300);
  ok((await page.getByRole("dialog").count()) === 0, "nothing opens by itself");
  ok((await page.evaluate(() => window.__prompted)) === 0, "not prompted by itself");
  await page.getByRole("button", { name: "Choose theme and units", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByText("Install Starfall as an app").click();
  await shot(page, "desktop-preferences-install");
  await dialog.getByRole("button", { name: "Install Starfall" }).click();
  ok((await page.evaluate(() => window.__prompted)) === 1, "the button shows the browser's prompt");
  ok((await dialog.getByRole("button", { name: "Install Starfall" }).count()) === 0, "a prompt is used once");
  await page.keyboard.press("Escape");

  // the title bar colour follows the theme: the saved one before the first paint, a new one at once
  const bar = () => page.evaluate(() => document.querySelector('meta[name="theme-color"]').content);
  const expected = { porcelain: "#f4f0e8", midnight: "#0c1628", slate: "#2d3e51" };
  for (const theme of Object.keys(expected)) {
    await page.evaluate((t) => localStorage.setItem("starlights-ui-theme", t), theme);
    await page.reload();
    await page.getByRole("button", { name: "Choose theme and units", exact: true }).waitFor();
    ok((await bar()) === expected[theme], `theme-color for ${theme}: ${await bar()}`);
  }
} finally {
  await desktop.close();
}

const browser = await chromium.launch({ headless: true, args: [`--host-resolver-rules=MAP starfall.invalid ${local.hostname}`] });
try {
  // phones: the menu's Install Starfall, with each platform's own steps
  const phones = [
    { name: "iphone", device: devices["iPhone 13"], steps: ["Add to Home Screen", "Open as Web App"], note: /keeps its own storage/ },
    { name: "android", device: devices["Pixel 7"], steps: ["Install app", "Create shortcut"] },
  ];
  for (const { name, device, steps, note } of phones) {
    const context = await browser.newContext({ ...device, reducedMotion: "reduce" });
    await context.addInitScript(signedIn);
    await answer(context);
    const page = await context.newPage();
    watch(page);
    await page.goto(`${base}/`);
    await page.getByRole("button", { name: "Open navigation" }).click();
    const entry = page.getByRole("menuitem", { name: "Install Starfall" });
    ok(await entry.isVisible(), `${name}: Install Starfall in the menu`);
    await shot(page, `${name}-menu`);
    await entry.click();
    const dialog = page.getByRole("dialog", { name: "Install Starfall" });
    await dialog.waitFor();
    for (const step of steps) ok(await dialog.getByText(step, { exact: false }).first().isVisible(), `${name}: "${step}"`);
    ok((await dialog.getByRole("button", { name: "Install Starfall" }).count()) === 0, `${name}: no prompt button without the browser's prompt`);
    if (note) ok(await dialog.getByText(note).isVisible(), `${name}: separate storage note`);
    await shot(page, `${name}-install`);
    await context.close();
  }

  // inside the installed app (iPhone's home-screen app): no Install entry
  const app = await browser.newContext({ ...devices["iPhone 13"], reducedMotion: "reduce" });
  await app.addInitScript(() => {
    localStorage.setItem("starlights.player", "Test Player");
    Object.defineProperty(Navigator.prototype, "standalone", { get: () => true, configurable: true });
  });
  await answer(app);
  const installed = await app.newPage();
  watch(installed);
  await installed.goto(`${base}/`);
  await installed.getByRole("button", { name: "Open navigation" }).click();
  await installed.getByRole("menuitem").first().waitFor();
  ok((await installed.getByRole("menuitem", { name: "Install Starfall" }).count()) === 0, "installed app: no Install entry");
  await app.close();

  // a plain http address other than this computer is not a secure context: no install, the dialog says so
  const insecure = await browser.newContext({ ...devices["Pixel 7"], reducedMotion: "reduce" });
  await insecure.addInitScript(signedIn);
  await answer(insecure);
  const page = await insecure.newPage();
  watch(page);
  await page.goto(`http://starfall.invalid:${local.port}/`);
  ok(!(await page.evaluate(() => window.isSecureContext)), "http address is not secure");
  await page.getByRole("button", { name: "Open navigation" }).click();
  await page.getByRole("menuitem", { name: "Install Starfall" }).click();
  ok(await page.getByRole("dialog").getByText(/secure https:\/\/ address/).isVisible(), "http: says installing needs https");
  await shot(page, "http-install");
  await insecure.close();
} finally {
  await browser.close();
}
ok(errors.length === 0, errors.join("; "));
console.log(`PWA checks: ${checks} passed`);
