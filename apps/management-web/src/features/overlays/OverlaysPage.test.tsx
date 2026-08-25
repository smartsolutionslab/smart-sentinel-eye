import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type {
  ListOverlaysResponse,
  Overlay,
} from '@smart-sentinel-eye/shared/api/overlays.api';

const listOverlaysMock = vi.fn();
const publishMock = vi.fn(async () => ({ data: 1 }));
const archiveMock = vi.fn(async () => ({ data: 1 }));
const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
// Spec 037: hoisted from an inline anonymous vi.fn. It satisfied the hook and
// could not be asserted on, so nothing could check that recovering an archived
// overlay actually branches.
const branchMock = vi.fn(async () => ({ data: 2 }));

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useListOverlaysQuery: (...args: unknown[]) => listOverlaysMock(...args),
    usePublishOverlayRevisionMutation: () => [publishMock, { isLoading: false }],
    useArchiveOverlayRevisionMutation: () => [archiveMock, { isLoading: false }],
    useBranchDraftOverlayRevisionMutation: () => [branchMock, { isLoading: false }],
    useRevertOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
  };
});

const { OverlaysPage } = await import('./OverlaysPage.js');

function chain(overrides: Partial<Overlay> = {}): Overlay {
  return {
    overlayIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    name: 'Line-1 Title',
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions: [
      {
        revisionIdentifier: '33333333-3333-3333-3333-333333333333',
        revisionNumber: 1,
        state: 'Draft',
        text: 'Production Line 1',
        normalizedX: 0.1,
        normalizedY: 0.1,
        normalizedWidth: 0.3,
        normalizedHeight: 0.08,
        fontSizePx: 32,
        createdAt: '2026-05-27T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        publishedAt: null,
        archivedAt: null,
      },
    ],
    ...overrides,
  };
}

function response(chains: Overlay[]): ListOverlaysResponse {
  return { chains, published: [] };
}

function renderPage() {
  return render(
    <Provider store={store}>
      <OverlaysPage />
    </Provider>,
  );
}

describe('OverlaysPage', () => {
  beforeEach(() => {
    listOverlaysMock.mockReset();
    publishMock.mockClear();
    archiveMock.mockClear();
  });

  it('Shows an empty-state message when no overlays exist', () => {
    listOverlaysMock.mockReturnValue({
      data: response([]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText(/no overlays to show/i)).toBeInTheDocument();
  });

  it('Renders one card per chain showing the label preview text', () => {
    listOverlaysMock.mockReturnValue({
      data: response([chain({ name: 'Line-1 Title' })]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText('Line-1 Title')).toBeInTheDocument();
    expect(screen.getByText('Production Line 1')).toBeInTheDocument();
  });

  it('Clicking Publish on a Draft fires the publish mutation', async () => {
    const user = userEvent.setup();
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    await user.click(screen.getByRole('button', { name: /^publish$/i }));
    expect(publishMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 1,
      version: 0,
    });
  });

  it('Shows a retry control when the list query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    listOverlaysMock.mockReturnValue({
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
});

/** Spec 036 — the confirmations. Same shape as LayoutsPage, same prohibitions. */
describe('OverlaysPage — archive confirmation', () => {
  function publishedChain() {
    return chain({
      revisions: [
        {
          revisionIdentifier: '33333333-3333-3333-3333-333333333333',
          revisionNumber: 2,
          state: 'Published',
          text: 'Production Line 1',
          normalizedX: 0.1,
          normalizedY: 0.1,
          normalizedWidth: 0.3,
          normalizedHeight: 0.08,
          fontSizePx: 32,
          createdAt: '2026-05-27T10:00:00Z',
          createdBy: '22222222-2222-2222-2222-222222222222',
          publishedAt: '2026-05-28T10:00:00Z',
          archivedAt: null,
        },
      ],
    });
  }

  function showing(chains: ReturnType<typeof chain>[]) {
    listOverlaysMock.mockReturnValue({
      data: response(chains),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  }

  async function openConfirmation(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole('button', { name: /^archive$/i }));
    return screen.getByRole('alertdialog');
  }

  /** T012 / FR-002 — asserted as a call count, not as the dialog closing. */
  it('Archives nothing when the confirmation is dismissed', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    const confirmation = await openConfirmation(user);
    await user.click(within(confirmation).getByRole('button', { name: /cancel/i }));

    expect(archiveMock).not.toHaveBeenCalled();
  });

  it('Archives once confirmed, sending the revision it named', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    const confirmation = await openConfirmation(user);
    expect(archiveMock).not.toHaveBeenCalled();

    await user.click(within(confirmation).getByRole('button', { name: /^archive$/i }));

    expect(archiveMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 2,
      version: 0,
    });
  });

  /** T013 / FR-003 — the name and revision, never the identifier. */
  it('Names the overlay and revision, and shows no identifier', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    const confirmation = await openConfirmation(user);

    expect(confirmation).toHaveTextContent('Line-1 Title');
    expect(confirmation).toHaveTextContent('2');
    expect(confirmation).not.toHaveTextContent('11111111-1111-1111-1111-111111111111');
    expect(confirmation).not.toHaveTextContent(/are you sure/i);
  });

  /**
   * Spec 037 T025 / FR-011, FR-012 — **rewritten**, not deleted.
   *
   * This was spec 036's T014, asserting the overlay could never be edited or
   * published again. ADR-0121 made that false, so the claim changed and the test
   * changed with it. Deleting it instead would have removed the only check on
   * this wording at the exact moment the wording changed.
   *
   * It asserts the **absence** of both false sentences as well as the presence
   * of the true one. A test that merely stopped asserting the old sentence would
   * pass against a page that still said it.
   */
  it('Says the overlay can be brought back and keeps its label', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    const confirmation = await openConfirmation(user);

    expect(confirmation).toHaveTextContent(/bring it back/i);
    expect(confirmation).toHaveTextContent(/label is kept/i);
    expect(confirmation).toHaveTextContent(/out of service/i);
    expect(confirmation).not.toHaveTextContent(/never be edited or published again/i);
    expect(confirmation).not.toHaveTextContent(/cannot be undone/i);
  });

  /** T015 / FR-008 — both directions, because published-only passes against a confirmation that always warns. */
  it('Always warns about kiosks, because archive only ever targets the live revision', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    expect(await openConfirmation(user)).toHaveTextContent(/kiosks/i);
  });
});

