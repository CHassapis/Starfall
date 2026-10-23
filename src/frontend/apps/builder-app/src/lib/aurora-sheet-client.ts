/**
 * The PDF character sheet, made off the page's thread. Made on the page, a large character's sheet held the page
 * for over two seconds (tools/frontend/sheet-performance.mjs), so nothing could be clicked meanwhile. A module worker
 * (lib/aurora-sheet.worker.ts) runs the same buildAuroraSheet with the same templates and fonts: the page reads the
 * HTML texts first (a worker has no HTML parser), and pictures are drawn on an off-screen canvas there. Where a worker
 * cannot start or fails, the sheet is made on the page as before, so an error reads the same either way. Requests by
 * id with the bytes handed back follow Forge Character Builder's sheet-render worker (MIT); the code is this app's own.
 */
import type { SheetData } from "@/lib/api/sheet";
import { buildAuroraSheet, sheetParagraphs } from "@/lib/aurora-sheet";
import type { SheetRequest, SheetResponse } from "@/lib/aurora-sheet.worker";

let worker: Worker | null = null;
// a worker that failed to load or broke is not started again on this page
let broken = false;
let next = 0;
const waiting = new Map<number, { resolve: (bytes: Uint8Array) => void; reject: (error: Error) => void }>();

function started(): Worker | null {
  if (worker || broken) return worker;
  if (typeof Worker === "undefined") return null;
  try {
    worker = new Worker(new URL("./aurora-sheet.worker.ts", import.meta.url), { type: "module", name: "pdf-sheet" });
  } catch {
    broken = true;
    return null;
  }
  worker.addEventListener("message", (event: MessageEvent<SheetResponse>) => {
    const answer = event.data;
    const request = waiting.get(answer.id);
    if (!request) return;
    waiting.delete(answer.id);
    if (answer.ok) request.resolve(new Uint8Array(answer.bytes));
    else request.reject(new Error(answer.error));
  });
  // a script that did not load, or an error outside a request: what waits is made on the page
  worker.addEventListener("error", (event) => {
    event.preventDefault();
    broken = true;
    worker?.terminate();
    worker = null;
    for (const request of waiting.values()) request.reject(new Error(event.message || "The sheet's worker stopped."));
    waiting.clear();
  });
  return worker;
}

/** Pictures by an address the worker reads the same way: relative ones resolved against the page. */
function absolute(data: SheetData): SheetData {
  const resolve = (url: string) => new URL(url, window.location.href).href;
  const symbol = data.story.organizationSymbol;
  return { ...data, portraitUrl: data.portraitUrl && resolve(data.portraitUrl), story: symbol ? { ...data.story, organizationSymbol: resolve(symbol) } : data.story };
}

/** The sheet's PDF: in the worker, or on the page where there is none or it failed. */
export async function makeSheetPdf(data: SheetData): Promise<Uint8Array> {
  const w = started();
  if (!w) return buildAuroraSheet(data);
  try {
    return await new Promise<Uint8Array>((resolve, reject) => {
      const id = ++next;
      waiting.set(id, { resolve, reject });
      try {
        w.postMessage({ id, data: absolute(data), paragraphs: sheetParagraphs(data) } satisfies SheetRequest);
      } catch (error) {
        waiting.delete(id);
        reject(error as Error);
      }
    });
  } catch (error) {
    // made on the page instead: the same sheet, or the same error to show
    console.warn("The PDF sheet is made on the page:", (error as Error).message);
    return buildAuroraSheet(data);
  }
}
