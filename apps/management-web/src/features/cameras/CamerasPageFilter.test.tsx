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
 * Finding a camera by name on the list page (spec 055).
 *
 * <para>
 * <b>The two failures worth guarding are both silent.</b> A filter that leaves
 * the offset where it was shows an empty table for a search that matched
 * something; and an empty table that says "no cameras registered yet" tells an
 * operator the fab is empty when their search simply missed.
 * </para>
 */
describe('CamerasPage — finding a camera by name', () => {
  beforeEach(() => {
    listCamerasMock.mockReset();
    assignedGroups.current = ['/fabs/munich'];
  });

  function page(items: number, count: number, offset = 0): CameraListPage {
    return {
      items: Array.from({ length: items }, (_unused, index) => ({
        cameraIdentifier: `cam-${String(offset + index).padStart(4, '0')}`,
        version: 1,
        fab: 'munich',
        name: `Line ${offset + index} Furnace`,
        rtspUrl: 'rtsp://10.0.5.1/h264',
        registeredAt: '2026-05-24T10:00:00Z',
        status: 'Registered',
      })),
      count,
      offset,
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

  it('Sends what the operator typed as a name fragment', async () => {
    listCamerasMock.mockReturnValue({ data: page(2, 2), isLoading: false, isFetching: false });
    const user = userEvent.setup();
    renderPage();

    const field = screen.getByLabelText(/find a camera/i);
    await user.click(field);
    await user.paste('furn');

    await vi.waitFor(() => expect(listCamerasMock).toHaveBeenLastCalledWith(expect.objectContaining({ name: 'furn' })));
  });

  it('Sends no fragment at all when the box is cleared', async () => {
    listCamerasMock.mockReturnValue({ data: page(2, 2), isLoading: false, isFetching: false });
    const user = userEvent.setup();
    renderPage();

    const field = screen.getByLabelText(/find a camera/i);
    await user.click(field);
    await user.paste('furn');
    await user.clear(field);

    await vi.waitFor(() =>
      expect(listCamerasMock).toHaveBeenLastCalledWith(expect.objectContaining({ name: undefined })),
    );
  });

  /**
   * **The silent one.** Filtering from a later page without resetting the offset
   * asks for rows 100–149 of a two-row result: an empty table, and a footer
   * reading "Showing 0–0 of 2" for a search that matched.
   */
  it('Returns to the first page when the fragment changes', async () => {
    listCamerasMock.mockReturnValue({ data: page(50, 250, 50), isLoading: false, isFetching: false });
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /next/i }));
    expect(listCamerasMock).toHaveBeenLastCalledWith(expect.objectContaining({ offset: 50 }));

    const field = screen.getByLabelText(/find a camera/i);
    await user.click(field);
    await user.paste('furn');

    await vi.waitFor(() =>
      expect(listCamerasMock).toHaveBeenLastCalledWith(expect.objectContaining({ offset: 0, name: 'furn' })),
    );
  });

  /**
   * **A miss and an empty catalogue are different facts**, and an operator acts
   * differently on each: one goes looking for the right name, the other
   * registers a camera. Registering a duplicate is refused, so getting this
   * wrong strands them.
   */
  it('Says a search matched nothing, rather than that there are no cameras', async () => {
    listCamerasMock.mockReturnValue({ data: page(0, 0), isLoading: false, isFetching: false });
    const user = userEvent.setup();
    renderPage();

    expect(screen.getByText(/no cameras registered yet/i)).toBeInTheDocument();

    const field = screen.getByLabelText(/find a camera/i);
    await user.click(field);
    await user.paste('nothingmatchesthis');

    expect(await screen.findByText(/no camera matches/i)).toBeInTheDocument();
    expect(screen.queryByText(/no cameras registered yet/i)).not.toBeInTheDocument();
  });

  /**
   * The footer's total comes from the response, so a filtered page reports the
   * matches. Asserted here because the number is what an operator reads to
   * decide whether to keep looking.
   */
  it('Counts the matches in the footer, not the catalogue', async () => {
    listCamerasMock.mockReturnValue({ data: page(2, 2), isLoading: false, isFetching: false });
    const user = userEvent.setup();
    renderPage();

    const field = screen.getByLabelText(/find a camera/i);
    await user.click(field);
    await user.paste('furn');

    expect(await screen.findByText(/Showing 1–2 of 2/)).toBeInTheDocument();
  });
});
