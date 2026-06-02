// ADR-0106 (#1005): the browser apps reach every context REST API through the
// single API gateway, cross-origin. The gateway's CORS policy (#1003) allows the
// app origins, and it routes on `/<context>/...` — stripping that prefix before
// forwarding — so `${origin}/<context>/<group>` lands on the service's `<group>`
// route (e.g. camera-catalog exposes `/cameras`). The gateway origin is injected
// by the host: Aspire sets VITE_API_GATEWAY_URL in dev; the deploy layer supplies
// the public URL in prod. An empty origin falls back to same-origin, which keeps
// unit tests and previews working and degrades to Ingress-relative routing.
//
// Realtime (ADR-0076 WebSocket) and WebRTC media do NOT go through here — they
// stay direct, off the gateway and off the §IV latency budget.
const gatewayOrigin: string = (import.meta.env.VITE_API_GATEWAY_URL ?? '').replace(/\/+$/, '');

export const gatewayApiUrl = (route: string): string => `${gatewayOrigin}/${route}`;
