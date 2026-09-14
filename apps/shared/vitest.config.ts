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
    // Spec 157: CameraViewerCameraSwap.test.tsx's failed-read scenario waits
    // out RTK Query's real 5 s `pollingInterval` on real timers (deliberately
    // — see its `realWait` comment), which alone exceeds the 5 s default. A
    // timed-out test does not stop its own in-flight promises; they keep
    // running and corrupt whichever test runs next, which is what surfaced
    // this rather than a bare timeout report.
    testTimeout: 10_000,
  },
});
