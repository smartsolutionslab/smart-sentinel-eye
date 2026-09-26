import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  resolve: {
    // The package imports its own modules by package name (Node self-
    // reference via `exports`); map the name onto src so vitest resolves
    // them without a built package.
    alias: {
      '@smart-sentinel-eye/shared': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    globals: true,
    environment: 'node',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
    // Raising `asyncUtilTimeout` alone (src/test/setup.ts) just renames the
    // failure once contention pushes a wait past Vitest's 5 s default — it
    // dies as "Test timed out in 5000ms" instead, which names nothing (spec
    // 216 §Claim 8). Match management-web's and kiosk-web's `testTimeout`.
    testTimeout: 30_000,
  },
});
