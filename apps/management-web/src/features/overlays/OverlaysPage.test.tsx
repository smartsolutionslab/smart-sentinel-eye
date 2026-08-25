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

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useListOverlaysQuery: (...args: unknown[]) => listOverlaysMock(...args),
    usePublishOverlayRevisionMutation: () => [publishMock, { isLoading: false }],
    useArchiveOverlayRevisionMutation: () => [archiveMock, { isLoading: false }],
    useBranchDraftOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 2 })), { isLoading: false }],
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

  /** T014 / FR-007 — the specific claim, not the generic phrase it softens into. */
  it('Says the overlay can never be edited or published again', async () => {
    const user = userEvent.setup();
    showing([publishedChain()]);
    renderPage();

    expect(await openConfirmation(user)).toHaveTextContent(/never be edited or published again/i);
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
