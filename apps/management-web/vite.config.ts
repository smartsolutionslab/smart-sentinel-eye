import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// Aspire injects backend service URLs as environment variables (ADR-0074).
// Local dev port chosen to match the Aspire JS resource wiring.
//
// The realtime SignalR hub (LayoutComposition) stays OFF the API gateway — it's
// not on the §IV REST path (see api/gateway.ts). The browser opens it at the
// app origin (`/hubs/layouts`); in dev the Vite server proxies `/hubs` to the
// Aspire-resolved layout-composition endpoint (`.WithReference(layoutComposition)`
// injects `services__layout-composition__http__0`), upgrading the WebSocket too.
// Without this the relative `/hubs/layouts` resolves to the Vite origin and 404s.
//
// `process` is supplied by Node when Vite evaluates this config; declare it
// locally so the app tsconfig (which has no @types/node) still typechecks.
declare const process: { env: Record<string, string | undefined> };

// VITE_LAYOUT_HUB_ORIGIN first: Aspire's own `services__layout-composition__*`
// keys contain hyphens, and POSIX shells (bash, dash) drop environment variables
// whose names aren't valid identifiers. `npm run dev` spawns Vite through
// `sh -c`, so on Linux those keys never arrive and the proxy below is silently
// omitted — every `/hubs/layouts/negotiate` then 404s. cmd.exe keeps them, which
// is why that only ever reproduced off Windows. AppHost injects the alias; the
// raw keys stay as a fallback for anything that starts Vite directly.
const layoutComposition =
  process.env['VITE_LAYOUT_HUB_ORIGIN'] ??
  process.env['services__layout-composition__http__0'] ??
  process.env['services__layout-composition__https__0'];

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: layoutComposition
      ? { '/hubs': { target: layoutComposition, ws: true, changeOrigin: true, secure: false } }
      : undefined,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
});
