/**
 * Homepage composition per theme. Run against a local server of the built app (VITE_API_BASE=""); every API request is
 * answered by the synthetic fixture. For each theme and width: the page has its theme before the first paint (no
 * flash of another palette), the hero is the horizontal brand block (that theme's supplied D20 emblem, loaded, over the
 * live STARFALL wordmark: no 512 × 870 portrait) beside the introduction, the five feature tiles keep the large
 * Character Builder tile left of four smaller ones on wide screens, no horizontal overflow and no layout shift. Then
 * the theme is changed through the theme dialog (the emblem follows at once) and the choice survives a reload.
 */
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { fixture } from "./fixtures.mjs";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : "playwright");
const base = process.env.FRONTEND_URL || "http://127.0.0.1:55052";
assert.ok(["localhost", "127.0.0.1"].includes(new URL(base).hostname), "Use a local test server");
const output = process.env.SCREENSHOT_DIR || "/tmp/starfall-branding-shots";
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const results = [];

async function open(viewport, theme) {
  const context = await browser.newContext({ viewport, reducedMotion: "reduce" });
  await context.addInitScript((theme) => {
    if (theme && !localStorage.getItem("starlights-ui-theme")) localStorage.setItem("starlights-ui-theme", theme);
    // the theme as the first paint sees it (index.html sets it before the app's script runs)
    document.addEventListener("DOMContentLoaded", () => { window.__firstTheme = document.documentElement.dataset.theme; }, { once: true });
    // layout shifts from the first paint on, read back by hero()
    window.__shift = 0;
    new PerformanceObserver((list) => list.getEntries().forEach((e) => { if (!e.hadRecentInput) window.__shift += e.value; })).observe({ type: "layout-shift", buffered: true });
  }, theme);
  const data = fixture();
  await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const req = route.request();
    let body = {};
    try { body = data.resolve(req.url(), req.method(), req.postDataJSON()); } catch { /* the homepage needs none */ }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  page.on("response", (r) => r.status() >= 400 && errors.push(`HTTP ${r.status()} ${new URL(r.url()).pathname}`));
  return { context, page, errors };
}

const hero = (page) => page.locator("header h1.starfall-brand").evaluate(async (h1) => {
  const emblem = h1.querySelector(".starfall-emblem");
  const url = getComputedStyle(emblem).backgroundImage.match(/url\("?([^")]+)"?\)/)?.[1] ?? "";
  const loaded = await new Promise((done) => { const img = new Image(); img.onload = () => done(img.naturalWidth > 0); img.onerror = () => done(false); img.src = url; });
  const box = h1.getBoundingClientRect();
  const intro = h1.nextElementSibling?.getBoundingClientRect();
  const tiles = [...document.querySelectorAll("main a")].filter((a) => a.querySelector("h3")).map((a) => ({ href: a.getAttribute("href"), r: a.getBoundingClientRect() }));
  return {
    emblem: url ? new URL(url, location.href).pathname : "", loaded, name: h1.textContent.trim(), portraits: document.querySelectorAll('main img[src*="/branding/starfall/"]').length,
    box: [box.width, box.height], introBeside: !!intro && intro.left >= box.right - 1 && intro.top < box.bottom, left: box.left, right: box.right, vw: innerWidth,
    tiles: tiles.map((t) => ({ href: t.href, x: t.r.left, y: t.r.top, w: t.r.width, h: t.r.height })),
    theme: document.documentElement.dataset.theme, firstTheme: window.__firstTheme,
    overflow: document.documentElement.scrollWidth - innerWidth, shift: window.__shift,
  };
});

