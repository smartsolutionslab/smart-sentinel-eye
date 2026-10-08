// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Spec 308 (issue #2709), plan.md §6.3. `ViewerOverlay`'s fault/warning tone
 * classes must switch from the signal role (`text-accent-fault` /
 * `-warning`, meaningful "on a theme surface") to the new on-video role
 * (`text-accent-fault-on-video` / `-warning-on-video`) — the ground this
 * overlay paints on, `--color-bg-video`, never changes per theme (spec §1),
 * so the per-theme `-text` role spec 299 added is the wrong fit here.
 *
 * Spec 319 (issue #2734): the neutral tone's own `text-fg-muted` also fails
 * 4.5:1 on video in light theme, the same ground problem spec 308 fixed for
 * the triad. `--color-fg-muted-on-video` / `text-fg-muted-on-video` is its
 * neutral counterpart — applied to both the neutral label (the connecting
 * case below, moved off spec 308's "untouched" assertion) and the hint line
 * (new `it`).
 *
 * Reuses `CameraViewer.test.tsx`'s harness shape (its WHEP/stream-health
 * doubles and `setHealth`), copied rather than imported — that file's
 * doubles are module-private (plan.md §6.3, §9 R3; spec 297 precedent).
 *
 * Phase-4a colour: red (spec §6). Red on develop: `ViewerOverlay` still
 * emits `text-accent-fault` / `text-accent-warning`, so every `-on-video`
 * assertion below fails for a missing class, not a typo.
 */

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

class FakePeerConnection {
  static instances: FakePeerConnection[] = [];
  static lastInstance(): FakePeerConnection {
    return FakePeerConnection.instances[FakePeerConnection.instances.length - 1]!;
  }
  ontrack: ((event: { streams: unknown[] }) => void) | null = null;
  onconnectionstatechange: (() => void) | null = null;
  connectionState = 'new';
  iceGatheringState = 'complete';
  localDescription: { type: string; sdp: string } | null = null;
  closed = false;

  constructor() {
    FakePeerConnection.instances.push(this);
  }

  addTransceiver() {}

  async createOffer() {
    return { type: 'offer', sdp: 'v=0\r\no=fake 1 1 IN IP4 127.0.0.1\r\ns=-\r\n' };
  }

  async setLocalDescription(desc: { type: string; sdp: string }) {
    this.localDescription = desc;
  }

  async setRemoteDescription() {}

  getReceivers() {
    return [];
  }

  addEventListener() {}

  removeEventListener() {}

  close() {
    this.closed = true;
  }

  setConnectionState(state: string) {
    this.connectionState = state;
    this.onconnectionstatechange?.();
  }
}

/** Minimal duck-typed WHEP answer; jsdom has no Response constructor. */
function sdpResponse(location: string | null = 'http://sfu.test/cam-42/whep/session-1') {
  return {
    ok: true,
    status: 200,
    headers: { get: (name: string) => (name.toLowerCase() === 'location' ? location : null) },
    text: async () => 'v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n',
  };
}

let streamHealth: { state: string; whepUrl: string; error: string | null } | undefined;

function setHealth(state: string, error: string | null = null) {
  streamHealth = { state, whepUrl: 'http://sfu.test/cam-42/whep', error };
}

function viewer() {
  return <CameraViewer cameraIdentifier="cam-42" getToken={async () => 'token'} />;
}

function renderViewer() {
  return render(viewer());
}

/**
 * Drains the async connect chain (offer → POST → answer) inside act.
 *
 * N microtask rounds bound an N-deep microtask chain — no wall-clock
 * dependence, so this is a bound and not an assumption (ADR-0150). Not a
 * substitute for `waitFor` when the work crosses into the timer phase.
 */
async function flushMicrotasks() {
  await act(async () => {
    for (let i = 0; i < 12; i += 1) {
      await Promise.resolve();
    }
  });
}

async function goLive() {
  await flushMicrotasks();
  act(() => {
    FakePeerConnection.lastInstance().setConnectionState('connected');
  });
}

describe('CameraViewer ViewerOverlay on-video tone', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection = FakePeerConnection;
    globalThis.fetch = vi.fn().mockResolvedValue(sdpResponse()) as unknown as typeof fetch;
    streamHealth = undefined;
    useGetStreamQueryMock.mockImplementation(() => ({
      data: streamHealth,
      currentData: streamHealth,
      isLoading: false,
      error: undefined,
    }));
    vi.spyOn(Math, 'random').mockReturnValue(0.5);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('Carries text-accent-warning-on-video, not text-accent-warning, while reconnecting', async () => {
    setHealth('Healthy');
    renderViewer();
    await goLive();

    act(() => {
      FakePeerConnection.lastInstance().setConnectionState('failed');
    });

    const label = screen.getByText('Reconnecting…');
    const classes = label.className.split(' ');
    expect(classes).toContain('text-accent-warning-on-video');
    expect(classes).not.toContain('text-accent-warning');
  });

  it('Carries text-accent-fault-on-video, not text-accent-fault, when stream health is Offline', async () => {
    setHealth('Offline', 'Source powered down.');
    renderViewer();
    await flushMicrotasks();

    const label = screen.getByText('Stream is offline');
    const classes = label.className.split(' ');
    expect(classes).toContain('text-accent-fault-on-video');
    expect(classes).not.toContain('text-accent-fault');
  });

  it('Carries text-accent-fault-on-video, not text-accent-fault, when the stream read itself fails', async () => {
    useGetStreamQueryMock.mockImplementation(() => ({
      data: undefined,
      currentData: undefined,
      isLoading: false,
      error: new Error('socket closed'),
    }));
    renderViewer();
    await flushMicrotasks();

    const label = screen.getByText('Viewer error');
    const classes = label.className.split(' ');
    expect(classes).toContain('text-accent-fault-on-video');
    expect(classes).not.toContain('text-accent-fault');
  });

  it('Carries text-fg-muted-on-video, not text-fg-muted, while connecting', async () => {
    setHealth('Healthy');
    renderViewer();
    await flushMicrotasks();

    const label = screen.getByText('Connecting…');
    const classes = label.className.split(' ');
    expect(classes).toContain('text-fg-muted-on-video');
    expect(classes).not.toContain('text-fg-muted');
    expect(classes).not.toContain('text-accent-warning-on-video');
    expect(classes).not.toContain('text-accent-fault-on-video');
  });

  it('Paints the hint line with text-fg-muted-on-video, not text-fg-muted', async () => {
    setHealth('Offline', 'Source powered down.');
    renderViewer();
    await flushMicrotasks();

    const hint = screen.getByText('Source powered down.');
    const classes = hint.className.split(' ');
    expect(classes).toContain('text-fg-muted-on-video');
    expect(classes).not.toContain('text-fg-muted');
  });
});
