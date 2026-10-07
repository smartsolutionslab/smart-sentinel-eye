import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { store } from '../../app/store.js';
import type { CameraListPage } from '@smart-sentinel-eye/shared/api/cameras.api';

// The page renders RegisterCameraDialog, which reads the operator's fabs from
// the OIDC groups claim. A single-fab default keeps the existing cases reading
// as before; the multi-fab case overrides it.
const assignedGroups = { current: ['/fabs/munich'] as string[] };

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: assignedGroups.current } } }),
}));

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
        version: 1,
        fab: 'munich',
        name: 'Line-1-Entrance',
        rtspUrl: 'rtsp://10.0.5.12/h264',
        registeredAt: '2026-05-24T10:00:00Z',
        status: 'Registered',
      },
      {
        cameraIdentifier: '22222222-2222-2222-2222-222222222222',
        version: 3,
        fab: 'dresden',
        name: 'Line-2-East',
        rtspUrl: 'rtsp://10.0.5.22/h264',
        registeredAt: '2026-05-23T10:00:00Z',
        status: 'Registered',
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
      {/* Router context: each row's name is now a link into that camera
          (spec 030 FR-001), and a Link outside a router throws. */}
      <MemoryRouter>
        <CamerasPage />
      </MemoryRouter>
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

  /**
   * Spec 273 (#2632) / ADR-0151 T004 — **declared rewrite**, not an edit to
   * evade. `toBeDisabled()` reads native `disabled` only; once Previous
   * announces unavailability through `Button`'s `unavailable` prop instead
   * (FR-001), this assertion's *mechanism* moves. The claim it pins —
   * Previous is unavailable on page 1 — is unchanged, and it is RED on
   * unmodified `develop`: today `aria-disabled` is absent (native `disabled`
   * is what applies), so neither half of this assertion holds yet.
   */
  it('Marks the Previous button unavailable, not natively disabled, while the first page is showing', () => {
    listCamerasMock.mockReturnValue({
      data: populatedPage(),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();

    const previous = screen.getByRole('button', { name: /previous/i });
    expect(previous).toHaveAttribute('aria-disabled', 'true');
    expect(previous).not.toHaveAttribute('disabled');
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

/**
 * Spec 273 (#2632) / ADR-0151 — C1/C2, new behaviour, RED. `CamerasPage`
 * still passes native `disabled={offset === 0 || isFetching}` to Previous
 * (`:177`) and `disabled={offset + items.length >= totalCount || isFetching}`
 * to Next (`:184`). Without C2's guard a click during `isFetching` queues a
 * second page move (plan.md §3.5), which the guard test below pins by
 * asserting the query is never asked for a second page while fetching.
 */
describe('CamerasPage — Previous and Next keep focus while unavailable (spec 273 C1/C2)', () => {
  beforeEach(() => {
    listCamerasMock.mockReset();
  });

  it('Sends no page change from Previous while on the first page', () => {
    listCamerasMock.mockReturnValue({
      data: populatedPage(),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();
    fireEvent.click(screen.getByRole('button', { name: /previous/i }));

    const lastCall = listCamerasMock.mock.calls.at(-1);
    expect(lastCall?.[0]).toMatchObject({ offset: 0 });
  });

  /**
   * Spec 273 (#2632) phase-6 remediation: at offset 0 the unguarded handler's
   * own `Math.max(0, 0 - PAGE_SIZE)` already clamps to 0, so the test above
   * cannot tell a present guard from an absent one. This one reaches offset
   * 50 first, then flips `isFetching` mid-catalogue and proves Previous's
   * `isFetching` guard — not the clamping math — is what refuses the click.
   */
  it('Sends no page change from Previous while a page is already in flight, mid-catalogue', () => {
    let fetching = false;
    listCamerasMock.mockImplementation(() => ({
      data: { ...populatedPage(), count: 100 },
      isLoading: false,
      isFetching: fetching,
      error: undefined,
      refetch: vi.fn(),
    }));

    const { rerender } = render_page();

    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));
    let lastCall = listCamerasMock.mock.calls.at(-1);
    expect(lastCall?.[0]).toMatchObject({ offset: 50 });

    fetching = true;
    rerender(
      <Provider store={store}>
        <MemoryRouter>
          <CamerasPage />
        </MemoryRouter>
      </Provider>,
    );

    fireEvent.click(screen.getByRole('button', { name: /previous/i }));

    lastCall = listCamerasMock.mock.calls.at(-1);
    expect(lastCall?.[0]).toMatchObject({ offset: 50 });
  });

  it('Marks Next unavailable, not natively disabled, on the last page', () => {
    listCamerasMock.mockReturnValue({
      data: populatedPage(),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();

    const next = screen.getByRole('button', { name: /^next$/i });
    expect(next).toHaveAttribute('aria-disabled', 'true');
    expect(next).not.toHaveAttribute('disabled');
  });

  it('Sends no page change from Next on the last page', () => {
    listCamerasMock.mockReturnValue({
      data: populatedPage(),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();
    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));

    const lastCall = listCamerasMock.mock.calls.at(-1);
    expect(lastCall?.[0]).toMatchObject({ offset: 0 });
  });

  it('Marks Next unavailable, not natively disabled, while a page is in flight, even mid-catalogue', () => {
    // count (100) is well beyond items.length (2): the boundary condition
    // alone is false here, so `isFetching` is the only reason Next disables.
    listCamerasMock.mockReturnValue({
      data: { ...populatedPage(), count: 100 },
      isLoading: false,
      isFetching: true,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();

    const next = screen.getByRole('button', { name: /^next$/i });
    expect(next).toHaveAttribute('aria-disabled', 'true');
    expect(next).not.toHaveAttribute('disabled');
  });

  it('Sends no second page request from Next while a page is already in flight', () => {
    listCamerasMock.mockReturnValue({
      data: { ...populatedPage(), count: 100 },
      isLoading: false,
      isFetching: true,
      error: undefined,
      refetch: vi.fn(),
    });

    render_page();
    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));

    const lastCall = listCamerasMock.mock.calls.at(-1);
    expect(lastCall?.[0]).toMatchObject({ offset: 0 });
  });
});

/**
 * Spec 310 (#2725) T004, plan.md §5 row 4. Three consecutive 403 refreshes of
 * the same list query must drop the stale rows — the page renders exactly as
 * a first-load refusal does (FR-004), not merely "fewer rows than before".
 *
 * RED today: `CamerasPage.tsx` wires no revocation fallback, so `data` keeps
 * the last successful page regardless of how many 403s accumulate — the
 * stale rows stay on screen beside the "Could not load cameras." banner
 * after the third one, instead of disappearing with it.
 */
describe('CamerasPage — revocation fallback, three consecutive 403s (spec 310 #2725)', () => {
  beforeEach(() => {
    listCamerasMock.mockReset();
  });

  function forbiddenRefresh(requestId: string) {
    return {
      data: populatedPage(),
      isLoading: false,
      isFetching: false,
      error: { status: 403 },
      requestId,
      refetch: vi.fn(),
    };
  }

  it('Keeps the stale rows after only two consecutive 403s', () => {
    listCamerasMock.mockReturnValue(forbiddenRefresh('cameras-r1'));
    const { rerender } = render_page();

    listCamerasMock.mockReturnValue(forbiddenRefresh('cameras-r2'));
    rerender(
      <Provider store={store}>
        <MemoryRouter>
          <CamerasPage />
        </MemoryRouter>
      </Provider>,
    );

    expect(screen.getByText('Line-1-Entrance')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load cameras/i);
  });

  it('Shows no stale row and the existing failure banner, exactly as a first-load refusal, after a third consecutive 403', () => {
    listCamerasMock.mockReturnValue(forbiddenRefresh('cameras-r1'));
    const { rerender, container } = render_page();

    listCamerasMock.mockReturnValue(forbiddenRefresh('cameras-r2'));
    rerender(
      <Provider store={store}>
        <MemoryRouter>
          <CamerasPage />
        </MemoryRouter>
      </Provider>,
    );

    listCamerasMock.mockReturnValue(forbiddenRefresh('cameras-r3'));
    rerender(
      <Provider store={store}>
        <MemoryRouter>
          <CamerasPage />
        </MemoryRouter>
      </Provider>,
    );

    expect(screen.queryByText('Line-1-Entrance')).toBeNull();
    expect(screen.queryByText('Line-2-East')).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load cameras/i);

    // FR-004: byte-for-byte the same render as a first-load failure.
    listCamerasMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch: vi.fn(),
    });
    const { container: firstLoadRefusal } = render_page();

    expect(container.innerHTML).toBe(firstLoadRefusal.innerHTML);
  });
});
