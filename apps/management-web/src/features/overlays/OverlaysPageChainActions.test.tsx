import { listOverlaysMock, branchMock, chain, response, renderPage } from '../../test/overlaysPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { Overlay } from '@smart-sentinel-eye/shared/api/overlays.api';

/** Spec 037 — recovering an archived overlay. Same shape as LayoutsPage. */
describe('OverlaysPage — recovering an archived overlay', () => {
  // `text` is a convenience override for the (single) label's text — spec
  // 150 moved it off the revision and onto `labels[0]` (now `elements[0]`,
  // spec 300 #2349), and every call site below still wants to say it in one
  // word.
  function revision(overrides: Partial<Omit<Overlay['revisions'][number], 'elements'>> & { text?: string }) {
    const { text, ...rest } = overrides;
    return {
      revisionIdentifier: '33333333-3333-3333-3333-333333333333',
      revisionNumber: 1,
      state: 'Archived' as const,
      elements: [
        {
          kind: 'Text' as const,
          color: '#9F9F9FD9',
          text: text ?? 'Production Line 1',
          normalizedX: 0.1,
          normalizedY: 0.1,
          normalizedWidth: 0.3,
          normalizedHeight: 0.08,
          fontSizePx: 32,
        },
      ],
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '22222222-2222-2222-2222-222222222222',
      publishedAt: null,
      archivedAt: '2026-05-29T10:00:00Z',
      ...rest,
    };
  }

  function showing(chains: Overlay[]) {
    listOverlaysMock.mockReturnValue({
      data: response(chains),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  }

  beforeEach(() => {
    branchMock.mockClear();
  });

  /** Spec 037 FR-013. The row used to offer nothing at all here. */
  it('Offers the edit action on a chain whose every revision is archived', () => {
    showing([chain({ revisions: [revision({ revisionNumber: 1 })] })]);

    renderPage();

    expect(screen.getByRole('button', { name: /edit \(new draft\)/i })).toBeInTheDocument();
  });

  it('Branches a draft when the archived chain is edited', async () => {
    const user = userEvent.setup();
    showing([chain({ revisions: [revision({ revisionNumber: 1 })] })]);
    renderPage();

    await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));

    expect(branchMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      version: 0,
    });
  });

  /**
   * Spec 038 T022 — **rewritten, not deleted**, and the claim is stronger than
   * the one it replaces.
   *
   * <p>
   * Spec 037 asserted this shape offered <b>no</b> edit button. It now offers
   * one: the chain has a live revision under a discarded draft, and issue 1879
   * was filed because the row offered nothing at all while the overlay was on
   * kiosks. That comment was written expecting this change.
   * </p>
   *
   * <p>
   * What the old test existed to prevent was branching from the <b>abandoned
   * draft</b> instead of the published one. Unlike the layout twin, this page
   * opens no designer, so the branch source is the server's choice and the
   * request carries only the chain — which means the claim available here is
   * that the row acts on the chain rather than being blocked by its newest
   * revision. The layout twin carries the stronger payload assertion.
   * </p>
   */
  it('Offers the edit action on a published revision under a discarded draft', async () => {
    const user = userEvent.setup();
    showing([
      chain({
        revisions: [
          revision({ revisionNumber: 1, state: 'Published', publishedAt: '2026-05-28T10:00:00Z', archivedAt: null }),
          revision({ revisionNumber: 2, revisionIdentifier: 'aa' }),
        ],
      }),
    ]);
    renderPage();

    await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));

    expect(branchMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      version: 0,
    });
  });

  /**
   * Spec 152 T006 / US2, rewritten for spec 305 (#2350) T011/T007: a
   * successful branch now navigates to the branched revision's own edit URL
   * instead of opening a dialog — seeding that revision is `OverlayEditPage`'s
   * job (its own suite covers it), not observable from this page, which
   * carries no list-state seed across the navigation at all (spec 305's
   * declared scope decision).
   */
  it('Navigates to the branched revision after Edit (new draft) succeeds', async () => {
    const user = userEvent.setup();
    showing([chain({ revisions: [revision({ revisionNumber: 1 })] })]);
    const { router } = renderPage();

    await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));

    await waitFor(() =>
      expect(router.state.location.pathname).toBe('/overlays/11111111-1111-1111-1111-111111111111/revisions/2/edit'),
    );
  });

  /**
   * Spec 152 Decision 2 / FR-003, rewritten for spec 305 (#2350): which
   * revision `onEdit` branches FROM (live vs. a discarded, newer-numbered
   * draft) is no longer observable from this page either — the branch call
   * carries only `{ overlayIdentifier, version }` (plan.md "Navigation
   * wiring in OverlaysPage"), and seeding the destination page is
   * `OverlayEditPage`'s job. What remains this page's to prove is that the
   * branched revision's own URL is still reached regardless of which
   * baseline the server branched from.
   */
  it('Navigates to the branched revision even when a published revision sits under a discarded draft', async () => {
    const user = userEvent.setup();
    showing([
      chain({
        revisions: [
          revision({
            revisionNumber: 1,
            state: 'Published',
            publishedAt: '2026-05-28T10:00:00Z',
            archivedAt: null,
            text: 'Live label',
          }),
          revision({ revisionNumber: 2, revisionIdentifier: 'aa', text: 'Discarded label' }),
        ],
      }),
    ]);
    const { router } = renderPage();

    await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));

    await waitFor(() =>
      expect(router.state.location.pathname).toBe('/overlays/11111111-1111-1111-1111-111111111111/revisions/2/edit'),
    );
  });

  /**
   * FR-003: "On failure it opens nothing and the existing `mutationError`
   * banner surfaces the refusal, as now." Trivially true today too — the page
   * opens no dialog on branch success OR failure yet — so this assertion does
   * not distinguish the two states before T012/T013 land; it pins the
   * negative half of FR-003 so a future implementation cannot open the editor
   * on a refusal by accident.
   */
  it('Navigates nowhere when the branch is refused', async () => {
    const user = userEvent.setup();
    branchMock.mockResolvedValueOnce({ error: { status: 409, data: { title: 'OVERLAY_REVISION_STALE' } } });
    showing([chain({ revisions: [revision({ revisionNumber: 1 })] })]);
    const { router } = renderPage();

    await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));
    // Phase-6 review nit N3: a single microtask flush can't tell "never
    // navigated" from "navigated, but not yet" — `waitFor` on the call that
    // must have happened first makes a late (not actually absent)
    // navigation unable to slip past this check.
    await waitFor(() => expect(branchMock).toHaveBeenCalled());

    expect(router.state.location.pathname).toBe('/overlays');
  });
});

