/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  // Read the repo-root .env. Vite exposes only VITE_* variables to the browser bundle,
  // and anything in the bundle is public: never prefix a secret with VITE_.
  envDir: '..',
  server: {
    port: 5173,
    strictPort: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.ts',
    // Fixed value so tests never depend on the real .env (CI has none).
    env: { VITE_API_URL: 'http://localhost:5080' },
  },
})
