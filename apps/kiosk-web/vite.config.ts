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

const layoutComposition =
  process.env['services__layout-composition__http__0'] ??
  process.env['services__layout-composition__https__0'];

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
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
