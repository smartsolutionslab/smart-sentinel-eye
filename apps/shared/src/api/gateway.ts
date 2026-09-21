import { fetchBaseQuery } from '@reduxjs/toolkit/query/react';
import { logResilienceEvent } from '../observability/resilienceLog.js';

// ADR-0106 (#1005): the browser apps reach every context REST API through the
// single API gateway, cross-origin. The gateway's CORS policy (#1003) allows the
// app origins, and it routes on `/<context>/...` — stripping that prefix before
// forwarding — so `${origin}/<context>/<group>` lands on the service's `<group>`
// route (e.g. camera-catalog exposes `/cameras`). The gateway origin is injected
// by the host: Aspire sets VITE_API_GATEWAY_URL in dev; the deploy layer supplies
// the public URL in prod. An empty origin falls back to same-origin outside a
// production build, which keeps unit tests and previews working; a production
// build with no origin throws at load instead of falling back (see below).
//
// Realtime (SignalR, ADR-0152) and WebRTC media do NOT go through here — they
// stay direct, off the gateway and off the §IV latency budget.
const gatewayOrigin: string = (import.meta.env.VITE_API_GATEWAY_URL ?? '').replace(/\/+$/, '');

// Spec 011 FR-010: a prod bundle without the gateway origin would silently
// fire API calls at the static-file origin — fail loudly at load instead.
if (import.meta.env.PROD && gatewayOrigin === '') {
  throw new Error('VITE_API_GATEWAY_URL must be set in production builds (see docs/deployment-frontend-env.md).');
}

export const gatewayApiUrl = (route: string): string => `${gatewayOrigin}/${route}`;

// ADR-0113 Layer 1: a mutating request carries the aggregate version it was
// read at, so the server can refuse an edit built on a stale view instead of
// silently overwriting whoever wrote in between.
//
// The version is threaded explicitly through each mutation's arguments rather
// than cached here and injected centrally. A central store would have to map a
// request URL back to the resource whose ETag it needs -- `POST
// /layouts/{id}/revisions/2/publish` is guarded by the ETag from `GET
// /layouts/{id}` -- and any miss degrades to a request with no version, which
// is the silent fallback this whole mechanism exists to remove. Passing it as
// an argument makes TypeScript reject a call site that forgets.
export const ifMatch = (version: number): Record<string, string> => ({ 'If-Match': `"${version}"` });

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

// Spec 011 FR-011/012: a 401 gets exactly one silent renewal and one retry
// before the session counts as expired. Both hooks are app-registered module
// singletons for the same reason as setAccessTokenProvider: the shared clients
// are app-agnostic, and registration must happen during render, before the
// first query dispatches.
//
// Spec 205 (#2301): a boolean tells the retry that a renewal happened and
// withholds the one thing it needs. `react-oidc-context` publishes the renewed
// user through a `useReducer` dispatch, which React schedules as a macrotask;
// the retry below is a microtask on the renewal's own promise chain and always
// runs first. Reading the token from any render-written slot — a module getter
// or a ref alike — therefore re-sends the bearer that just failed.
type SessionRenewer = () => Promise<string | undefined>;

let sessionRenewer: SessionRenewer = () => Promise.resolve(undefined);
let onSessionExpired: () => void = () => undefined;

export const setSessionRenewer = (renew: SessionRenewer): void => {
  sessionRenewer = renew;
};

export const setOnSessionExpired = (handler: () => void): void => {
  onSessionExpired = handler;
};

const isUsable = (token: string | undefined): token is string => token !== undefined && token !== '';

// A burst of queries after token death must not stampede the identity
// provider: every concurrent 401 awaits the single in-flight renewal.
let renewalInFlight: Promise<string | undefined> | null = null;

const renewSessionOnce = (): Promise<string | undefined> => {
  if (renewalInFlight === null) {
    logResilienceEvent('session', 'renew-start');
    renewalInFlight = sessionRenewer()
      .catch(() => undefined)
      .then((token) => {
        renewalInFlight = null;
        logResilienceEvent('session', isUsable(token) ? 'renew-success' : 'renew-failure');
        return token;
      });
  }
  return renewalInFlight;
};

const gatewayQueryFor = (route: string, bearer: AccessTokenGetter): ReturnType<typeof fetchBaseQuery> =>
  fetchBaseQuery({
    baseUrl: gatewayApiUrl(route),
    prepareHeaders: (headers) => {
      const token = bearer();
      if (isUsable(token)) {
        headers.set('Authorization', `Bearer ${token}`);
      }
      return headers;
    },
  });

export const gatewayBaseQuery = (route: string): ReturnType<typeof fetchBaseQuery> => {
  // `() => accessTokenProvider()`, not `accessTokenProvider` — every RTK
  // client below calls this at MODULE scope, at import time, long before
  // AuthGate renders and calls setAccessTokenProvider. Passing the binding
  // directly would capture today's value (the `() => undefined` default)
  // forever; the wrapper defers the read to request time instead, by which
  // point registration has happened (phase-6 review, #2301 — the same class
  // of stale-closure bug this whole fix exists to close, one line away from
  // reintroducing it).
  const baseQuery = gatewayQueryFor(route, () => accessTokenProvider());

  return async (args, queryApi, extraOptions) => {
    let result = await baseQuery(args, queryApi, extraOptions);
    if (result.error === undefined || result.error.status !== 401) {
      return result;
    }

    const renewed = await renewSessionOnce();
    if (isUsable(renewed)) {
      result = await gatewayQueryFor(route, () => renewed)(args, queryApi, extraOptions);
      if (result.error === undefined || result.error.status !== 401) {
        return result;
      }
    }

    logResilienceEvent('session', 'expired');
    onSessionExpired();
    return result;
  };
};
