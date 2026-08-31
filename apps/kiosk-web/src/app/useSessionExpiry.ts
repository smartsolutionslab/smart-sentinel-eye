import { useCallback, useEffect, useRef, useState } from 'react';
import type { AuthContextProps } from 'react-oidc-context';
import { setOnSessionExpired, setSessionRenewer } from '@smart-sentinel-eye/shared/api/gateway';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { classifyIdentityFailure, type IdentityFailureVerdict } from './identityFailure.js';
import { delayForAttemptMs } from './retrySchedule.js';

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

/**
 * Enough of a cause to diagnose it later, and no more.
 *
 * <p>
 * Goes to the resilience log, never to the wall (FR-009 and FR-010). The
 * provider's own wording is what a screen shows today — "Failed to fetch" — and
 * it means nothing to the person reading it.
 * </p>
 */
const describeCause = (cause: unknown): string => {
  if (typeof cause !== 'object' || cause === null) return String(cause);

  const code = (cause as { error?: unknown }).error;
  if (typeof code === 'string' && code.length > 0) return code;

  return (cause as Error).name || 'unknown';
};

export interface SessionExpiryResult {
  /** Interactive credentials are genuinely required (data-model §3 expired-final). */
  sessionExpired: boolean;
  /**
   * Why renewal failed, and therefore what the screen should do (spec 051).
   * `undefined` while nothing has failed.
   */
  identityFailure: Exclude<IdentityFailureVerdict, 'interactive'> | undefined;
  /** Which attempt the retry loop is on, for a screen that wants to say so. */
  attempt: number;
  /** Manual retry from the session-expired screen: clears the loop guard first. */
  retrySignIn: () => void;
  /**
   * Try again now from the reconnecting screen.
   *
   * <p>
   * Resets the schedule rather than starting a second one. The retry lives in a
   * single effect keyed on the attempt number, so moving that number cancels the
   * pending timer and schedules one replacement — there is no arrangement here
   * that can leave two loops running (FR-013).
   * </p>
   */
  retryNow: () => void;
}

/**
 * Kiosk sign-in session machine (spec 011 data-model §3): silent renewal via
 * the gateway-registered renewer, automatic re-sign-in with deep-link
 * restoration on expiry, and a loop-guarded full-screen expired state when
 * the provider requires interaction (FR-011…014).
 */
