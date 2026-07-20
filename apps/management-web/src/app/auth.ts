import type { AuthProviderProps } from 'react-oidc-context';

// OIDC config for management-web (ADR-0080). Same Keycloak realm as kiosk-web,
// the shared SPA public client `smart-sentinel-eye-web` (whose default client
// scopes include `sse.management`, so the minted token carries it). The Keycloak
// origin is injected by the host (Aspire: VITE_KEYCLOAK_URL) and MUST match the
// issuer the services validate (ServiceDefaults.AddBearerAuthentication reads the
// same Aspire-published endpoint), so we never hardcode the port. management-web
// has no router yet, so the redirect lands on the app root and react-oidc-context
// processes the callback in place; onSigninCallback strips the code/state query.
const KEYCLOAK_BASE_URL = (import.meta.env.VITE_KEYCLOAK_URL ?? 'http://localhost:8080').replace(/\/+$/, '');

// Spec 011 FR-010: a prod bundle without the Keycloak origin would try to
// authenticate against localhost on the operator's machine — fail loudly.
if (import.meta.env.PROD && (import.meta.env.VITE_KEYCLOAK_URL ?? '') === '') {
  throw new Error(
    'VITE_KEYCLOAK_URL must be set in production builds (see docs/deployment-frontend-env.md).',
  );
}

export const oidcConfig: AuthProviderProps = {
  authority: `${KEYCLOAK_BASE_URL}/realms/smart-sentinel-eye`,
  client_id: 'smart-sentinel-eye-web',
  redirect_uri:
    typeof window !== 'undefined' ? `${window.location.origin}/` : 'http://localhost:5173/',
  // The realm does not expose a requestable `profile` scope; `sse.management`
  // is a default client scope of smart-sentinel-eye-web and grandfathers the
  // granular sse.* policies (ServiceDefaults RequireScopeExtensions).
  scope: 'openid sse.management',
  onSigninCallback: () => {
    if (typeof window !== 'undefined') {
      window.history.replaceState({}, document.title, '/');
    }
  },
};
