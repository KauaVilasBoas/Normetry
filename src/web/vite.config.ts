import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// The browser calls the core API on this origin under /api, and the dev server forwards it, so the
// API needs no CORS. The AppHost sets NORMETRY_API_URL; the fallback is the API's http profile.
const apiUrl = process.env.NORMETRY_API_URL ?? "http://localhost:5208";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // The Keycloak client only accepts http://localhost:5173. The AppHost serves that port too and
    // passes its own --port to the dev server behind it.
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": { target: apiUrl, rewrite: (path) => path.replace(/^\/api/, "") },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    restoreMocks: true,
    unstubGlobals: true,
  },
});
