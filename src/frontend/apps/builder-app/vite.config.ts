import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import path from "path";
import { env as nodeEnv } from "process";
import { readdirSync, readFileSync } from "fs";
import { createRequire } from "module";
import { defineConfig, loadEnv, type Plugin } from "vite";

/** pdf.js's standard fonts (for PDFs that use a font without embedding it), served at /pdfjs/standard_fonts/. */
function pdfjsStandardFonts(): Plugin {
  const dir = path.join(path.dirname(createRequire(import.meta.url).resolve("pdfjs-dist/package.json")), "standard_fonts");
  const prefix = "/pdfjs/standard_fonts/";
  return {
    name: "pdfjs-standard-fonts",
    configureServer(server) {
      server.middlewares.use(prefix, (req, res, next) => {
        const file = path.basename(decodeURIComponent((req.url ?? "").split("?")[0]));
        if (!readdirSync(dir).includes(file)) return next();
        res.setHeader("Content-Type", "application/octet-stream");
        res.end(readFileSync(path.join(dir, file)));
      });
    },
    generateBundle() {
      for (const file of readdirSync(dir)) this.emitFile({ type: "asset", fileName: `${prefix.slice(1)}${file}`, source: readFileSync(path.join(dir, file)) });
    },
  };
}

// pdf-lib and its fonts: only the printable sheet and its worker use them, so they stay out of what every page loads.
// Whole packages per chunk, no maxSize: splitting by size cut packages apart into chunks that import each other, and
// a module then ran before the chunk holding its helper had (the sheet page threw "bt is not a function").
// pdf-lib first, so the libraries both use (pako) go with it and fontkit's chunk holds fontkit alone
const pdfLibGroups = [
  { name: "pdf-lib", test: /node_modules[\\/](pdf-lib|@pdf-lib[\\/](?!fontkit[\\/]))/, priority: 3 },
  { name: "fontkit", test: /node_modules[\\/]@pdf-lib[\\/]fontkit[\\/]/, priority: 2 },
];
// the other libraries in a chunk of their own: cached across releases (the app's code changes, they rarely do)
const chunkGroups = [
  ...pdfLibGroups,
  { name: "vendor", test: /node_modules[\\/](?!pdf-lib[\\/]|@pdf-lib[\\/]|pdfjs-dist[\\/])/, priority: 1 },
];

export default defineConfig(({ mode }) => {
  const viteEnv = loadEnv(mode, process.cwd(), "");

  const apiHttps = viteEnv.VITE_API_HTTPS || nodeEnv.services__backend__https__0 || "N/A";
  const apiHttp = viteEnv.VITE_API_HTTP || nodeEnv.services__backend__http__0 || "N/A";
  // `??` so an explicitly empty VITE_API_BASE means "same origin" (used by the Docker build behind a reverse proxy)
  const apiBase = viteEnv.VITE_API_BASE ?? (nodeEnv.services__backend__https__0 || nodeEnv.services__backend__http__0 || "N/A");

  console.log("===== RESOLVED ENVIRONMENT =====");
  console.log("NODE_ENV =", nodeEnv.NODE_ENV);
  console.log("HOST =", nodeEnv.HOST);
  console.log("PORT =", nodeEnv.PORT);
  console.log("API HTTPS =", apiHttps);
  console.log("API HTTP =", apiHttp);
  console.log("API BASE =", apiBase);
  console.log("================================");

  return {
    plugins: [react(), tailwindcss(), pdfjsStandardFonts()],
    resolve: {
      tsconfigPaths: true,
      alias: {
        "@components": path.resolve(__dirname, "../../packages/ui/src/components/ui"),
        "@components/ui": path.resolve(__dirname, "../../packages/ui/src/components/ui"),
        "@starlights/ui": path.resolve(__dirname, "../../packages/ui/src"),
      },
    },
    build: {
      rolldownOptions: { output: { codeSplitting: { groups: chunkGroups } } },
      // @pdf-lib/fontkit ships as one prebuilt module of ~711 kB minified plus its own copy of pako (~45 kB), so its chunk
      // cannot be smaller (the vendor chunk is ~700 kB); anything new that grows past this still gets the warning
      chunkSizeWarningLimit: 800,
    },
    // the sheet's PDF is made in a module worker (aurora-sheet-client.ts: { type: "module" }), split the same way
    worker: {
      format: "es",
      rolldownOptions: { output: { codeSplitting: { groups: pdfLibGroups } } },
    },
    server: {
      port: parseInt(nodeEnv.PORT || "55052"),
      host: "127.0.0.1",
      open: false,
    },
    define: {
      "import.meta.env.VITE_API_HTTPS": JSON.stringify(apiHttps),
      "import.meta.env.VITE_API_HTTP": JSON.stringify(apiHttp),
      "import.meta.env.VITE_API_BASE": JSON.stringify(apiBase),
    },
  };
});
