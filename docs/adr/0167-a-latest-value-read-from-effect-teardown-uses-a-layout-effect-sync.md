# ADR-0167: A latest-value read from effect teardown syncs via `useLayoutEffect`

**Status:** Accepted
**Date:** 2026-10-07
**Supersedes:** —
**Superseded by:** —

**Relates to:** issue #2544, which this ADR resolves; spec 222 (#2302),
whose `[T007]` filed it; spec 142 FR-004 and ADR-0143 (the session-release
`DELETE` gets one attempt); #2157 (the wider `useWhepSession` restructuring,
**not** decided here).

**Drafting note.** Scoped narrowly, per the product owner's choice on #2544:
"fix the effect ordering", not "accept as correct." It decides which token a
WHEP session release presents, and the hook mechanism that guarantees it. It
does not restructure `useWhepSession`'s state machine (#2157). It does not
close the `apps/shared` connect-path test gap #2544 describes as "a second
slice."

## Context

`useWhepSession` (`apps/shared/src/ui/composites/useWhepSession.ts`) keeps its
caller's `getToken` behind a ref, synced by a dependency-less `useEffect`
(:135-137). Callers pass a fresh inline closure every render. Naming it as a
dependency would renegotiate the peer connection on every render.

The session effect's cleanup calls `WhepClient.close()`, which calls
`releaseSession()` (`WhepClient.ts:237`). That **synchronously** calls
`getToken()` and sends a fire-and-forget `DELETE`. React runs every passive
cleanup in a commit before any passive setup. So when a session change
(`whepUrl`) and a new `getToken` land in **one** commit, the cleanup reads the
ref before the sync effect has updated it:

    PROBE B tokenSeenAtClose = ["token-a"]   (same commit: previous token)
    Probe C at unmount, token changed earlier: ["token-b"]   (fresh)

Spec 142 FR-004 already states the intent the hook defeats: `getToken()` "is
re-resolved … at release time" so the release presents a live credential. The
`DELETE` is not retried (ADR-0143). A 401 from a superseded token therefore
leaks the MediaMTX session until ICE reclaims it, at about 30 s.

Three ways to keep the ref current were examined against what this repo
enforces:

- **Assigning the ref during render** is refused by `react-hooks/refs`
  (on via `eslint-plugin-react-hooks` 7.1.1 `recommended`). It would need a new
  suppression — the autonomous lane may not add one (ADR-0144). It also
  writes values from renders that never commit (an interrupted transition,
  Suspense, StrictMode's double render).
- **`useEffectEvent`** updates its implementation in the commit's mutation
  phase, before any passive cleanup, and only for committed renders. The
  codebase already uses it in three dialogs. It needs no suppression, but it
  is a newer, less familiar primitive, and this decision prefers a mechanism
  whose timing guarantee is `useLayoutEffect`'s well-established one.
- **`useLayoutEffect`** for the sync is ordered correctly because layout
  setups run before passive cleanups, in every commit, unconditionally. It
  needs no new primitive and no suppression. It adds a small amount of
  synchronous work before paint — a ref assignment, not meaningful work.

## Decision

1. **A WHEP session release presents the freshest committed credential**,
   not the one that authorized the session. This confirms spec 142 FR-004's
   existing intent.
2. **In `useWhepSession`, the `getToken` ref is synced via `useLayoutEffect`**,
   replacing the passive `useEffect` sync. The effect itself is otherwise
   unchanged — same dependency-less body, same `getTokenRef.current = getToken`
   assignment — only the hook it runs in changes:
   ```ts
   useLayoutEffect(() => {
     getTokenRef.current = getToken;
   });
   ```
   Layout effects run synchronously after the DOM is updated but before the
   browser paints, and — critically — before any passive effect's cleanup in
   the same commit. This closes exactly the gap Probe B demonstrates: the
   session effect's passive cleanup now always sees the ref already updated
   for the current commit.
3. **The rule beyond this hook.** A "latest value" that is read *from an
   effect cleanup*, or from code a cleanup invokes synchronously, is synced
   with `useLayoutEffect`, not a plain `useEffect`. A ref synced by a passive
   `useEffect` is only safe when nothing reads it during another effect's
   teardown in the same commit.

## Consequences

- #2544 is resolved. The fix is a phase-4 change to one file (swap
  `useEffect` → `useLayoutEffect` for the ref-sync), with a red-first test in
  `apps/shared`: same commit, new `whepUrl` plus new `getToken`, and the
  release `DELETE` must carry `Bearer <new token>`. That is Probe B, inverted.
  Behaviour-changing, so it must be observed red.
- **Latency (§IV): N/A.** Session release happens off the event→overlay path.
  The session effect's dependencies and its connect behaviour do not change.
  The added layout-effect work is a single ref assignment, not a measurable
  cost.
- No new lint suppression; `react-hooks/refs` stays unweakened.
- **Existing ref-sync sites are not defects by this ADR.**
  `useWallAlignment` (`getToken`), `CameraViewer` (`onLagMeasured`),
  `useLabelDelay` and `useLayoutLifecycle` use the same ref-synced-by-effect
  shape. In the first three, the synced ref is read from intervals or timers,
  not from another effect's teardown in the same commit — point 3 doesn't
  apply to them. `useLayoutLifecycle`'s cleanup calls `hub.stop()`. Whether
  SignalR's stop re-reads `accessTokenFactory` on a fallback transport is
  unverified. If it does, that site falls under point 3 and gets its own
  issue. It is not decided here.
- Advisory, not enforced: no lint rule distinguishes "read in teardown" from
  "read in a timer." A reviewer applies point 3 by inspection.

## Alternatives Considered

- **Accept that a release presents the token that authorized the session.**
  This was the alternative #2544 itself raised. Rejected by the product owner
  on #2544. It also contradicts spec 142 FR-004's stated reason for resolving
  the token at release time.
- **Name `getToken` as a dependency of the session effect.** Rejected.
  Callers' inline closures would tear down and renegotiate the peer connection
  on every parent render (the same failure class as issue #1889, on a
  §IV-path hook). Even a memoized caller would pay a visible reconnect on
  every token refresh.
- **Assign the ref during render.** Rejected. It needs a new
  `react-hooks/refs` suppression (the autonomous lane may not add one,
  ADR-0144), and it writes values from renders that never commit.
- **`useEffectEvent`.** Viable, and closes the same gap by a different
  mechanism (committed in the mutation phase rather than the layout phase).
  Not chosen: this decision prefers `useLayoutEffect`'s longer-established,
  more widely understood timing guarantee for a fix on a constitution
  §IV-adjacent hook, over introducing a newer primitive's semantics as the
  load-bearing mechanism. Three existing call sites in the codebase already
  use `useEffectEvent` for a different shape (event handlers passed to
  non-React APIs) — this ADR doesn't relitigate those.
- **Fold #2544 into #2157's restructuring.** Rejected. #2157 needs its own
  spec and a latency measurement. This defect is a one-file fix with no
  §IV impact and should not wait for it.
