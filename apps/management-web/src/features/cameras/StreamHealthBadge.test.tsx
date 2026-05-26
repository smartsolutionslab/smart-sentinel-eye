import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';

const getStreamMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: (...args: unknown[]) => getStreamMock(...args),
  };
});

const { StreamHealthBadge } = await import('./StreamHealthBadge.js');

function streamWith(overrides: Partial<StreamHealth> = {}): StreamHealth {
  return {
    cameraIdentifier: 'cam-1',
    state: 'Healthy',
    whepUrl: 'http://mediamtx/cam-1/whep',
    transcodeMode: 'Passthrough',
    lastSuccessAt: '2026-05-26T10:00:00Z',
    error: null,
    ...overrides,
  };
}

describe('StreamHealthBadge', () => {
  beforeEach(() => {
    getStreamMock.mockReset();
  });

  it.each([
    ['Healthy', 'Healthy'],
    ['Degraded', 'Degraded'],
    ['Offline', 'Offline'],
    ['Provisioning', 'Provisioning'],
  ] as const)('Renders the %s state pill', (state, label) => {
    getStreamMock.mockReturnValue({
      data: streamWith({ state }),
      isLoading: false,
      error: undefined,
    });

    render(
      <Provider store={store}>
        <StreamHealthBadge cameraIdentifier="cam-1" />
      </Provider>,
    );

    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it("Surfaces 'Unknown' when the stream query errors", () => {
    getStreamMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: { status: 500 },
    });

    render(
      <Provider store={store}>
        <StreamHealthBadge cameraIdentifier="cam-1" />
      </Provider>,
    );

    expect(screen.getByText('Unknown')).toBeInTheDocument();
  });

  it('Carries the error text in the tooltip when degraded', () => {
    getStreamMock.mockReturnValue({
      data: streamWith({ state: 'Degraded', error: 'source unreachable' }),
      isLoading: false,
      error: undefined,
    });

    render(
      <Provider store={store}>
        <StreamHealthBadge cameraIdentifier="cam-1" />
      </Provider>,
    );

    expect(screen.getByText('Degraded').getAttribute('title')).toContain('source unreachable');
  });
});
