import { useEffect, useLayoutEffect, useRef } from 'react';
import { useAuth } from 'react-oidc-context';
import type { User } from 'oidc-client-ts';
import { useDispatch } from 'react-redux';
import { setAccessTokenProvider } from '@smart-sentinel-eye/shared/api/gateway';
import { resetApiCaches, type AppDispatch } from './store.js';
import { createSubjectWatcher } from './subjectWatcher.js';

/**
 * Spec 303 (#2524): resets every RTK Query cache when the authenticated OIDC
 * subject changes, so a new operator never sees a previous operator's cached
 * records (FR-001). Placed at the auth layer (`AuthGate`), not per page,
 * because a subject change is a property of the session, not of any one
 * page (decision on #2524, spec.md §1).
 *
 * Detection happens in the `userLoaded` event handler, not in a render
 * effect: oidc-client-ts's `signinSilent` raises `userLoaded` synchronously
 * before its promise resolves, so the reset lands before `gateway.ts` sends
 * its 401 retry (FR-002, plan.md §1).
 *
 * `AuthProvider` loads any already-signed-in user on mount via
 * `userManager.getUser()`, which oidc-client-ts calls with
 * `raiseEvent = false` — so the very first subject of a page load never
 * raises `userLoaded`. The seed effect below observes `auth.user?.profile.sub`
 * directly so that subject is still recorded. It runs as a layout effect, not
 * a passive one: `AuthProvider`'s own bootstrap dispatch lands outside any
 * `act()`-wrapped update in some hosts, which can defer a passive effect's
 * flush behind a later, event-driven one and make the seed observe a
 * now-stale bootstrap value AFTER a real event already advanced the watcher.
 * A layout effect is flushed synchronously with the commit it belongs to, so
 * it cannot be reordered behind a later render's work this way.
 *
 * `receivedEventRef` additionally guards the seed against ever re-processing
 * a bootstrap value once a real `userLoaded` event has fired: idempotent for
 * an unchanged subject, but without the guard a late bootstrap commit could
 * still read as a change from whatever the event already observed.
 *
 * Phase-6 review (security): the event handler also re-registers the
 * gateway's access-token provider with the event's own `user`, before
 * dispatching the resets. `AuthGate` (`App.tsx`) registers the provider from
 * `auth.user` during render too, but that render is scheduled by
 * `react-oidc-context`'s own `useReducer` dispatch — a later, separate update
 * from this handler's synchronous `userLoaded` callback. Any RTK Query hook
 * still subscribed when `resetApiCaches` clears its cache resubscribes
 * immediately (RTK 2.12's hooks middleware), and that resubscribe fetch would
 * otherwise read the stale provider closure and carry the PREVIOUS subject's
 * bearer — reopening the disclosure this hook exists to close. Registering
 * here first means every request from this point on, including that
 * resubscribe, already carries the new subject's token.
 */
export function useResetApiCachesOnSubjectChange(): void {
  const auth = useAuth();
  const dispatch = useDispatch<AppDispatch>();

  const observeRef = useRef<((subject: string | undefined) => void) | undefined>(undefined);
  if (observeRef.current === undefined) {
    observeRef.current = createSubjectWatcher(() => resetApiCaches(dispatch));
  }

  const receivedEventRef = useRef(false);

  useEffect(() => {
    const onUserLoaded = (user: User) => {
      setAccessTokenProvider(() => user.access_token);
      receivedEventRef.current = true;
      observeRef.current?.(user.profile.sub);
    };
    auth.events.addUserLoaded(onUserLoaded);
    return () => auth.events.removeUserLoaded(onUserLoaded);
  }, [auth.events]);

  useLayoutEffect(() => {
    if (receivedEventRef.current) {
      return;
    }
    observeRef.current?.(auth.user?.profile.sub);
  }, [auth.user?.profile.sub]);
}