/**
 * Spec 038 T020 / SC-001 — **every reachable shape offers at least one action,
 * asserted shape by shape.** The twin of LayoutsPage's, and for the same reason:
 * an aggregate assertion repeats the method that produced the defect.
 */
describe('OverlaysPage — every chain shape offers something', () => {
  function rev(revisionNumber: number, state: 'Draft' | 'Published' | 'Archived') {
    return {
      revisionIdentifier: `r${revisionNumber}`,
      revisionNumber,
      state,
      elements: [
        {
          kind: 'Text' as const,
          color: '#9F9F9FD9',
          text: 'Production Line 1',
          normalizedX: 0.1,
          normalizedY: 0.1,
          normalizedWidth: 0.3,
          normalizedHeight: 0.08,
          fontSizePx: 32,
        },
      ],
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '22222222-2222-2222-2222-222222222222',
      publishedAt: state === 'Published' ? '2026-05-28T10:00:00Z' : null,
      archivedAt: state === 'Archived' ? '2026-05-29T10:00:00Z' : null,
    };
  }

  // Spec 152 FR-004: 'Edit draft' joins the exhaustive set. Adding it only here
  // (and not to a shape's expectation below) would make the new button
  // invisible to `.filter((label) => ACTION_LABELS.includes(label))` — the
  // quiet way to pass this table while covering nothing.
  const ACTION_LABELS = ['Publish', 'Discard draft', 'Edit draft', 'Edit (new draft)', 'Revert', 'Archive'];

  const shapes: ReadonlyArray<[string, ReturnType<typeof rev>[], string[]]> = [
    ['{D}', [rev(1, 'Draft')], ['Publish', 'Discard draft', 'Edit draft']],
    ['{P}', [rev(1, 'Published')], ['Edit (new draft)', 'Revert', 'Archive']],
    ['{A}', [rev(1, 'Archived')], ['Edit (new draft)']],
    [
      '{P,D}',
      [rev(1, 'Published'), rev(2, 'Draft')],
      ['Publish', 'Discard draft', 'Edit draft', 'Edit (new draft)', 'Revert', 'Archive'],
    ],
    // The shape issue 1879 filed: this row used to offer nothing at all.
    ['{P,A}', [rev(1, 'Published'), rev(2, 'Archived')], ['Edit (new draft)', 'Revert', 'Archive']],
    ['{A,D}', [rev(1, 'Archived'), rev(2, 'Draft')], ['Publish', 'Discard draft', 'Edit draft']],
    // Two open drafts: branch off a published revision, then revert it.
    ['{D,D}', [rev(1, 'Draft'), rev(2, 'Draft')], ['Publish', 'Discard draft', 'Edit draft']],
    [
      '{P,D,D}',
      [rev(1, 'Draft'), rev(2, 'Draft'), rev(3, 'Published')],
      ['Publish', 'Discard draft', 'Edit draft', 'Edit (new draft)', 'Revert', 'Archive'],
    ],
  ];

  for (const [name, revisions, expected] of shapes) {
    it(`${name} offers ${expected.join(', ')}`, () => {
      listOverlaysMock.mockReturnValue({
        data: response([chain({ revisions })]),
        isLoading: false,
        isFetching: false,
        error: undefined,
        refetch: vi.fn(),
      });
      renderPage();

      const offered = screen
        .getAllByRole('button')
        .map((button) => button.textContent ?? '')
        .filter((label) => ACTION_LABELS.includes(label));

      expect(offered.sort()).toEqual([...expected].sort());
    });
  }
});
