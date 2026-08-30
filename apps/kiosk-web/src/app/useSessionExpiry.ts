import { useCallback, useEffect, useRef, useState } from 'react';
import type { AuthContextProps } from 'react-oidc-context';
import { setOnSessionExpired, setSessionRenewer } from '@smart-sentinel-eye/shared/api/gateway';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';

export const REDIRECT_GUARD_STORAGE_KEY = 'sse.auth.redirectGuard';
export const WAS_AUTHENTICATED_STORAGE_KEY = 'sse.auth.wasAuthenticated';

// A redirect younger than this that still landed us back unauthenticated means
// the provider needs interaction — redirecting again would just loop.
const REDIRECT_GUARD_WINDOW_MS = 60_000;

const redirectGuardIsFresh = (): boolean => {
  const raw = window.sessionStorage.getItem(REDIRECT_GUARD_STORAGE_KEY);
  return raw !== null && Date.now() - Number(raw) < REDIRECT_GUARD_WINDOW_MS;
};

/**
 * Whether this screen has ever signed in.
 *
 * <p>
 * <b>Kept where a restart cannot destroy it</b> (ADR-0131), alongside the grant
 * itself. It lived with the browser process until spec 049, so a rebooted kiosk
 * reported that it had never signed in — and then showed the *first-boot* manual
 * sign-in button rather than recovering on its own. Moving the grant and leaving
 * this behind fixed half a mechanism: the screen held a usable grant and
 * displayed a button asking someone to press it.
 * </p>
 */
export const hasBeenAuthenticated = (): boolean => window.localStorage.getItem(WAS_AUTHENTICATED_STORAGE_KEY) !== null;

export interface SessionExpiryResult {
  /** Interactive credentials are genuinely required (data-model §3 expired-final). */
  sessionExpired: boolean;
  /** Manual retry from the session-expired screen: clears the loop guard first. */
  retrySignIn: () => void;
}

/**
 * Kiosk sign-in session machine (spec 011 data-model §3): silent renewal via
 * the gateway-registered renewer, automatic re-sign-in with deep-link
 * restoration on expiry, and a loop-guarded full-screen expired state when
 * the provider requires interaction (FR-011…014).
 */
export function useSessionExpiry(auth: AuthContextProps): SessionExpiryResult {
  const [sessionExpired, setSessionExpired] = useState(false);
  const redirectStarted = useRef(false);

  const beginReauthentication = useCallback(() => {
    if (redirectStarted.current) {
      return;
    }
    if (redirectGuardIsFresh()) {
      logResilienceEvent('session', 'expired→final');
      setSessionExpired(true);
      return;
    }
    redirectStarted.current = true;
    window.sessionStorage.setItem(REDIRECT_GUARD_STORAGE_KEY, String(Date.now()));
    logResilienceEvent('session', 'expired→redirecting', { returnTo: window.location.pathname });
    void auth.signinRedirect({ state: { returnTo: window.location.pathname } });
  }, [auth]);

  // Registered during render for the same reason as setAccessTokenProvider in
  // AuthGate: an effect would race the first query's 401 renewal path.
  setSessionRenewer(() =>
    auth
      .signinSilent()
      .then((user) => user !== null)
      .catch(() => false),
  );
  // Registered during render on purpose, for the reason given above: moving
  // this into an effect races the first query's 401 renewal path.
  // eslint-disable-next-line react-hooks/refs -- see above
  setOnSessionExpired(beginReauthentication);

  useEffect(() => {
    const removeRenewError = auth.events.addSilentRenewError(beginReauthentication);
    const removeExpired = auth.events.addAccessTokenExpired(beginReauthentication);
    return () => {
      removeRenewError();
      removeExpired();
    };
  }, [auth.events, beginReauthentication]);

  useEffect(() => {
    if (auth.isAuthenticated) {
      // Both records of "this screen has a session": one that outlives the
      // process, and one that distinguishes a restart from a mid-run expiry.
      // Set here rather than during render — a ref written while rendering is
      // exactly the pattern the lint rule forbids, and it caught this.
      authenticatedThisLife.current = true;
      window.localStorage.setItem(WAS_AUTHENTICATED_STORAGE_KEY, 'true');
    }
  }, [auth.isAuthenticated]);

  // **Spend the stored grant before asking a person** (spec 049).
  //
  // A kiosk that restarts holds a grant on disk whose access token has usually
  // expired, and the OIDC library will not renew it by itself: its silent-renew
  // service listens for a token *about to* expire, and a token that loads
  // already expired cancels that timer instead of raising it. So nothing tried
  // the refresh token, the expired event fired, and the screen went straight to
  // an interactive redirect — which after a restart has no sign-in cookie to
  // ride on, and lands on the login form.
  //
  // Attempted once, and only for a screen that has signed in before, so a
  // first-boot kiosk still asks for credentials rather than silently failing.
  const silentAttempted = useRef(false);
  // Whether this page has held a session since it loaded. It distinguishes a
  // *restart* — signed in before, never in this page's life — from a session
  // expiring mid-run, which already had a path and keeps it (spec 011 FR-013).
  const authenticatedThisLife = useRef(false);

  useEffect(() => {
    if (auth.isAuthenticated || auth.isLoading || auth.activeNavigator !== undefined) return;
    if (!hasBeenAuthenticated() || silentAttempted.current || authenticatedThisLife.current) return;

    silentAttempted.current = true;
    logResilienceEvent('session', 'restart→silent');
    void auth
      .signinSilent()
      .then((user) => {
        if (user === null) {
          beginReauthentication();
        }
      })
      .catch(() => {
        // The grant is spent or the session behind it has gone. A person is
        // genuinely needed; falling through says so rather than retrying a
        // credential that will not work.
        beginReauthentication();
      });
  }, [auth, beginReauthentication]);

  // The interactive fallback, once the stored grant has been tried and failed.
  useEffect(() => {
    if (
      !auth.isAuthenticated &&
      !auth.isLoading &&
      auth.activeNavigator === undefined &&
      hasBeenAuthenticated() &&
      (silentAttempted.current || authenticatedThisLife.current)
    ) {
      // Reacts to an external system settling — the OIDC library finishing its
      // load and reporting no session — which is what effects are for. There is
      // nothing to derive during render: the trigger is the transition into
      // that state, not the state itself.
      beginReauthentication();
    }
  }, [auth.isAuthenticated, auth.isLoading, auth.activeNavigator, beginReauthentication]);

  const retrySignIn = useCallback(() => {
    window.sessionStorage.removeItem(REDIRECT_GUARD_STORAGE_KEY);
    redirectStarted.current = false;
    void auth.signinRedirect({ state: { returnTo: window.location.pathname } });
  }, [auth]);

  return { sessionExpired, retrySignIn };
}
