import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { CameraListPage } from '@smart-sentinel-eye/shared/api/cameras.api';

const listCamerasMock = vi.fn();
const registerCameraMock = vi.fn(async () => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListCamerasQuery: (...args: unknown[]) => listCamerasMock(...args),
    useRegisterCameraMutation: () => [registerCameraMock, { isLoading: false, error: undefined, reset: vi.fn() }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: () => ({ data: undefined, isLoading: false, error: undefined }),
    useListStreamsQuery: () => ({ data: [], isLoading: false, error: undefined }),
  };
});

const { CamerasPage } = await import('./CamerasPage.js');

function emptyPage(): CameraListPage {
  return { items: [], count: 0, offset: 0, limit: 50 };
}

function populatedPage(): CameraListPage {
  return {
    items: [
      {
        cameraIdentifier: '11111111-1111-1111-1111-111111111111',
        name: 'Line-1-Entrance',
        rtspUrl: 'rtsp://10.0.5.12/h264',
        registeredAt: '2026-05-24T10:00:00Z',
      },
      {
        cameraIdentifier: '22222222-2222-2222-2222-222222222222',
        name: 'Line-2-East',
        rtspUrl: 'rtsp://10.0.5.22/h264',
        registeredAt: '2026-05-23T10:00:00Z',
      },
    ],
    count: 2,
    offset: 0,
    limit: 50,
  };
}

function render_page() {
  return render(
    <Provider store={store}>
      <CamerasPage />
    </Provider>,
  );
}

describe('CamerasPage', () => {
  beforeEach(() => {
    listCamerasMock.mockReset();
    registerCameraMock.mockClear();
  });

  it('Shows the empty-state message when no cameras are registered', () => {
    listCamerasMock.mockReturnValue({
      data: emptyPage(),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();

    expect(screen.getByText(/no cameras registered yet/i)).toBeInTheDocument();
    expect(screen.getByText(/no cameras$/i)).toBeInTheDocument();
  });

  it('Renders each camera row with name and RTSP URL', () => {
    listCamerasMock.mockReturnValue({
      data: populatedPage(),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();

    expect(screen.getByText('Line-1-Entrance')).toBeInTheDocument();
    expect(screen.getByText('Line-2-East')).toBeInTheDocument();
    expect(screen.getByText('rtsp://10.0.5.12/h264')).toBeInTheDocument();
    expect(screen.getByText(/showing 1.2 of 2/i)).toBeInTheDocument();
  });

  it('Sends an updated sort field when the user clicks a sortable header', async () => {
    const user = userEvent.setup();
    listCamerasMock.mockReturnValue({
      data: populatedPage(),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();

    await user.click(screen.getByRole('button', { name: /name/i }));

    const lastCall = listCamerasMock.mock.calls.at(-1);
    expect(lastCall?.[0]).toMatchObject({ sort: 'name', order: 'asc', offset: 0 });
  });

  it('Disables the Previous button while the first page is showing', () => {
    listCamerasMock.mockReturnValue({
      data: populatedPage(),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();

    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();
  });

  it('Shows a retry control when the list query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    listCamerasMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch,
    });

    render_page();

    const retry = screen.getByRole('button', { name: /retry/i });
    await user.click(retry);
    expect(refetch).toHaveBeenCalledOnce();
  });
});
