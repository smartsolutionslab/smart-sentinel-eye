import { WebStorageStateStore } from 'oidc-client-ts';
import type { UserManagerSettings } from 'oidc-client-ts';
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
 * Whether this screen is a wall display.
 *
 * <p>
 * <b>One flag decides both the client and the scopes</b>, and that is the point
 * (spec 052). They cannot be chosen separately, so there is no half
 * configuration: a screen asking for a long-lived grant is always the screen
 * signing in as the client that offers one.
 * </p>
 */
const IS_WALL_DISPLAY = (import.meta.env.VITE_KIOSK_MODE ?? '').toLowerCase() === 'wall';

/**
 * The client this screen signs in as, and what it asks for.
 *
 * <p>
 * <b>A wall display uses a different client, and the reason is not the scope it
 * requests.</b> Scopes belong to clients, so a second client is the only place a
 * wall display's authority can be narrowed — <c>kiosk-wall</c> carries the read
 * scopes and not <c>sse.events.write</c>. A never-expiring grant that could
 * inject events into a fab is what spec 050 shipped while recording that the
 * account could change nothing.
 * </p>
 *
 * <p>
 * <b>Why the scope cannot simply be added to kiosk-web.</b> An optional scope
 * refuses nobody only while nobody asks for it: an account <i>without</i> the
 * matching privilege that requests it is refused the <b>entire sign-in</b>, not
 * merely that scope. Adding it here would lock out every operator — spec 050's
 * failure by a different route. And the application cannot decide per account,
 * because the scope is requested before anyone has signed in.
 * </p>
 */
const SCREEN = IS_WALL_DISPLAY
  ? { clientId: 'kiosk-wall', scope: 'openid offline_access' }
  : { clientId: 'kiosk-web', scope: 'openid' };

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
/**
 * Intersected with the settings type rather than left as `AuthProviderProps`
 * alone, which is a union whose other member carries a pre-built manager and
 * therefore none of these fields. The union is right for the provider and
 * useless for anything reading the configuration back — including the tests
 * that assert this kiosk asks for no authority beyond coming back.
 */
export const oidcConfig: AuthProviderProps & UserManagerSettings = {
  authority: `${KEYCLOAK_BASE_URL}/realms/smart-sentinel-eye`,
  client_id: SCREEN.clientId,
  redirect_uri:
    typeof window !== 'undefined' ? `${window.location.origin}/oidc/callback` : 'http://localhost:5174/oidc/callback',
  // `openid` alone: the six sse.* scopes and `sse-groups` are DEFAULT client
  // scopes, so Keycloak applies them whether or not they are asked for — and
  // `sse-groups` sets `include.in.token.scope: false`, so it could not be
  // requested anyway. Naming any scope this client does not hold fails the
  // whole sign-in with `invalid_scope`, no token at all.
  // **Deliberately not `offline_access`, and this is a scoped decision rather
  // than an omission** (ADR-0131). A long-lived grant is what would escape the
  // ten-hour session ceiling, and it costs more than it first appears: the
  // identity provider requires an `offline_access` **realm role** on whoever
  // signs the screen in, which hands that account the power to mint long-lived
  // tokens generally. That is authority this feature is not willing to buy, so
  // the ceiling is left standing and tracked separately.
  //
  // The six sse.* scopes and `sse-groups` stay unnamed: they are DEFAULT client
  // scopes, applied whether or not they are asked for, and `sse-groups` cannot
  // be requested at all. Naming any scope this client does not hold fails the
  // whole sign-in with `invalid_scope`, no token at all — observed, not
  // theorised, when this line briefly asked for one the realm had not granted.
  //
  // **Spec 052 adds the wall-display case.** A wall screen signs in as
  // `kiosk-wall` and asks for `offline_access` as well, which is what lets its
  // grant outlive the session ceiling. Every other screen is unchanged.
  scope: SCREEN.scope,

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
