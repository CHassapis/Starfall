/** Run against an isolated Vite server with VITE_API_BASE="". Every API request is intercepted. */
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { fixture, CHARACTER_ID } from "./fixtures.mjs";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : "playwright");
const base = process.env.FRONTEND_URL || "http://127.0.0.1:55052";
assert.ok(["localhost", "127.0.0.1"].includes(new URL(base).hostname), "Use a local test server");
const output = process.env.SCREENSHOT_DIR || "/tmp/starlights-frontend-shots";
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [];
const results = [];
try {
  for (const theme of ["porcelain", "slate", "midnight"])
    for (const width of [320, 360, 390, 768, 1024, 1440]) {
      const context = await browser.newContext({ viewport: { width, height: 900 }, reducedMotion: "reduce" });
      await context.addInitScript((theme) => {
        if (!localStorage.getItem("starlights-ui-theme")) localStorage.setItem("starlights-ui-theme", theme);
        localStorage.setItem("starlights.player", "Test Player");
      }, theme);
      const data = fixture();
      await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
        const req = route.request();
        try {
          const body = data.resolve(req.url(), req.method(), req.postDataJSON());
          await route.fulfill({
            status: req.method() === "POST" && new URL(req.url()).pathname === "/api/characters" ? 201 : 200,
            contentType: "application/json",
            body: JSON.stringify(body),
          });
        } catch (error) {
          errors.push(error.message); console.error(error.message);
          await route.fulfill({ status: 501, contentType: "application/json", body: '{"error":"missing fixture"}' });
        }
      });
      const page = await context.newPage();
      page.on("pageerror", (e) => { errors.push(e.message); console.error(e.message); });
      async function shot(name) {
        await page.evaluate(() => scrollTo(0, 0));
        await page.screenshot({ path: `${output}/${name}-${theme}-${width}.png`, fullPage: true });
        const size = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, width: innerWidth }));
        assert.ok(size.scroll <= size.width + 1, `${name}/${theme}/${width}: horizontal overflow ${size.scroll}`);
        results.push({ page: name, theme, width, overflow: false });
      }
      await page.goto(base);
      await page.getByRole("heading", { name: "Starfall", exact: true }).waitFor();
      await shot("home");
      await page.goto(`${base}/characters/create`);
      await page.getByRole("textbox", { name: "Name", exact: true }).waitFor();
      await shot("creation");
      await page.goto(`${base}/characters/${CHARACTER_ID}`);
      await page.getByRole("heading", { name: /^Mira Vale$/i }).waitFor();
      await page.getByRole("tab", { name: /^Magic/ }).click();
      const picker = page.locator("section.spell-browser").first();
      await picker.getByRole("button", { name: "Choose spell", exact: true }).click();
      await picker.getByRole("textbox", { name: "Search available spells" }).fill("Circle");
      assert.equal(await picker.getByRole("heading", { name: "Circle of Shelter", exact: true }).count(), 1, "Reprints should share one selection card");
      await picker.locator("summary").filter({ hasText: "Spell details" }).click();
      await page.getByText("One spell, 2 printings.", { exact: false }).waitFor();
      await shot("spell-choice");
      await page.getByRole("button", { name: "Learn", exact: true }).click();
      await page.getByRole("button", { name: "Remove", exact: true }).first().waitFor();
      assert.equal(data.choices[0].selected.name, "Circle of Shelter");
      await page.getByRole("button", { name: "Choose spell", exact: true }).first().click();
      await page.getByRole("textbox", { name: "Search available spells" }).fill("Circle");
      assert.ok(await page.getByRole("button", { name: "Learn", exact: true }).isDisabled(), "Cannot learn a reprint twice for the feature");
      await page.getByRole("button", { name: "Save", exact: true }).click();
      await page.getByText("No unsaved changes.", { exact: true }).waitFor();
      await page.goto(`${base}/characters/${CHARACTER_ID}/play`);

      await page.getByRole("heading", { name: /^Mira Vale$/i }).waitFor();
      await shot("sheet");
      await page.getByRole("textbox", { name: "Amount", exact: true }).fill("2");
      await page.getByRole("button", { name: /^Heal/ }).click();
      await page.waitForFunction(() => !document.querySelector("fieldset")?.disabled);
      assert.equal(data.combat.damage, 1, "HP changes use the existing combat endpoint");
      await page.goto(`${base}/battle/${CHARACTER_ID}`);
      await page.getByRole("heading", { name: /^Mira Vale$/i }).waitFor();
      await shot("battle");
      await page.getByRole("button", { name: "Choose theme and units", exact: true }).click();
      await page.getByRole("button", { name: /^Slate Slate blue/ }).click();
      await page.getByRole("button", { name: "Cancel", exact: true }).click();
      assert.equal(await page.locator("html").getAttribute("data-theme"), theme, "Cancel restores saved theme");
      await page.getByRole("button", { name: "Choose theme and units", exact: true }).click();
      await page.getByRole("button", { name: /^Midnight Deep navy/ }).click();
      await page.getByRole("button", { name: "Save theme", exact: true }).click();
      await page.reload();
      assert.equal(await page.locator("html").getAttribute("data-theme"), "midnight", "Theme survives reload");
      await context.close();
    }
  assert.deepEqual([...new Set(errors)], [], "No application errors or unmocked API requests");
  await writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
  console.log(`PASS: ${results.length} viewport/theme/page checks; learning/reprints, HP and theme cancel. Screenshots: ${output}`);
} finally {
  await browser.close();
}
