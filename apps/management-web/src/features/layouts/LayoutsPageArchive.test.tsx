import {
  listLayoutsMock,
  archiveMock,
  branchMock,
  chain,
  response,
  renderPage,
} from '../../test/layoutsPageHarness.js';
import { describe, it, expect, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { Layout } from '@smart-sentinel-eye/shared/api/layouts.api';

/**
 * Spec 036 — the confirmations. Layouts carry the sharpest wording in the
 * feature, and the only conditional.
 */
describe('LayoutsPage — archive confirmation', () => {
  function publishedChain() {
    return chain({
      revisions: [
        {
          revisionIdentifier: '33333333-3333-3333-3333-333333333333',
          revisionNumber: 4,
          state: 'Published',
          gridRows: 1,
          gridCols: 1,
          tiles: [{ cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 }],
          createdAt: '2026-05-26T10:00:00Z',
          createdBy: '22222222-2222-2222-2222-222222222222',
          publishedAt: '2026-05-27T10:00:00Z',
          archivedAt: null,
        },
      ],
    });
  }

  /**
   * Spec 266 (issue #2335) US2 — new behaviour, RED. Archive moves behind the
   * row's "More actions" menu (Q4 default), so opening the confirmation is a
   * two-step trigger→menuitem interaction rather than one direct button
   * click. Red today: there is no "More actions" trigger yet, so `getByRole`
   * throws before the menu item is ever sought.
   */
  async function openConfirmation(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole('button', { name: /more actions/i }));
    await user.click(await screen.findByRole('menuitem', { name: /^archive$/i }));
    return screen.getByRole('alertdialog');
  }

  /**
   * T012 / FR-002, asserted as a **call count**. A confirmation that closes
   * cleanly and archives anyway passes any assertion about the dialog closing.
   */
  it('Archives nothing when the confirmation is dismissed', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({
      data: response([publishedChain()]),
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    renderPage();

    const confirmation = await openConfirmation(user);
    await user.click(within(confirmation).getByRole('button', { name: /cancel/i }));

    expect(archiveMock).not.toHaveBeenCalled();
  });

  it('Archives once confirmed, sending the revision it named', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({
      data: response([publishedChain()]),
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    renderPage();

    const confirmation = await openConfirmation(user);
    expect(archiveMock).not.toHaveBeenCalled();

    await user.click(within(confirmation).getByRole('button', { name: /^archive$/i }));

    expect(archiveMock).toHaveBeenCalledWith({
      layoutIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 4,
      version: 0,
    });
  });

  /** T013 / FR-003 — the name and the revision, and never the identifier. */
  it('Names the layout and revision, and shows no identifier', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({
      data: response([publishedChain()]),
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    renderPage();

    const confirmation = await openConfirmation(user);

    expect(confirmation).toHaveTextContent('Line-1');
    expect(confirmation).toHaveTextContent('4');
    expect(confirmation).not.toHaveTextContent('11111111-1111-1111-1111-111111111111');
    expect(confirmation).not.toHaveTextContent(/are you sure/i);
  });

  /**
   * Spec 037 T025 / FR-011, FR-012 — **rewritten**, not deleted.
   *
   * This was spec 036's T014, asserting the layout could never be edited or
   * published again. ADR-0121 made that false, so the claim changed and the test
   * changed with it. Deleting it instead would have removed the only check on
   * this wording at the exact moment the wording changed.
   *
   * It asserts the **absence** of both false sentences as well as the presence
   * of the true one. A test that merely stopped asserting the old sentence would
   * pass against a page that still said it — and "cannot be undone" is where
   * this wording lands under any hurried edit, false now in the other direction.
   */
  it('Says the layout can be brought back and keeps its tiles', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({
      data: response([publishedChain()]),
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    renderPage();

    const confirmation = await openConfirmation(user);

    expect(confirmation).toHaveTextContent(/bring it back/i);
    expect(confirmation).toHaveTextContent(/tiles are kept/i);
    expect(confirmation).toHaveTextContent(/out of service/i);
    expect(confirmation).not.toHaveTextContent(/never be edited or published again/i);
    expect(confirmation).not.toHaveTextContent(/cannot be undone/i);
  });

  /**
   * Spec 038 T024 — **rewritten**, and its premise replaced rather than its
   * wording.
   *
   * <p>
   * This was spec 036's T015: one dialog whose kiosk sentence was conditional on
   * the revision being published. Spec 038 removes the condition — Archive is
   * offered only when a live revision exists and targets it, so the sentence is
   * always true here. The case the condition existed for is now a *different*
   * dialog, asserted below.
   * </p>
   *
   * <p>
   * **No fixture varies a draft state.** A test that still varied one would keep
   * passing while the now-constant flag lingered in the page.
   * </p>
   */
  it('Always warns about kiosks, because archive only ever targets the live revision', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({
      data: response([publishedChain()]),
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
    renderPage();

    expect(await openConfirmation(user)).toHaveTextContent(/kiosks/i);
  });

  describe('LayoutsPage — recovering an archived layout', () => {
    function revision(overrides: Partial<Layout['revisions'][number]>) {
      return {
        revisionIdentifier: '33333333-3333-3333-3333-333333333333',
        revisionNumber: 1,
        state: 'Archived' as const,
        gridRows: 1,
        gridCols: 1,
        tiles: [{ cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 }],
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        publishedAt: null,
        archivedAt: '2026-05-28T10:00:00Z',
        ...overrides,
      };
    }

    /**
     * Spec 037 FR-013. The row used to offer nothing at all here.
     */
    it('Offers the edit action on a chain whose every revision is archived', () => {
      listLayoutsMock.mockReturnValue({
        data: response([chain({ revisions: [revision({ revisionNumber: 1 })] })]),
        isLoading: false,
        isFetching: false,
        refetch: vi.fn(),
      });

      renderPage();

      expect(screen.getByRole('button', { name: /edit \(new draft\)/i })).toBeInTheDocument();
    });

    it('Branches a draft when the archived chain is edited', async () => {
      const user = userEvent.setup();
      listLayoutsMock.mockReturnValue({
        data: response([chain({ revisions: [revision({ revisionNumber: 1 })] })]),
        isLoading: false,
        isFetching: false,
        refetch: vi.fn(),
      });
      renderPage();

      await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));

      expect(branchMock).toHaveBeenCalledWith({
        layoutIdentifier: '11111111-1111-1111-1111-111111111111',
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
     * was filed because the row offered nothing at all while the layout was on
     * kiosks. That comment was written expecting this change.
     * </p>
     *
     * <p>
     * What the old test existed to prevent was branching from the <b>abandoned
     * draft</b> instead of the published wall. Asserting the button's absence was
     * only a proxy for that while the button did not exist. The direct form is
     * available now: assert the editor opens with the <b>published</b> revision's
     * grid and tiles. Asserting the branch mutation fired would prove nothing —
     * branching from the abandoned draft fires it too.
     * </p>
     */
    it('Edits a published revision under a discarded draft from the PUBLISHED one', async () => {
      const user = userEvent.setup();
      listLayoutsMock.mockReturnValue({
        data: response([
          chain({
            revisions: [
              revision({
                revisionNumber: 1,
                state: 'Published',
                publishedAt: '2026-05-27T10:00:00Z',
                archivedAt: null,
                gridRows: 1,
                gridCols: 2,
                tiles: [
                  { cameraIdentifier: 'live-left', overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 },
                  { cameraIdentifier: 'live-right', overlayIdentifier: null, row: 0, col: 1, rowSpan: 1, colSpan: 1 },
                ],
              }),
              revision({
                revisionNumber: 2,
                revisionIdentifier: 'aa',
                gridRows: 1,
                gridCols: 1,
                tiles: [
                  { cameraIdentifier: 'discarded', overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 },
                ],
              }),
            ],
          }),
        ]),
        isLoading: false,
        isFetching: false,
        refetch: vi.fn(),
      });
      renderPage();

      await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));

      // The designer opens pre-loaded from the branch source. Two camera pickers
      // means the 1×2 published grid; one would mean the discarded draft's 1×1.
      //
      // Counted by role, not by label alone: the dialog also carries a "Find a
      // camera" field (spec 055), whose label matches /camera/i as truly as the
      // pickers do. The claim here was always about the *pickers*, and the role
      // is what says so — the sibling assertions in LayoutEditorDialog.test.tsx
      // already spell it this way.
      const editor = await screen.findByRole('dialog');
      expect(within(editor).getAllByRole('combobox', { name: /camera/i })).toHaveLength(2);
    });
  });

  /**
   * Spec 038 T020 / SC-001 — **every reachable shape offers at least one action,
   * asserted shape by shape.**
   *
   * <p>
   * Not as a loop over a fixture list. The defect being fixed is precisely a
   * shape nobody enumerated, and an aggregate assertion repeats that method.
   * There are eight, and three of them (`{A,D}`, `{D,D}`, `{P,D,D}`) were found
   * only by deriving them from the operations rather than reading the code.
   * </p>
   */
  describe('LayoutsPage — every chain shape offers something', () => {
    function rev(revisionNumber: number, state: 'Draft' | 'Published' | 'Archived') {
      return {
        revisionIdentifier: `r${revisionNumber}`,
        revisionNumber,
        state,
        gridRows: 1,
        gridCols: 1,
        tiles: [{ cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 }],
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        publishedAt: state === 'Published' ? '2026-05-27T10:00:00Z' : null,
        archivedAt: state === 'Archived' ? '2026-05-28T10:00:00Z' : null,
      };
    }

    function showing(revisions: ReturnType<typeof rev>[]) {
      listLayoutsMock.mockReturnValue({
        data: response([chain({ revisions })]),
        isLoading: false,
        isFetching: false,
        refetch: vi.fn(),
      });
    }

    const shapes: ReadonlyArray<[string, ReturnType<typeof rev>[], string[]]> = [
      ['{D}', [rev(1, 'Draft')], ['Publish', 'Discard draft']],
      ['{P}', [rev(1, 'Published')], ['Edit (new draft)', 'Revert', 'Archive']],
      ['{A}', [rev(1, 'Archived')], ['Edit (new draft)']],
      [
        '{P,D}',
        [rev(1, 'Published'), rev(2, 'Draft')],
        ['Publish', 'Discard draft', 'Edit (new draft)', 'Revert', 'Archive'],
      ],
      // The shape issue 1879 filed: this row used to offer nothing at all.
      ['{P,A}', [rev(1, 'Published'), rev(2, 'Archived')], ['Edit (new draft)', 'Revert', 'Archive']],
      ['{A,D}', [rev(1, 'Archived'), rev(2, 'Draft')], ['Publish', 'Discard draft']],
      // Two open drafts: branch off a published revision, then revert it.
      ['{D,D}', [rev(1, 'Draft'), rev(2, 'Draft')], ['Publish', 'Discard draft']],
      [
        '{P,D,D}',
        [rev(1, 'Draft'), rev(2, 'Draft'), rev(3, 'Published')],
        ['Publish', 'Discard draft', 'Edit (new draft)', 'Revert', 'Archive'],
      ],
    ];

    // Spec 266 (issue #2335) Q4 default — Discard draft, Revert and Archive
    // now render as menuitems behind a per-row "More actions" trigger rather
    // than as direct buttons (see the "LayoutsPage — archive and discard on
    // one chain" describe below for the same trigger→menuitem mechanism).
    // The expected set per shape is unchanged; only how each action is
    // discovered changes. `{A}` never renders a trigger at all — `moreActions`
    // is empty for a fully-archived chain (LayoutsPage.tsx) — so it is the one
    // shape this loop must not try to open.
    for (const [name, revisions, expected] of shapes) {
      it(`${name} offers ${expected.join(', ')}`, async () => {
        const user = userEvent.setup();
        showing(revisions);
        renderPage();

        const inlineButtons = screen
          .getAllByRole('button')
          .map((button) => button.textContent ?? '')
          .filter((label) => ACTION_LABELS.includes(label));

        const trigger = screen.queryByRole('button', { name: /more actions/i });
        let menuItems: string[] = [];
        if (trigger) {
          await user.click(trigger);
          const menu = await screen.findByRole('menu');
          menuItems = within(menu)
            .getAllByRole('menuitem')
            .map((item) => item.textContent ?? '')
            .filter((label) => ACTION_LABELS.includes(label));
          await user.keyboard('{Escape}');
        }

        const offered = [...inlineButtons, ...menuItems];
        expect(offered.sort()).toEqual([...expected].sort());
      });
    }
  });
});

/** Every action a row can offer, so a shape test can assert the exact set. */
const ACTION_LABELS = ['Publish', 'Discard draft', 'Edit (new draft)', 'Revert', 'Archive'];
