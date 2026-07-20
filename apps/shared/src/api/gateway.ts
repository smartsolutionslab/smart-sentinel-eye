import { fetchBaseQuery } from '@reduxjs/toolkit/query/react';

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

// Spec 011 FR-010: a prod bundle without the gateway origin would silently
// fire API calls at the static-file origin — fail loudly at load instead.
if (import.meta.env.PROD && gatewayOrigin === '') {
  throw new Error(
    'VITE_API_GATEWAY_URL must be set in production builds (see docs/deployment-frontend-env.md).',
  );
}

export const gatewayApiUrl = (route: string): string => `${gatewayOrigin}/${route}`;

// Every context API requires a Keycloak-minted JWT (ADR-0007/0008; the gateway
// forwards Authorization unmodified, ADR-0106). The RTK Query clients live in
// this shared package and are app-agnostic, so each app registers a getter that
// sources the current access token from its OIDC user; prepareHeaders attaches
// it as a bearer on every gateway request.
type AccessTokenGetter = () => string | undefined;

let accessTokenProvider: AccessTokenGetter = () => undefined;

export const setAccessTokenProvider = (provider: AccessTokenGetter): void => {
  accessTokenProvider = provider;
};

export const gatewayBaseQuery = (route: string): ReturnType<typeof fetchBaseQuery> =>
  fetchBaseQuery({
    baseUrl: gatewayApiUrl(route),
    prepareHeaders: (headers) => {
      const token = accessTokenProvider();
      if (token !== undefined && token !== '') {
        headers.set('Authorization', `Bearer ${token}`);
      }
      return headers;
    },
  });
