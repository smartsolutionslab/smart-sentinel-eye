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

export const hasBeenAuthenticated = (): boolean =>
  window.sessionStorage.getItem(WAS_AUTHENTICATED_STORAGE_KEY) !== null;

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
      window.sessionStorage.setItem(WAS_AUTHENTICATED_STORAGE_KEY, 'true');
    }
  }, [auth.isAuthenticated]);

  // A kiosk that was signed in and lost its session must re-authenticate on
  // its own instead of falling back to the manual sign-in screen (FR-013).
  useEffect(() => {
    if (!auth.isAuthenticated && !auth.isLoading && auth.activeNavigator === undefined && hasBeenAuthenticated()) {
      // Reacts to an external system settling — the OIDC library finishing its
      // load and reporting no session — which is what effects are for. There is
      // nothing to derive during render: the trigger is the transition into
      // that state, not the state itself.
      // eslint-disable-next-line react-hooks/set-state-in-effect -- see above
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
