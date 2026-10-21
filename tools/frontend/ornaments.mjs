/**
 * The theme ornaments (public/ornaments/<theme>/) across the app. Run ONLY against a local, disposable install
 * (built app + API with test data); it reads, never writes. Env: FRONTEND_URL, TOKEN (an admin token of that
 * install), SCREENSHOT_DIR; SHOTS_ONLY=1 takes the screenshots without the ornament checks (for a build without them).
 * For each page, theme and width: no horizontal overflow, no page errors, only the current theme's ornaments loaded
 * (all found), decorations hidden from assistive technology and not clickable, the shell frame's four corners and four
 * edge jewels attached to its border line (none on phones), keyboard focus never covered by a decoration. Then: theme
 * switching without reload, print, and every ornament file missing (the pages must look and work the same, minus the
 * pictures).
 */
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : "playwright");
const base = process.env.FRONTEND_URL || "http://127.0.0.1:8198";
assert.ok(["localhost", "127.0.0.1"].includes(new URL(base).hostname), "Use a local, disposable install");
const token = process.env.TOKEN ?? "";
const shotsOnly = process.env.SHOTS_ONLY === "1";
const output = process.env.SCREENSHOT_DIR || "/tmp/starfall-ornament-shots";
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const results = [];

async function open(viewport, theme, { missing = false } = {}) {
  const context = await browser.newContext({ viewport, reducedMotion: "reduce" });
  await context.addInitScript(({ theme, token }) => {
    localStorage.setItem("starlights-ui-theme", theme);
    localStorage.setItem("starlights.player", "Mk2 Tester");
    if (token) localStorage.setItem("starlights.player-tokens", JSON.stringify({ "*admin*": token }));
  }, { theme, token });
  if (missing) await context.route((url) => url.pathname.startsWith("/ornaments/"), (route) => route.abort());
  const page = await context.newPage();
  const errors = [];
  const ornaments = [];
  page.on("pageerror", (e) => errors.push(e.message.slice(0, 200)));
  page.on("response", (r) => {
    const path = new URL(r.url()).pathname;
    if (path.startsWith("/ornaments/")) ornaments.push({ path, status: r.status() });
    else if (r.status() >= 500) errors.push(`HTTP ${r.status()} ${path}`);
  });
  return { context, page, errors, ornaments };
}

// what the page shows of the ornaments, measured in the page
const inspect = (page) => page.evaluate(() => {
  const vis = (el) => { const s = getComputedStyle(el); const r = el.getBoundingClientRect(); return s.display !== "none" && s.visibility !== "hidden" && r.width > 0 && r.height > 0; };
  // the shell's corners draw their rule 13/256 in from their corner, the edges through their middle: both must lie on
  // the frame's border line (its centre is half a pixel inside the box)
  const shell = document.querySelector(".starfall-shell");
  const frame = shell?.getBoundingClientRect();
  const framed = !!shell && parseFloat(getComputedStyle(shell).borderTopWidth) > 0;
  const near = (a, b) => Math.abs(a - b) <= 1.5;
  const corners = [...document.querySelectorAll(".shell-ornament")].filter(vis).map((el) => {
    const r = el.getBoundingClientRect();
    const k = el.dataset.corner, e = el.dataset.edge, inset = (r.width * 13) / 256;
    const attached = k
      ? near(k.includes("l") ? r.left + inset : r.right - inset, k.includes("l") ? frame.left + 0.5 : frame.right - 0.5) &&
        near(k.startsWith("t") ? r.top + inset : r.bottom - inset, k.startsWith("t") ? frame.top + 0.5 : frame.bottom - 0.5)
      : e === "top" || e === "bottom" ? near(r.top + r.height / 2, e === "top" ? frame.top + 0.5 : frame.bottom - 0.5)
      : near(r.left + r.width / 2, e === "left" ? frame.left + 0.5 : frame.right - 0.5);
    return { kind: k ?? e, hidden: el.getAttribute("aria-hidden") === "true", pe: getComputedStyle(el).pointerEvents, attached };
  });
  const tiles = [...document.querySelectorAll(".ornament-corner")].filter(vis).map((el) => ({ hidden: el.getAttribute("aria-hidden") === "true", pe: getComputedStyle(el).pointerEvents }));
  const dividers = [...document.querySelectorAll(".celestial-divider")].filter(vis).map((el) => ({ hidden: el.getAttribute("aria-hidden") === "true", pe: getComputedStyle(el).pointerEvents }));
  const pseudo = [...document.querySelectorAll("[data-ornament]")].filter(vis).map((el) => getComputedStyle(el, "::before").pointerEvents);
  const nav = document.querySelector(".nav-active");
  return {
    overflow: document.documentElement.scrollWidth - innerWidth, height: document.documentElement.scrollHeight,
    framed, corners, tiles, dividers, pseudo, navUnderline: nav ? getComputedStyle(nav).backgroundImage : null, theme: document.documentElement.dataset.theme,
    share: frame ? frame.width / innerWidth : 0,
  };
});

