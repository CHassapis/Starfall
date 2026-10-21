/**
 * Imperial / Metric. Run against a local server of the built app (VITE_API_BASE=""); every API request is answered by
 * the synthetic fixture. Imperial is the default; choosing Metric in the theme dialog changes the character sheet at
 * once (speed in metres), without saving the theme; the choice survives a reload and reaches another open tab; Imperial
 * again restores exactly the original text. The stored value is the only thing written (`starfall-units`).
 */
import assert from "node:assert/strict";
import { pathToFileURL } from "node:url";
import { fixture, CHARACTER_ID } from "./fixtures.mjs";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : "playwright");
const base = process.env.FRONTEND_URL || "http://127.0.0.1:55052";
assert.ok(["localhost", "127.0.0.1"].includes(new URL(base).hostname), "Use a local test server");
const browser = await chromium.launch({ headless: true });
let checks = 0;
const ok = (value, message) => { assert.ok(value, message); checks++; };
try {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: "reduce" });
  await context.addInitScript(() => localStorage.setItem("starlights.player", "Test Player"));
  const data = fixture();
  const writes = [];
  await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const req = route.request();
    if (req.method() !== "GET") writes.push(`${req.method()} ${new URL(req.url()).pathname}`);
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(data.resolve(req.url(), req.method(), req.postDataJSON())) });
  });
  const errors = [];
  const open = async () => {
    const page = await context.newPage();
    page.on("pageerror", (e) => errors.push(e.message));
    await page.goto(`${base}/characters/${CHARACTER_ID}/play`);
    await page.getByRole("heading", { name: /^Mira Vale$/i }).waitFor();
    return page;
  };
  const speed = (page) => page.locator("span", { hasText: /^Speed$/ }).first().locator("xpath=following-sibling::span[1]").innerText();

  const page = await open();
  const imperial = await speed(page);
  ok(/^\d+ ft$/i.test(imperial), `Imperial by default: speed ${imperial}`);
  ok((await page.evaluate(() => localStorage.getItem("starfall-units"))) === null, "nothing stored until chosen");
  const theme = await page.evaluate(() => document.documentElement.dataset.theme);

  // a second tab, open while the choice is made
  const other = await open();

  await page.getByRole("button", { name: "Choose theme and units", exact: true }).click();
  const group = page.getByRole("group", { name: "Measurement units" });
  ok((await group.getByRole("button", { name: /^Imperial/ }).getAttribute("aria-pressed")) === "true", "Imperial pressed");
  await group.getByRole("button", { name: /^Metric/ }).click();
  ok((await group.getByRole("button", { name: /^Metric/ }).getAttribute("aria-pressed")) === "true", "Metric pressed");
  // applies at once, without "Save theme"; closing the dialog keeps it and leaves the theme alone
  await page.keyboard.press("Escape");
  const metric = await speed(page);
  const feet = Number(imperial.match(/\d+/)[0]);
  ok(metric.toLowerCase() === `${(feet * 3) / 10} m`, `Metric speed ${metric} for ${imperial}`);
  ok((await page.evaluate(() => localStorage.getItem("starfall-units"))) === "metric", "stored");
  ok((await page.evaluate(() => document.documentElement.dataset.theme)) === theme, "theme unchanged");

  await other.waitForFunction(() => /\bm$/i.test([...document.querySelectorAll("span")].find((s) => s.textContent === "Speed")?.nextElementSibling?.textContent ?? ""));
  ok(/m$/i.test(await speed(other)), "the other tab follows");

  await page.reload();
  await page.getByRole("heading", { name: /^Mira Vale$/i }).waitFor();
  ok((await speed(page)).toLowerCase() === metric.toLowerCase(), "survives a reload");

  await page.getByRole("button", { name: "Choose theme and units", exact: true }).click();
  await page.getByRole("group", { name: "Measurement units" }).getByRole("button", { name: /^Imperial/ }).click();
  await page.keyboard.press("Escape");
  ok((await speed(page)) === imperial, "Imperial restores the original text");

  ok(writes.length === 0, `no data written: ${writes.join(", ")}`);
  ok(errors.length === 0, errors.join("; "));
  await context.close();

  // a weight input in Metric: shown in kg, saved untouched it keeps the stored pounds exactly; typed, kg × 2
  const admin = await browser.newContext({ viewport: { width: 1280, height: 900 }, reducedMotion: "reduce" });
  await admin.addInitScript(() => {
    localStorage.setItem("starlights.player", "Test Player");
    localStorage.setItem("starlights.player-tokens", JSON.stringify({ "*admin*": "test-admin" }));
    localStorage.setItem("starfall-units", "metric");
  });
  const brew = fixture();
  const saved = [];
  const item = { id: "hb-lantern", name: "Test Lantern", rarity: "Common", kind: "Wondrous item", description: "It glows.", weight: 3, attunement: false, source: "Homebrew" };
  await admin.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const req = route.request();
    const path = new URL(req.url()).pathname;
    let body;
    if (path === "/api/elements/homebrew/items" && req.method() === "PUT") saved.push(req.postDataJSON()), (body = req.postDataJSON());
    else if (path === "/api/elements/homebrew/items") body = { items: [item] };
    else if (path.startsWith("/api/elements/homebrew/")) body = { [path.split("/").pop()]: [] };
    else body = brew.resolve(req.url(), req.method(), req.postDataJSON());
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  const hb = await admin.newPage();
  hb.on("pageerror", (e) => errors.push(e.message));
  await hb.goto(`${base}/homebrew`);
  const save = async (typed) => {
    await hb.getByRole("button", { name: "Edit Test Lantern" }).click();
    const weight = hb.getByRole("dialog").locator("label", { hasText: "Weight in kg" }).locator("input");
    ok((await weight.inputValue()) === "1.5", `3 lb shown as ${await weight.inputValue()} kg`);
    if (typed !== undefined) await weight.fill(typed);
    await hb.getByRole("dialog").getByRole("button", { name: "Save", exact: true }).click();
    await hb.getByRole("dialog").waitFor({ state: "detached" });
  };
  await save();
  ok(saved.at(-1)?.weight === 3, `saved untouched: weight ${saved.at(-1)?.weight} (stored 3)`);
  await save("2");
  ok(saved.at(-1)?.weight === 4, `typed 2 kg: weight ${saved.at(-1)?.weight} lb`);
  ok(errors.length === 0, errors.join("; "));
  await admin.close();
  console.log(`units: ${checks} checks passed`);
} finally {
  await browser.close();
}
