import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const build = vi.hoisted(() => vi.fn());
vi.mock("@/lib/aurora-sheet", () => ({ buildAuroraSheet: build }));

describe("PDF worker picture support", () => {
  let receive: (event: { data: { id: number; data: object; paragraphs: object } }) => void;
  let reply: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    vi.resetModules();
    build.mockReset().mockResolvedValue(new Uint8Array([1, 2, 3]));
    reply = vi.fn();
    vi.stubGlobal("self", {
      addEventListener: (_type: string, listener: typeof receive) => { receive = listener; },
      postMessage: reply,
    });
    vi.stubGlobal("createImageBitmap", vi.fn());
    await import("./aurora-sheet.worker");
  });
  afterEach(() => vi.unstubAllGlobals());

  it.each([undefined, class { getContext() { return null; } }, class { getContext() { return {}; } }])(
    "returns a failure for the page to retry when worker canvas support is incomplete",
    async (canvas) => {
      vi.stubGlobal("OffscreenCanvas", canvas);
      receive({ data: { id: 7, data: {}, paragraphs: {} } });
      await vi.waitFor(() => expect(reply).toHaveBeenCalled());
      expect(build).not.toHaveBeenCalled();
      expect(reply.mock.calls[0][0]).toEqual({ id: 7, ok: false, error: "The sheet's worker cannot render pictures." });
    },
  );

  it("renders with supported canvas APIs and transfers the PDF bytes", async () => {
    vi.stubGlobal("OffscreenCanvas", class { getContext() { return {}; } convertToBlob() {} });
    const data = { name: "Test character" };
    receive({ data: { id: 8, data, paragraphs: {} } });
    await vi.waitFor(() => expect(reply).toHaveBeenCalled());
    expect(build).toHaveBeenCalledWith(data, {});
    const [response, options] = reply.mock.calls[0];
    expect(response.id).toBe(8);
    expect(response.ok).toBe(true);
    expect([...new Uint8Array(response.bytes)]).toEqual([1, 2, 3]);
    expect(options.transfer).toEqual([response.bytes]);
  });
});
