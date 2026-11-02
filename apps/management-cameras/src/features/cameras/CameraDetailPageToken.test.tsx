import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import { Provider } from 'react-redux';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { createApiStore } from '@smart-sentinel-eye/shared/store';

/**
 * Spec 481 T003. What the page's `getToken` resolves when the operator holds no
 * token. `CameraDetailPage.test` pins the identity across a renew and the renewed
 * value; nothing asked about a missing token, so `null` could become `''` or
 * `undefined` unnoticed, and `CameraViewer` passes that value to its WHEP
 * session as the bearer. Green on today's code.
 */

const token = vi.hoisted(() => ({ value: undefined as string | undefined }));

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: token.value === undefined ? undefined : { access_token: token.value } }),
}));

const viewerRenders = vi.hoisted(() => [] as { getToken: () => Promise<string | null> }[]);

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useGetCameraQuery: () => ({ data: camera, isLoading: false, error: undefined }),
  };
});

vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: (props: { getToken: () => Promise<string | null> }) => {
    viewerRenders.push(props);
    return <div data-testid="camera-viewer" />;
  },
}));

const { CameraDetailPage } = await import('./CameraDetailPage.js');
const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { streamsApi } = await import('@smart-sentinel-eye/shared/api/streams.api');
const store = createApiStore([camerasApi, streamsApi]);

const camera = {
  cameraIdentifier: '11111111-1111-1111-1111-111111111111',
  version: 7,
  fab: 'munich',
  name: 'Line-1-Entrance',
  rtspUrl: 'rtsp://10.0.5.12/h264',
  registeredAt: '2026-05-24T10:00:00Z',
  status: 'Registered',
};

function renderPage() {
  return render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/cameras/${camera.cameraIdentifier}`]}>
        <Routes>
          <Route path="/cameras/:cameraIdentifier" element={<CameraDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

describe('CameraDetailPage — getToken when the operator holds no token', () => {
  beforeEach(() => {
    viewerRenders.length = 0;
  });

  it('resolves null, as a promise, not an empty string or undefined', async () => {
    token.value = undefined;

    renderPage();

    const result = viewerRenders.at(0)!.getToken();
    expect(result).toBeInstanceOf(Promise);
    await expect(result).resolves.toBeNull();
  });
});
