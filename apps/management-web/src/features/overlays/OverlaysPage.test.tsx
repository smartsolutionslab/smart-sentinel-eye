import {
  listOverlaysMock,
  publishMock,
  archiveMock,
  editDraftMock,
  chain,
  response,
  renderPage,
  mutationState,
} from '../../test/overlaysPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

describe('OverlaysPage', () => {
  beforeEach(() => {
    listOverlaysMock.mockReset();
    publishMock.mockClear();
    archiveMock.mockClear();
    editDraftMock.mockClear();
    mutationState.publish = { isLoading: false };
    mutationState.archive = { isLoading: false };
    mutationState.branch = { isLoading: false };
    mutationState.revert = { isLoading: false };
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

  it('Groups the state filters under the name Filter by state', () => {
    listOverlaysMock.mockReturnValue({
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

  /**
   * Spec 300 (#2349) phase-6 S5: `firstTextOrShapesOnly` replaced
   * `summarised.labels[0]!.text`, which would have shown the Box's own
   * fields (or crashed) rather than the set's one Text element.
   */
  it("Shows the Text element's text for a mixed [Box, Text] revision", () => {
    listOverlaysMock.mockReturnValue({
      data: response([
        chain({
          name: 'Mixed overlay',
          revisions: [
            {
              revisionIdentifier: '33333333-3333-3333-3333-333333333333',
              revisionNumber: 1,
              state: 'Draft',
              elements: [
                {
                  kind: 'Box',
                  color: '#D32F2FFF',
                  normalizedX: 0.05,
                  normalizedY: 0.05,
                  normalizedWidth: 0.2,
                  normalizedHeight: 0.2,
                },
                {
                  kind: 'Text',
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

    expect(screen.getByText('Production Line 1')).toBeInTheDocument();
    expect(screen.queryByText('Shapes only')).not.toBeInTheDocument();
  });

  /**
   * Spec 300 (#2349) phase-6 S5: a shapes-only revision carries no Text
   * element at all — `summarised.labels[0]!.text` would have crashed here.
   */
  it('Shows "Shapes only" for a revision with no Text element', () => {
    listOverlaysMock.mockReturnValue({
      data: response([
        chain({
          name: 'Shapes-only overlay',
          revisions: [
            {
              revisionIdentifier: '33333333-3333-3333-3333-333333333333',
              revisionNumber: 1,
              state: 'Draft',
              elements: [
                {
                  kind: 'Box',
                  color: '#D32F2FFF',
                  normalizedX: 0.05,
                  normalizedY: 0.05,
                  normalizedWidth: 0.2,
                  normalizedHeight: 0.2,
                },
              ],
              createdAt: '2026-05-27T10:00:00Z',
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

    expect(screen.getByText('Shapes only')).toBeInTheDocument();
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

/**
 * Spec 427 (issue #2668) US3, R10, phase 4a RED: the state filters are ToggleChips, so the current
 * filter is announced as pressed and the pressed state moves when another is chosen. Today the
 * filters are raw buttons with no aria-pressed, so no button is ever pressed.
 */
describe('OverlaysPage — the state filters announce which one is on (spec 427 R10)', () => {
  beforeEach(() => {
    listOverlaysMock.mockReset();
    listOverlaysMock.mockReturnValue({
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
