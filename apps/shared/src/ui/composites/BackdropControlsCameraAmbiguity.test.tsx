// @vitest-environment jsdom
import { cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

const useListAllCameraChoicesQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: (...args: unknown[]) => useListAllCameraChoicesQueryMock(...args),
  };
});

const { BackdropControls } = await import('./BackdropControls.js');

const MUNICH = {
  cameraIdentifier: 'cam-munich',
  version: 1,
  fab: 'munich',
  name: 'Line-1-Entrance',
  rtspUrl: 'rtsp://10.0.5.1/h264',
  registeredAt: '2026-01-01T00:00:00Z',
  status: 'Registered',
};
const BERLIN = { ...MUNICH, cameraIdentifier: 'cam-berlin', fab: 'berlin' };

/**
 * Issue #2686 — camera names are unique only *within* a fab, and
 * `useListAllCameraChoicesQuery` spans every fab the operator holds. Two
 * same-named cameras from different fabs rendered bare are indistinguishable
 * in the picker. `GridDesigner.tsx` already solved this (spec 055 / issue
 * #2607) with a fab-qualifying label; this is the same rule, now shared via
 * `apps/shared` so `BackdropControls` can use it too.
 */
describe('BackdropControls — camera names ambiguous across fabs (issue #2686)', () => {
  afterEach(() => {
    cleanup();
  });

  it('Qualifies two identically-named cameras from different fabs by fab', () => {
    useListAllCameraChoicesQueryMock.mockReturnValue({
      data: { items: [MUNICH, BERLIN], count: 2, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    });

    render(
      <BackdropControls
        backdrop="checkerboard"
        onBackdropChange={vi.fn()}
        hasCapturedFrame={false}
        getToken={async () => 'token'}
        selectedCamera=""
        onCameraChange={vi.fn()}
        captureState="idle"
        onCapture={vi.fn()}
        onCancelCapture={vi.fn()}
      />,
    );

    const select = screen.getByRole('combobox', { name: /camera/i });

    expect(within(select).getByRole('option', { name: 'Line-1-Entrance (munich)' })).toBeInTheDocument();
    expect(within(select).getByRole('option', { name: 'Line-1-Entrance (berlin)' })).toBeInTheDocument();
  });

  it('Announces the fab-qualified name while a capture is in flight', () => {
    useListAllCameraChoicesQueryMock.mockReturnValue({
      data: { items: [MUNICH, BERLIN], count: 2, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    });

    render(
      <BackdropControls
        backdrop="checkerboard"
        onBackdropChange={vi.fn()}
        hasCapturedFrame={false}
        getToken={async () => 'token'}
        selectedCamera={BERLIN.cameraIdentifier}
        onCameraChange={vi.fn()}
        captureState="capturing"
        onCapture={vi.fn()}
        onCancelCapture={vi.fn()}
      />,
    );

    expect(screen.getByTestId('frame-capture-live-region').textContent).toBe(
      'Capturing a frame from Line-1-Entrance (berlin)…',
    );
  });
});
