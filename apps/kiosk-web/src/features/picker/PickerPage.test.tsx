import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { ListLayoutsResponse } from '@smart-sentinel-eye/shared/api/layouts.api';
import { store } from '../../app/store.js';

const listLayoutsMock = vi.fn();
const navigateMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: (...args: unknown[]) => listLayoutsMock(...args),
  };
});

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    user: { access_token: 'fake-token' },
  }),
}));

vi.mock('@smart-sentinel-eye/shared/realtime/layoutHub', () => ({
  createLayoutHubClient: () => ({
    start: () => Promise.resolve(),
    stop: () => Promise.resolve(),
    state: () => 'Disconnected',
  }),
}));

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

const { PickerPage } = await import('./PickerPage.js');

function renderPage() {
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <PickerPage />
      </MemoryRouter>
    </Provider>,
  );
}

function published(overrides: Partial<ListLayoutsResponse['published'][number]> = {}) {
  return {
    layoutIdentifier: '11111111-1111-1111-1111-111111111111',
    name: 'Line-1',
    revisionNumber: 1,
    cameraIdentifier: '22222222-2222-2222-2222-222222222222',
    overlayIdentifier: null,
    publishedAt: '2026-05-26T10:00:00Z',
    ...overrides,
  };
}

describe('PickerPage', () => {
  beforeEach(() => {
    listLayoutsMock.mockReset();
    navigateMock.mockReset();
  });

  it('Shows an empty-state message when no Published layouts exist', () => {
    listLayoutsMock.mockReturnValue({
      data: { chains: [], published: [] },
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByText(/no layouts published yet/i)).toBeInTheDocument();
  });

  it('Renders one card per Published layout', () => {
    listLayoutsMock.mockReturnValue({
      data: { chains: [], published: [published(), published({ name: 'Line-2', layoutIdentifier: 'b' })] },
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByText('Line-1')).toBeInTheDocument();
    expect(screen.getByText('Line-2')).toBeInTheDocument();
  });

  it('Navigates to the cell view when a card is tapped', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({
      data: { chains: [], published: [published()] },
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    await user.click(screen.getByRole('button', { name: /line-1/i }));
    expect(navigateMock).toHaveBeenCalledWith('/layouts/11111111-1111-1111-1111-111111111111');
  });

  it('Surfaces a retry control when the list query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    listLayoutsMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: { status: 500 },
      refetch,
    });

    renderPage();
    await user.click(screen.getByRole('button', { name: /retry/i }));
    expect(refetch).toHaveBeenCalledOnce();
  });
});
