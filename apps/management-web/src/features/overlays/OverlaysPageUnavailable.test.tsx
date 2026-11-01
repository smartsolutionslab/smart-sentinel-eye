import {
  listOverlaysMock,
  publishMock,
  archiveMock,
  branchMock,
  chain,
  response,
  renderPage,
  mutationState,
} from '../../test/overlaysPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';

/**
 * Spec 273 (#2632) / ADR-0151 — O1/O2, new behaviour, RED. `OverlaysPage`
 * still passes native `disabled={disabled}` to Publish (`:169`) and Discard
 * draft (`:184`), both reading one shared per-row local (`publishing ||
 * archiving || branching || reverting`, `:156`). Flipping `publishState`
 * disables both at once, the same way the real page's shared local does.
 */
describe('OverlaysPage — Publish and Discard draft keep focus while unavailable (spec 273 O1/O2)', () => {
  beforeEach(() => {
    publishMock.mockClear();
    archiveMock.mockClear();
    listOverlaysMock.mockReset();
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
    mutationState.publish = { isLoading: false };
    mutationState.archive = { isLoading: false };
    mutationState.branch = { isLoading: false };
    mutationState.revert = { isLoading: false };
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

  it('Announces Discard draft as unavailable with aria-disabled, not native disabled, while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    const discard = screen.getByRole('button', { name: /discard draft/i });
    expect(discard).toHaveAttribute('aria-disabled', 'true');
    expect(discard).not.toHaveAttribute('disabled');
  });

  it('Opens no discard confirmation while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /discard draft/i }));

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
});

describe('OverlaysPage — Edit (new draft), Revert and Archive keep focus while unavailable (spec 273 O3/O4/O5)', () => {
  function publishedOnly() {
    return chain({
      revisions: [
        {
          revisionIdentifier: 'r1',
          revisionNumber: 2,
          state: 'Published' as const,
          elements: [
            {
              kind: 'Text' as const,
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
          publishedAt: '2026-05-28T10:00:00Z',
          archivedAt: null,
        },
      ],
    });
  }

  beforeEach(() => {
    branchMock.mockClear();
    archiveMock.mockClear();
    listOverlaysMock.mockReset();
    listOverlaysMock.mockReturnValue({
      data: response([publishedOnly()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
    mutationState.publish = { isLoading: false };
    mutationState.archive = { isLoading: false };
    mutationState.branch = { isLoading: false };
    mutationState.revert = { isLoading: false };
  });

  it('Announces Edit (new draft), Revert and Archive as unavailable with aria-disabled, not native disabled, while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    for (const name of [/edit \(new draft\)/i, /^revert$/i, /^archive$/i]) {
      const button = screen.getByRole('button', { name });
      expect(button).toHaveAttribute('aria-disabled', 'true');
      expect(button).not.toHaveAttribute('disabled');
    }
  });

  it('Sends no branch request from Edit (new draft) while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));

    expect(branchMock).not.toHaveBeenCalled();
  });

  it('Opens no archive confirmation while a mutation is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /^archive$/i }));

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
});

/**
 * [J1] (spec.md §5) — `:209` Edit draft is deliberately OUT of scope: its
 * handler sends nothing at all, so activating it never disables itself, and
 * the only way it is seen disabled is another row action's mutation, while
 * which focus already belongs elsewhere. Recorded as a **pin**, green today
 * and required to stay green and UNCHANGED after 4b (T003) — a silent flip
 * to `unavailable` here would be scope creep this test catches.
 */
describe('OverlaysPage — Edit draft (:209) stays native disabled, out of scope (spec 273 [J1])', () => {
  beforeEach(() => {
    listOverlaysMock.mockReset();
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
    mutationState.publish = { isLoading: false };
    mutationState.archive = { isLoading: false };
    mutationState.branch = { isLoading: false };
    mutationState.revert = { isLoading: false };
  });

  it('Edit draft carries native disabled, not aria-disabled, while another mutation on the row is in flight', () => {
    mutationState.publish = { isLoading: true };
    renderPage();

    const editDraft = screen.getByRole('button', { name: /^edit draft$/i });
    expect(editDraft).toBeDisabled();
    expect(editDraft).not.toHaveAttribute('aria-disabled', 'true');
  });
});