// tab through the first controls: what is focused must be what is on top at its centre
async function focusClear(page, presses = 12) {
  const covered = [];
  for (let i = 0; i < presses; i++) {
    await page.keyboard.press("Tab");
    const hit = await page.evaluate(() => {
      const el = document.activeElement;
      if (!el || el === document.body) return null;
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth) return null;
      const top = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
      return !top || el === top || el.contains(top) || top.contains(el) ? null : `${el.tagName} covered by ${top.className || top.tagName}`;
    });
    if (hit) covered.push(hit);
  }
  return covered;
}

const list = token ? await (await fetch(`${base}/api/characters`, { headers: { "X-Player-Token": token } })).json() : { characters: [] };
const id = process.env.CHARACTER_ID || list.characters?.[0]?.characterId;
const pages = [
  ["home", "/"], ["characters", "/characters"], ["builder", `/characters/${id}`], ["sheet", `/characters/${id}/play`],
  ["campaigns", "/campaigns"], ["battle", `/battle/${id}`], ["compendium", "/compendium"], ["homebrew", "/homebrew"], ["lore", "/lore"],
  ["content", "/content"], ["about", "/about"],
];
const heights = {};
const known = (process.env.KNOWN_OVERFLOW ?? "").split(",").filter(Boolean);

try {
  for (const theme of ["porcelain", "slate", "midnight"])
    // 2560 × 1440 and 3840 × 2160: the large-monitor frame (wider, 144px corners) and the larger type step
    for (const width of [390, 768, 1440, 1920, 2560, 3840]) {
      for (const [name, path] of pages) {
        if ((width === 1920 || width === 768) && !["home", "compendium", "homebrew", "campaigns"].includes(name)) continue;
        if (width > 1920 && !["home", "campaigns", "lore"].includes(name)) continue;
        const { context, page, errors, ornaments } = await open({ width, height: width > 1920 ? (width * 9) / 16 : 900 }, theme);
        await page.goto(base + path);
        await page.waitForLoadState("networkidle");
        await page.waitForTimeout(500);
        await page.screenshot({ path: `${output}/${name}-${theme}-${width}.png` });
        const m = await inspect(page);
        heights[`${name}/${theme}/${width}`] = m.height;
        const label = `${name}/${theme}/${width}`;
        // KNOWN_OVERFLOW lists pages that overflow without this change too (shown by a SHOTS_ONLY run of the baseline)
        if (!shotsOnly && !known.includes(name)) assert.ok(m.overflow <= 1, `${label}: horizontal overflow ${m.overflow}`);
        assert.deepEqual(errors, [], `${label}: ${errors.join("; ")}`);
        if (!shotsOnly) {
          assert.equal(m.theme, theme, `${label}: theme`);
          assert.ok(ornaments.every((o) => o.path.startsWith(`/ornaments/${theme}/`)), `${label}: other themes' ornaments loaded ${ornaments.map((o) => o.path)}`);
          assert.ok(ornaments.every((o) => o.status === 200 || o.status === 304), `${label}: ornament missing ${JSON.stringify(ornaments)}`);
          assert.ok(m.corners.every((c) => c.hidden && c.pe === "none" && c.attached), `${label}: shell ornament ${JSON.stringify(m.corners)}`);
          assert.equal(m.framed, width >= 768, `${label}: shell frame ${m.framed ? "on a phone" : "missing"}`);
          assert.equal(m.corners.length, width >= 768 ? 8 : 0, `${label}: ${m.corners.length} shell corners and edges`);
          // large monitors: the frame uses the screen (no 1440px island in the middle of a 2560 or 3840 window)
          if (width >= 2560) assert.ok(m.share >= 0.6, `${label}: frame is ${Math.round(m.share * 100)}% of the window`);
          assert.ok(m.tiles.every((t) => t.hidden && t.pe === "none"), `${label}: tile corner ${JSON.stringify(m.tiles)}`);
          assert.ok(m.dividers.every((d) => d.hidden && d.pe === "none"), `${label}: divider ${JSON.stringify(m.dividers)}`);
          assert.ok(m.pseudo.every((pe) => pe === "none"), `${label}: corner takes clicks`);
          if (m.navUnderline) assert.ok(m.navUnderline.includes(`/ornaments/${theme}/nav-active-underline.svg`), `${label}: nav underline ${m.navUnderline}`);
          const covered = await focusClear(page);
          assert.deepEqual(covered, [], `${label}: focus covered: ${covered.join("; ")}`);
        }
        results.push({ page: name, theme, width, overflow: m.overflow, shell: m.corners.length, tiles: m.tiles.length, dividers: m.dividers.length, ornaments: [...new Set(ornaments.map((o) => o.path.split("/").pop()))] });
        await context.close();
      }
    }

  if (!shotsOnly) {
    // the theme dialog ("settings"): switching changes the ornaments at once, no reload; the dialog's own corners too
    const { context, page, ornaments } = await open({ width: 1440, height: 900 }, "porcelain");
    await page.goto(base + "/characters");
    await page.waitForLoadState("networkidle");
    for (const [name, theme] of [["Midnight", "midnight"], ["Slate", "slate"], ["Porcelain", "porcelain"]]) {
      await page.getByRole("button", { name: "Choose theme and units" }).first().click();
      const dialog = page.getByRole("dialog");
      const corner = await dialog.evaluate((el) => getComputedStyle(el, "::before").backgroundImage);
      assert.ok(corner.includes("/panel-corner.svg"), `dialog corner ${corner}`);
      await page.getByRole("button", { name: new RegExp(`^${name}`) }).click();
      await page.getByRole("button", { name: "Save theme" }).click();
      await page.waitForFunction((t) => getComputedStyle(document.querySelector(".nav-active")).backgroundImage.includes(`/ornaments/${t}/`), theme);
      if (name === "Midnight") await page.screenshot({ path: `${output}/settings-dialog-midnight.png` });
    }
    assert.ok(["midnight", "slate"].every((t) => ornaments.some((o) => o.path.startsWith(`/ornaments/${t}/`))), "ornaments followed the theme");
    results.push({ switched: "porcelain→midnight→slate→porcelain", reload: false });

    // printing hides them
    await page.goto(base + "/");
    await page.emulateMedia({ media: "print" });
    const printed = await page.evaluate(() => [...document.querySelectorAll(".shell-ornament, .ornament-corner, .celestial-divider")].filter((el) => getComputedStyle(el).display !== "none").length);
    assert.equal(printed, 0, "ornaments in print");
    const printFrame = await page.evaluate(() => getComputedStyle(document.querySelector(".starfall-shell")).borderTopWidth);
    assert.equal(printFrame, "0px", "shell frame in print");
    await context.close();

    // every ornament file missing: same layout, no errors
    for (const [name, path] of pages)
      for (const width of [390, 1440]) {
        const { context, page, errors } = await open({ width, height: 900 }, "midnight", { missing: true });
        await page.goto(base + path);
        await page.waitForLoadState("networkidle");
        await page.waitForTimeout(500);
        const m = await inspect(page);
        if (!known.includes(name)) assert.ok(m.overflow <= 1, `missing/${name}/${width}: overflow`);
        assert.deepEqual(errors, [], `missing/${name}/${width}: ${errors.join("; ")}`);
        assert.ok(Math.abs(m.height - heights[`${name}/midnight/${width}`]) <= 2, `missing/${name}/${width}: height ${m.height} vs ${heights[`${name}/midnight/${width}`]}`);
        await context.close();
      }
    results.push({ missingAssets: "same height, no errors" });
  }
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
  console.log(`ornaments: ${results.length} checks passed${shotsOnly ? " (screenshots only)" : ""}`);
} finally {
  await browser.close();
}
