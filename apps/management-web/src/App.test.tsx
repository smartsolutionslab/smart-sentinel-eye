import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListCamerasQuery: () => ({
      data: { items: [], count: 0, offset: 0, limit: 50 },
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    }),
    useRegisterCameraMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, error: undefined }],
  };
});

const { App } = await import('./App.js');
const { store } = await import('./app/store.js');

describe('App shell', () => {
  it('Renders the Cameras page heading and the Register button', () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );
    expect(screen.getByRole('heading', { name: /cameras/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /register camera/i })).toBeInTheDocument();
  });
});
