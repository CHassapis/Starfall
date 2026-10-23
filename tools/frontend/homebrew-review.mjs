/**
 * The Homebrew page's import diagnostics and edit review. Run against a local server of the built app
 * (VITE_API_BASE=""); the homebrew API is answered here, everything else by the synthetic fixture. On a desktop and a
 * 360px touch phone, as the admin: uploading a file lists what the import ran into; editing a spell shows "Review and
 * save" (what changes, who uses it, a pick it leaves behind), an edit after the review asks for a new one, the save sends the revision the
 * review saw, a save someone overtook comes back with a fresh review saying so, and saving again with the new
 * revision goes through. No horizontal overflow, no page
 * errors.
 */
import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { fixture } from "./fixtures.mjs";
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

const SPELL_ID = "ID_STARLIGHTS_HOMEBREW_SPELL_TEST_SPARKLE";
const spell = (level, revision) => ({
  id: SPELL_ID, name: "Test Sparkle", source: "Test Homebrew", level, school: "Evocation", castingTime: "1 action", range: "60 feet", duration: "Instantaneous",
  verbal: true, somatic: true, material: false, concentration: false, ritual: false, lists: ["Wizard"], description: "A made-up spell.", revision,
});
// Mira Vale has it prepared as a Wizard spell: taking it off the Wizard list leaves that behind
const LEFT_BEHIND = "Prepared as a Wizard spell, but it would no longer be on the Wizard list. It stays prepared until it is changed.";
const preview = (before, after, revision, diagnostics = [], lists = ["Wizard"]) => {
  const offWizard = !lists.includes("Wizard");
  const changes = [
    ...(before === after ? [] : [{ field: "level", before: String(before), after: String(after) }]),
    ...(offWizard ? [{ field: "lists", before: "Wizard", after: lists.join(", ") }] : []),
  ];
  return {
    kind: "spells", id: SPELL_ID, isNew: false, revision, changes,
    elements: changes.length === 0 ? [] : [{ elementId: SPELL_ID, name: "Test Sparkle", change: "changed" }],
    characters: [{
      characterId: "c0ffee00-0000-4000-8000-000000000001", name: "Mira Vale", playerName: "Test Player", uses: [SPELL_ID], loses: [],
      invalidated: offWizard ? [{ elementId: SPELL_ID, name: "Test Sparkle", reason: LEFT_BEHIND }] : [],
    }],
    diagnostics,
  };
};
const uploadDiagnostics = [
  { severity: "warning", code: "append-target-missing", importer: "homebrew", message: "The append to ID_NOBODY was not applied: ID_NOBODY is not in the loaded content.", file: "homebrew/test-feats.xml", elementId: "ID_NOBODY" },
  { severity: "warning", code: "grant-unresolved", importer: "homebrew", message: "Brew Feat grants ID_GONE, which is not in the content; that grant was left out.", file: "homebrew/test-feats.xml", elementId: "ID_BREW_FEAT", elementName: "Brew Feat", detail: "ID_GONE" },
];

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
    // the server's side: the saved spell (level 1, revision rev-1) was saved again elsewhere (level 3, rev-2) after
    // this browser read it
    const server = { level: 3, revision: "rev-2" };
    const puts = [];
    const previews = [];
    let uploads = 0;
    await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
      const req = route.request();
      const url = new URL(req.url());
      const json = (status, body) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
      const p = url.pathname;
      if (p === "/api/elements/homebrew" && req.method() === "GET") return json(200, { files: [] });
      // the first file's grant is reported by its own import only: the second import does not look at it again
      if (p === "/api/elements/homebrew" && req.method() === "POST") {
        uploads++;
        // each file: one element in full, one in part (its grant)
        return json(200, { file: req.postDataJSON().fileName, elementsImported: 2, elementsReplaced: 0, warnings: [], diagnostics: uploads === 1 ? uploadDiagnostics : uploadDiagnostics.slice(0, 1),
          entries: { complete: 1, partial: 1, textOnly: 0, skipped: 0 } });
      }
      if (p === "/api/elements/homebrew/spells" && req.method() === "GET") return json(200, { spells: [spell(1, "rev-1")] });
      if (p === "/api/elements/homebrew/classes") return json(200, { classes: [] });
      if (p === "/api/elements/homebrew/subclasses") return json(200, { subclasses: [] });
      if (p === "/api/elements/homebrew/subclass-targets") return json(200, { targets: [] });
      // as the server: compared with what is saved now, and a form opened on an older revision is told so
      if (p === "/api/elements/homebrew/spells/preview") {
        const body = req.postDataJSON();
        previews.push(body);
        const opened = body.revision && body.revision !== server.revision
          ? [{ severity: "warning", code: "stale-revision", importer: "homebrew", message: "Someone saved it after you opened it: the changes listed compare with what is saved now. Saving will ask you to confirm once more.", elementId: SPELL_ID }]
          : [];
        return json(200, preview(server.level, body.level, server.revision, opened, body.lists));
      }
      if (p === "/api/elements/homebrew/spells" && req.method() === "PUT") {
        const body = req.postDataJSON();
        puts.push(body.expectedRevision ?? null);
        if (body.expectedRevision !== server.revision)
          return json(409, preview(server.level, body.level, server.revision, [{ severity: "error", code: "stale-revision", importer: "homebrew", message: "Someone saved it after you opened it, so it was not saved. Check what changed below, then save again.", elementId: SPELL_ID }], body.lists));
        Object.assign(server, { level: body.level, revision: "rev-3" });
        return json(200, spell(body.level, "rev-3"));
      }
      if (p === "/api/characters" && req.method() === "GET") return json(200, { characters: [] });
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
      // a dialog is fixed: the page does not scroll for it, so its own edges are checked
      const box = await page.getByRole("dialog").boundingBox().catch(() => null);
      if (box) ok(box.x >= 0 && box.x + box.width <= size.width + 1, `${device.name} ${what}: the dialog (${box.x}–${box.x + box.width}) is wider than the screen (${size.width})`);
    };

    // a file upload lists what the import ran into
    await page.goto(`${base}/homebrew?tab=files`);
    await page.getByRole("button", { name: "Add homebrew (.xml)" }).waitFor();
    await page.locator('input[type="file"]').setInputFiles([
      { name: "test-feats.xml", mimeType: "text/xml", buffer: Buffer.from("<elements />") },
      { name: "test-more.xml", mimeType: "text/xml", buffer: Buffer.from("<elements />") },
    ]);
    const status = page.getByRole("status").filter({ hasText: "Saved, but the homebrew needs attention: 2 warnings" });
    await status.waitFor();
    await page.getByText("2 homebrew files added").waitFor();
    ok(uploads === 2, `both files uploaded (${uploads})`);
    ok(await status.getByText("Brew Feat grants ID_GONE").isVisible(), "the first file's grant stays listed after the second upload");
    ok(await status.getByText("Homebrew · homebrew/test-feats.xml · Brew Feat (ID_BREW_FEAT)").isVisible(), "where: importer, file, element");
    ok(await status.getByText("Entries: 2 in full · 2 in part").isVisible(), "what the two imports made, added up");
    await noOverflow("upload diagnostics");
    await page.screenshot({ path: `${output}/homebrew-upload-diagnostics-${device.name}.png` });

    // an edit is reviewed before it is saved
    await page.goto(`${base}/homebrew?tab=spells`);
    await page.getByRole("button", { name: "Edit Test Sparkle" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.locator("select").first().selectOption("2");
    await dialog.getByRole("button", { name: "Review and save" }).click();
    const review = dialog.getByRole("region", { name: "What saving does" });
    await review.waitFor();
    ok(await review.getByText("1 change · 1 character uses it").isVisible(), "the summary");
    const level = review.getByRole("listitem").filter({ hasText: "Level:" });
    ok((await level.textContent()).replace(/\s+/g, " ").trim() === "Level: 3 → 2", "compared with what is saved now (level 3, saved since the form was opened)");
    ok(await review.getByText("Someone saved it after you opened it: the changes listed compare").isVisible(), "the review says the form is out of date");
    ok(await review.getByText("Mira Vale").isVisible(), "who uses it");
    ok(previews.at(-1)?.level === 2 && previews.at(-1)?.revision === "rev-1" && !previews.at(-1)?.expectedRevision, "the review sends the form as it is, with the revision it was opened on");
    await review.scrollIntoViewIfNeeded();
    await noOverflow("review");
    await page.screenshot({ path: `${output}/homebrew-review-${device.name}.png` });

    // taking the spell off the Wizard list: the review names the pick that leaves behind
    const wizard = dialog.getByRole("group", { name: "Class spell lists" }).getByRole("checkbox", { name: "Wizard" });
    await dialog.getByRole("button", { name: "Back to editing" }).click();
    await wizard.uncheck();
    await dialog.getByRole("button", { name: "Review and save" }).click();
    await review.waitFor();
    ok(await review.getByText("2 changes · 1 character uses it, 1 has a pick that no longer fits").isVisible(), "the summary counts the pick left behind");
    ok(await review.getByText(`Test Sparkle: ${LEFT_BEHIND}`).isVisible(), "the pick left behind, and why");
    await noOverflow("pick left behind");
    await page.screenshot({ path: `${output}/homebrew-review-left-behind-${device.name}.png` });
    await dialog.getByRole("button", { name: "Back to editing" }).click();
    await wizard.check();
    await dialog.getByRole("button", { name: "Review and save" }).click();
    await review.waitFor();
    ok((await review.getByText(LEFT_BEHIND).count()) === 0, "back on the list, nothing is left behind");

    // changing the form after the review asks for a new one, so nothing unreviewed is saved
    await dialog.locator("select").first().selectOption("4");
    await dialog.getByRole("button", { name: "Review and save" }).waitFor();
    ok((await review.count()) === 0, "an edit after the review puts the review away");
    await dialog.locator("select").first().selectOption("2");
    await dialog.getByRole("button", { name: "Review and save" }).click();
    await review.waitFor();

    // saved elsewhere since the form was opened: the save (based on the form's revision) is refused, and the fresh
    // review says so
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await dialog.getByText("Someone saved it after you opened it, so it was not saved").waitFor();
    ok(puts.at(-1) === "rev-1", `the save sends the revision the form was opened on (${puts.at(-1)})`);
    ok((await level.textContent()).replace(/\s+/g, " ").trim() === "Level: 3 → 2", "the fresh review compares with what is saved now (level 3)");
    ok(await dialog.getByRole("button", { name: "Save", exact: true }).isEnabled(), "it can be saved again");
    await noOverflow("stale review");
    await page.screenshot({ path: `${output}/homebrew-review-stale-${device.name}.png` });

    // saving again with the new revision goes through
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await page.getByText("Test Sparkle saved").waitFor();
    await dialog.waitFor({ state: "detached" });
    ok(puts.join(",") === "rev-1,rev-2", `the second save sends the fresh revision (${puts.join(",")})`);
    await context.close();
  }
  assert.deepEqual(errors, [], "page errors or missing fixtures");
  console.log(`homebrew review: ${checks} checks passed (desktop, 360px phone)`);
} finally {
  await browser.close();
}
