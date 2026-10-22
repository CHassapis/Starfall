/**
 * Makes the PDF character sheet off the page's thread (lib/aurora-sheet-client.ts sends the work here): the same
 * buildAuroraSheet, with the HTML texts the page read beforehand, since a worker has no HTML parser. Answers each
 * request by its id with the PDF's bytes (handed over, not copied) or the error's message.
 */
import type { SheetData } from "@/lib/api/sheet";
import { buildAuroraSheet, type SheetParagraphs } from "@/lib/aurora-sheet";

export interface SheetRequest {
  id: number;
  data: SheetData;
  paragraphs: SheetParagraphs;
}

export type SheetResponse = { id: number; ok: true; bytes: ArrayBuffer } | { id: number; ok: false; error: string };

self.addEventListener("message", (event: MessageEvent<SheetRequest>) => {
  const { id, data, paragraphs } = event.data;
  const answer = (response: SheetResponse, transfer: ArrayBuffer[] = []) => self.postMessage(response, { transfer });
  Promise.resolve().then(() => {
    // Picture rendering deliberately skips unreadable images. Missing worker canvas support is different:
    // fail the request before rendering so the client falls back instead of silently dropping every picture.
    if (typeof OffscreenCanvas === "undefined" || typeof createImageBitmap !== "function") {
      throw new Error("The sheet's worker cannot render pictures.");
    }
    const canvas = new OffscreenCanvas(1, 1);
    if (!canvas.getContext("2d") || typeof canvas.convertToBlob !== "function") {
      throw new Error("The sheet's worker cannot render pictures.");
    }
    return buildAuroraSheet(data, paragraphs);
  }).then(
    (bytes) => {
      const buffer = (bytes.byteOffset === 0 && bytes.byteLength === bytes.buffer.byteLength ? bytes.buffer : bytes.slice().buffer) as ArrayBuffer;
      answer({ id, ok: true, bytes: buffer }, [buffer]);
    },
    (error: unknown) => answer({ id, ok: false, error: error instanceof Error ? error.message : String(error) }),
  );
});
