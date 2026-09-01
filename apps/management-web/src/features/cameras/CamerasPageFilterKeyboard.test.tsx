import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { store } from '../../app/store.js';
import type { CameraListPage } from '@smart-sentinel-eye/shared/api/cameras.api';

const assignedGroups = { current: ['/fabs/munich'] as string[] };

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { groups: assignedGroups.current } } }),
}));

const listCamerasMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListCamerasQuery: (...args: unknown[]) => listCamerasMock(...args),
    useRegisterCameraMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, reset: vi.fn() }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return { ...actual, useListStreamsQuery: () => ({ data: [] }) };
});

const { CamerasPage } = await import('./CamerasPage.js');

/**
 * The keyboard path (spec 055 US3).
 *
 * <para>
 * <b>The check most likely to be skipped, and the reason is not laziness.</b>
 * The feature looks finished whether or not this works, because it is built
 * with a pointer in hand — so the only thing that catches a chooser reachable
 * only by mouse is a test that never uses one.
 * </para>
 *
 * <para>
 * Nothing here asserts the filter is <i>pleasant</i> to drive by keyboard, only
 * that it is possible. That is the honest limit of an automated check on this.
 * </para>
 */
describe('CamerasPage — the filter without a pointer', () => {
  beforeEach(() => {
    listCamerasMock.mockReset();
    assignedGroups.current = ['/fabs/munich'];
  });

  function page(items: number, count: number): CameraListPage {
    return {
      items: Array.from({ length: items }, (_unused, index) => ({
        cameraIdentifier: `cam-${String(index).padStart(4, '0')}`,
        version: 1,
        fab: 'munich',
        name: `Line ${index} Furnace`,
        rtspUrl: 'rtsp://10.0.5.1/h264',
        registeredAt: '2026-05-24T10:00:00Z',
        status: 'Registered',
      })),
      count,
      offset: 0,
      limit: 50,
    };
  }

  function renderPage() {
    return render(
      <Provider store={store}>
        <MemoryRouter>
          <CamerasPage />
        </MemoryRouter>
      </Provider>,
    );
  }

  /**
   * **Tab to it and type — no click anywhere.** A field reachable only by
   * pointer is one an operator working by keyboard cannot use at all, and
   * nothing else in the suite would notice.
   */
  it('Is reachable and usable with the keyboard alone', async () => {
    listCamerasMock.mockReturnValue({ data: page(2, 2), isLoading: false, isFetching: false });
    const user = userEvent.setup();
    renderPage();

    const field = screen.getByLabelText(/find a camera/i);

    await user.tab();
    while (document.activeElement !== field) {
      await user.tab();
    }

    await user.keyboard('furn');

    await vi.waitFor(() =>
      expect(listCamerasMock).toHaveBeenLastCalledWith(expect.objectContaining({ name: 'furn' })),
    );
  });

  /**
   * The field carries a real label, not a placeholder standing in for one. A
   * placeholder disappears the moment anything is typed and is not reliably
   * announced — so a control labelled only that way is unnamed to a screen
   * reader by the time it matters.
   */
  it('Has a label rather than only a placeholder', () => {
    listCamerasMock.mockReturnValue({ data: page(2, 2), isLoading: false, isFetching: false });
    renderPage();

    const field = screen.getByLabelText(/find a camera/i);

    expect(field.tagName).toBe('INPUT');
    expect(field.getAttribute('id')).not.toBeNull();
  });

  /**
   * The table keeps its own semantics under a filter. The filter narrows rows;
   * it does not turn the table into something else, and an operator navigating
   * by table structure must still find one.
   */
  it('Leaves the table a table while filtering', async () => {
    listCamerasMock.mockReturnValue({ data: page(2, 2), isLoading: false, isFetching: false });
    const user = userEvent.setup();
    renderPage();

    const field = screen.getByLabelText(/find a camera/i);
    await user.click(field);
    await user.paste('furn');

    expect(await screen.findByRole('table', { name: /registered cameras/i })).toBeInTheDocument();
  });
});
