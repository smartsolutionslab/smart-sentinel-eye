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
  it('Warns about kiosks for a published revision and not for a draft', async () => {
    const user = userEvent.setup();

    showing([publishedChain()]);
    const published = renderPage();
    expect(await openConfirmation(user)).toHaveTextContent(/kiosks/i);
    published.unmount();

    showing([chain()]);
    renderPage();
    expect(await openConfirmation(user)).not.toHaveTextContent(/kiosks/i);
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
   * The other direction, and the reason the gate tests the **chain** rather
   * than `newest.state`.
   *
   * A chain can hold a Published revision under an abandoned newer draft. Its
   * newest revision is Archived, but it is not stranded at all — kiosks are
   * still showing it. Gating on `newest.state === 'Archived'` would offer it a
   * recovery it does not need.
   *
   * That shape has a separate problem — it is offered no row actions whatsoever,
   * filed as issue 1879 and deliberately not fixed here. This asserts only that
   * the new gate does not misclassify it as recoverable.
   */
  it('Does not treat a published revision under an abandoned draft as recoverable', () => {
    showing([
      chain({
        revisions: [
          revision({ revisionNumber: 1, state: 'Published', publishedAt: '2026-05-28T10:00:00Z', archivedAt: null }),
          revision({ revisionNumber: 2, revisionIdentifier: 'aa' }),
        ],
      }),
    ]);

    renderPage();

    expect(screen.queryByRole('button', { name: /edit \(new draft\)/i })).not.toBeInTheDocument();
  });
});
