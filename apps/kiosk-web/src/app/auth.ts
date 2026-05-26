import type { AuthProviderProps } from 'react-oidc-context';

const KEYCLOAK_BASE_URL =
  (typeof window !== 'undefined' && (window as { __KEYCLOAK_URL__?: string }).__KEYCLOAK_URL__) ??
  'http://localhost:8080';

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
  scope: 'openid profile sse.management',
  onSigninCallback: () => {
    if (typeof window !== 'undefined') {
      window.history.replaceState({}, document.title, '/');
    }
  },
};
