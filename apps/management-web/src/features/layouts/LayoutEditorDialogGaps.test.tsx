import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { CONFLICT_FALLBACK } from '@smart-sentinel-eye/shared/api/problemDetail';
import { store } from '../../app/store.js';
import type { LayoutEditTarget } from './LayoutEditorDialog.js';

const CAMERA = {
  cameraIdentifier: '11111111-1111-1111-1111-111111111111',
  name: 'Line 2 Furnace',
  rtspUrl: 'rtsp://10.0.5.12/h264',
  registeredAt: '2026-05-25T10:00:00Z',
};

/** Set per test, outside the mock factories. */
const state = vi.hoisted(() => ({ cameras: [] as unknown[], editError: undefined as unknown }));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [
      vi.fn(async () => ({ data: 2 })),
      { isLoading: false, error: state.editError, reset: vi.fn() },
    ],
    useGetLayoutQuery: () => ({
      data: undefined,
      currentData: { version: 7 },
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: { items: state.cameras, count: state.cameras.length, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return { ...actual, useListOverlaysQuery: () => ({ data: { chains: [], published: [] }, isLoading: false }) };
});

const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

const EDIT_TARGET: LayoutEditTarget = {
  layoutIdentifier: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  revisionNumber: 2,
  name: 'Cnc-Hall',
  grid: { rows: 1, cols: 1 },
  tiles: [
    { cameraIdentifier: CAMERA.cameraIdentifier, overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 },
  ],
};

function renderDialog(editTarget?: LayoutEditTarget) {
  return render(
    <Provider store={store}>
      <LayoutEditorDialog open onOpenChange={() => {}} editTarget={editTarget} />
    </Provider>,
  );
}

/**
 * Spec 476 T004. Two behaviours the family left unpinned (found by breaking each once and watching the whole family stay
 * green): Save's `knownCameras.size === 0` term, and the stale-conflict wording used when the server sends no detail.
 * Green on the unsplit dialog by design; it must stay green after.
 */
describe('LayoutEditorDialog — behaviours the family left unpinned', () => {
  beforeEach(() => {
    state.cameras = [];
    state.editError = undefined;
  });

  it('Keeps Save unavailable until the dialog has seen at least one camera', () => {
    state.cameras = [];

    renderDialog();

    expect(screen.getByRole('button', { name: /save as draft/i })).toHaveAttribute('aria-disabled', 'true');
  });

  it('Offers Save once a camera has been seen', () => {
    state.cameras = [CAMERA];

    renderDialog();

    expect(screen.getByRole('button', { name: /save as draft/i })).not.toHaveAttribute('aria-disabled', 'true');
  });

  it('Falls back to the conflict wording, not "Try again", for a stale refusal that carries no detail', async () => {
    state.cameras = [CAMERA];
    state.editError = { status: 409, data: { title: 'LAYOUT_REVISION_STALE' } };

    renderDialog(EDIT_TARGET);

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain(CONFLICT_FALLBACK);
    expect(alert.textContent).not.toContain('Try again');
  });

  it('Keeps the retry wording for a refusal that is not a stale conflict and carries no detail', async () => {
    state.cameras = [CAMERA];
    state.editError = { status: 409, data: { title: 'LAYOUT_NAME_TAKEN' } };

    renderDialog(EDIT_TARGET);

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('Could not save the layout. Try again.');
  });
});
