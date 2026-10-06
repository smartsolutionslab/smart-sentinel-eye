import { useGetOverlayQuery, type OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import { skipToken } from '@reduxjs/toolkit/query/react';
import { RetryBanner } from '@smart-sentinel-eye/shared/ui/composites/RetryBanner';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { OverlayDraftForm, type OverlayEditTarget } from './OverlayDraftForm.js';
import { useCanvasFit } from './useCanvasFit.js';

/** Spec 305 (#2350) plan.md "Edit page — seeding invariants", step 1. */
const REVISION_NUMBER_PATTERN = /^[1-9]\d*$/;

/**
 * Shown optimistically while the chain's first read is still in flight (no
 * seed captured yet) — a single Text element, not an empty set. Keeping
 * `hasTextElement` true from the very first render avoids a false→true
 * transition in `OverlayDraftForm`'s own `textIndex` once the real seed
 * lands: that memo recomputes synchronously from the new `editTarget` the
 * instant the prop changes, one render before the form's own
 * `reset(defaultValues)` effect (which runs after commit) has caught the
 * live `elements` field array up to it — an empty placeholder would read
 * `elements[0]` as `undefined` for that one render and crash `OverlayEditor`
 * on `value.text`.
 */
const PLACEHOLDER_ELEMENTS: OverlayElement[] = [
  {
    kind: 'Text',
    color: '#FFFFFFD9',
    text: '',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    fontSizePx: 32,
  },
];

/**
 * Spec 305 (#2350) US1 — `/overlays/:overlayIdentifier/revisions/:revisionNumber/edit`.
 *
 * Replaces `OverlaysPage`'s edit dialog (ADR-0146's "fit a dialog" finding):
 * the editing controls move to their own page so the canvas is no longer
 * capped at the dialog's 448px width. The editing behaviour itself —
 * `OverlayDraftForm` — is unchanged; this page's own job is purely: parse
 * the URL, read the chain, decide which of the four notice states applies
 * (not-found, not-a-draft, a failed read, or the editor), and seed the form
 * exactly once from the first successful read (FR-003).
 */
export function OverlayEditPage() {
  const navigate = useNavigate();
  const { overlayIdentifier = '', revisionNumber: revisionNumberParam } = useParams();

  // FR-007/plan.md step 1: a revision segment that is not a positive
  // integer is treated as not-found, and — the literal instruction — the
  // query argument is `skipToken` itself, not merely "don't use the
  // result", so no GET is ever sent for it.
  const revisionNumber =
    revisionNumberParam !== undefined && REVISION_NUMBER_PATTERN.test(revisionNumberParam)
      ? Number(revisionNumberParam)
      : undefined;

  // plan.md step 2: `refetchOnMountOrArgChange` so a reopen within the 60s
  // cache window still re-reads (FR-011's "re-read from the server" would
  // otherwise only sometimes be true); `currentData`, never `data`, for the
  // same reason `OverlayDraftForm`'s own identical call states at length —
  // `data` survives a `skipToken` step and an argument change, which is
  // exactly how a navigation from one overlay to another would let this
  // page see the PREVIOUS overlay's chain while the new one's GET is still
  // in flight (re-pinned on the route by `OverlayEditPageNavigation.test.tsx`).
  //
  // This page's own call and `OverlayDraftForm`'s identical call below share
  // one RTK Query cache entry for the same `overlayIdentifier` — one
  // request, not two (plan.md).
  const {
    currentData: chain,
    isError: chainFailed,
    error: chainError,
    refetch: refetchChain,
  } = useGetOverlayQuery(revisionNumber === undefined ? skipToken : overlayIdentifier, {
    refetchOnMountOrArgChange: true,
  });

  const revision = chain?.revisions.find((candidate) => candidate.revisionNumber === revisionNumber);

  // plan.md step 4, "Seed once": captured from the first `currentData` that
  // resolves to a Draft revision, and never overwritten again while this
  // key is current — a later read (Retry, a conflict's own refetch) only
  // ever updates `currentChain`/the If-Match version inside
  // `OverlayDraftForm` itself, never this seed, which is what keeps an
  // in-progress edit from being wiped by a background refetch.
  //
  // `useState` + a synchronous reset during render — React's own documented
  // "adjusting state when a prop changes" pattern — not a ref: this project's
  // `react-hooks/refs` lint rule forbids reading a ref during render, and
  // more importantly a ref write here would still let ONE stale render
  // through before an effect-deferred reset took effect, which is exactly
  // the leak `OverlayEditPageNavigation.test.tsx` (B must never show A's
  // content) exists to catch. Calling `setState` mid-render instead bails
  // out and re-renders immediately, before anything commits.
  const seedKey = `${overlayIdentifier}/${revisionNumber}`;
  const [seed, setSeed] = useState<{ key: string; editTarget: OverlayEditTarget } | null>(null);
  let currentSeed = seed;
  if (currentSeed !== null && currentSeed.key !== seedKey) {
    currentSeed = null;
    setSeed(null);
  }
  if (currentSeed === null && revisionNumber !== undefined && chain !== undefined && revision?.state === 'Draft') {
    currentSeed = {
      key: seedKey,
      editTarget: { overlayIdentifier, revisionNumber, name: chain.name, elements: revision.elements },
    };
    setSeed(currentSeed);
  }

  // FR-006. Measured on the page's own content column; `OverlayEditor` keeps
  // its 800x450 default whenever this is `undefined` (no `ResizeObserver`,
  // or no layout yet).
  const { fit, ref: containerRef } = useCanvasFit();

  if (revisionNumber === undefined) {
    return <NotFoundNotice />;
  }

  // Phase-6 blockers 1+2 (#2350): both of these are only decided "while
  // nothing is seeded yet". In RTK Query 2.12, a rejected refetch keeps
  // `currentData` but sets `isError` — so once `OverlayDraftForm` is holding
  // an in-progress edit, a LATER bad read (the operator's own Retry, or a
  // conflict's own tag-invalidation refetch — `editDraftOverlayRevision`
  // invalidates `Overlay` on error too) must not swap the whole form out for
  // one of these page-level notices, discarding whatever is typed.
  // `OverlayDraftForm`'s own `ChainRecoveryNotice` (a failed read) and its
  // own `OVERLAY_REVISION_NOT_DRAFT` handling (a conflict, plan.md step 5)
  // are what cover both cases once seeded — this page defers to them
  // entirely rather than only partially.
  if (currentSeed === null) {
    if (chainFailed) {
      if (isNotFound(chainError)) {
        return <NotFoundNotice />;
      }
      return (
        <section ref={containerRef} className="p-6">
          <PageHeader revisionNumber={revisionNumber} name={chain?.name} />
          <RetryBanner message="Could not load this overlay." onRetry={() => void refetchChain()} />
        </section>
      );
    }

    if (chain !== undefined) {
      if (revision === undefined) {
        return <NotFoundNotice />;
      }
      if (revision.state !== 'Draft') {
        return <NotDraftNotice />;
      }
    }
  }

  // Phase-6 should-fix 4 (#2350): nothing has seeded yet (the chain's first
  // read is still in flight, and none of the notices above fired), but the
  // form still mounts now — unmounting it the instant the real seed lands a
  // moment later is exactly how `OverlayEditPageNavigation.test.tsx`'s own
  // A->B case is driven (one `OverlayDraftForm` instance, never a fresh
  // mount for the same key), and Save's own gate (`currentChain ===
  // undefined` inside `OverlayDraftForm`) already keeps Save unavailable for
  // this whole window. What must not happen is the operator typing into, or
  // dragging, the PLACEHOLDER content below and having it silently
  // overwritten the instant the real seed arrives — `readOnly` keeps the
  // editor visible and Save's disablement observable, while refusing that
  // input until there is something real to edit.
  if (currentSeed === null) {
    return (
      <section ref={containerRef} className="p-6">
        <PageHeader revisionNumber={revisionNumber} name={chain?.name} />
        <OverlayDraftForm
          key={seedKey}
          editTarget={{ overlayIdentifier, revisionNumber, name: '', elements: PLACEHOLDER_ELEMENTS }}
          readOnly
          onDone={() => navigate('/overlays')}
          onCancel={() => navigate('/overlays')}
          canvasWidthPx={fit?.widthPx}
          canvasHeightPx={fit?.heightPx}
        />
      </section>
    );
  }

  return (
    <section ref={containerRef} className="p-6">
      <PageHeader revisionNumber={revisionNumber} name={chain?.name ?? currentSeed.editTarget.name} />
      <OverlayDraftForm
        key={seedKey}
        editTarget={currentSeed.editTarget}
        onDone={() => navigate('/overlays')}
        onCancel={() => navigate('/overlays')}
        canvasWidthPx={fit?.widthPx}
        canvasHeightPx={fit?.heightPx}
      />
    </section>
  );
}

function isNotFound(error: unknown): boolean {
  return typeof error === 'object' && error !== null && 'status' in error && (error as { status: unknown }).status === 404;
}

function PageHeader({ revisionNumber, name }: { revisionNumber: number; name: string | undefined }) {
  return (
    <header className="mb-6 flex items-center justify-between">
      <div>
        <h1 className="text-2xl font-semibold">Edit overlay draft</h1>
        <p className="text-sm text-fg-muted">
          Editing draft v{revisionNumber} of {name ?? '…'}. The change is saved onto this draft.
        </p>
      </div>
      <Link to="/overlays" className="text-sm text-fg-muted hover:text-fg-primary">
        Back to overlays
      </Link>
    </header>
  );
}

function NotFoundNotice() {
  return (
    <section className="p-6">
      <p className="text-sm text-fg-muted">
        This overlay does not exist. <Link to="/overlays">Back to overlays</Link>
      </p>
    </section>
  );
}

function NotDraftNotice() {
  return (
    <section className="p-6">
      <p className="text-sm text-fg-muted">
        This revision is no longer a draft. <Link to="/overlays">Back to overlays</Link>
      </p>
    </section>
  );
}
