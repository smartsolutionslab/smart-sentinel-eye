import type { AuthProviderProps } from 'react-oidc-context';

// OIDC config for management-web (ADR-0080). Same Keycloak realm as kiosk-web,
// its own public client `management-web` (spec 200, issue #2279 — replaces
// `smart-sentinel-eye-web`, whose default-granted `sse.management` bundle
// grandfathered every granular `sse.*` policy for every operator regardless of
// role). The Keycloak origin is injected by the host (Aspire: VITE_KEYCLOAK_URL)
// and MUST match the issuer the services validate (ServiceDefaults.AddBearerAuthentication
// reads the same Aspire-published endpoint), so we never hardcode the port.
// management-web has no router yet, so the redirect lands on the app root and
// react-oidc-context processes the callback in place; onSigninCallback strips
// the code/state query.
const KEYCLOAK_BASE_URL = (import.meta.env.VITE_KEYCLOAK_URL ?? 'http://localhost:8080').replace(/\/+$/, '');

// Spec 011 FR-010: a prod bundle without the Keycloak origin would try to
// authenticate against localhost on the operator's machine — fail loudly.
if (import.meta.env.PROD && (import.meta.env.VITE_KEYCLOAK_URL ?? '') === '') {
  throw new Error('VITE_KEYCLOAK_URL must be set in production builds (see docs/deployment-frontend-env.md).');
}

export const oidcConfig: AuthProviderProps = {
  authority: `${KEYCLOAK_BASE_URL}/realms/smart-sentinel-eye`,
  client_id: 'management-web',
  redirect_uri: typeof window !== 'undefined' ? `${window.location.origin}/` : 'http://localhost:5173/',
  // `openid` alone: the twenty-one granular sse.* scopes and `sse-groups` are
  // DEFAULT client scopes of `management-web`, so Keycloak applies them
  // whether or not they are asked for. Naming any scope this client does not
  // hold (including the retired `sse.management` bundle) fails the whole
  // sign-in with `invalid_scope` and yields no token at all — see
  // `apps/kiosk-web/src/app/auth.ts` for the same behaviour, observed there
  // when this line briefly asked for a scope the realm had not granted.
  scope: 'openid',
  // Spec 011 FR-013: land back where the operator was when the sign-in
  // round-trip started (deep-link restoration via the OIDC state).
  onSigninCallback: (user) => {
    if (typeof window === 'undefined') {
      return;
    }
    const state = user?.state as { returnTo?: unknown } | undefined;
    const returnTo =
      typeof state?.returnTo === 'string' && state.returnTo.startsWith('/') && !state.returnTo.startsWith('//')
        ? state.returnTo
        : '/';
    window.history.replaceState({}, document.title, returnTo);
  },
};
