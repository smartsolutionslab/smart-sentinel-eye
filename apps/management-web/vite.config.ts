import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { fontPreload } from '../shared/src/ui/fonts/fontPreload.ts';

// Aspire injects backend service URLs as environment variables (ADR-0074).
// Local dev port chosen to match the Aspire JS resource wiring.

export default defineConfig({
  plugins: [
    react(),
    // The sign-in screen's <h1> and body text are this app's first-paint
    // faces: Sans SemiBold for the heading, Sans Regular for the body.
    fontPreload(['IBMPlexSans-Regular-Latin1.woff2', 'IBMPlexSans-SemiBold-Latin1.woff2']),
  ],
  server: {
    port: 5173,
    strictPort: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // A wait allowed 10 s (src/test/setup.ts) that is killed at Vitest's 5 s
    // default has not been fixed — its error message has been changed from
    // "Unable to find an element" to "Test timed out in 5000ms", which names
    // nothing. Measured, not predicted: with the deadline raised and this
    // default left alone, a contended run failed exactly that way (spec 216
    // §Claim 8), and the failing test consumed 4630 ms under contention even
    // while still capped at the old 1000 ms deadline.
    testTimeout: 30_000,
  },
});
