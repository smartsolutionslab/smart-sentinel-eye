import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { store } from '../../app/store.js';

/**
 * Spec 274 T2f-baseline: `WallsPage.tsx` has no covering test today
 * (confirmed absent — spec 274 §1.2). This characterises the page's current,
 * unchanged behaviour before T2f swaps its inline banner for `RetryBanner`;
 * this case must keep passing, unmodified, after that swap.
 */

const listWallsMock = vi.fn();
const listLayoutsMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/walls.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/walls.api')>();
  return {
    ...actual,
    useListWallsQuery: (...args: unknown[]) => listWallsMock(...args),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: (...args: unknown[]) => listLayoutsMock(...args),
  };
});

const { WallsPage } = await import('./WallsPage.js');

function renderPage() {
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <WallsPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe('WallsPage', () => {
  beforeEach(() => {
    listWallsMock.mockReset();
    listLayoutsMock.mockReset();
    listLayoutsMock.mockReturnValue({
      data: { chains: [], published: [] },
      isLoading: false,
    });
  });

  it('Shows a retry control when the list query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    listWallsMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch,
    });

    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Could not load walls.');

    const retry = screen.getByRole('button', { name: /retry/i });
    await user.click(retry);
    expect(refetch).toHaveBeenCalledOnce();
  });
});
