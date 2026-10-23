/**
 * Favorite rolls. Run against a local server of the built app (VITE_API_BASE=""); every API request is answered by
 * the synthetic fixture. Every die lands on its middle face (Math.random is 0.5: a d8 is 5, a d20 is 11). On a desktop
 * and a 360px touch phone: in the battle view "Test Smite: 2d8+3" is added (it rolls 5 to 19); "2d6 fire" is refused
 * as dice before "Test Luck: d20+5" is added; Test Smite rolls 13 (5 + 5 + 3), is changed to 3d8+3 and rolls 18; the
 * sheet shows the same two rolls, Test Luck rolls 16 (11 + 5) there and is removed there. Everything is saved in the
 * combat state, nowhere else, and is there again after a reload; no horizontal overflow, no page errors.
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
      Math.random = () => 0.5;
    });
    const data = fixture();
    const writes = [];
    await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
      const req = route.request();
      const url = new URL(req.url());
      if (req.method() !== "GET") writes.push(`${req.method()} ${url.pathname}`);
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
      const box = await page.getByRole("dialog").last().boundingBox().catch(() => null);
      if (box) ok(box.x >= 0 && box.x + box.width <= size.width + 1, `${device.name} ${what}: the dialog (${box.x}–${box.x + box.width}) is wider than the screen (${size.width})`);
    };
    const saved = async (check, what) => {
      await page.waitForFunction(() => !document.querySelector('[aria-busy="true"]')).catch(() => {});
      for (let i = 0; i < 40 && !check(); i++) await page.waitForTimeout(100);
      ok(check(), what);
    };
    const own = () => data.combat.favoriteRolls ?? [];
    const summary = () => own().map((f) => `${f.name}=${f.formula}`).join();

    await page.goto(`${base}/battle/${CHARACTER_ID}`);
    await page.getByRole("heading", { name: /^Mira Vale$/i }).waitFor();
    let panel = page.locator("section").filter({ has: page.getByRole("heading", { name: "Favorite rolls" }) });
    await panel.scrollIntoViewIfNeeded();
    ok(await panel.getByText(/^Name the dice you roll often/).isVisible(), "an empty list says what it is for");

    // Test Smite: 2d8+3, added with the Enter key
    await panel.getByRole("button", { name: "Add a favorite roll" }).click();
    let dialog = page.getByRole("dialog");
    await dialog.getByRole("textbox", { name: "Name" }).fill("Test Smite");
    await dialog.getByRole("textbox", { name: "Dice" }).fill("2d8+3");
    ok(await dialog.getByText("Rolls 5 to 19.").isVisible(), "2d8+3 rolls 5 to 19, shown while it is typed");
    await noOverflow("favorite roll dialog");
    await page.screenshot({ path: `${output}/favorite-roll-dialog-${device.name}.png` });
    await dialog.getByRole("textbox", { name: "Dice" }).press("Enter");
    await dialog.waitFor({ state: "detached" });
    await saved(() => summary() === "Test Smite=2d8+3", "Test Smite is saved in the combat state");

    // what is not dice is refused before it is saved
    await panel.getByRole("button", { name: "Add a favorite roll" }).click();
    await dialog.getByRole("textbox", { name: "Name" }).fill("Test Luck");
    await dialog.getByRole("textbox", { name: "Dice" }).fill("2d6 fire");
    ok(await dialog.getByText(/^Not dice the app can roll/).isVisible(), "2d6 fire is not dice the app rolls");
    ok(await dialog.getByRole("textbox", { name: "Dice" }).getAttribute("aria-invalid") === "true", "the dice field says it is wrong");
    ok(await dialog.getByRole("button", { name: "Add", exact: true }).isDisabled(), "it cannot be added");
    await dialog.getByRole("textbox", { name: "Dice" }).fill("d20+5");
    ok(await dialog.getByText("Rolls 6 to 25.").isVisible(), "d20+5 rolls 6 to 25");
    await dialog.getByRole("button", { name: "Add", exact: true }).click();
    await dialog.waitFor({ state: "detached" });
    await saved(() => summary() === "Test Smite=2d8+3,Test Luck=d20+5", "both rolls saved, in the order added");

    // rolled with one press: each d8 lands on 5
    await panel.getByRole("button", { name: "Roll Test Smite: 2d8+3" }).click();
    ok(await panel.getByRole("status").getByText("Last roll: Test Smite: 13 (2d8+3 → 5 + 5 + 3 = 13)").isVisible(), "Test Smite rolls 13, each die shown");
    // the toast the compendium's dice show
    ok(await page.getByText("Test Smite: 13", { exact: true }).waitFor({ timeout: 5000 }).then(() => true, () => false), "the roll is shown as the compendium's dice show one");
    ok(await page.getByText("2d8+3 → 5 + 5 + 3 = 13", { exact: true }).isVisible(), "with each die under it");
    await noOverflow("favorite rolls");
    await page.screenshot({ path: `${output}/favorite-rolls-battle-${device.name}.png` });

    // changed: the same roll, now 3d8+3
    const smiteId = own()[0].id;
    await panel.getByRole("button", { name: "Change Test Smite" }).click();
    ok(await dialog.getByRole("heading", { name: "Change Test Smite" }).isVisible(), "the change dialog names the roll");
    await dialog.getByRole("textbox", { name: "Dice" }).fill("3d8+3");
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await dialog.waitFor({ state: "detached" });
    await saved(() => summary() === "Test Smite=3d8+3,Test Luck=d20+5" && own()[0].id === smiteId, "Test Smite is changed in its place, with its id");
    await panel.getByRole("button", { name: "Roll Test Smite: 3d8+3" }).click();
    ok(await panel.getByRole("status").getByText("Last roll: Test Smite: 18 (3d8+3 → 5 + 5 + 5 + 3 = 18)").isVisible(), "3d8+3 rolls 18");

    // the sheet: the same rolls; Test Luck rolled and removed there
    await page.goto(`${base}/characters/${CHARACTER_ID}/play`);
    await page.getByRole("heading", { name: "Mira Vale" }).waitFor();
    panel = page.locator("#resources");
    await panel.getByRole("heading", { name: "Favorite rolls" }).scrollIntoViewIfNeeded();
    ok(await panel.getByRole("button", { name: "Roll Test Smite: 3d8+3" }).isVisible(), "the sheet shows Test Smite as changed in the battle view");
    await panel.getByRole("button", { name: "Roll Test Luck: d20+5" }).click();
    ok(await panel.getByRole("status").getByText("Last roll: Test Luck: 16 (d20+5 → 11 + 5 = 16)").isVisible(), "Test Luck rolls 16 on the sheet");
    await noOverflow("sheet favorite rolls");
    await page.screenshot({ path: `${output}/favorite-rolls-sheet-${device.name}.png` });
    await panel.getByRole("button", { name: "Change Test Luck" }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByRole("button", { name: "Remove" }).click();
    await dialog.waitFor({ state: "detached" });
    await saved(() => summary() === "Test Smite=3d8+3", "Test Luck is removed");
    ok(!(await panel.getByRole("button", { name: /^Roll Test Luck/ }).count()), "the sheet no longer shows it");

    // after a reload the battle view shows what was saved
    await page.goto(`${base}/battle/${CHARACTER_ID}`);
    await page.getByRole("heading", { name: /^Mira Vale$/i }).waitFor();
    ok(await page.getByRole("button", { name: "Roll Test Smite: 3d8+3" }).isVisible(), "Test Smite is there after a reload");
    ok(!(await page.getByRole("button", { name: /^Roll Test Luck/ }).count()), "Test Luck is not");
    ok(writes.length > 0 && writes.every((w) => w === `PUT /api/characters/${CHARACTER_ID}/combat`), `only the combat state is written (${[...new Set(writes)].join(", ")})`);
    await context.close();
  }
  assert.deepEqual(errors, [], "page errors or missing fixtures");
  console.log(`favorite rolls: ${checks} checks passed (desktop, 360px phone)`);
} finally {
  await browser.close();
}