/** Spec 037 — recovering an archived overlay. Same shape as LayoutsPage. */
describe('OverlaysPage — recovering an archived overlay', () => {
  function revision(overrides: Partial<Overlay['revisions'][number]>) {
    return {
      revisionIdentifier: '33333333-3333-3333-3333-333333333333',
      revisionNumber: 1,
      state: 'Archived' as const,
      text: 'Production Line 1',
      normalizedX: 0.1,
      normalizedY: 0.1,
      normalizedWidth: 0.3,
      normalizedHeight: 0.08,
      fontSizePx: 32,
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '22222222-2222-2222-2222-222222222222',
      publishedAt: null,
      archivedAt: '2026-05-29T10:00:00Z',
      ...overrides,
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
      text: 'Production Line 1',
      normalizedX: 0.1,
      normalizedY: 0.1,
      normalizedWidth: 0.3,
      normalizedHeight: 0.08,
      fontSizePx: 32,
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '22222222-2222-2222-2222-222222222222',
      publishedAt: state === 'Published' ? '2026-05-28T10:00:00Z' : null,
      archivedAt: state === 'Archived' ? '2026-05-29T10:00:00Z' : null,
    };
  }

  const ACTION_LABELS = ['Publish', 'Discard draft', 'Edit (new draft)', 'Revert', 'Archive'];

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

/**
 * Spec 038 T021 / T024 — the two destructive actions on **one** chain. The twin
 * of LayoutsPage's, and the shape where the old row was wrong: **Archive**
 * archived the draft while its confirmation said the overlay was going out of
 * service.
 */
describe('OverlaysPage — archive and discard on one chain', () => {
  function rev(revisionNumber: number, state: 'Draft' | 'Published') {
    return {
      revisionIdentifier: `r${revisionNumber}`,
      revisionNumber,
      state,
      text: 'Production Line 1',
      normalizedX: 0.1,
      normalizedY: 0.1,
      normalizedWidth: 0.3,
      normalizedHeight: 0.08,
      fontSizePx: 32,
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '22222222-2222-2222-2222-222222222222',
      publishedAt: state === 'Published' ? '2026-05-28T10:00:00Z' : null,
      archivedAt: null,
    };
  }

  beforeEach(() => {
    archiveMock.mockClear();
    listOverlaysMock.mockReturnValue({
      data: response([chain({ revisions: [rev(4, 'Published'), rev(5, 'Draft')] })]),
      isLoading: false,
      isFetching: false,
      error: undefined,
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
   * they name, so asserting that the request fired asserts nothing. Asserted on
   * separate chains, a swap would pass twice.
   */
  it('Archives the LIVE revision and discards the DRAFT, not the other way round', async () => {
    const user = userEvent.setup();
    renderPage();

    await confirm(user, /^archive$/i, /^archive$/i);
    expect(archiveMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 4,
      version: 0,
    });

    archiveMock.mockClear();

    await confirm(user, /discard draft/i, /^discard$/i);
    expect(archiveMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 5,
      version: 0,
    });
  });

  /**
   * The forbidden claims are that the overlay goes **out of service**, that
   * kiosks **stop showing** it, and that it can be **brought back**. Not the
   * word "kiosk": saying kiosks are *unaffected* is true, and it is the most
   * reassuring thing this dialog can say.
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
   * `Published` — that appears either way.
   */
  it('Names the live revision in the badge, and says a draft is open', () => {
    renderPage();

    expect(screen.getByText('v4 · Published · draft v5')).toBeInTheDocument();
  });
});
