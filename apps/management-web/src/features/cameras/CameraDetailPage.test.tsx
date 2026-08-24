import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

const listCameras = vi.hoisted(() => vi.fn());
const getCamera = vi.hoisted(() =>
  vi.fn(() => ({ data: undefined as unknown, isLoading: false, error: undefined as unknown })),
);

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useGetCameraQuery: (...args: unknown[]) => getCamera(...(args as [])),
    // Not stubbed with data on purpose: T025 asserts this is never called, so a
    // detail view that reached for the catalogue would fail rather than quietly
    // work off a cached list.
    useListCamerasQuery: (...args: unknown[]) => {
      listCameras(...(args as []));
      return { data: undefined, isLoading: false, isFetching: false, error: undefined, refetch: vi.fn() };
    },
  };
});

const { CameraDetailPage } = await import('./CameraDetailPage.js');
const { store } = await import('../../app/store.js');

const camera = {
  cameraIdentifier: '11111111-1111-1111-1111-111111111111',
  version: 7,
  fab: 'munich',
  name: 'Line-1-Entrance',
  rtspUrl: 'rtsp://10.0.5.12/h264',
  registeredAt: '2026-05-24T10:00:00Z',
  status: 'Registered',
};

function renderAt(identifier: string) {
  return render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/cameras/${identifier}`]}>
        <Routes>
          <Route path="/cameras/:cameraIdentifier" element={<CameraDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

describe('CameraDetailPage', () => {
  beforeEach(() => {
    listCameras.mockClear();
    getCamera.mockReturnValue({ data: camera, isLoading: false, error: undefined });
  });

  it('Shows the camera it was asked for', () => {
    renderAt(camera.cameraIdentifier);

    expect(screen.getByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();
    expect(screen.getByText('munich')).toBeInTheDocument();
    expect(screen.getByText('rtsp://10.0.5.12/h264')).toBeInTheDocument();

    // The counterpart to the retired case below: an active camera offers the
    // correction, so the absence there is scoping rather than the control
    // simply never having been built.
    expect(screen.getByRole('button', { name: /correct the address/i })).toBeInTheDocument();
  });

  /**
   * FR-003 and SC-002. A detail view that rendered correctly while fetching the
   * whole catalogue would pass every other test in this file — the saving is
   * invisible from the outside, so it has to be asserted directly.
   */
  it('Does not fetch the catalogue to show one camera', () => {
    renderAt(camera.cameraIdentifier);

    expect(listCameras).not.toHaveBeenCalled();
  });

  /**
   * FR-007. Retirement takes a camera out of the default listing, not out of
   * existence — the record is what the audit trail refers to.
   */
  it('Opens a retired camera and says it is retired', () => {
    getCamera.mockReturnValue({
      data: { ...camera, status: 'Decommissioned' },
      isLoading: false,
      error: undefined,
    });

    renderAt(camera.cameraIdentifier);

    expect(screen.getByRole('heading', { name: 'Line-1-Entrance' })).toBeInTheDocument();

    // The explanation, not just the badge — an operator has to know the record
    // is kept and that the camera cannot be changed, which is what stops the
    // absence of an edit control reading as a bug.
    expect(screen.getByRole('status')).toHaveTextContent(/retired/i);
    expect(screen.getByRole('status')).toHaveTextContent(/no longer be changed/i);

    // T031. Absent, not present-and-failing. Asserting that submitting fails
    // would pass against a dialog the operator can open and fill in before
    // being refused, which is exactly what FR-007 rules out.
    expect(screen.queryByRole('button', { name: /correct the address/i })).toBeNull();
  });

  /**
   * FR-008, and the reason this file asserts sameness rather than a message.
   *
   * The API answers a camera in another fab exactly as it answers one that
   * never existed, because a camera record carries its RTSP address. The app
   * can undo that in one helpful sentence, so what is checked is that both
   * causes render the *same* thing — not that each renders something.
   */
  it('Reports a camera it may not see exactly as one that does not exist', () => {
    getCamera.mockReturnValue({ data: undefined, isLoading: false, error: { status: 404 } });
    const { container: refusedForAnotherFab } = renderAt('22222222-2222-2222-2222-222222222222');
    const crossFab = refusedForAnotherFab.innerHTML;

    getCamera.mockReturnValue({ data: undefined, isLoading: false, error: { status: 404 } });
    const { container: refusedForUnknown } = renderAt('33333333-3333-3333-3333-333333333333');

    expect(refusedForUnknown.innerHTML).toBe(crossFab);
    expect(screen.getAllByRole('heading', { name: /no such camera/i }).length).toBeGreaterThan(0);
  });

  it('Says nothing about access, ever', () => {
    getCamera.mockReturnValue({ data: undefined, isLoading: false, error: { status: 404 } });

    const { container } = renderAt('22222222-2222-2222-2222-222222222222');

    // The specific sentence that would undo spec 029's indistinguishability at
    // the last hop, and the one most likely to be added later as an
    // improvement.
    expect(container.textContent).not.toMatch(/access|permission|not yours|another fab/i);
  });
});
