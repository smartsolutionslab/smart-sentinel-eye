// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel } from '@smart-sentinel-eye/shared/api/overlays.api';
import { OverlayEditor } from './OverlayEditor';
import type { CameraChoicesResult } from './BackdropControls';

/**
 * Spec 484 T003. The operator's own **Cancel capture** press, through the real `OverlayEditor`.
 *
 * <p>
 * `OverlayEditorCapture.test.tsx` covers every other way a capture ends (a delivered frame, a failure, a camera change, the token
 * going away) but none presses the Cancel button itself. Cancelling must release the grabber, leave no failed alert (it is
 * `idle`, not `failed`), and let the operator capture again. Green on today's code.
 * </p>
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
    ],
    count: 1,
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

const renderFrameGrabber = vi.fn((props: { cameraIdentifier: string }) => (
  <div data-testid="grabber" data-camera={props.cameraIdentifier} />
));

function renderEditor() {
  return render(
    <OverlayEditor
      value={LABEL}
      onChange={vi.fn()}
      getToken={getToken}
      cameras={CAMERAS}
      renderFrameGrabber={renderFrameGrabber}
      initialCamera="cam-a"
    />,
  );
}

const captureButton = () => screen.getByRole('button', { name: /capture frame/i });
const cancelButton = () => screen.getByRole('button', { name: /cancel capture/i });

describe('OverlayEditor — the operator cancels a capture (spec 484)', () => {
  beforeEach(() => {
    renderFrameGrabber.mockClear();
  });

  afterEach(() => {
    cleanup();
  });

  it('releases the grabber and offers Capture again, with no failed alert', () => {
    renderEditor();
    fireEvent.click(captureButton());
    expect(screen.getByTestId('grabber').getAttribute('data-camera')).toBe('cam-a');

    fireEvent.click(cancelButton());

    expect(screen.queryByTestId('grabber'), 'the grabber is released').toBeNull();
    expect(screen.queryByRole('button', { name: /cancel capture/i }), 'the Cancel button goes away').toBeNull();
    expect(screen.queryByTestId('frame-capture-alert'), 'cancelling is idle, not failed').toBeNull();
    expect(captureButton().hasAttribute('disabled'), 'Capture is offered again').toBe(false);
  });

  it('lets the operator capture again from the same camera after cancelling', () => {
    renderEditor();
    fireEvent.click(captureButton());
    fireEvent.click(cancelButton());
    renderFrameGrabber.mockClear();

    fireEvent.click(captureButton());

    expect(screen.getByTestId('grabber').getAttribute('data-camera')).toBe('cam-a');
    expect(renderFrameGrabber).toHaveBeenCalled();
  });
});
