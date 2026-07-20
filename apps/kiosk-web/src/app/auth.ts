import type { AuthProviderProps } from 'react-oidc-context';

// Injected by the host (Aspire: VITE_KEYCLOAK_URL) and MUST match the issuer the
// services validate (ServiceDefaults.AddBearerAuthentication uses the same Aspire
// endpoint), so we never hardcode the port. The deploy layer supplies it in prod.
const KEYCLOAK_BASE_URL = (import.meta.env.VITE_KEYCLOAK_URL ?? 'http://localhost:8080').replace(
  /\/+$/,
  '',
);

// Spec 011 FR-010: a prod bundle without the Keycloak origin would try to
// authenticate against localhost on the kiosk device — fail loudly.
if (import.meta.env.PROD && (import.meta.env.VITE_KEYCLOAK_URL ?? '') === '') {
  throw new Error(
    'VITE_KEYCLOAK_URL must be set in production builds (see docs/deployment-frontend-env.md).',
  );
}

/**
 * OIDC config for kiosk-web. Same Keycloak realm as management-web,
 * separate public client (``smart-sentinel-eye-kiosk``) added to the
 * realm-export JSON in PR A. Per Phase-1 Q&A, kiosk-web reuses the
 * admin sign-in flow; unattended-kiosk credentials are deferred.
 */
export const oidcConfig: AuthProviderProps = {
  authority: `${KEYCLOAK_BASE_URL}/realms/smart-sentinel-eye`,
  client_id: 'smart-sentinel-eye-kiosk',
  redirect_uri:
    typeof window !== 'undefined'
      ? `${window.location.origin}/oidc/callback`
      : 'http://localhost:5174/oidc/callback',
  // The realm does not expose a requestable `profile` scope; `sse.management`
  // is a default client scope and grandfathers the granular sse.* policies.
  scope: 'openid sse.management',
  onSigninCallback: () => {
    if (typeof window !== 'undefined') {
      window.history.replaceState({}, document.title, '/');
    }
  },
};
