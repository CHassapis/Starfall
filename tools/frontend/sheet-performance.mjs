/**
 * How the PDF sheet treats the page while it is made, on a large synthetic character (fixture({ large: true }): 72
 * spells and 40 features with long made-up texts). Run against a local server of the built app (VITE_API_BASE="").
 * It records the main thread's long tasks (over 50 ms) from opening the sheet page until "Download PDF" is ready (the
 * PDF made) and on until every page is shown (pdf.js drawing them), and a timer that should fire every 20 ms: its
 * longest wait is how long the page could not answer a click. Prints the figures as JSON; with MAX_BLOCK_MS set, fails
 * when a single task while the PDF is made is longer.
 */
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { fixture, CHARACTER_ID } from "./fixtures.mjs";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : "playwright");
const base = process.env.FRONTEND_URL || "http://127.0.0.1:55052";
assert.ok(["localhost", "127.0.0.1"].includes(new URL(base).hostname), "Use a local test server");
const output = process.env.SCREENSHOT_DIR || "/tmp/starlights-frontend-shots";
const runs = Number(process.env.RUNS || 3);
const limit = process.env.MAX_BLOCK_MS ? Number(process.env.MAX_BLOCK_MS) : null;
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [];
const results = [];
try {
  for (let run = 0; run < runs; run++) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    await context.addInitScript(() => {
      localStorage.setItem("starlights.player", "Test Player");
      const w = window;
      w.__perf = { tasks: [], ready: null, shown: null, gaps: [] };
      new PerformanceObserver((list) => {
        for (const e of list.getEntries()) w.__perf.tasks.push({ start: Math.round(e.startTime), ms: Math.round(e.duration) });
      }).observe({ type: "longtask", buffered: true });
      // a timer every 20 ms: a long wait between two ticks is a page that cannot answer
      let last = performance.now();
      setInterval(() => {
        const now = performance.now();
        if (now - last > 40) w.__perf.gaps.push({ start: Math.round(last), ms: Math.round(now - last) });
        last = now;
      }, 20);
      // when the PDF is made ("Download PDF" can be pressed) and when every page is shown
      new MutationObserver(() => {
        const download = [...document.querySelectorAll("button")].find((b) => b.textContent?.includes("Download PDF"));
        if (download && !download.disabled && w.__perf.ready === null) w.__perf.ready = Math.round(performance.now());
        const print = [...document.querySelectorAll("button")].find((b) => b.textContent?.includes("Print"));
        if (print && !print.disabled && w.__perf.shown === null) w.__perf.shown = Math.round(performance.now());
      }).observe(document, { subtree: true, childList: true, attributes: true });
    });
    const data = fixture({ large: true });
    await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
      const req = route.request();
      try {
        await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(data.resolve(req.url(), req.method(), req.postDataJSON())) });
      } catch (error) {
        errors.push(error.message);
        await route.fulfill({ status: 501, contentType: "application/json", body: '{"error":"missing fixture"}' });
      }
    });
    const page = await context.newPage();
    page.on("pageerror", (e) => errors.push(e.message));
    const opened = Date.now();
    await page.goto(`${base}/characters/${CHARACTER_ID}/sheet`);
    await page.getByRole("button", { name: "Download PDF" }).and(page.locator(":enabled")).waitFor({ timeout: 120_000 });
    await page.getByRole("button", { name: "Print" }).and(page.locator(":enabled")).waitFor({ timeout: 180_000 });
    const pages = await page.getByRole("img", { name: /character sheet, page/ }).count();
    const perf = await page.evaluate(() => window.__perf);
    const before = (t) => t.start < perf.ready;
    const sum = (list) => list.reduce((n, t) => n + t.ms, 0);
    const longest = (list) => list.reduce((n, t) => Math.max(n, t.ms), 0);
    const making = perf.tasks.filter(before);
    const drawing = perf.tasks.filter((t) => !before(t));
    results.push({
      run: run + 1,
      pages,
      wallSeconds: Math.round((Date.now() - opened) / 100) / 10,
      readyMs: perf.ready,
      shownMs: perf.shown,
      making: { longTasks: making.length, longestMs: longest(making), blockingMs: sum(making.map((t) => ({ ms: Math.max(0, t.ms - 50) }))), longestTimerWaitMs: longest(perf.gaps.filter(before)) },
      drawing: { longTasks: drawing.length, longestMs: longest(drawing), blockingMs: sum(drawing.map((t) => ({ ms: Math.max(0, t.ms - 50) }))), longestTimerWaitMs: longest(perf.gaps.filter((t) => !before(t))) },
    });
    if (run === 0) await page.screenshot({ path: `${output}/sheet-large.png` });
    await context.close();
  }
  await writeFile(`${output}/sheet-performance.json`, JSON.stringify(results, null, 2));
  console.log(JSON.stringify(results));
  assert.deepEqual(errors, [], "page errors or missing fixtures");
  assert.ok(results.every((r) => r.pages >= 10), `a large sheet (${results.map((r) => r.pages).join(", ")} pages)`);
  if (limit !== null) {
    const worst = Math.max(...results.map((r) => r.making.longestMs));
    assert.ok(worst <= limit, `while the PDF is made, no task on the page's thread is longer than ${limit} ms (longest ${worst} ms)`);
    console.log(`sheet performance: the longest task while the PDF is made is ${worst} ms (at most ${limit})`);
  }
} finally {
  await browser.close();
}
