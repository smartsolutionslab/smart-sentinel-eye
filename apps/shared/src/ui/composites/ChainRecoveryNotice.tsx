import { useEffect, useRef, useState } from 'react';

export interface ChainRecoveryNoticeProps {
  /** The noun spliced into every message — `'overlay'` or `'layout'` (spec 156 FR-012). */
  noun: string;
  /** `chainFailed` — the chain read (`isError`) has been refused. */
  readFailed: boolean;
  /** `chainFetching` — the chain read (`isFetching`) is currently in flight, for any reason. */
  reReading: boolean;
  /** `refetchChain` — starts a re-read of the chain. */
  onReRead: () => void;
  /** The submit-refused banner text, or `null` when nothing has been refused. */
  backendError: string | null;
  /** Whether the submit-refused banner offers a Reload control. */
  offerReload: boolean;
  /** Called once, when a re-read the operator started with Retry succeeds. Never called for Reload (FR-008). */
  onReadRecovered: () => void;
}

/** Shared by `activate()`'s click-driven announcement and the unrequested-re-read
 *  rising edge (FR-009), so the two copies of this string cannot drift apart. */
function reReadingAnnouncement(noun: string) {
  return `Re-reading the ${noun}…`;
}

type RecoveryOrigin = 'retry' | 'reload';

/**
 * Spec 156 (issue #2372) — the recovery control that survives its own
 * activation.
 *
 * `chainFailed` is RTK Query's `isError`, and `refetch()` sets the query to
 * `pending` unconditionally, so `isError` goes false the instant Retry (or
 * Reload) is *clicked* — before the response exists. A control whose mount
 * condition is `readFailed` alone is destroyed at that instant, which is the
 * defect this component exists to fix: the control now stays mounted for the
 * whole in-flight window too, `aria-disabled` rather than natively `disabled`
 * (a native disable blurs the focused element — the same finding spec 154
 * made for Undo/Redo, `OverlayEditor.tsx:649-657`).
 *
 * `origin` is what keeps Retry's and Reload's in-flight windows from
 * colliding: both call the same `onReRead` (the same chain query), so
 * `reReading` alone cannot tell which control started the current fetch.
 * Reload's own arm is driven by `backendError`, which `onReRead` never
 * touches, so it needs no help from `reReading` to stay mounted — and must
 * not be *replaced* by the chain-read arm merely because the same query
 * happens to be fetching (FR-008). Only a Retry-originated fetch joins the
 * chain-read arm's mount condition.
 */
