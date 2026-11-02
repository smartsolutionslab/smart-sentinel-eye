// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import type { CameraViewerOverlay, CameraViewerProps } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

/**
 * Spec 479 (FR-003). **What the tile paints: the status text and the overlay
 * elements.**
 *
 * <p>
 * Characterisation of presentation the covering files leave unobserved,
 * written before `CameraViewer` is split: the label for each status the
 * tile can sit in (including the two they never reach — `idle` and
 * `Provisioning`), the rule that a read failure never overrides `offline`,
 * the absence of the status overlay while live, and the geometry,
 * pointer-events and identity of the overlay elements. Every assertion is on
 * the rendered DOM.
 * </p>
 *
 * <p>
 * Lives in its own file: the ten covering `CameraViewer*.test.tsx` files must
 * pass unmodified through the split.
 * </p>
 */

interface WhepClientDoubleOptions {
  onConnectionStateChange?: (state: string) => void;
}

const sessions: WhepClientDoubleOptions[] = [];

// When false, `connect()` leaves the tile on `connecting`.
let autoConnect = true;

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    // Must report `connected` for the live case, or the tile never leaves
    // `connecting` and "no status overlay while live" is vacuous — the warning
    // CameraViewerAlignment.test.tsx carries, found the hard way there.
    constructor(private readonly options: WhepClientDoubleOptions) {
      sessions.push(options);
    }

    async connect(videoEl: HTMLVideoElement) {
      videoEl.dataset['connected'] = 'true';
      if (autoConnect) this.options.onConnectionStateChange?.('connected');
    }
    close() {}
    stats = () => Promise.resolve(new Map<string, unknown>());
    setPlayoutTarget = () => 'applied' as const;
  },
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

const STREAM: StreamHealth = {
  cameraIdentifier: 'cam-42',
  state: 'Healthy',
  whepUrl: 'http://sfu/whep/cam-42',
  transcodeMode: 'Passthrough',
  lastSuccessAt: null,
  error: null,
};

function viewer(props: Partial<CameraViewerProps> = {}) {
  return (
    <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} stream={STREAM} {...props} />
  );
}

const driveFakes = () =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });

const statusRegion = () => screen.getByTestId('camera-viewer-status');

