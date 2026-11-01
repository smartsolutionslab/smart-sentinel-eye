import {
  listLayoutsMock,
  publishMock,
  archiveMock,
  branchMock,
  editDraftMock,
  revertMock,
  chain,
  response,
  renderPage,
  mutationState,
} from '../../test/layoutsPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

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
