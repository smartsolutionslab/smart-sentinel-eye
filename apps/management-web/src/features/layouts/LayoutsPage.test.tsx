import {
  listLayoutsMock,
  publishMock,
  archiveMock,
  branchMock,
  editDraftMock,
  revertMock,
  refusal,
  chain,
  response,
  renderPage,
  mutationState,
  LayoutsPage,
} from '../../test/layoutsPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, within, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { CONFLICT_FALLBACK } from '@smart-sentinel-eye/shared/api/problemDetail';
import { store } from '../../app/store.js';

describe('LayoutsPage', () => {
  beforeEach(() => {
    listLayoutsMock.mockReset();
    publishMock.mockClear();
    archiveMock.mockClear();
    branchMock.mockClear();
    editDraftMock.mockClear();
    revertMock.mockClear();
    mutationState.publish = { isLoading: false };
  });

  it('Shows an empty-state message when no layouts exist', () => {
    listLayoutsMock.mockReturnValue({
      data: response([]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText(/no layouts to show/i)).toBeInTheDocument();
  });

  it('Groups the state filters under the name Filter by state', () => {
    listLayoutsMock.mockReturnValue({
      data: response([]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    const group = screen.getByRole('group', { name: 'Filter by state' });
    expect(
      within(group)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['All', 'Draft', 'Published', 'Archived']);
  });

  it('Renders one card per chain with its newest revision summary', () => {
    listLayoutsMock.mockReturnValue({
      data: response([chain({ name: 'Line-1' }), chain({ name: 'Line-2', layoutIdentifier: 'aa' })]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText('Line-1')).toBeInTheDocument();
    expect(screen.getByText('Line-2')).toBeInTheDocument();
  });

  it('Clicking Publish on a Draft fires the publish mutation', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    await user.click(screen.getByRole('button', { name: /^publish$/i }));
    expect(publishMock).toHaveBeenCalledWith({
      layoutIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 1,
      version: 0,
    });
  });

  it('Shows a retry control when the list query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    listLayoutsMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch,
    });

    renderPage();

    await user.click(screen.getByRole('button', { name: /retry/i }));
    expect(refetch).toHaveBeenCalledOnce();
  });

  it('Summarizes a multi-tile revision as "N tiles, R×C"', () => {
    listLayoutsMock.mockReturnValue({
      data: response([
        chain({
          revisions: [
            {
              revisionIdentifier: '33333333-3333-3333-3333-333333333333',
              revisionNumber: 1,
              state: 'Draft',
              gridRows: 2,
              gridCols: 2,
              tiles: [
                { cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 },
                { cameraIdentifier: 'b', overlayIdentifier: null, row: 0, col: 1, rowSpan: 1, colSpan: 1 },
                { cameraIdentifier: 'c', overlayIdentifier: null, row: 1, col: 0, rowSpan: 1, colSpan: 1 },
                { cameraIdentifier: 'd', overlayIdentifier: null, row: 1, col: 1, rowSpan: 1, colSpan: 1 },
              ],
              createdAt: '2026-05-26T10:00:00Z',
              createdBy: '22222222-2222-2222-2222-222222222222',
              publishedAt: null,
              archivedAt: null,
            },
          ],
        }),
      ]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText('4 tiles, 2×2')).toBeInTheDocument();
  });

  it('Clicking Edit on a Published chain branches a draft and opens the editor', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({
      data: response([
        chain({
          revisions: [
            {
              revisionIdentifier: '33333333-3333-3333-3333-333333333333',
              revisionNumber: 3,
              state: 'Published',
              gridRows: 1,
              gridCols: 2,
              tiles: [
                { cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 },
                { cameraIdentifier: 'b', overlayIdentifier: null, row: 0, col: 1, rowSpan: 1, colSpan: 1 },
              ],
              createdAt: '2026-05-26T10:00:00Z',
              createdBy: '22222222-2222-2222-2222-222222222222',
              publishedAt: '2026-05-27T10:00:00Z',
              archivedAt: null,
            },
          ],
        }),
      ]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));
    expect(branchMock).toHaveBeenCalledWith({
      layoutIdentifier: '11111111-1111-1111-1111-111111111111',
      version: 0,
    });
    // The editor opens pre-loaded: its "Edit layout" dialog title appears.
    expect(await screen.findByText(/edit layout/i)).toBeInTheDocument();
  });
});

/**
 * Spec 231 (#2433) US1 — new behaviour, RED. `LayoutsPage` passes a plain
 * fallback string to `problemDetail` instead of
 * `isStaleConflict(mutationError) ? CONFLICT_FALLBACK : 'Could not apply that
 * change.'`, the pattern OverlaysPage.tsx and RulesPage.tsx already use. Every
 * test here injects an error through `publishState`, since `mutationError` on
 * this page reads `publishState.error ?? archiveState.error ?? branchState.error
 * ?? revertState.error` and publish is first in that chain.
 */
describe('LayoutsPage — the stale-conflict fallback (spec 231 US1)', () => {
  beforeEach(() => {
    listLayoutsMock.mockReset();
    listLayoutsMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  it('Shows the conflict fallback, not the generic sentence, for a stale refusal without a detail', () => {
    mutationState.publish = { isLoading: false, error: refusal(409, 'LAYOUT_REVISION_STALE') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(CONFLICT_FALLBACK);
    expect(alert).not.toHaveTextContent('Could not apply that change.');
    expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument();
  });

  it('Keeps the generic fallback for a non-stale 409 without a detail', () => {
    mutationState.publish = { isLoading: false, error: refusal(409, 'LAYOUT_NAME_TAKEN') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Could not apply that change.');
    expect(alert).not.toHaveTextContent(/someone else/i);
  });

  it('Shows the server’s own detail verbatim when a stale refusal carries one', () => {
    mutationState.publish = {
      isLoading: false,
      error: refusal(409, 'LAYOUT_REVISION_STALE', 'Layout has changed since version 0 (now 1).'),
    };
    renderPage();

    expect(screen.getByRole('alert')).toHaveTextContent('Layout has changed since version 0 (now 1).');
  });

  /**
   * Spec 298 (issue #2693) US1 — new behaviour, RED (ADR-0139/0144). The
   * refusal box still paints the call-site alpha blend
   * (`bg-accent-fault/10`), not the opaque `FaultNotice` tint.
   */
  it('Shows a refused change on the shared fault notice, not call-site alpha', () => {
    mutationState.publish = { isLoading: false, error: refusal(409, 'LAYOUT_NAME_TAKEN') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveClass('bg-accent-fault-subtle', 'border-accent-fault-border', 'text-accent-fault');
    expect([...alert.classList].some((c) => /\/\d+$/.test(c))).toBe(false);
  });
});

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

/**
 * Spec 273 (#2632) / ADR-0151 — L1/L2/L3, new behaviour, RED. `LayoutsPage`
 * still passes native `disabled={disabled}` to Publish (`:226`), Edit (new
 * draft) (`:247`) and the More actions trigger (`:261`), all three reading one
 * shared per-row local (`publishing || archiving || branching || reverting`,
 * `:161`). Flipping `publishState.isLoading` disables all three at once, the
 * same way the real page's shared local does.
 */
describe('LayoutsPage — Publish keeps focus while unavailable (spec 273 L1)', () => {
  beforeEach(() => {
    publishMock.mockClear();
    listLayoutsMock.mockReset();
    listLayoutsMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  it('Announces Publish as unavailable with aria-disabled, not native disabled, while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    const publish = screen.getByRole('button', { name: /^publish$/i });
    expect(publish).toHaveAttribute('aria-disabled', 'true');
    expect(publish).not.toHaveAttribute('disabled');
  });

  it('Sends no second Publish while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /^publish$/i }));

    expect(publishMock).not.toHaveBeenCalled();
  });
});

describe('LayoutsPage — Edit (new draft) keeps focus while unavailable (spec 273 L2)', () => {
  function publishedOnly() {
    return chain({
      revisions: [
        {
          revisionIdentifier: 'r1',
          revisionNumber: 3,
          state: 'Published' as const,
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

  beforeEach(() => {
    branchMock.mockClear();
    listLayoutsMock.mockReset();
    listLayoutsMock.mockReturnValue({
      data: response([publishedOnly()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  it('Announces Edit (new draft) as unavailable with aria-disabled, not native disabled, while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    const edit = screen.getByRole('button', { name: /edit \(new draft\)/i });
    expect(edit).toHaveAttribute('aria-disabled', 'true');
    expect(edit).not.toHaveAttribute('disabled');
  });

  it('Sends no branch request while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));

    expect(branchMock).not.toHaveBeenCalled();
  });
});

/**
 * L3 carries no new guard (plan.md §3.3): every menu entry already refuses
 * on the same `disabled` local, so the fix does not add a handler-level
 * guard on the trigger itself — see T014's counterfactual for the proof that
 * the entries' own `disabled` is what does the refusing. Radix's
 * `DropdownMenu.Trigger` opens on its own internal `onPointerDown`, which our
 * code never gates (spec 273 J2) — only the menu *items* underneath carry
 * `disabled` and refuse `onSelect`. So the trigger opens the menu even while
 * unavailable; it is the item, not the trigger, that refuses.
 */
describe('LayoutsPage — More actions trigger keeps focus while unavailable (spec 273 L3)', () => {
  function publishedOnly() {
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
      ],
    });
  }

  beforeEach(() => {
    revertMock.mockClear();
    listLayoutsMock.mockReset();
    listLayoutsMock.mockReturnValue({
      data: response([publishedOnly()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  it('Announces More actions as unavailable with aria-disabled, not native disabled, while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    const trigger = screen.getByRole('button', { name: /more actions/i });
    expect(trigger).toHaveAttribute('aria-disabled', 'true');
    expect(trigger).not.toHaveAttribute('disabled');
  });

  it('Opens the menu but sends no revert while the trigger is unavailable', async () => {
    const user = userEvent.setup();
    mutationState.publish = { isLoading: true };
    renderPage();

    const trigger = screen.getByRole('button', { name: /more actions/i });
    await user.click(trigger);

    const menu = await screen.findByRole('menu');
    await user.click(within(menu).getByRole('menuitem', { name: /^revert$/i }));

    expect(revertMock).not.toHaveBeenCalled();
  });
});

/**
 * Spec 310 (#2725) T004, plan.md §5 row 4. Three consecutive 403 refreshes of
 * the layouts list must drop the stale rows — the page renders exactly as a
 * first-load refusal does (FR-004). RED today: `LayoutsPage.tsx` wires no
 * revocation fallback, so `data` keeps the last successful page regardless
 * of how many 403s accumulate.
 */
describe('LayoutsPage — revocation fallback, three consecutive 403s (spec 310 #2725)', () => {
  beforeEach(() => {
    listLayoutsMock.mockReset();
    // Independent of the mutation describes above — reset explicitly rather
    // than rely on execution order, since a stray `error` here would show
    // the mutation FaultNotice and confuse this describe's own assertions.
    mutationState.publish = { isLoading: false };
  });

  function forbiddenRefresh(requestId: string) {
    return {
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: { status: 403 },
      requestId,
      refetch: vi.fn(),
    };
  }

  it('Keeps the stale rows after only two consecutive 403s', () => {
    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r1'));
    const { rerender } = renderPage();

    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r2'));
    rerender(
      <Provider store={store}>
        <LayoutsPage />
      </Provider>,
    );

    expect(screen.getByText('Line-1')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load layouts/i);
  });

  it('Shows no stale row and the existing failure banner, exactly as a first-load refusal, after a third consecutive 403', () => {
    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r1'));
    const { rerender, container } = renderPage();

    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r2'));
    rerender(
      <Provider store={store}>
        <LayoutsPage />
      </Provider>,
    );

    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r3'));
    rerender(
      <Provider store={store}>
        <LayoutsPage />
      </Provider>,
    );

    expect(screen.queryByText('Line-1')).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load layouts/i);

    // FR-004: byte-for-byte the same render as a first-load failure.
    listLayoutsMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch: vi.fn(),
    });
    const { container: firstLoadRefusal } = renderPage();

    expect(container.innerHTML).toBe(firstLoadRefusal.innerHTML);
  });
});

/**
 * Spec 427 (issue #2668) US3, R10, phase 4a RED: the state filters are ToggleChips, so the current
 * filter is announced as pressed and the pressed state moves when another is chosen. Today the
 * filters are raw buttons with no aria-pressed, so no button is ever pressed.
 */
describe('LayoutsPage — the state filters announce which one is on (spec 427 R10)', () => {
  beforeEach(() => {
    listLayoutsMock.mockReset();
    listLayoutsMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  function pressedFilterNames() {
    return screen.queryAllByRole('button', { pressed: true }).map((button) => button.textContent);
  }

  it('Presses All on first render and no other filter', () => {
    renderPage();

    expect(screen.getByRole('button', { name: 'All', pressed: true })).toBeInTheDocument();
    expect(pressedFilterNames()).toEqual(['All']);
  });

  it('Moves the pressed state to the chosen filter', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: 'Archived' }));

    expect(screen.getByRole('button', { name: 'Archived', pressed: true })).toBeInTheDocument();
    expect(pressedFilterNames()).toEqual(['Archived']);
  });
});
