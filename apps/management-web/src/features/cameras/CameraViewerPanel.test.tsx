import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const getStreamMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: (...args: unknown[]) => getStreamMock(...args),
  };
});

// CameraViewer mounts a WhepClient that talks to RTCPeerConnection. In the
// jsdom test environment those globals don't exist; stubbing the composite
// is the cleanest way to assert the panel's wiring without simulating
// WebRTC.
vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: ({ cameraIdentifier }: { cameraIdentifier: string }) => (
    <div data-testid="camera-viewer">viewer:{cameraIdentifier}</div>
  ),
}));

const { CameraViewerPanel } = await import('./CameraViewerPanel.js');

describe('CameraViewerPanel', () => {
  beforeEach(() => {
    getStreamMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: undefined,
    });
  });

  it('Renders nothing when no camera is selected', () => {
    const { container } = render(
      <Provider store={store}>
        <CameraViewerPanel cameraIdentifier={null} cameraName={null} onClose={() => {}} />
      </Provider>,
    );

    expect(container).toBeEmptyDOMElement();
  });

  it('Mounts the viewer for the selected camera', () => {
    render(
      <Provider store={store}>
        <CameraViewerPanel
          cameraIdentifier="cam-42"
          cameraName="Line-7"
          onClose={() => {}}
        />
      </Provider>,
    );

    expect(screen.getByRole('dialog', { name: /line-7/i })).toBeInTheDocument();
    expect(screen.getByTestId('camera-viewer').textContent).toContain('cam-42');
  });

  it('Invokes onClose when the Close button is clicked', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <Provider store={store}>
        <CameraViewerPanel
          cameraIdentifier="cam-42"
          cameraName="Line-7"
          onClose={onClose}
        />
      </Provider>,
    );

    await user.click(screen.getByRole('button', { name: /close/i }));

    expect(onClose).toHaveBeenCalledOnce();
  });
});
