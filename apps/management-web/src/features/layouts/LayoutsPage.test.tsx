import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { Layout, ListLayoutsResponse } from '@smart-sentinel-eye/shared/api/layouts.api';

const listLayoutsMock = vi.fn();
const publishMock = vi.fn(async () => ({ data: 1 }));
const archiveMock = vi.fn(async () => ({ data: 1 }));
const branchMock = vi.fn(async () => ({ data: 2 }));
const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
const editDraftMock = vi.fn(async () => ({ data: 2 }));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: (...args: unknown[]) => listLayoutsMock(...args),
    useGetLayoutQuery: () => ({ data: chain(), isLoading: false }),
    usePublishRevisionMutation: () => [publishMock, { isLoading: false }],
    useArchiveRevisionMutation: () => [archiveMock, { isLoading: false }],
    useBranchDraftRevisionMutation: () => [branchMock, { isLoading: false }],
    useRevertRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useCreateLayoutDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [editDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListCamerasQuery: () => ({
      data: { items: [], count: 0, offset: 0, limit: 50 },
      isLoading: false,
    }),
  };
});

const { LayoutsPage } = await import('./LayoutsPage.js');

function chain(overrides: Partial<Layout> = {}): Layout {
  return {
    layoutIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    fab: 'munich',
    name: 'Line-1',
    createdAt: '2026-05-26T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions: [
      {
        revisionIdentifier: '33333333-3333-3333-3333-333333333333',
        revisionNumber: 1,
        state: 'Draft',
        gridRows: 1,
        gridCols: 1,
        tiles: [{ cameraIdentifier: '44444444-4444-4444-4444-444444444444', overlayIdentifier: null, row: 0, col: 0 }],
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        publishedAt: null,
        archivedAt: null,
      },
    ],
    ...overrides,
  };
}

function response(chains: Layout[]): ListLayoutsResponse {
  return { chains, published: [] };
}

function renderPage() {
  return render(
    <Provider store={store}>
      <LayoutsPage />
    </Provider>,
  );
}

describe('LayoutsPage', () => {
  beforeEach(() => {
    listLayoutsMock.mockReset();
    publishMock.mockClear();
    archiveMock.mockClear();
    branchMock.mockClear();
    editDraftMock.mockClear();
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
                { cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0 },
                { cameraIdentifier: 'b', overlayIdentifier: null, row: 0, col: 1 },
                { cameraIdentifier: 'c', overlayIdentifier: null, row: 1, col: 0 },
                { cameraIdentifier: 'd', overlayIdentifier: null, row: 1, col: 1 },
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
                { cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0 },
                { cameraIdentifier: 'b', overlayIdentifier: null, row: 0, col: 1 },
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
          tiles: [{ cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0 }],
          createdAt: '2026-05-26T10:00:00Z',
          createdBy: '22222222-2222-2222-2222-222222222222',
          publishedAt: '2026-05-27T10:00:00Z',
          archivedAt: null,
        },
      ],
    });
  }

  async function openConfirmation(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole('button', { name: /^archive$/i }));
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
        tiles: [{ cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0 }],
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
                  { cameraIdentifier: 'live-left', overlayIdentifier: null, row: 0, col: 0 },
                  { cameraIdentifier: 'live-right', overlayIdentifier: null, row: 0, col: 1 },
                ],
              }),
              revision({
                revisionNumber: 2,
                revisionIdentifier: 'aa',
                gridRows: 1,
                gridCols: 1,
                tiles: [{ cameraIdentifier: 'discarded', overlayIdentifier: null, row: 0, col: 0 }],
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
        tiles: [{ cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0 }],
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

    for (const [name, revisions, expected] of shapes) {
      it(`${name} offers ${expected.join(', ')}`, () => {
        showing(revisions);
        renderPage();

        const offered = screen
          .getAllByRole('button')
          .map((button) => button.textContent ?? '')
          .filter((label) => expected.includes(label) || ACTION_LABELS.includes(label));

        expect(offered.sort()).toEqual([...expected].sort());
      });
    }
  });
});

/** Every action a row can offer, so a shape test can assert the exact set. */
const ACTION_LABELS = ['Publish', 'Discard draft', 'Edit (new draft)', 'Revert', 'Archive'];

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
          tiles: [{ cameraIdentifier: 'a', overlayIdentifier: null, row: 0, col: 0 }],
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
          tiles: [{ cameraIdentifier: 'b', overlayIdentifier: null, row: 0, col: 0 }],
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

  async function confirm(user: ReturnType<typeof userEvent.setup>, label: RegExp, button: RegExp) {
    await user.click(screen.getByRole('button', { name: label }));
    const dialog = screen.getByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: button }));
  }

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

    await user.click(screen.getByRole('button', { name: /discard draft/i }));
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

    await user.click(screen.getByRole('button', { name: /^archive$/i }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('revision 4');
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: /cancel/i }));

    await user.click(screen.getByRole('button', { name: /discard draft/i }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('draft revision 5');
  });

  it('Discards nothing when the discard confirmation is dismissed', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /discard draft/i }));
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
