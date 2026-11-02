// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import { OverlayEditor } from './OverlayEditor.js';
import type { CameraChoicesResult } from './BackdropControls.js';

/**
 * Characterisation gap-fill for the capture-frame wiring of `OverlayEditor`
 * (spec 478, T003): which camera a capture starts on, what ends one, and when
 * the caller's frame grabber is mounted. `OverlayEditorBackdrop.test.tsx`
 * covers one complete successful capture; these cover the seams around it.
 *
 * The caller-supplied `renderFrameGrabber` is a stand-in that exposes the
 * three things `OverlayEditor` hands it, so no WHEP session is needed: the
 * editor only decides when the grabber exists and which camera it targets.
 */

const CAMERAS = {
  data: {
    items: [
      {
        cameraIdentifier: 'cam-a',
        version: 1,
        fab: 'munich',
        name: 'Line-1 Inlet',
        rtspUrl: 'rtsp://a',
        registeredAt: '2026-01-01T00:00:00Z',
        status: 'Registered',
      },
      {
        cameraIdentifier: 'cam-b',
        version: 1,
        fab: 'munich',
        name: 'Line-2 Outlet',
        rtspUrl: 'rtsp://b',
        registeredAt: '2026-01-01T00:00:00Z',
        status: 'Registered',
      },
    ],
    count: 2,
    complete: true,
  },
  isLoading: false,
  isError: false,
} as unknown as CameraChoicesResult;

const LABEL: OverlayLabel = {
  text: 'Furnace',
  normalizedX: 0.1,
  normalizedY: 0.1,
  normalizedWidth: 0.3,
  normalizedHeight: 0.08,
  fontSizePx: 32,
};

const getToken = async () => 'token';

const renderFrameGrabber = vi.fn(
  (props: { cameraIdentifier: string; onCaptured: (dataUrl: string) => void; onFailed: () => void }) => (
    <div data-testid="grabber" data-camera={props.cameraIdentifier}>
      <button type="button" onClick={() => props.onCaptured('data:image/png;base64,STUB')}>
        deliver frame
      </button>
      <button type="button" onClick={props.onFailed}>
        deliver failure
      </button>
    </div>
  ),
);

function renderEditor(overrides: { initialCamera?: string; withToken?: boolean } = {}) {
  const element = (withToken: boolean) => (
    <OverlayEditor
      value={LABEL}
      onChange={vi.fn()}
      getToken={withToken ? getToken : undefined}
      cameras={CAMERAS}
      renderFrameGrabber={renderFrameGrabber}
      initialCamera={overrides.initialCamera}
    />
  );
  const view = render(element(overrides.withToken ?? true));
  return { ...view, rerenderWithoutToken: () => view.rerender(element(false)) };
}

function cameraSelect(): HTMLElement & { value: string } {
  return screen.getByRole('combobox', { name: /camera/i }) as HTMLElement & { value: string };
}

function startCapture(camera: string): void {
  fireEvent.change(cameraSelect(), { target: { value: camera } });
  fireEvent.click(screen.getByRole('button', { name: /capture frame/i }));
}

describe('OverlayEditor frame capture wiring (spec 478 gap-fill)', () => {
  beforeEach(() => {
    renderFrameGrabber.mockClear();
  });

  afterEach(() => {
    cleanup();
  });

  it('Starts the picker on the camera the host passed as initialCamera and captures from it', () => {
    renderEditor({ initialCamera: 'cam-b' });

    expect(cameraSelect().value).toBe('cam-b');

    fireEvent.click(screen.getByRole('button', { name: /capture frame/i }));

    expect(screen.getByTestId('grabber').getAttribute('data-camera')).toBe('cam-b');
  });

  it('Starts no capture and mounts no grabber while no camera is chosen', () => {
    renderEditor();

    fireEvent.click(screen.getByRole('button', { name: /capture frame/i }));

    expect(renderFrameGrabber).not.toHaveBeenCalled();
    expect(screen.queryByTestId('grabber')).toBeNull();
    expect(screen.queryByRole('button', { name: /cancel capture/i })).toBeNull();
  });

  it('Releases the grabber when the camera is changed mid-capture', () => {
    renderEditor();
    startCapture('cam-a');
    expect(screen.getByTestId('grabber').getAttribute('data-camera')).toBe('cam-a');

    fireEvent.change(cameraSelect(), { target: { value: 'cam-b' } });

    expect(screen.queryByTestId('grabber')).toBeNull();
    expect(screen.queryByRole('button', { name: /cancel capture/i })).toBeNull();
  });

  it('Clears a failed-capture alert when a different camera is picked', () => {
    renderEditor();
    startCapture('cam-a');
    fireEvent.click(screen.getByRole('button', { name: 'deliver failure' }));
    expect(screen.getByTestId('frame-capture-alert')).not.toBeNull();

    fireEvent.change(cameraSelect(), { target: { value: 'cam-b' } });

    expect(screen.queryByTestId('frame-capture-alert')).toBeNull();
  });

  it('Releases the grabber once a frame has been delivered', () => {
    renderEditor();
    startCapture('cam-a');

    fireEvent.click(screen.getByRole('button', { name: 'deliver frame' }));

    expect(screen.queryByTestId('grabber')).toBeNull();
    expect(screen.queryByRole('button', { name: /cancel capture/i })).toBeNull();
  });

  it('Releases the grabber if the token source goes away mid-capture', () => {
    const { rerenderWithoutToken } = renderEditor();
    startCapture('cam-a');
    expect(screen.getByTestId('grabber')).not.toBeNull();

    rerenderWithoutToken();

    expect(screen.queryByTestId('grabber')).toBeNull();
  });
});