export function ChainRecoveryNotice({
  noun,
  readFailed,
  reReading,
  onReRead,
  backendError,
  offerReload,
  onReadRecovered,
}: ChainRecoveryNoticeProps) {
  const [origin, setOrigin] = useState<RecoveryOrigin | null>(null);
  const [announcement, setAnnouncement] = useState({ text: '', token: 0 });
  // Bumped only when Retry's own re-read is refused a *second* (or later)
  // time — the `key` that forces the alert `<p>` below to genuinely
  // remount rather than merely flip its `role` attribute (blocker 1,
  // phase-6 review). Left at 0 for the first failure (the initial mount,
  // and the click -> in-flight transition immediately after it): that
  // `<p>` must stay the SAME node across that one transition, because the
  // Retry button lives inside it and the discriminating test captures a
  // reference to it before the click and reuses that reference after.
  const [retryFailureKey, setRetryFailureKey] = useState(0);
  const retryButtonRef = useRef<HTMLButtonElement>(null);
  const wasReReadingRef = useRef(reReading);
  // Per-MOUNT discriminator for FR-010 (issue #2387, phase-6 finding 1):
  // `false` until a read SETTLES while this notice is mounted, then latched
  // `true` for the rest of this mount's life. The discriminator this
  // replaced — `currentChain !== undefined` at the call site — answers "does
  // RTK Query hold a value for this arg", which is also true on a WARM
  // reopen: Radix remounts `Dialog.Content` fresh (no `forceMount`) with the
  // previous mount's cached value already attached, `refetchOnMountOrArgChange`
  // forces a refetch regardless, and that discriminator could not tell the
  // resulting fetch apart from a genuine re-read — so it announced on a
  // plain dialog open within the cache's `keepUnusedDataFor` window. This ref
  // cannot make that mistake: nothing settles for THIS mount until its own
  // first fetch completes, warm cache or not.
  const hadPriorReadRef = useRef(false);
  // Skips the refocus effect's first run (the initial mount — nothing was
  // focused yet, and FR-007 forbids taking focus on a dialog that just
  // opened) so it only fires on an actual `retryFailureKey` bump.
  const isFirstFailureKeyRenderRef = useRef(true);
  // Per-FETCH discriminator (phase-6 finding 1, issue #2387): `origin` alone
  // cannot say "did a control start THIS fetch" once a refused Retry/Reload
  // has latched it, because `origin` is deliberately NOT cleared on refusal
  // (see the comment on that `setAnnouncement` below) — the alert and the
  // Retry arm's own mount condition (`chainArmActive`) both still need it
  // after the failure. Left latched, `origin === null` stops meaning
  // "unrequested" the moment it has ever been non-null: every later
  // unrequested re-read (the `invalidatesTags` refetch FR-009/FR-010 exist
  // for) would misread as the SAME click that already failed, announcing
  // nothing on the rising edge and calling `onReadRecovered()` / bumping
  // `retryFailureKey` on the falling edge — exactly the focus-yank FR-009
  // forbids.
  //
  // `activatedOriginRef` is written once, by `activate()`, for the fetch
  // about to start; the rising edge below consumes it into
  // `currentFetchOriginRef` (which lives for that one fetch's whole
  // in-flight window) and resets it to `null` — so a LATER, uncontrolled
  // fetch always finds it `null`, regardless of what `origin` (the
  // UI-arm-selection state) still remembers from a past click.
  const activatedOriginRef = useRef<RecoveryOrigin | null>(null);
  const currentFetchOriginRef = useRef<RecoveryOrigin | null>(null);

  // The focus move is latched by the operator's act, not by `readFailed`
  // going false on its own (FR-007's trap, plan.md §4): a normal dialog open
  // also goes `true -> false` on `readFailed` between renders as its first
  // read resolves, and a plain effect on that alone would steal focus to
  // Save on every open. Moving focus (and touching `retryFailureKey`) only
  // happens when `currentFetchOriginRef` says a control latched THIS fetch
  // — not `origin`, which stays latched across a refused fetch and so
  // cannot tell a later, uncontrolled fetch apart from the click that
  // already failed (finding 1 above).
  //
  // An unrequested fetch (`currentFetchOriginRef.current === null`) still
  // gets an ANNOUNCEMENT-only reaction (spec 160 FR-009) once THIS MOUNT has
  // seen its own read settle at least once (`hadPriorReadRef`, FR-010) — the
  // dominant trigger in production is the rejected mutation's own
  // `invalidatesTags`, which never touches `origin` at all, so without this
  // branch the disable that FR-001 keeps silently unavailable and silently
  // available again.
  //
  // The `setState` calls below are the point of the effect, not a lint
  // accident: this is React's own "subscribe to an external system"
  // case (https://react.dev/learn/you-might-not-need-an-effect) — the
  // external system is the chain query's fetch lifecycle, which this
  // component only receives as props, and there is no render-time
  // computation that substitutes for reacting to *how it got there*
  // (`useWhepSession.ts:159`, `RegisterCameraDialog.tsx:42` disable the
  // same rule for the same reason).
  useEffect(() => {
    const wasReReading = wasReReadingRef.current;
    wasReReadingRef.current = reReading;

    // FR-009 rising edge: an unrequested re-read (nothing in
    // `activatedOriginRef` — no click started THIS fetch) says so on the way
    // in — but only once THIS MOUNT has seen its own read settle before.
    // FR-010's trap: the dialog's own FIRST read also goes `false -> true`
    // right here, and `hadPriorReadRef` is what tells the two apart (it
    // cannot be true yet on a first read, whatever the RTK Query cache
    // already holds — see the ref's own comment).
    //
    // `activatedOriginRef` is consumed (read, then reset to `null`) here
    // rather than left latched — it describes only "did a control just
    // start THIS fetch", not the longer-lived `origin` state that still
    // drives which arm is mounted.
    if (!wasReReading && reReading) {
      currentFetchOriginRef.current = activatedOriginRef.current;
      activatedOriginRef.current = null;
      if (currentFetchOriginRef.current === null && hadPriorReadRef.current) {
        setAnnouncement((previous) => ({ text: reReadingAnnouncement(noun), token: previous.token + 1 }));
      }
      return;
    }

    if (!wasReReading || reReading) return;

    // This mount's read has just settled — success or failure, requested or
    // not, it makes no difference here. Latched unconditionally, once, so a
    // LATER re-read is recognised as genuine (FR-010); read below to tell
    // THIS settle apart from a later one.
    const isFirstSettleForThisMount = !hadPriorReadRef.current;
    hadPriorReadRef.current = true;

    // Consumed once per fetch, mirroring the rising edge above: this is
    // "did a control start THE FETCH THAT JUST SETTLED", not `origin`
    // (which stays latched after a refusal for reasons unrelated to this
    // settle).
    const thisFetchOrigin = currentFetchOriginRef.current;
    currentFetchOriginRef.current = null;

    if (thisFetchOrigin === null) {
      // Falling edge, unrequested. FR-009's announcement half only: never
      // `setOrigin` (already `null`), never `setRetryFailureKey` (that key
      // owns the Retry `<p>`'s remount and would move focus for a click
      // that never happened), never `onReadRecovered()` (focus is already
      // on Save per FR-001/FR-004; moving it would steal focus from an
      // operator who has moved on).
      if (isFirstSettleForThisMount) {
        // Say nothing: this transition is indistinguishable from a plain
        // dialog open, which FR-010 requires to stay silent — including on
        // a warm reopen, where the cache already held a value before this
        // settle ever ran.
        return;
      }
      if (readFailed) {
        // The chain arm's own `role="alert"` insertion is what announces
        // the refusal (the same rule FR-006 below already sets for the
        // Retry/Reload path) — this only clears the status region.
        // eslint-disable-next-line react-hooks/set-state-in-effect -- see above
        setAnnouncement((previous) => ({ text: '', token: previous.token + 1 }));
      } else {
        setAnnouncement((previous) => ({
          text: `The ${noun} was read. Save is available.`,
          token: previous.token + 1,
        }));
      }
      return;
    }

    const requestedBy = thisFetchOrigin;
    if (readFailed) {
      // Refused again (FR-006/blocker 1): the status region is cleared —
      // the alert's own (re-)insertion is what announces the failure now.
      //
      // `origin` is deliberately NOT cleared here (blocker 2, phase-6
      // review): this effect's own `setOrigin(null)` would otherwise land
      // in the very next render, and for a Reload-originated refusal that
      // render has `readFailed` genuinely true with nothing left to exclude
      // it from `chainArmActive` — reopening the exact hole this fix
      // closes, one render later. Leaving `origin` latched costs nothing
      // for Retry (its own exclusion never consults `origin`), and a fresh
      // click re-latches it to the same value regardless.
      setAnnouncement((previous) => ({ text: '', token: previous.token + 1 }));
      if (requestedBy === 'retry') {
        // Forces the alert `<p>` below to remount (blocker 1) so a
        // second-and-later refusal earns a fresh insertion-announcement
        // instead of a reused node whose `role` merely flips back on —
        // the exact unreliability #2346 recorded. Gated on `'retry'`:
        // Reload's own alert is a structurally separate, permanently
        // mounted element (`!chainArmActive && backendError !== null`
        // below) that this key never touches.
        setRetryFailureKey((previous) => previous + 1);
      }
      return;
    }
    // Succeeded (FR-005/FR-008). Safe to clear `origin` here: `chainArmActive`
    // no longer needs the exclusion once the arm that mattered has
    // resolved, and leaving `origin` stuck on a past success would wrongly
    // suppress a later, unrelated chain failure.
    setOrigin(null);
    setAnnouncement((previous) => ({
      text: `The ${noun} was read. Save is available.`,
      token: previous.token + 1,
    }));
    if (requestedBy === 'retry') {
      onReadRecovered();
    }
    // `origin` (the state) is deliberately absent here: the effect now reads
    // only `activatedOriginRef`/`currentFetchOriginRef` for the per-fetch
    // discriminator (finding 1 above) and `setOrigin`, neither of which
    // needs `origin`'s current value at effect time.
  }, [reReading, readFailed, noun, onReadRecovered]);

  // Companion to the remount above: a `key` change unmounts the old Retry
  // button along with its old `<p>`, and the browser does not carry focus
  // to whatever replaces a removed focused element — it falls to `<body>`.
  // This is what puts it back, mirroring FR-005's `onReadRecovered()` call
  // for the success path; this is the refusal path's equivalent.
  useEffect(() => {
    if (isFirstFailureKeyRenderRef.current) {
      isFirstFailureKeyRenderRef.current = false;
      return;
    }
    retryButtonRef.current?.focus();
  }, [retryFailureKey]);

  function activate(kind: RecoveryOrigin) {
    // FR-004: the handler refuses to act while a re-read it started is
    // already in flight — `aria-disabled` keeps the control clickable, so
    // this is what actually stops it.
    if (reReading) return;
    setOrigin(kind);
    // Latches the per-fetch discriminator (finding 1 above) for the fetch
    // `onReRead()` is about to start — the rising edge below consumes this
    // into `currentFetchOriginRef` and resets it, so it cannot leak into a
    // LATER, uncontrolled fetch the way `origin` (never cleared on refusal)
    // would.
    activatedOriginRef.current = kind;
    // `key`-token remount, mirroring `OverlayEditor.tsx:501-508`'s
    // `announceUndo` — but here it is defensive, not load-bearing. FR-006's
    // `text: ''` clear below always writes a distinct value between two
    // "Re-reading the {noun}…" announcements, so the same-value bail-out
    // (#2344) never gets a chance to fire on this message, key or not —
    // checked by counterfactual (phase-6 review): dropping the key left
    // `OverlayEditorDialogChainRecovery.test.tsx`'s FR-009 test green. Kept
    // anyway — one prop, cheap insurance against a future change that
    // removes that intervening clear, and it keeps this in step with the
    // sibling pattern, where the same trick *is* load-bearing (two
    // consecutive successful undos both write 'Undone' with nothing
    // between them).
    setAnnouncement((previous) => ({ text: reReadingAnnouncement(noun), token: previous.token + 1 }));
    onReRead();
  }

  // `origin !== 'reload'` (blocker 2, phase-6 review): without it, a
  // Reload-originated re-read that is itself refused still flips this true
  // on `readFailed` alone, handing the chain-read arm priority and
  // unmounting the `backendError`/Reload arm out from under the focused
  // button — the exact defect this component exists to fix, on a path
  // FR-008 never enumerated (it covers only Reload's success). Excluding
  // `'reload'` keeps that arm exclusively responsible for its own re-read,
  // success or failure alike.
  const chainArmActive = origin !== 'reload' && (readFailed || (reReading && origin === 'retry'));

  const controlClassName = 'underline aria-disabled:opacity-50 aria-disabled:cursor-progress';

  return (
    <>
      {/* FR-001. Always mounted for the life of the dialog (#2346: a region
          inserted with its content is not reliably announced), sr-only, and
          carrying a `data-testid` for the reason `OverlayEditor.tsx:631-638`
          gives — the accessible name/role alone is not enough to address it
          without depending on JSX order. */}
      <p role="status" data-testid={`${noun}-chain-recovery-status`} className="sr-only">
        <span key={announcement.token}>{announcement.text}</span>
      </p>
      {/*
        One alert at a time, never two siblings: the chain read failing (or
        being re-read after a Retry) takes priority over a standing
        `backendError`, because it blocks Save outright and a submit error
        still on screen is stale the moment the chain can no longer even be
        confirmed current. `role="alert"` only while `readFailed` is true —
        during the in-flight window there is nothing to interrupt a screen
        reader for.

        The Retry button lives INSIDE this `<p>` (not beside it): the
        discriminating test captures the button once, before the operator
        ever clicks it — while it is still nested inside `role="alert"` —
        and reuses that same reference through the in-flight window, so the
        very first `readFailed: true -> false` transition (click -> in
        flight) must reuse this exact node, not replace it.

        `key={retryFailureKey}` (blocker 1, phase-6 review): left at its
        initial value across that one required transition, then bumped —
        forcing a genuine unmount/remount of this whole `<p>`, button
        included — specifically when Retry's OWN re-read is refused a
        SECOND time. Without it this element is merely reconciled across
        every `readFailed` edge, so only its `role` attribute flips off and
        back on; a repeat refusal then earns no fresh insertion-announcement
        (the exact unreliability #2346 recorded), and with the status region
        cleared by FR-006 the operator hears nothing at all on a repeat
        failure — the common case when a backend is down. The remount does
        take the button's DOM focus with it (a browser does not carry focus
        to whatever replaces a removed focused element), so the effect above
        explicitly restores it — mirroring `onReadRecovered()` on the
        success path.
      */}
      {chainArmActive && (
        <p key={retryFailureKey} role={readFailed ? 'alert' : undefined} className="text-sm text-accent-fault">
          {readFailed && <>The {noun} could not be read. </>}
          <button
            ref={retryButtonRef}
            type="button"
            className={controlClassName}
            aria-disabled={reReading}
            onClick={() => activate('retry')}
          >
            Retry
          </button>
        </p>
      )}
      {!chainArmActive && backendError !== null && (
        <p role="alert" className="text-sm text-accent-fault">
          {backendError}{' '}
          {offerReload && (
            // Reload, never retry: refetching the chain replaces the version
            // the dialog would resubmit with the one actually stored. Its
            // own arm does not depend on `chainArmActive` — nothing clears
            // `backendError` but the dialog's close effect, so it stays
            // mounted and focused through its own re-read, success or
            // failure alike (FR-008, blocker 2).
            <button
              type="button"
              className={controlClassName}
              aria-disabled={reReading}
              onClick={() => activate('reload')}
            >
              Reload
            </button>
          )}
        </p>
      )}
    </>
  );
}
