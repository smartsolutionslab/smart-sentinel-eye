import { listLayoutsMock, archiveMock, chain, response, renderPage } from '../../test/layoutsPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

/**
 * Spec 038 T021 / T024 — the two destructive actions on **one** chain.
 *
 * <p>
 * A chain with a live revision and an open draft is where the old row was
 * wrong: <b>Archive</b> archived the draft while its confirmation said the
 * layout was going out of service.
 * </p>
 */
describe('LayoutsPage — archive and discard on one chain', () => {
  function liveWithDraft() {
    return chain({
      revisions: [
        {
          revisionIdentifier: 'r1',
          revisionNumber: 4,
          state: 'Published' as const,
          gridRows: 1,
          gridCols: 1,
          tiles: [{ cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 }],
          createdAt: '2026-05-26T10:00:00Z',
          createdBy: '22222222-2222-2222-2222-222222222222',
          publishedAt: '2026-05-27T10:00:00Z',
          archivedAt: null,
        },
        {
          revisionIdentifier: 'r2',
          revisionNumber: 5,
          state: 'Draft' as const,
          gridRows: 1,
          gridCols: 1,
          tiles: [{ cameraIdentifier: 'b', overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 }],
          createdAt: '2026-05-28T10:00:00Z',
          createdBy: '22222222-2222-2222-2222-222222222222',
          publishedAt: null,
          archivedAt: null,
        },
      ],
    });
  }

  beforeEach(() => {
    archiveMock.mockClear();
    listLayoutsMock.mockReturnValue({
      data: response([liveWithDraft()]),
      isLoading: false,
      isFetching: false,
      refetch: vi.fn(),
    });
  });

  /**
   * Spec 266 (issue #2335) US2 — new behaviour, RED. `itemLabel` now names a
   * menu item behind "More actions" rather than a row button — see
   * `openConfirmation` above for why this is expected red today.
   */
  async function confirm(user: ReturnType<typeof userEvent.setup>, itemLabel: RegExp, button: RegExp) {
    await user.click(screen.getByRole('button', { name: /more actions/i }));
    await user.click(await screen.findByRole('menuitem', { name: itemLabel }));
    const dialog = screen.getByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: button }));
  }

  /**
   * Spec 266 (issue #2335) US2 (Q4 default) — new behaviour, RED. Publish and
   * Edit (new draft) stay inline; Discard draft, Revert and Archive move
   * behind one "More actions" trigger per row. Red today: the row renders
   * all five as direct buttons, so this row shows exactly two.
   */
  it('Keeps Publish and Edit (new draft) inline, and offers no direct button for Discard draft, Revert or Archive', () => {
    renderPage();

    expect(screen.getByRole('button', { name: /^publish$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /edit \(new draft\)/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /discard draft/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^revert$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^archive$/i })).not.toBeInTheDocument();
  });

  /**
   * Spec 266 (issue #2335) US2 — new behaviour, RED: there is no "More
   * actions" trigger yet, so `getByRole` throws before the menu is ever
   * opened.
   */
  it('"More actions" opens a menu with Discard draft, Revert and Archive', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /more actions/i }));

    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('menuitem', { name: /discard draft/i })).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: /^revert$/i })).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: /^archive$/i })).toBeInTheDocument();
  });

  /**
   * Spec 266 (issue #2335) US2 scenario 2 — new behaviour, RED. The one this
   * spec is built to hold: a non-modal menu (`Root modal={false}`,
   * plan.md §3.2) so opening the ConfirmDialog from the menu's `onSelect`
   * does not leave `pointer-events: none` on `<body>` or race the two focus
   * scopes. Covered directly on the primitive (`DropdownMenu.test.tsx`); this
   * is the same guarantee through the real consumer.
   */
  it('Choosing Archive from the menu opens the confirmation, and Escape returns focus to More actions', async () => {
    const user = userEvent.setup();
    renderPage();

    const trigger = screen.getByRole('button', { name: /more actions/i });
    await user.click(trigger);
    await user.click(await screen.findByRole('menuitem', { name: /^archive$/i }));

    expect(screen.getByRole('alertdialog')).toBeInTheDocument();

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(document.body.style.pointerEvents).not.toBe('none');
  });

  /**
   * **The targets, on the same chain.** Both calls succeed whichever revision
   * they name — which is exactly why the old defect went unnoticed — so
   * asserting that the request fired asserts nothing. Asserted on separate
   * chains, a swap would pass twice.
   */
  it('Archives the LIVE revision and discards the DRAFT, not the other way round', async () => {
    const user = userEvent.setup();
    renderPage();

    await confirm(user, /^archive$/i, /^archive$/i);
    expect(archiveMock).toHaveBeenCalledWith({
      layoutIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 4,
      version: 0,
    });

    archiveMock.mockClear();

    await confirm(user, /discard draft/i, /^discard$/i);
    expect(archiveMock).toHaveBeenCalledWith({
      layoutIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 5,
      version: 0,
    });
  });

  /**
   * The falsehood this feature exists to remove, asserted as an **absence**.
   * A dialog that says both the true and the false sentence passes any
   * assertion about the true one — and copying the archive body across is the
   * fast way to build this dialog.
   *
   * <p>
   * The forbidden claims are that the layout goes <b>out of service</b>, that
   * kiosks are <b>sent away</b> or <b>stop showing</b> it, and that it can be
   * <b>brought back</b>. Not the word "kiosk": saying kiosks are <i>unaffected</i>
   * is true, and it is the most reassuring thing this dialog can say. An earlier
   * draft of this test banned the word and contradicted the contract it was
   * written from.
   * </p>
   */
  it('Claims no consequence that does not apply when discarding a draft', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /more actions/i }));
    await user.click(await screen.findByRole('menuitem', { name: /discard draft/i }));
    const dialog = screen.getByRole('alertdialog');

    expect(dialog).toHaveTextContent(/cannot be recovered/i);
    expect(dialog).toHaveTextContent(/stays exactly as it is/i);
    expect(dialog).toHaveTextContent(/kiosks are unaffected/i);
    expect(dialog).not.toHaveTextContent(/out of service/i);
    expect(dialog).not.toHaveTextContent(/sent away/i);
    expect(dialog).not.toHaveTextContent(/stop showing/i);
    expect(dialog).not.toHaveTextContent(/bring it back/i);
  });

  it('Names a different revision in each confirmation', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /more actions/i }));
    await user.click(await screen.findByRole('menuitem', { name: /^archive$/i }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('revision 4');
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: /cancel/i }));

    await user.click(screen.getByRole('button', { name: /more actions/i }));
    await user.click(await screen.findByRole('menuitem', { name: /discard draft/i }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('draft revision 5');
  });

  it('Discards nothing when the discard confirmation is dismissed', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /more actions/i }));
    await user.click(await screen.findByRole('menuitem', { name: /discard draft/i }));
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: /cancel/i }));

    expect(archiveMock).not.toHaveBeenCalled();
  });

  /**
   * Spec 038 FR-009 / T023. Asserts the revision **number**, not the word
   * `Published` — that appears either way, so it would pass against a row still
   * reporting its newest revision.
   */
  it('Names the live revision in the badge, and says a draft is open', () => {
    renderPage();

    expect(screen.getByText('v4 · Published · draft v5')).toBeInTheDocument();
  });
});
