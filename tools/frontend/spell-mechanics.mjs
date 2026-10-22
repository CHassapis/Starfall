/**
 * A homebrew spell's mechanics, said outright. Run against a local server of the built app (VITE_API_BASE=""); every
 * API request is answered by the synthetic fixture (and the homebrew API here). On a desktop and a 360px touch phone:
 * the spell form shows the mechanics that fit the roll and level and sends only those; in the battle view a cantrip
 * whose mechanics say "1d8 radiant, 2d8 from level 3" rolls 2d8 for the level 3 fixture character, its "Why?" works it
 * out on the spot (1 die, +1 from level 3), and its "what it does" says the numbers come from the mechanics. Explaining
 * writes nothing; no horizontal overflow, no page errors.
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
    await context.addInitScript(() => {
      localStorage.setItem("starlights.player", "Test Player");
      localStorage.setItem("starlights.player-tokens", JSON.stringify({ "*admin*": "synthetic-admin-token" }));
    });
    const data = fixture();
    // Arc Light, a cantrip Mira Vale knows, as a homebrew spell that says what it rolls
    Object.assign(data.entries.find((e) => e.id === "spell-0").setters, {
      "starlights-spell-roll": "save",
      "starlights-spell-save": "Dexterity",
      "starlights-spell-save-success": "half",
      "starlights-spell-damage": "1d8 radiant",
      "starlights-spell-cantrip-scaling": "3:2d8; 11:3d8",
    });
    const writes = [];
    const traced = [];
    const previews = [];
    await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
      const req = route.request();
      const url = new URL(req.url());
      const p = url.pathname;
      const json = (status, body) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
      if (p.endsWith("/calculation/trace")) traced.push(url.searchParams.get("value"));
      if (p === "/api/elements/homebrew/spells" && req.method() === "GET") return json(200, { spells: [] });
      if (p === "/api/elements/homebrew/classes") return json(200, { classes: [] });
      if (p === "/api/elements/homebrew/spells/preview") {
        previews.push(req.postDataJSON());
        return json(200, { kind: "spells", id: "ID_STARLIGHTS_HOMEBREW_SPELL_TEST_FLARE", isNew: true, revision: null, changes: [], elements: [], characters: [], diagnostics: [] });
      }
      if (req.method() !== "GET") writes.push(`${req.method()} ${p}`);
      try {
        await json(200, data.resolve(req.url(), req.method(), req.postDataJSON()));
      } catch (error) {
        errors.push(error.message);
        await json(501, { error: "missing fixture" });
      }
    });
    const page = await context.newPage();
    page.on("pageerror", (e) => errors.push(e.message));
    const noOverflow = async (what) => {
      const size = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, width: innerWidth }));
      ok(size.scroll <= size.width + 1, `${device.name} ${what}: horizontal overflow ${size.scroll} > ${size.width}`);
      const box = await page.getByRole("dialog").last().boundingBox().catch(() => null);
      if (box) ok(box.x >= 0 && box.x + box.width <= size.width + 1, `${device.name} ${what}: the dialog (${box.x}–${box.x + box.width}) is wider than the screen (${size.width})`);
    };

    // the form: nothing of the mechanics until a roll is chosen, then what fits the roll and the level
    await page.goto(`${base}/homebrew?tab=spells`);
    await page.getByRole("button", { name: "New spell" }).first().click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("textbox").first().fill("Test Flare");
    const mechanics = dialog.getByRole("group", { name: "What it rolls (optional)" });
    ok((await mechanics.getByRole("textbox", { name: /^Damage/ }).count()) === 0, "no mechanics fields while the roll is not said");
    await mechanics.getByRole("combobox", { name: /^Roll/ }).selectOption("save");
    await mechanics.getByRole("combobox", { name: /^Saving throw/ }).selectOption("Constitution");
    await mechanics.getByRole("textbox", { name: /^Damage/ }).fill("3d6 fire");
    ok(await mechanics.getByRole("textbox", { name: /^Dice for each slot level above/ }).isVisible(), "a 1st-level spell scales by slot level");
    await mechanics.getByRole("textbox", { name: /^Dice for each slot level above/ }).fill("1d6");
    await mechanics.getByRole("checkbox", { name: "Half damage on a successful save" }).check();
    await noOverflow("spell form");
    await mechanics.scrollIntoViewIfNeeded();
    await page.screenshot({ path: `${output}/spell-mechanics-form-${device.name}.png` });
    await dialog.getByRole("button", { name: "Review and save" }).click();
    await dialog.getByRole("region", { name: "What saving does" }).waitFor();
    const sent = previews.at(-1);
    ok(sent.roll === "save" && sent.saveAbility === "Constitution" && sent.halfOnSave === true && sent.damage === "3d6 fire" && sent.slotScaling === "1d6", `the review sends the mechanics (${JSON.stringify(sent)})`);
    // an attack has no saving throw, and a cantrip scales by character level: what no longer fits is not sent
    await dialog.getByRole("button", { name: "Back to editing" }).click();
    await mechanics.getByRole("combobox", { name: /^Roll/ }).selectOption("ranged-attack");
    await dialog.locator("select").first().selectOption("0");
    ok(await mechanics.getByRole("textbox", { name: /^Cantrip dice by character level/ }).isVisible(), "a cantrip scales by character level");
    ok((await mechanics.getByRole("combobox", { name: /^Saving throw/ }).count()) === 0, "an attack asks for no saving throw");
    await dialog.getByRole("button", { name: "Review and save" }).click();
    await dialog.getByRole("region", { name: "What saving does" }).waitFor();
    const refit = previews.at(-1);
    ok(refit.roll === "ranged-attack" && refit.saveAbility === null && refit.halfOnSave === false && refit.slotScaling === null && refit.damage === "3d6 fire", `only what fits is sent (${JSON.stringify(refit)})`);
    await page.keyboard.press("Escape");
    await dialog.waitFor({ state: "detached" });

    // the battle view: Arc Light rolls what its mechanics say at level 3
    await page.goto(`${base}/battle/${CHARACTER_ID}`);
    await page.getByRole("heading", { name: /^Mira Vale$/i }).waitFor();
    const before = writes.length;
    await page.getByRole("tab", { name: "Spells" }).click();
    const why = page.getByRole("button", { name: "Why this value? Arc Light damage dice", exact: true });
    await why.scrollIntoViewIfNeeded();
    ok(await page.getByText("2d8 radiant").first().isVisible(), "2d8 radiant at level 3 (1d8, 2d8 from level 3)");
    await why.click();
    const card = page.getByRole("dialog").last();
    await card.getByText("Arc Light damage dice: 2").first().waitFor();
    ok(await card.getByText("1d8 radiant, as its mechanics say").first().isVisible(), "it starts from the mechanics' dice");
    ok(await card.getByText("From character level 3 it rolls 2d8 (level 3)").first().isVisible(), "the tier reached");
    await noOverflow("spell trace");
    await page.screenshot({ path: `${output}/spell-mechanics-why-${device.name}.png` });
    await page.keyboard.press("Escape");
    await page.getByRole("dialog").waitFor({ state: "detached" });
    ok((await page.getByRole("button", { name: /^Why this value\? Beacon/ }).count()) === 0, "a spell that rolls nothing has nothing to explain");

    // its "what it does" says where the numbers come from
    await page.getByRole("button", { name: "Arc Light: what it does" }).click();
    const info = page.getByRole("dialog").last();
    await info.getByText("From the spell's mechanics.").waitFor();
    ok(await info.getByText(/Dexterity saving throw, half damage on a success/).isVisible(), "the save and half damage, from the mechanics");
    await noOverflow("spell info");
    ok(traced.length === 0, `the spell's figures need no request (${traced.join(", ")})`);
    ok(writes.length === before, `explaining wrote nothing (${writes.slice(before).join(", ")})`);
    await context.close();
  }
  assert.deepEqual(errors, [], "page errors or missing fixtures");
  console.log(`spell mechanics: ${checks} checks passed (desktop, 360px phone)`);
} finally {
  await browser.close();
}
