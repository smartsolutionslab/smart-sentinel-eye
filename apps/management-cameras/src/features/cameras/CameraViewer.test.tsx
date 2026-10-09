import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createApiStore } from '@smart-sentinel-eye/shared/store';

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    // No stream data → no WhepClient is constructed; the overlay branch is
    // independent of the streaming path and is what this test is about.
    useGetStreamQuery: () => ({
      data: undefined,
      isLoading: false,
      error: undefined,
    }),
  };
});

const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { streamsApi } = await import('@smart-sentinel-eye/shared/api/streams.api');
// Spec 316 T009: see CamerasPage.test.tsx's own comment.
const store = createApiStore([camerasApi, streamsApi]);
const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

function renderViewer(overlay?: {
  kind: 'Text';
  color: string;
  text: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
  fontSizePx: number;
}) {
  return render(
    <Provider store={store}>
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={async () => null}
        overlays={overlay === undefined ? undefined : [overlay]}
      />
    </Provider>,
  );
}

describe('CameraViewer overlay rendering', () => {
  it('Renders the overlay label when the overlay prop is set', () => {
    renderViewer({
      kind: 'Text',
      color: '#FFFFFFD9',
      text: 'Production Line 1',
      normalizedX: 0.5,
      normalizedY: 0.05,
      normalizedWidth: 0.3,
      normalizedHeight: 0.08,
      fontSizePx: 48,
    });

    const label = screen.getByTestId('camera-viewer-overlay-label');
    expect(label).toBeInTheDocument();
    expect(label).toHaveTextContent('Production Line 1');
    expect(label.style.left).toBe('50%');
    expect(label.style.top).toBe('5%');
  });

  it('Renders no overlay label when the overlay prop is absent', () => {
    renderViewer();
    expect(screen.queryByTestId('camera-viewer-overlay-label')).toBeNull();
  });
});
