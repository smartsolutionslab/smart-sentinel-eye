import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type {
  Layout,
  ListLayoutsResponse,
} from '@smart-sentinel-eye/shared/api/layouts.api';

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
    listLayoutsMock.mockReturnValue({ data: response([publishedChain()]), isLoading: false, isFetching: false, refetch: vi.fn() });
    renderPage();

    const confirmation = await openConfirmation(user);
    await user.click(within(confirmation).getByRole('button', { name: /cancel/i }));

    expect(archiveMock).not.toHaveBeenCalled();
  });

  it('Archives once confirmed, sending the revision it named', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({ data: response([publishedChain()]), isLoading: false, isFetching: false, refetch: vi.fn() });
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
    listLayoutsMock.mockReturnValue({ data: response([publishedChain()]), isLoading: false, isFetching: false, refetch: vi.fn() });
    renderPage();

    const confirmation = await openConfirmation(user);

    expect(confirmation).toHaveTextContent('Line-1');
    expect(confirmation).toHaveTextContent('4');
    expect(confirmation).not.toHaveTextContent('11111111-1111-1111-1111-111111111111');
    expect(confirmation).not.toHaveTextContent(/are you sure/i);
  });

  /**
   * T014 / FR-007 — the sentence most likely to be softened away.
   *
   * "This cannot be undone" is true of all four archive confirmations and
   * understates this one: the layout does not merely stay archived, it becomes
   * permanently unusable. Asserting the generic phrase would pass against the
   * softened wording, so the specific claim is what is asserted.
   */
  it('Says the layout can never be edited or published again', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({ data: response([publishedChain()]), isLoading: false, isFetching: false, refetch: vi.fn() });
    renderPage();

    const confirmation = await openConfirmation(user);

    expect(confirmation).toHaveTextContent(/never be edited or published again/i);
  });

  /**
   * T015 / FR-008 — **both directions in one test**, deliberately.
   *
   * Asserting only the published case passes against a confirmation that always
   * warns, and an overstated warning is one operators learn to click through —
   * which costs more than the warning buys. Archiving a draft strands nothing
   * and no kiosk is showing a draft.
   */
  it('Warns about kiosks for a published revision and not for a draft', async () => {
    const user = userEvent.setup();

    listLayoutsMock.mockReturnValue({ data: response([publishedChain()]), isLoading: false, isFetching: false, refetch: vi.fn() });
    const published = renderPage();
    expect(await openConfirmation(user)).toHaveTextContent(/kiosks/i);
    published.unmount();

    // The default fixture's newest revision is a Draft.
    listLayoutsMock.mockReturnValue({ data: response([chain()]), isLoading: false, isFetching: false, refetch: vi.fn() });
    renderPage();
    expect(await openConfirmation(user)).not.toHaveTextContent(/kiosks/i);
  });
});