export function useSessionExpiry(auth: AuthContextProps): SessionExpiryResult {
  const [sessionExpired, setSessionExpired] = useState(false);
  const [identityFailure, setIdentityFailure] = useState<Exclude<IdentityFailureVerdict, 'interactive'> | undefined>(
    undefined,
  );
  // Where the retry loop has got to. Zero means "try now" — see retryNow.
  const [attempt, setAttempt] = useState(0);
  const redirectStarted = useRef(false);

  /**
   * Decide what a failed renewal means, and act on it.
   *
   * <p>
   * <b>One verdict from two disjoint sources</b> (spec 051 FR-012). Where a
   * cause exists it decides, because it says exactly what the provider did.
   * Where there is none — a redirect that completed and still landed
   * unauthenticated — only the loop guard can speak, and what it says is that a
   * person is needed. They never both decide the same failure, so they cannot
   * disagree.
   * </p>
   */
  const beginReauthentication = useCallback(
    (cause?: unknown) => {
      if (redirectStarted.current) {
        return;
      }

      if (cause !== undefined) {
        const verdict = classifyIdentityFailure(cause);
        logResilienceEvent('session', 'renewal→' + verdict, { cause: describeCause(cause) });
        setIdentityFailure(verdict);
        // **A refused screen is never redirected.** Sending it to the provider
        // is what puts a username and password prompt on a factory wall for
        // anyone walking past, which is what happens today (FR-007).
        if (verdict === 'recoverable') {
          setAttempt(1);
        }
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
    },
    [auth],
  );

  // Registered during render for the same reason as setAccessTokenProvider in
  // AuthGate: an effect would race the first query's 401 renewal path.
  // **The renewer still resolves to the same boolean** — the gateway's 401 path
  // depends on it and cannot see any of the screens this feature adds — but the
  // rejection is classified on the way past instead of being discarded. That one
  // thrown-away value is what made every identity failure look alike.
  //
  // Registered during render for the reason given above, and the callback now
  // closes over `beginReauthentication` — which reads the redirect guard's ref.
  // The ref is only ever read when the renewer actually runs, which is during a
  // request, never during a render.
  // eslint-disable-next-line react-hooks/refs -- registered during render on purpose; see above
  setSessionRenewer(() =>
    auth
      .signinSilent()
      .then((user) => user !== null)
      .catch((cause: unknown) => {
        beginReauthentication(cause);
        return false;
      }),
  );
  // Registered during render on purpose, for the reason given above: moving
  // this into an effect races the first query's 401 renewal path.
  // eslint-disable-next-line react-hooks/refs -- see above
  setOnSessionExpired(beginReauthentication);

  useEffect(() => {
    // The renew-error event hands over the error; the expiry event has none to
    // give. That asymmetry is precisely the two sources described above, and it
    // is why the second passes nothing rather than passing something empty.
    const removeRenewError = auth.events.addSilentRenewError((cause: unknown) => beginReauthentication(cause));
    const removeExpired = auth.events.addAccessTokenExpired(() => beginReauthentication());
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
      .catch((cause: unknown) => {
        // **The cause is passed on rather than assumed.** A spent grant and an
        // absent provider both arrive here, and only one of them needs a person.
        // Treating them alike is how a restart during an outage ended up at a
        // login form on an unattended wall.
        beginReauthentication(cause);
      });
  }, [auth, beginReauthentication]);

  // The interactive fallback, once the stored grant has been tried and failed.
  useEffect(() => {
    if (
      !auth.isAuthenticated &&
      !auth.isLoading &&
      auth.activeNavigator === undefined &&
      hasBeenAuthenticated() &&
      // **Only the mid-run case.** A restart is handled entirely by the silent
      // attempt above, which asks for a person itself if the grant is spent.
      //
      // Gating this on "a silent attempt has started" instead raced it: both
      // effects run in the same commit, so this one saw the attempt flagged and
      // the session still absent, and redirected before the exchange could
      // resolve. The refresh was succeeding with a 200 while the screen was
      // already on its way to a login form — which looked exactly like the
      // refresh failing, and cost two wrong diagnoses before instrumentation
      // showed the token call succeeding.
      authenticatedThisLife.current
    ) {
      // Reacts to an external system settling — the OIDC library finishing its
      // load and reporting no session — which is what effects are for. There is
      // nothing to derive during render: the trigger is the transition into
      // that state, not the state itself.
      beginReauthentication();
    }
  }, [auth.isAuthenticated, auth.isLoading, auth.activeNavigator, beginReauthentication]);

  /**
   * **The wall coming back by itself** (spec 051 US1).
   *
   * <p>
   * One effect, keyed on the attempt number. Every failure moves that number,
   * which cancels the pending timer and schedules its replacement — so the loop
   * cannot fork, and a manual retry is simply a move to zero. That is the whole
   * of FR-013's "must not leave two loops running": there is no arrangement of
   * this code that starts a second one.
   * </p>
   */
  useEffect(() => {
    if (identityFailure !== 'recoverable') return undefined;

    const timer = window.setTimeout(
      () => {
        void auth
          .signinSilent()
          .then((user) => {
            if (user === null) {
              setAttempt((previous) => previous + 1);
              return;
            }
            logResilienceEvent('session', 'renewal→recovered', { attempt });
            setIdentityFailure(undefined);
            setAttempt(0);
          })
          .catch((cause: unknown) => {
            // A provider that came back only to refuse this screen stops the
            // loop rather than continuing it: retrying cannot help, and a screen
            // that keeps saying "reconnecting" would be lying to whoever reads it.
            if (classifyIdentityFailure(cause) === 'refused') {
              logResilienceEvent('session', 'renewal→refused', { cause: describeCause(cause) });
              setIdentityFailure('refused');
              return;
            }
            setAttempt((previous) => previous + 1);
          });
      },
      attempt === 0 ? 0 : delayForAttemptMs(attempt),
    );

    return () => window.clearTimeout(timer);
  }, [identityFailure, attempt, auth]);

  const retryNow = useCallback(() => setAttempt(0), []);

  const retrySignIn = useCallback(() => {
    window.sessionStorage.removeItem(REDIRECT_GUARD_STORAGE_KEY);
    redirectStarted.current = false;
    void auth.signinRedirect({ state: { returnTo: window.location.pathname } });
  }, [auth]);

  return { sessionExpired, identityFailure, attempt, retrySignIn, retryNow };
}
