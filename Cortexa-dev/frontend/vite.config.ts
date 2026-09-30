import { execSync } from 'node:child_process';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';

// Resolve the build stamp used by the %VITE_APP_VERSION% marker in index.html.
// Prefer an explicit env var (set by CI), then the current git SHA, then 'dev'.
function resolveAppVersion(): string {
  if (process.env.VITE_APP_VERSION) return process.env.VITE_APP_VERSION;
  try {
    return execSync('git rev-parse --short HEAD', { encoding: 'utf8' }).trim();
  } catch {
    return 'dev';
  }
}

process.env.VITE_APP_VERSION = resolveAppVersion();

// Local dev talks to the app SAME-ORIGIN and Vite proxies /api to the deployed
// gateway server-side. This mirrors Azure Static Web Apps (which forwards /api
// to the linked backend) and sidesteps the gateway's origin-locked CORS policy,
// which only whitelists the production SWA host — not localhost / tunnel URLs.
const DEV_PROXY_TARGET =
  process.env.VITE_DEV_PROXY_TARGET ??
  'https://cortexa-dev-api-gateway.proudsmoke-86efe866.eastus.azurecontainerapps.io';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // Explicit host/port so the dev server always binds all interfaces inside
    // the preview container, regardless of how pnpm forwards (or swallows)
    // the --host/--port CLI flags from Dockerfile.dev's CMD.
    host: '0.0.0.0',
    port: 5173,
    proxy: {
      '/api': {
        target: DEV_PROXY_TARGET,
        changeOrigin: true,
        secure: true,
        // Rewrite the refresh-token cookie's domain so the browser stores it
        // against the dev host and replays it on subsequent /api/auth/refresh.
        cookieDomainRewrite: '',
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: 'hidden',
    rollupOptions: {
      output: {
        manualChunks: (id: string) => {
          // Keep the heavy client-side PDF export renderer out of the main vendor chunk.
          if (id.includes('@react-pdf/') || id.includes('/pdfkit') || id.includes('/fontkit')) return 'pdf';
          // pdfjs-dist backs the lazy PdfProvenanceViewer (US124) — its own chunk so it
          // never loads on the main bundle path, only when the viewer is lazy-imported.
          if (id.includes('pdfjs-dist')) return 'pdfjs';
          // shiki + its per-language grammars/themes back the lazy CodeProvenanceViewer
          // (US125) — isolated the same way, only loaded when a code-kind candidate
          // is actually viewed.
          if (id.includes('shiki')) return 'shiki';
          if (id.includes('node_modules')) return 'vendor';
        },
      },
    },
  },
});
