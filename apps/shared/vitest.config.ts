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
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  },
});
