/**
 * "Why this value?" on the interactive sheet. Run against a local server of the built app (VITE_API_BASE=""); every
 * API request is answered by the synthetic fixture. On a desktop (a popover) and a 360px touch phone (a bottom sheet):
 * armor class, hit point maximum and initiative are explained by the web app's own calculation; a skill (with the
 * ability modifier it refers to, asked for when opened) and the spell save DC by the server's trace endpoint; with the
 * Shield spell in play the armor class card says the figure shown includes it; explaining writes nothing; no
 * horizontal overflow and no page errors.
 */
import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { fixture, CHARACTER_ID } from "./fixtures.mjs";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : "playwright");
const base = process.env.FRONTEND_URL || "http://127.0.0.1:55052";
assert.ok(["localhost", "127.0.0.1"].includes(new URL(base).hostname), "Use a local test server");
const output = process.env.SCREENSHOT_DIR || "/tmp/starlights-frontend-shots";
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [];
let checks = 0;
const ok = (value, message) => {
  assert.ok(value, message);
  checks++;
};
try {
  for (const device of [
    { name: "desktop", viewport: { width: 1280, height: 900 } },
    { name: "phone", viewport: { width: 360, height: 780 }, hasTouch: true, isMobile: true },
  ]) {
    const context = await browser.newContext({ viewport: device.viewport, hasTouch: !!device.hasTouch, isMobile: !!device.isMobile, reducedMotion: "reduce" });
    await context.addInitScript(() => localStorage.setItem("starlights.player", "Test Player"));
    const data = fixture();
    const writes = [];
    const traced = [];
    await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
      const req = route.request();
      const url = new URL(req.url());
      if (req.method() !== "GET") writes.push(`${req.method()} ${url.pathname}`);
      if (url.pathname.endsWith("/calculation/trace")) traced.push(url.searchParams.get("value"));
      try {
        await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(data.resolve(req.url(), req.method(), req.postDataJSON())) });
      } catch (error) {
        errors.push(error.message);
        await route.fulfill({ status: 501, contentType: "application/json", body: '{"error":"missing fixture"}' });
      }
    });
    const page = await context.newPage();
    page.on("pageerror", (e) => errors.push(e.message));
    const noOverflow = async (what) => {
      const size = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, width: innerWidth }));
      ok(size.scroll <= size.width + 1, `${device.name} ${what}: horizontal overflow ${size.scroll} > ${size.width}`);
    };
    const explain = async (label, shot) => {
      const button = page.getByRole("button", { name: `Why this value? ${label}`, exact: true });
      await button.scrollIntoViewIfNeeded();
      await button.click();
      const card = page.getByRole("dialog").last();
      await card.waitFor();
      await page.waitForTimeout(150);
      await noOverflow(label);
      if (shot) await page.screenshot({ path: `${output}/why-${shot}-${device.name}.png` });
      return card;
    };
    const close = async () => {
      await page.keyboard.press("Escape");
      await page.getByRole("dialog").waitFor({ state: "detached" });
    };

    await page.goto(`${base}/characters/${CHARACTER_ID}/play`);
    await page.getByRole("heading", { name: "Mira Vale" }).waitFor();
    await noOverflow("sheet");
    const before = writes.length;

    // the web app's own figures: worked out on the spot, no request
    let card = await explain("Armor class", "armor-class");
    ok(await card.getByText("Armor class: 11").first().isVisible(), "armor class 11");
    ok(await card.getByText("Unarmored (10 + Dexterity)").first().isVisible(), "the unarmored calculation is named");
    await close();
    card = await explain("Hit point maximum", "hit-points");
    ok(await card.getByText("Hit points: 17").first().isVisible(), "hit points 17: d6 + 2 × 4 + Con 1 × 3");
    ok(await card.getByText("Level 1: the whole d6").first().isVisible(), "the first level's whole die");
    await card.getByText("How Constitution for each level was worked out").click();
    ok(await card.getByText("× 3").first().isVisible(), "Constitution times the level is a multiplication");
    await close();
    card = await explain("Initiative");
    ok(await card.getByText("Initiative: 1").first().isVisible(), "initiative +1");
    await close();
    ok(traced.length === 0, `the web app's figures need no request (${traced.join(", ")})`);

    // the server's: asked for when opened, and what a step refers to when that is opened
    card = await explain("Arcana", "skill");
    await card.getByText("Arcana: 5").first().waitFor();
    await card.getByText("How Intelligence modifier was worked out").click();
    await card.getByText("Intelligence modifier: 3").first().waitFor();
    ok(await card.getByText("rounded down").first().isVisible(), "the modifier's rounding is shown");
    await close();
    await page.locator("#spellcasting > details > summary").click();
    card = await explain("Wizard spell save DC");
    await card.getByText("Wizard spell save DC: 13").first().waitFor();
    await close();
    ok(traced.join(",") === "skill:arcana,ability-modifier:intelligence,spell-save-dc:wizard", `traces asked for: ${traced.join(",")}`);
    ok(writes.length === before, `explaining wrote nothing (${writes.slice(before).join(", ")})`);

    // with the Shield spell in play the armor class shown is 16; the card explains the sheet's 11 and says so
    data.combat.effects.push("shield");
    await page.reload();
    await page.getByRole("heading", { name: "Mira Vale" }).waitFor();
    card = await explain("Armor class", "armor-class-shield");
    ok(await card.getByText("Shown as 16 with temporary effects (Shield +5). This is the sheet's 11 before them.").first().isVisible(), "the Shield spell is named");
    await close();
    await context.close();
  }
  assert.deepEqual(errors, [], "page errors or missing fixtures");
  console.log(`calculation traces: ${checks} checks passed (desktop popover, 360px bottom sheet)`);
} finally {
  await browser.close();
}