describe('CameraViewer presentation (FR-003)', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    sessions.length = 0;
    autoConnect = true;
    vi.spyOn(console, 'info').mockImplementation(() => {});
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  describe('the status text', () => {
    it('Reads Idle for a tile that has no stream and no read error', async () => {
      const { container } = render(viewer({ stream: undefined }));
      await driveFakes();

      expect(screen.getByText('Idle')).toBeVisible();
      expect(container.querySelector('[aria-hidden="true"]')?.textContent).toBe('Idle');
      expect(statusRegion().textContent, 'a tile that was born idle announces nothing').toBe('');
    });

    it('Reads Idle for a tile whose stream carries no whepUrl and is not provisioning', async () => {
      render(viewer({ stream: { ...STREAM, whepUrl: '' } }));
      await driveFakes();

      expect(screen.getByText('Idle')).toBeVisible();
      expect(sessions, 'no session without a url').toHaveLength(0);
    });

    it('Reads Provisioning stream… while the stream is being provisioned and no session exists yet', async () => {
      render(viewer({ stream: { ...STREAM, state: 'Provisioning', whepUrl: '' } }));
      await driveFakes();

      expect(sessions).toHaveLength(0);
      expect(screen.getByText('Provisioning stream…')).toBeVisible();
      expect(statusRegion().textContent, 'idle is never announced, whatever the overlay paints').toBe('');
    });

    it('Reads Stream is offline, not Viewer error, when a read fails after the tile went offline', async () => {
      const { rerender } = render(viewer({ stream: { ...STREAM, state: 'Offline', error: 'Source powered down.' } }));
      await driveFakes();
      expect(screen.getByText('Stream is offline')).toBeVisible();

      // The next poll fails outright: no stream for this camera, an error
      // instead. `offline` already has its own read of the stream state, so the
      // failed read must not overwrite it.
      rerender(viewer({ stream: undefined, queryError: new Error('network down') }));
      await driveFakes();

      expect(screen.getByText('Stream is offline')).toBeVisible();
      expect(screen.queryByText('Viewer error')).toBeNull();
      expect(statusRegion().textContent).toBe('Stream is offline. Source powered down.');
    });

    it('Reads Viewer error for a read that fails when the tile was not offline', async () => {
      render(viewer({ stream: undefined, queryError: new Error('network down') }));
      await driveFakes();

      expect(screen.getByText('Viewer error')).toBeVisible();
      expect(screen.queryByText('Idle')).toBeNull();
    });

    it('Paints no status overlay while live, and announces nothing', async () => {
      const { container } = render(viewer());
      await driveFakes();

      expect(sessions).toHaveLength(1);
      expect(statusRegion().textContent).toBe('');
      expect(screen.queryByText('Live')).toBeNull();
      expect(container.querySelector('[aria-hidden="true"]'), 'no overlay of any kind while live').toBeNull();
    });

    it('Paints the status overlay again once the tile stops being live', async () => {
      const { container, rerender } = render(viewer());
      await driveFakes();
      expect(container.querySelector('[aria-hidden="true"]')).toBeNull();

      rerender(viewer({ stream: { ...STREAM, state: 'Degraded', error: 'Source unreachable.' } }));
      await driveFakes();

      expect(screen.getByText('Reconnecting…')).toBeVisible();
      expect(container.querySelector('[aria-hidden="true"]')?.textContent).toContain('Reconnecting…');
    });
  });

  describe('the overlay elements', () => {
    const LABEL: CameraViewerOverlay = {
      kind: 'Text',
      text: 'Press 3',
      color: '#FFFFFFFF',
      fontSizePx: 24,
      normalizedX: 0.25,
      normalizedY: 0.5,
      normalizedWidth: 0.125,
      normalizedHeight: 0.75,
    };

    const BOX: CameraViewerOverlay = {
      kind: 'Box',
      color: '#FF0000FF',
      normalizedX: 0.0625,
      normalizedY: 0.25,
      normalizedWidth: 0.5,
      normalizedHeight: 0.375,
    };

    it('Places a text label by percent of the tile, on all four sides, and never lets it take the pointer', async () => {
      render(viewer({ overlays: [LABEL] }));
      await driveFakes();

      const label = screen.getByTestId('camera-viewer-overlay-label');
      expect(label.textContent).toBe('Press 3');
      expect(label.style.position).toBe('absolute');
      expect(label.style.left).toBe('25%');
      expect(label.style.top).toBe('50%');
      expect(label.style.width).toBe('12.5%');
      expect(label.style.height).toBe('75%');
      expect(label.style.pointerEvents).toBe('none');
    });

    it.each([
      ['Box', BOX],
      ['Ellipse', { ...BOX, kind: 'Ellipse' } as CameraViewerOverlay],
    ])(
      'Places a %s by percent of the tile, hides it from assistive technology, and never lets it take the pointer',
      async (kind, shape) => {
        render(viewer({ overlays: [shape] }));
        await driveFakes();

        const element = screen.getByTestId('camera-viewer-overlay-shape');
        expect(element.getAttribute('data-kind')).toBe(kind);
        expect(element.getAttribute('aria-hidden')).toBe('true');
        expect(element.style.position).toBe('absolute');
        expect(element.style.left).toBe('6.25%');
        expect(element.style.top).toBe('25%');
        expect(element.style.width).toBe('50%');
        expect(element.style.height).toBe('37.5%');
        expect(element.style.pointerEvents).toBe('none');
      },
    );

    it('Identifies an overlay by its position in the set, not by its text', async () => {
      const labelWith = (text: string): CameraViewerOverlay => ({ ...LABEL, text });
      const errors = vi.spyOn(console, 'error').mockImplementation(() => {});
      const { rerender } = render(viewer({ overlays: [labelWith('Door'), labelWith('Door')] }));
      await driveFakes();

      const before = screen.getAllByTestId('camera-viewer-overlay-label');
      expect(before.map((node) => node.textContent)).toEqual(['Door', 'Door']);

      // Same slots, different text: the elements keep their identity (position
      // is the key), and two equal texts do not collide.
      rerender(viewer({ overlays: [labelWith('Gate'), labelWith('Door')] }));
      await driveFakes();

      const after = screen.getAllByTestId('camera-viewer-overlay-label');
      expect(after.map((node) => node.textContent)).toEqual(['Gate', 'Door']);
      expect(after[0], 'slot 0 is the same element, updated in place').toBe(before[0]);
      expect(after[1]).toBe(before[1]);
      expect(errors, 'React must have nothing to say about duplicate keys').not.toHaveBeenCalled();
    });
  });
});
