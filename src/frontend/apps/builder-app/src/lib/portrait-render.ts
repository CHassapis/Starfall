/**
 * Draws a portrait the way the character sheet shows it: the picture placed in the sheet's portrait box by its frame
 * (size and which part shows), on white. The builder's preview and the PDF use the same drawing.
 */
import { placePicture, type PortraitFrame } from "@/lib/rules/portrait-frame";

/** The picture at a URL, decoded (same-origin uploads and the app's own portraits). */
export async function loadPicture(url: string): Promise<ImageBitmap> {
  return createImageBitmap(await (await fetch(url)).blob());
}

export type AnyCanvas = HTMLCanvasElement | OffscreenCanvas;

/** A canvas to draw on: the page's, or an off-screen one where there is no page (the PDF sheet's worker). */
export function newCanvas(): AnyCanvas {
  return typeof document !== "undefined" ? document.createElement("canvas") : new OffscreenCanvas(1, 1);
}

/** Either canvas's 2D context: the drawing calls used here are the same on both. */
export const context2d = (canvas: AnyCanvas) => (canvas as HTMLCanvasElement).getContext("2d")!;

/** A canvas of the box's shape (pixelsPerUnit pixels per box unit) with the framed picture drawn in it. */
export function renderFramed(picture: CanvasImageSource & { width: number; height: number }, box: { w: number; h: number }, frame: PortraitFrame, pixelsPerUnit: number, canvas: AnyCanvas = newCanvas()) {
  canvas.width = Math.round(box.w * pixelsPerUnit);
  canvas.height = Math.round(box.h * pixelsPerUnit);
  const context = context2d(canvas);
  context.fillStyle = "#fff";
  context.fillRect(0, 0, canvas.width, canvas.height);
  const p = placePicture({ w: picture.width, h: picture.height }, box, frame);
  context.imageSmoothingQuality = "high";
  context.drawImage(picture, p.left * pixelsPerUnit, p.top * pixelsPerUnit, p.w * pixelsPerUnit, p.h * pixelsPerUnit);
  return canvas;
}

export async function canvasJpeg(canvas: AnyCanvas): Promise<ArrayBuffer> {
  const blob =
    "convertToBlob" in canvas
      ? await canvas.convertToBlob({ type: "image/jpeg", quality: 0.9 })
      : await new Promise<Blob>((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error("no image"))), "image/jpeg", 0.9));
  return blob.arrayBuffer();
}
