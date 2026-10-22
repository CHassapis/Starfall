/**
 * The PDF sheet made in its worker is the sheet made on the page. Run against a local server of the built app
 * (VITE_API_BASE=""); every API request is answered by the synthetic fixture (fixture({ large: true }), with a made-up
 * portrait framed by the player, the same picture as the organization symbol, and the portrait's last page). The
 * large sheet is made twice: in the worker, and with workers switched off (the page makes it, as before the worker);
 * each page pdf.js draws is compared, picture for picture. A template that cannot be loaded gives the same message
 * on the sheet page as before. No page errors.
 */
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { deflateSync } from "node:zlib";
import { fixture, CHARACTER_ID } from "./fixtures.mjs";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : "playwright");
const base = process.env.FRONTEND_URL || "http://127.0.0.1:55052";
assert.ok(["localhost", "127.0.0.1"].includes(new URL(base).hostname), "Use a local test server");
const output = process.env.SCREENSHOT_DIR || "/tmp/starlights-frontend-shots";
await mkdir(output, { recursive: true });
let checks = 0;
const ok = (value, message) => {
  assert.ok(value, message);
  checks++;
};

// a made-up 96 × 72 picture (a colour gradient) as a PNG data URL, so the page and the worker read it the same way
function gradientPng(w, h) {
  const crcTable = Array.from({ length: 256 }, (_, n) => {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    return c >>> 0;
  });
  const crc = (buf) => {
    let c = 0xffffffff;
    for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8);
    return (c ^ 0xffffffff) >>> 0;
  };
  const chunk = (type, data) => {
    const body = Buffer.concat([Buffer.from(type), data]);
    const out = Buffer.alloc(body.length + 8);
    out.writeUInt32BE(data.length, 0);
    body.copy(out, 4);
    out.writeUInt32BE(crc(body), body.length + 4);
    return out;
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(w, 0);
  header.writeUInt32BE(h, 4);
  header.set([8, 2, 0, 0, 0], 8);
  const rows = Buffer.alloc(h * (w * 3 + 1));
  for (let y = 0; y < h; y++)
    for (let x = 0; x < w; x++) rows.set([Math.round((255 * x) / w), Math.round((255 * y) / h), 160], y * (w * 3 + 1) + 1 + x * 3);
  return `data:image/png;base64,${Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", header), chunk("IDAT", deflateSync(rows)), chunk("IEND", Buffer.alloc(0))]).toString("base64")}`;
}
const picture = gradientPng(96, 72);

const browser = await chromium.launch({ headless: true });
const errors = [];
/** The sheet's pages as pdf.js draws them, each as a hash of its PNG; whether a worker made it; requests for templates. */
async function makeSheet({ workers, brokenTemplate = false }) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, deviceScaleFactor: 1 });
  await context.addInitScript((off) => {
    localStorage.setItem("starlights.player", "Test Player");
    if (off) delete window.Worker;
  }, !workers);
  const data = fixture({ large: true });
  let templateRequests = 0;
  await context.route(
    (url) => url.pathname.includes("Aurora.Pages.equipment_page"),
    async (route) => {
      templateRequests++;
      if (brokenTemplate) await route.fulfill({ status: 404, body: "" });
      else await route.continue();
    },
  );
  await context.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const req = route.request();
    const path = new URL(req.url()).pathname;
    try {
      let body = data.resolve(req.url(), req.method(), req.postDataJSON());
      if (path === `/api/characters/${CHARACTER_ID}`) body = { character: { ...body.character, portraitUrl: picture } };
      if (path.endsWith("/story")) body = { fields: { portraitFrame: "1.6,0.3,-0.2", organizationSymbol: picture, backstory: "A made-up past." } };
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    } catch (error) {
      errors.push(error.message);
      await route.fulfill({ status: 501, contentType: "application/json", body: '{"error":"missing fixture"}' });
    }
  });
  const page = await context.newPage();
  page.on("pageerror", (e) => errors.push(e.message));
  const started = [];
  page.on("worker", (w) => started.push(w.url()));
  await page.goto(`${base}/characters/${CHARACTER_ID}/sheet`);
  if (brokenTemplate) {
    const failed = await page.getByText(/^Could not make the sheet:/).textContent({ timeout: 120_000 });
    await context.close();
    return { failed, started, templateRequests };
  }
  await page.getByRole("button", { name: "Print" }).and(page.locator(":enabled")).waitFor({ timeout: 180_000 });
  const pngs = await page.getByRole("img", { name: /character sheet, page/ }).evaluateAll(async (images) =>
    Promise.all(images.map(async (img) => Array.from(new Uint8Array(await (await fetch(img.src)).arrayBuffer())))),
  );
  if (workers) await page.screenshot({ path: `${output}/sheet-worker.png` });
  await context.close();
  return { pages: pngs.map((bytes) => createHash("sha256").update(Buffer.from(bytes)).digest("hex").slice(0, 16)), started, templateRequests };
}

try {
  const inWorker = await makeSheet({ workers: true });
  const onPage = await makeSheet({ workers: false });
  ok(inWorker.started.some((url) => url.includes("aurora-sheet.worker")), `the sheet was made in its worker (${inWorker.started.join(", ")})`);
  ok(!onPage.started.some((url) => url.includes("aurora-sheet.worker")), "without workers it was made on the page");
  ok(inWorker.pages.length >= 13, `the large sheet with its portrait page (${inWorker.pages.length} pages)`);
  ok(inWorker.pages.length === onPage.pages.length, `as many pages (${inWorker.pages.length}, ${onPage.pages.length})`);
  inWorker.pages.forEach((hash, i) => ok(hash === onPage.pages[i], `page ${i + 1} is the same picture (${hash}, ${onPage.pages[i]})`));
  // the pages differ from each other, so the comparison compares something
  ok(new Set(inWorker.pages).size > inWorker.pages.length / 2, "the pages differ from each other");

  // a template that cannot be loaded: the same message as before, whether the worker or the page met it first
  const broken = await makeSheet({ workers: true, brokenTemplate: true });
  ok(broken.failed === 'Could not make the sheet: The sheet template "Aurora.Pages.equipment_page.pdf" could not be loaded.', `the message (${broken.failed})`);
  ok(broken.templateRequests >= 2, `the worker asked for the template, then the page (${broken.templateRequests} requests)`);
  const before = await makeSheet({ workers: false, brokenTemplate: true });
  ok(before.failed === broken.failed, `the same message as without the worker (${before.failed})`);

  assert.deepEqual(errors, [], "page errors or missing fixtures");
  console.log(`sheet pdf worker: ${checks} checks passed (${inWorker.pages.length} pages the same in the worker and on the page)`);
} finally {
  await browser.close();
}
