import {
  listLayoutsMock,
  publishMock,
  branchMock,
  revertMock,
  chain,
  response,
  renderPage,
  mutationState,
} from '../../test/layoutsPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, within, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

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
