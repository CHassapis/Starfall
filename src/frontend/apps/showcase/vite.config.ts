import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import path from "path"
import { env } from "process"
import { defineConfig, loadEnv } from "vite"

export default defineConfig(({ mode }) => {
  const viteEnv = loadEnv(mode, process.cwd(), "")

  const HTTPS =
    viteEnv.VITE_API_HTTPS || env.services__backend__https__0 || "N/A"
  const HTTP = viteEnv.VITE_API_HTTP || env.services__backend__http__0 || "N/A"
  const ROOT =
    viteEnv.VITE_API_BASE ||
    env.services__backend__https__0 ||
    env.services__backend__http__0 ||
    "N/A"

  console.log("===== CONTENT MANAGER ENVIRONMENT =====")
  console.log("NODE_ENV =", env.NODE_ENV)
  console.log("HOST =", env.HOST)
  console.log("PORT =", env.PORT)
  console.log("API HTTPS =", HTTPS)
  console.log("API HTTP =", HTTP)
  console.log("API BASE =", ROOT)
  console.log("================================")

  return {
    plugins: [react(), tailwindcss()],
    resolve: {
      dedupe: ["react", "react-dom"],
      alias: {
        "@": path.resolve(__dirname, "./src"),
      },
    },
    // the libraries (and the workspace's ui-framework) in chunks of their own, under the 500 kB size warning
    build: {
      rolldownOptions: {
        output: { codeSplitting: { groups: [{ name: "vendor", test: /node_modules[\\/]|packages[\\/]ui-framework[\\/]/, maxSize: 400_000 }] } },
      },
    },
    server: {
      port: parseInt(env.PORT || "55054"),
      open: true,
    },
  }
})
