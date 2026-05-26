import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { Layout } from '@smart-sentinel-eye/shared/api/layouts.api';
import { store } from '../../app/store.js';

const getLayoutMock = vi.fn();
const navigateMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useGetLayoutQuery: (...args: unknown[]) => getLayoutMock(...args),
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
    useParams: () => ({ layoutIdentifier: 'cam-1' }),
    useNavigate: () => navigateMock,
  };
});

vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: ({ cameraIdentifier }: { cameraIdentifier: string }) => (
    <div data-testid="camera-viewer">{cameraIdentifier}</div>
  ),
}));

const { CellPage } = await import('./CellPage.js');

function chain(overrides: Partial<Layout> = {}): Layout {
  return {
    layoutIdentifier: 'cam-1',
    name: 'Line-1',
    createdAt: '2026-05-26T10:00:00Z',
    createdBy: '00000000-0000-0000-0000-000000000001',
    revisions: [
      {
        revisionIdentifier: 'r1',
        revisionNumber: 1,
        state: 'Published',
        cameraIdentifier: 'cam-99',
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '00000000-0000-0000-0000-000000000001',
        publishedAt: '2026-05-26T10:00:00Z',
        archivedAt: null,
      },
    ],
    ...overrides,
  };
}

function renderPage() {
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <CellPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe('CellPage', () => {
  beforeEach(() => {
    getLayoutMock.mockReset();
    navigateMock.mockReset();
  });

  it('Renders the CameraViewer for the Published revision', () => {
    getLayoutMock.mockReturnValue({
      data: chain(),
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByTestId('camera-viewer')).toHaveTextContent('cam-99');
    expect(screen.getByRole('heading', { name: 'Line-1' })).toBeInTheDocument();
  });

  it('Falls back to the picker prompt when no Published revision exists', () => {
    getLayoutMock.mockReturnValue({
      data: chain({
        revisions: [
          {
            revisionIdentifier: 'r1',
            revisionNumber: 1,
            state: 'Draft',
            cameraIdentifier: 'cam-99',
            createdAt: '2026-05-26T10:00:00Z',
            createdBy: '00000000-0000-0000-0000-000000000001',
            publishedAt: null,
            archivedAt: null,
          },
        ],
      }),
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();
    expect(screen.getByText(/layout is no longer available/i)).toBeInTheDocument();
  });
});