function check(h, theme, label, width) {
  assert.equal(h.theme, theme, `${label}: page theme`);
  assert.equal(h.firstTheme, theme, `${label}: first paint had theme ${h.firstTheme}`);
  assert.equal(h.emblem, `/ornaments/${theme}/home-d20-icon.svg`, `${label}: emblem`);
  assert.ok(h.loaded, `${label}: emblem did not load`);
  assert.equal(h.name, "Starfall", `${label}: wordmark`);
  assert.equal(h.portraits, 0, `${label}: the old portrait artwork is shown`);
  assert.ok(h.box[1] <= h.box[0] * 0.85, `${label}: brand block is portrait-shaped ${h.box}`);
  if (width >= 640) assert.ok(h.introBeside, `${label}: introduction not beside the brand block`);
  assert.equal(h.tiles.length, 5, `${label}: feature tiles ${h.tiles.length}`);
  if (width >= 1024) {
    const [builder, ...small] = h.tiles;
    assert.equal(builder.href, "/characters", `${label}: first tile`);
    assert.ok(small.every((t) => t.x >= builder.x + builder.w - 1 && t.w < builder.w && t.h < builder.h), `${label}: four smaller tiles right of the Character Builder`);
  }
  assert.ok(h.left >= 0 && h.right <= h.vw + 0.5, `${label}: off screen ${h.left}..${h.right} of ${h.vw}`);
  assert.ok(h.overflow <= 1, `${label}: horizontal overflow ${h.overflow}`);
  assert.ok(h.shift < 0.02, `${label}: layout shift ${h.shift.toFixed(3)}`);
}

try {
  for (const theme of ["porcelain", "slate", "midnight"])
    for (const width of [320, 375, 768, 1024, 1440, 1920]) {
      const { context, page, errors } = await open({ width, height: 900 }, theme);
      await page.goto(base + "/");
      await page.locator("header h1.starfall-brand").waitFor();
      await page.waitForLoadState("networkidle");
      const h = await hero(page);
      check(h, theme, `${theme}/${width}`, width);
      assert.deepEqual(errors, [], `${theme}/${width}: ${errors.join("; ")}`);
      await page.screenshot({ path: `${output}/home-${theme}-${width}.png` });
      results.push({ theme, width, box: h.box.map(Math.round), shift: Number(h.shift.toFixed(4)) });
      await context.close();
    }

  // switching through the theme dialog, at a phone and a desktop width, then reloading
  for (const width of [375, 1440]) {
    const { context, page } = await open({ width, height: 900 }, "porcelain");
    await page.goto(base + "/");
    await page.locator("header h1.starfall-brand").waitFor();
    for (const [name, theme] of [["Midnight", "midnight"], ["Slate", "slate"], ["Porcelain", "porcelain"], ["Midnight", "midnight"]]) {
      await page.getByRole("button", { name: "Choose theme and units" }).first().click();
      await page.getByRole("button", { name: new RegExp(`^${name}`) }).click();
      // the preview already shows the emblem of the theme being tried
      await page.waitForFunction((t) => getComputedStyle(document.querySelector(".starfall-emblem")).backgroundImage.includes(`/ornaments/${t}/home-d20-icon.svg`), theme);
      await page.getByRole("button", { name: "Save theme" }).click();
      const h = await hero(page);
      assert.equal(h.emblem, `/ornaments/${theme}/home-d20-icon.svg`, `switch to ${theme} at ${width}`);
      assert.ok(h.loaded, `switch to ${theme} at ${width}: not loaded`);
    }
    await page.reload();
    await page.locator("header h1.starfall-brand").waitFor();
    check(await hero(page), "midnight", `reload at ${width}`, width);
    results.push({ switched: width, reload: "midnight" });
    await context.close();
  }

  // the favicons, the home-screen icon and the navigation emblem are the Starfall D20 icon set (public/icons), and load
  const { context, page } = await open({ width: 1280, height: 900 }, "slate");
  await page.goto(base + "/");
  const icons = await page.evaluate(() => [...document.querySelectorAll('link[rel~="icon"], link[rel="apple-touch-icon"]')].map((l) => new URL(l.href).pathname));
  assert.ok(icons.includes("/icons/favicon.ico") && icons.includes("/icons/apple-touch-icon.png"), `favicons ${icons}`);
  for (const icon of icons) {
    const response = await page.request.get(base + icon);
    assert.ok(response.ok() && /^image\//.test(response.headers()["content-type"] ?? ""), `${icon} is an image`);
  }
  const emblem = page.locator('header img[src="/icons/icon-192.png"]');
  assert.equal(await emblem.count() > 0, true, "navigation emblem");
  assert.ok(await emblem.first().evaluate((img) => img.complete && img.naturalWidth === 192), "navigation emblem loaded");
  await context.close();
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
  console.log(`homepage branding: ${results.length} checks passed`);
} finally {
  await browser.close();
}
