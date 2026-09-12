import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

// V3 Vue WebUI. Production builds use a temporary root without `#`; see build-webui.ps1.
// Local development uses the same workaround in dev-webui.ps1 and proxies API traffic to port 9460.
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  // Use root-relative assets because the backend's web root already points at the published `webui` directory.
  // A relative base would request `/webui/assets/*`, adding a second prefix and triggering the SPA HTML fallback.
  base: '/',
  server: {
    host: '127.0.0.1',
    port: 5173,
    proxy: {
      '/api': { target: 'http://127.0.0.1:9460', changeOrigin: true },
      '/ws': { target: 'ws://127.0.0.1:9460', ws: true },
    },
  },
});
