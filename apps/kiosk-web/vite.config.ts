import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// The realtime SignalR hub (LayoutComposition) stays OFF the API gateway — it's
// not on the §IV REST path (see api/gateway.ts). The browser opens it at the
// app origin (`/hubs/layouts`); in dev the Vite server proxies `/hubs` to the
// Aspire-resolved layout-composition endpoint (`.WithReference(layoutComposition)`
// injects `services__layout-composition__http__0`), upgrading the WebSocket too.
// Without this the relative `/hubs/layouts` resolves to the Vite origin and
// 404s on negotiate, so no live highlight/lifecycle pushes land.
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
    // **Taken from the environment, because one bundle now runs twice** (spec
    // 052): an ordinary kiosk and a wall display, differing only in
    // configuration. The host has always injected `PORT`; this file ignored it
    // and hardcoded the same number, which worked only while there was one
    // instance. A second one bound the same port, and `strictPort` turned that
    // into an immediate exit — reported upstream as "running", because the
    // process had indeed started.
    port: Number(process.env['PORT'] ?? 5174),
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
