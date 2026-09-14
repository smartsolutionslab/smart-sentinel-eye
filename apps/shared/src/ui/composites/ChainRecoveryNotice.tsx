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
  const wasReReadingRef = useRef(reReading);

  // The focus/announcement move is latched by the operator's act, not by
  // `readFailed` going false on its own (FR-007's trap, plan.md §4): a
  // normal dialog open also goes `true -> false` on `readFailed` between
  // renders as its first read resolves, and a plain effect on that alone
  // would steal focus to Save on every open. Firing only on `reReading`'s
  // falling edge, and only when `origin` says a control latched it, avoids
  // that — an unrequested fetch (or none) leaves `origin` `null` and this
  // effect does nothing.
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
    if (!wasReReading || reReading || origin === null) return;

    const requestedBy = origin;
    setOrigin(null);
    if (readFailed) {
      // Refused again (FR-006): the control is still mounted and still
      // focused — there is nothing to restore. The status region is cleared;
      // the alert's own insertion is what announces the failure.
      // eslint-disable-next-line react-hooks/set-state-in-effect -- see above
      setAnnouncement((previous) => ({ text: '', token: previous.token + 1 }));
      return;
    }
    // Succeeded (FR-005/FR-008).
    setAnnouncement((previous) => ({
      text: `The ${noun} was read. Save is available.`,
      token: previous.token + 1,
    }));
    if (requestedBy === 'retry') {
      onReadRecovered();
    }
  }, [reReading, readFailed, origin, noun, onReadRecovered]);

  function activate(kind: RecoveryOrigin) {
    // FR-004: the handler refuses to act while a re-read it started is
    // already in flight — `aria-disabled` keeps the control clickable, so
    // this is what actually stops it.
    if (reReading) return;
    setOrigin(kind);
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
    setAnnouncement((previous) => ({ text: `Re-reading the ${noun}…`, token: previous.token + 1 }));
    onReRead();
  }

  const chainArmActive = readFailed || (reReading && origin === 'retry');

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

        BUG (blocker 1, phase-6 review): this `<p>` is left mounted across
        the WHOLE `chainArmActive` span — in-flight and failed alike, since
        both live on the same element — so only `role` toggles off and back
        on; the element itself is never re-inserted. A second-and-later
        failure therefore earns no fresh insertion-announcement — the exact
        unreliability #2346 recorded, and the reason FR-001 makes the status
        region always-mounted rather than toggled. FR-006 also clears the
        status region to '' on failure, so a repeat refusal currently
        announces nothing at all — the common case when a backend is down.
        Needs a genuine remount on each `readFailed` transition (e.g. a
        `key` bumped alongside `announcement.token`), not a reused element.
      */}
      {chainArmActive && (
        <p role={readFailed ? 'alert' : undefined} className="text-sm text-accent-fault">
          {readFailed && <>The {noun} could not be read. </>}
          <button type="button" className="underline" aria-disabled={reReading} onClick={() => activate('retry')}>
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
            // mounted and focused through its own re-read (FR-008).
            <button type="button" className="underline" aria-disabled={reReading} onClick={() => activate('reload')}>
              Reload
            </button>
          )}
        </p>
      )}
    </>
  );
}
