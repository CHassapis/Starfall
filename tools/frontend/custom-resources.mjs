/**
 * Resources of the player's own. Run against a local server of the built app (VITE_API_BASE=""); every API request is
 * answered by the synthetic fixture (a level 3 Wizard, Intelligence +3, proficiency +2, armor class 11). On a desktop
 * and a 360px touch phone: in the battle view a pool of 2 × Wizard level (6, back at dawn), a stance with +2 AC while
 * switched on, and a move that spends 2 from the pool are added; spending the move leaves the pool at 4 of 6 and its
 * "Why?" works the 6 out; the sheet shows the same 4 of 6 and the stance's armor class; a long rest leaves the pool
 * spent and switches the stance off, dawn brings the pool back; removing the move forgets it. Everything is saved in
 * the combat state, nowhere else; no horizontal overflow, no page errors.
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
    const own = () => data.combat.resources ?? [];
    const idOf = (name) => own().find((r) => r.name === name)?.id;

    await page.goto(`${base}/battle/${CHARACTER_ID}`);
    await page.getByRole("heading", { name: /^Mira Vale$/i }).waitFor();
    await page.getByRole("tab", { name: "Resources" }).click();

    // a pool of 2 × Wizard level, back at dawn
    const add = async () => {
      await page.getByRole("button", { name: "Add a resource" }).click();
      return page.getByRole("dialog");
    };
    let dialog = await add();
    await dialog.getByRole("textbox", { name: /^Name/ }).fill("Test Arcane Pool");
    await dialog.getByRole("combobox", { name: /^Maximum follows/ }).selectOption("class-level");
    await dialog.getByRole("combobox", { name: /^Class/ }).fill("Wizard");
    await dialog.getByRole("spinbutton", { name: /^Times/ }).fill("2");
    await dialog.getByRole("combobox", { name: /^Comes back on/ }).selectOption("dawn");
    ok(await dialog.getByText("Now: 6 uses.").isVisible(), "2 × Wizard level 3 = 6, shown while it is set");
    await noOverflow("resource dialog");
    await page.screenshot({ path: `${output}/resource-dialog-${device.name}.png` });
    await dialog.getByRole("button", { name: "Add", exact: true }).click();
    await dialog.waitFor({ state: "detached" });
    await saved(() => own().some((r) => r.name === "Test Arcane Pool" && r.max.from === "class-level" && r.max.times === 2 && r.recharge === "dawn"), "the pool is saved in the combat state");

    // a stance: one use a long rest, +2 AC while switched on
    dialog = await add();
    await dialog.getByRole("textbox", { name: /^Name/ }).fill("Test Ward Stance");
    await dialog.getByRole("spinbutton", { name: /^Armor class/ }).fill("2");
    await dialog.getByRole("button", { name: "Add", exact: true }).click();
    await dialog.waitFor({ state: "detached" });

    // a move that spends 2 from the pool
    dialog = await add();
    await dialog.getByRole("textbox", { name: /^Name/ }).fill("Test Surge");
    await dialog.getByRole("radio", { name: "Spent from another resource" }).check();
    await dialog.getByRole("combobox", { name: /^Spends from/ }).fill("Test Arcane Pool");
    await dialog.getByRole("spinbutton", { name: /^Each use costs/ }).fill("2");
    await dialog.getByRole("button", { name: "Add", exact: true }).click();
    await dialog.waitFor({ state: "detached" });
    await saved(() => own().length === 3, "three resources saved");

    const item = (name) => page.getByRole("listitem").filter({ has: page.getByRole("button", { name: `${name}: what it does` }) });
    ok(await item("Test Arcane Pool").getByText("6/6 per day (dawn)").isVisible(), "the pool: 6 of 6, back at dawn");
    await item("Test Surge").getByRole("button", { name: "Spend" }).click();
    await saved(() => data.combat.uses[`custom:${idOf("Test Arcane Pool")}`] === 2, "the move spent 2 from the pool, kept under the pool's id");
    await item("Test Arcane Pool").getByText("4/6 per day (dawn)").waitFor();
    ok(true, "the pool shows 4 of 6");

    // "Why?" works the maximum out on the spot
    await item("Test Arcane Pool").getByRole("button", { name: "Why this value? Test Arcane Pool maximum uses" }).click();
    const card = page.getByRole("dialog").last();
    await card.getByText("Test Arcane Pool: maximum uses: 6").first().waitFor();
    ok(await card.getByText("Wizard level").first().isVisible(), "it starts from the Wizard level");
    ok(await card.getByText("Times 2").first().isVisible(), "times 2");
    await noOverflow("resource trace");
    await page.screenshot({ path: `${output}/resource-why-${device.name}.png` });
    await page.keyboard.press("Escape");
    await page.getByRole("dialog").waitFor({ state: "detached" });

    // the stance switched on
    await item("Test Ward Stance").getByRole("button", { name: "Test Ward Stance: switch on" }).click();
    await saved(() => (data.combat.effects ?? []).includes(`custom:${idOf("Test Ward Stance")}`), "the stance's effect is on, in the combat state");
    await noOverflow("resources");
    await page.screenshot({ path: `${output}/resources-battle-${device.name}.png`, fullPage: true });

    // the sheet: the same count, and the stance's armor class
    await page.goto(`${base}/characters/${CHARACTER_ID}/play`);
    await page.getByRole("heading", { name: "Mira Vale" }).waitFor();
    const resources = page.locator("#resources");
    await resources.scrollIntoViewIfNeeded();
    ok(await resources.getByText("4 / 6 remaining · your own, back at dawn").isVisible(), "the sheet shows the pool as the battle view does");
    await page.getByRole("button", { name: "Why this value? Armor class", exact: true }).click();
    const ac = page.getByRole("dialog").last();
    ok(await ac.getByText("Shown as 13 with temporary effects (Test Ward Stance +2). This is the sheet's 11 before them.").first().isVisible(), "the stance's +2 is in the armor class shown");
    await page.keyboard.press("Escape");
    await page.getByRole("dialog").waitFor({ state: "detached" });

    // a long rest: the pool stays spent (dawn is not a rest), the stance ends
    await resources.getByRole("button", { name: "Long rest" }).click();
    const rest = page.getByRole("dialog");
    ok(await rest.getByText("At dawn, not now: Test Arcane Pool.").isVisible(), "the long rest says the pool waits for dawn");
    await rest.getByRole("button", { name: "Take the long rest" }).click();
    await rest.waitFor({ state: "detached" });
    await saved(() => data.combat.uses[`custom:${idOf("Test Arcane Pool")}`] === 2 && !(data.combat.effects ?? []).length, "after the long rest the pool is still spent and the stance is off");
    // dawn brings it back
    await resources.getByRole("button", { name: "Dawn" }).click();
    ok(await rest.getByText("Comes back: Test Arcane Pool.").isVisible(), "dawn names the pool");
    await noOverflow("dawn");
    await rest.getByRole("button", { name: "A new day" }).click();
    await rest.waitFor({ state: "detached" });
    await saved(() => !data.combat.uses[`custom:${idOf("Test Arcane Pool")}`], "dawn gave the pool back");
    await resources.getByText("6 / 6 remaining · your own, back at dawn").waitFor();
    ok(true, "the sheet shows 6 of 6 again");

    // removing the move forgets it
    await page.goto(`${base}/battle/${CHARACTER_ID}`);
    await page.getByRole("tab", { name: "Resources" }).click();
    await item("Test Surge").getByRole("button", { name: "Change Test Surge" }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByRole("button", { name: "Remove", exact: true }).click();
    await dialog.getByRole("button", { name: "Remove it and what was spent" }).click();
    await dialog.waitFor({ state: "detached" });
    await saved(() => own().map((r) => r.name).join() === "Test Arcane Pool,Test Ward Stance", "the move is removed");
    // the resources are saved in the combat state only (the long rest also gives back spell slots, in the magic state)
    ok(writes.every((w) => /\/(combat|magic)$/.test(w)) && writes.filter((w) => w.endsWith("/magic")).length === 1, `only the combat state, and the long rest's slots (${[...new Set(writes)].join(", ")})`);
    await context.close();
  }
  assert.deepEqual(errors, [], "page errors or missing fixtures");
  console.log(`custom resources: ${checks} checks passed (desktop, 360px phone)`);
} finally {
  await browser.close();
}
