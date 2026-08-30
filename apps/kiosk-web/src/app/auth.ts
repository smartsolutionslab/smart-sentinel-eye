import { WebStorageStateStore } from 'oidc-client-ts';
import type { AuthProviderProps } from 'react-oidc-context';
import { REDIRECT_GUARD_STORAGE_KEY } from './useSessionExpiry.js';

// Injected by the host (Aspire: VITE_KEYCLOAK_URL) and MUST match the issuer the
// services validate (ServiceDefaults.AddBearerAuthentication uses the same Aspire
// endpoint), so we never hardcode the port. The deploy layer supplies it in prod.
const KEYCLOAK_BASE_URL = (import.meta.env.VITE_KEYCLOAK_URL ?? 'http://localhost:8080').replace(/\/+$/, '');

// Spec 011 FR-010: a prod bundle without the Keycloak origin would try to
// authenticate against localhost on the kiosk device — fail loudly.
if (import.meta.env.PROD && (import.meta.env.VITE_KEYCLOAK_URL ?? '') === '') {
  throw new Error('VITE_KEYCLOAK_URL must be set in production builds (see docs/deployment-frontend-env.md).');
}

/**
 * OIDC config for kiosk-web. Same Keycloak realm as management-web, its own
 * public client — ``kiosk-web``, whose default client scopes are exactly
 * ``KeycloakScopeBundles.Kiosk``, the set Identity grants every kiosk device it
 * enrols. A browser kiosk is not a second notion of what a kiosk may do.
 *
 * Spec 041: this used to sign in as a retired client that carried no
 * ``sse-groups`` scope and therefore no fab claim, so every fab-scoped read was
 * refused and the kiosk could never list a wall. That client also carried
 * ``sse.management`` — write-everything authority on a screen bolted to a
 * factory wall, using none of it.
 */
export const oidcConfig: AuthProviderProps = {
  authority: `${KEYCLOAK_BASE_URL}/realms/smart-sentinel-eye`,
  client_id: 'kiosk-web',
  redirect_uri:
    typeof window !== 'undefined' ? `${window.location.origin}/oidc/callback` : 'http://localhost:5174/oidc/callback',
  // `openid` alone: the six sse.* scopes and `sse-groups` are DEFAULT client
  // scopes, so Keycloak applies them whether or not they are asked for — and
  // `sse-groups` sets `include.in.token.scope: false`, so it could not be
  // requested anyway. Naming any scope this client does not hold fails the
  // whole sign-in with `invalid_scope`, no token at all.
  // `offline_access` is what escapes the ten-hour ceiling (ADR-0131). The
  // sign-in session ends on that clock **regardless of activity**, so a wall
  // that never reboots still dropped to a prompt roughly twice a day per
  // screen — the failure nobody had recorded, because it needs ten hours to
  // observe. An offline grant is not bound by it.
  //
  // The six sse.* scopes and `sse-groups` stay unnamed: they are DEFAULT client
  // scopes, applied whether or not they are asked for, and `sse-groups` cannot
  // be requested at all. **Authority is unchanged by this feature** — naming any
  // scope this client does not hold fails the whole sign-in with
  // `invalid_scope`, no token at all.
  scope: 'openid offline_access',

  // **Storage that outlives the browser process** (ADR-0131). The default is
  // tied to the process, so a reboot lost every token unconditionally and no
  // server-side session setting could help — nothing on the device remembered
  // anything. This is what makes a power cut recoverable at all.
  //
  // It is also the line that widens the exposure, and the ADR records the trade
  // rather than leaving it here: a powered-off stolen screen now yields a
  // usable grant where it yielded nothing. What bounds the loss is that the
  // grant is that screen's alone, revocable on its own, and view-only in one
  // fab — not where it is kept, which is readable on the machine it sits on.
  userStore: typeof window === 'undefined' ? undefined : new WebStorageStateStore({ store: window.localStorage }),

  // Renew before expiry rather than after it, so a screen that nobody is
  // watching never passes through the expired state on its way back.
  automaticSilentRenew: true,
  // Spec 011 FR-013: a completed sign-in ends any expiry flow — drop the loop
  // guard and land back on the layout the kiosk was showing (deep-link
  // restoration through the OIDC state round-trip).
  onSigninCallback: (user) => {
    if (typeof window === 'undefined') {
      return;
    }
    window.sessionStorage.removeItem(REDIRECT_GUARD_STORAGE_KEY);
    const state = user?.state as { returnTo?: unknown } | undefined;
    const returnTo =
      typeof state?.returnTo === 'string' && state.returnTo.startsWith('/') && !state.returnTo.startsWith('//')
        ? state.returnTo
        : '/';
    window.history.replaceState({}, document.title, returnTo);
  },
};
