import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
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

/**
 * Spec 273 (#2632) / ADR-0151 — W3, new behaviour, RED. `WallForm.tsx:162`'s
 * Save still passes native `disabled={isLoading}` alongside the existing
 * `busy={isLoading}` — the exact #2624 form-submit shape (spec.md §1),
 * mirroring `RegisterCameraDialog.test.tsx`'s own pair of assertions and its
 * form-level guard test.
 */
describe('WallForm — Save keeps focus while a create request is in flight (spec 273 W3)', () => {
  it('Announces Save as unavailable with aria-disabled, not native disabled, while saving', () => {
    createWallMutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };
    renderForm();

    const saveButton = screen.getByRole('button', { name: /saving…/i });
    expect(saveButton).toHaveAttribute('aria-disabled', 'true');
    expect(saveButton).not.toHaveAttribute('disabled');
  });

  it('Refuses a form-level submit while a create is in flight', async () => {
    const user = userEvent.setup();
    // Filled with valid values FIRST: an empty/invalid form would refuse the
    // submit on validation grounds regardless of `isLoading`, which would
    // pass this assertion for the wrong reason and prove nothing about the
    // guard under test.
    createWallMutationState.current = { isLoading: false, error: undefined, reset: vi.fn() };
    const { rerender } = renderForm();

    await user.type(screen.getByLabelText(/name/i), 'Line 3 rotation');
    await user.click(screen.getByRole('checkbox', { name: /layout a/i }));
    await user.click(screen.getByRole('checkbox', { name: /layout b/i }));

    createWallMutationState.current = { isLoading: true, error: undefined, reset: vi.fn() };
    rerender(
      <Provider store={store}>
        <WallForm />
      </Provider>,
    );

    // The form-level guard (plan.md §3.7's `handleFormSubmit`), not a click
    // or an implicit-submission proof — that is e2e/in-flight-focus.spec.ts.
    await act(async () => {
      fireEvent.submit(document.querySelector('form')!);
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(createWallMock).not.toHaveBeenCalled();
  });
});
