import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// Aspire injects backend service URLs as environment variables (ADR-0074).
// Local dev port chosen to match the Aspire JS resource wiring.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
});
