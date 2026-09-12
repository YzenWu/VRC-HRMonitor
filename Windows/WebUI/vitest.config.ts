import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';

// Tests use a separate configuration because they need neither the Vite base path nor the development proxy.
// Vitest resolves test files directly instead of loading the `/src` entry from index.html, so it can run in this project path.
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.spec.ts'],
    reporters: 'default',
  },
});
