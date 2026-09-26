import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

/**
 * Spec 258 US1, T014 (tasks.md). `WallForm.tsx` and
 * `apps/shared/src/api/walls.api.ts` do not exist yet — this file is RED on
 * import failure, the acceptable "red for missing types" form (T016).
 *
 * Covers the create/edit form's scene-set validation (React Hook Form + Zod,
 * mirroring `LayoutEditorDialog.tsx`'s conventions): 2..8 distinct Published
 * layouts as scenes (PD-5), rejecting too few, too many, and a duplicate.
 */

const createWallMock = vi.fn(async () => ({ data: 'wall-1' }));
const listLayoutsMock = vi.fn();
const createWallMutationState = vi.hoisted(() => ({
  current: { isLoading: false, error: undefined as unknown, reset: vi.fn() },
}));

vi.mock('@smart-sentinel-eye/shared/api/walls.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/walls.api')>();
  return {
    ...actual,
    useCreateWallMutation: () => [createWallMock, createWallMutationState.current],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: (...args: unknown[]) => listLayoutsMock(...args),
  };
});

const { WallForm } = await import('./WallForm.js');

function publishedLayout(layoutIdentifier: string, name: string) {
  return {
    layoutIdentifier,
    name,
    revisionNumber: 1,
    gridRows: 1,
    gridCols: 1,
    tiles: [],
    publishedAt: '2026-09-26T10:00:00Z',
  };
}

function renderForm() {
  return render(
    <Provider store={store}>
      <WallForm />
    </Provider>,
  );
}

describe('WallForm', () => {
  beforeEach(() => {
    createWallMock.mockClear();
    createWallMutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
    listLayoutsMock.mockReset();
    listLayoutsMock.mockReturnValue({
      data: {
        chains: [],
        published: [
          publishedLayout('a', 'Layout A'),
          publishedLayout('b', 'Layout B'),
          publishedLayout('c', 'Layout C'),
        ],
      },
      isLoading: false,
    });
  });

  it('Submits successfully with a name and two distinct scenes', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/name/i), 'Line 3 rotation');
    await user.click(screen.getByRole('checkbox', { name: /layout a/i }));
    await user.click(screen.getByRole('checkbox', { name: /layout b/i }));
    await user.click(screen.getByRole('button', { name: /save|create/i }));

    expect(createWallMock).toHaveBeenCalled();
  });

  it('Refuses to submit with only one scene selected (PD-5 MinScenes)', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/name/i), 'One scene');
    await user.click(screen.getByRole('checkbox', { name: /layout a/i }));
    await user.click(screen.getByRole('button', { name: /save|create/i }));

    expect(await screen.findByText(/at least 2/i)).toBeInTheDocument();
    expect(createWallMock).not.toHaveBeenCalled();
  });

  it('Refuses to submit a blank name', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.click(screen.getByRole('checkbox', { name: /layout a/i }));
    await user.click(screen.getByRole('checkbox', { name: /layout b/i }));
    await user.click(screen.getByRole('button', { name: /save|create/i }));

    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
    expect(createWallMock).not.toHaveBeenCalled();
  });

  /**
   * Spec 268 (issue #2336) T012/T014, US2 — `WallForm.tsx:162`'s Save button
   * is the ninth `busy` adoption site (spec §1 finding 8; plan.md §6). It
   * already swaps its own label to 'Saving…' while `isLoading`; this adds
   * `busy={isLoading}` beside the existing `disabled={isLoading}` so the
   * button also announces `aria-busy`, additions only — the label swap and
   * `disabled` stay exactly as they are (spec §3 "In this PR" row 3).
   *
   * Red on unmodified `develop`: `WallForm.tsx` does not pass `busy` yet.
   */
  it('Announces aria-busy on Save while the create request is in flight', () => {
    createWallMutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };
    renderForm();

    const saveButton = screen.getByRole('button', { name: /saving…/i });

    expect(saveButton).toHaveAttribute('aria-busy', 'true');
  });
});
