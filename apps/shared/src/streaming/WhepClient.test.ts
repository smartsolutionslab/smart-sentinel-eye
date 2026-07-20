import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WhepClient } from './WhepClient.js';

class FakePeerConnection {
  static instances: FakePeerConnection[] = [];
  static initialIceGatheringState = 'complete';
  static lastInstance(): FakePeerConnection {
    return FakePeerConnection.instances[FakePeerConnection.instances.length - 1]!;
  }
  ontrack: ((event: { streams: MediaStream[] }) => void) | null = null;
  onconnectionstatechange: (() => void) | null = null;
  connectionState = 'new';
  iceGatheringState: string;
  transceivers: { direction: string; kind: string }[] = [];
  localDescription: RTCSessionDescriptionInit | null = null;
  remoteDescription: RTCSessionDescriptionInit | null = null;
  closed = false;
  receivers: { track: { stop: () => void } }[] = [];
  private iceGatheringListeners: (() => void)[] = [];

  constructor() {
    this.iceGatheringState = FakePeerConnection.initialIceGatheringState;
    FakePeerConnection.instances.push(this);
  }

  addTransceiver(kind: string, init: { direction: string }) {
    this.transceivers.push({ kind, direction: init.direction });
  }

  async createOffer(): Promise<RTCSessionDescriptionInit> {
    return { type: 'offer', sdp: 'v=0\r\no=fake 1 1 IN IP4 127.0.0.1\r\ns=-\r\n' };
  }

  async setLocalDescription(desc: RTCSessionDescriptionInit) {
    this.localDescription = desc;
  }

  async setRemoteDescription(desc: RTCSessionDescriptionInit) {
    this.remoteDescription = desc;
  }

  getReceivers() {
    return this.receivers;
  }

  close() {
    this.closed = true;
  }

  addEventListener(_type: string, listener: () => void) {
    this.iceGatheringListeners.push(listener);
  }

  removeEventListener(_type: string, listener: () => void) {
    this.iceGatheringListeners = this.iceGatheringListeners.filter((l) => l !== listener);
  }

  completeIceGathering() {
    this.iceGatheringState = 'complete';
    for (const listener of [...this.iceGatheringListeners]) {
      listener();
    }
  }

  setConnectionState(state: string) {
    this.connectionState = state;
    this.onconnectionstatechange?.();
  }
}

const answerSdp = 'v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n';

async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 10; i += 1) {
    await Promise.resolve();
  }
}

describe('WhepClient', () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let videoEl: HTMLVideoElement;

  beforeEach(() => {
    FakePeerConnection.instances = [];
    FakePeerConnection.initialIceGatheringState = 'complete';
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection =
      FakePeerConnection;
    videoEl = { srcObject: null } as unknown as HTMLVideoElement;
    fetchMock = vi.fn();
    globalThis.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('Posts an SDP offer with the bearer token and applies the answer', async () => {
    fetchMock.mockResolvedValue(
      new Response(answerSdp, {
        status: 200,
        headers: { 'Content-Type': 'application/sdp' },
      }),
    );
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token-xyz',
    });

    await client.connect(videoEl);

    expect(fetchMock).toHaveBeenCalledOnce();
    const [, init] = fetchMock.mock.calls[0]!;
    expect((init as RequestInit).method).toBe('POST');
    const headers = (init as RequestInit).headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer token-xyz');
    expect(headers['Content-Type']).toBe('application/sdp');
    expect(FakePeerConnection.lastInstance().remoteDescription?.type).toBe('answer');
  });

  it('Throws WhepError(unauthorized) on a 401 response', async () => {
    fetchMock.mockResolvedValue(new Response('', { status: 401 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'bad-token',
    });

    await expect(client.connect(videoEl)).rejects.toMatchObject({
      name: 'WhepError',
      kind: 'unauthorized',
    });
  });

  it('Throws WhepError(stream-unavailable) when the body mentions unavailable', async () => {
    fetchMock.mockResolvedValue(new Response('stream is unavailable (offline)', { status: 403 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });

    await expect(client.connect(videoEl)).rejects.toMatchObject({
      name: 'WhepError',
      kind: 'stream-unavailable',
    });
  });

  it('Throws WhepError(forbidden) on a generic 403', async () => {
    fetchMock.mockResolvedValue(new Response('missing scope', { status: 403 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });

    await expect(client.connect(videoEl)).rejects.toMatchObject({
      name: 'WhepError',
      kind: 'forbidden',
    });
  });

  it('close() releases the peer connection', async () => {
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);

    client.close();

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
  });

  it('Throws when reused without creating a new instance', async () => {
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);

    await expect(client.connect(videoEl)).rejects.toThrow(/already connected/i);
  });

  it('Invokes onConnectionStateChange whenever the peer connection state changes', async () => {
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const states: string[] = [];
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
      onConnectionStateChange: (state) => states.push(state),
    });
    await client.connect(videoEl);

    FakePeerConnection.lastInstance().setConnectionState('connected');
    FakePeerConnection.lastInstance().setConnectionState('failed');

    expect(states).toEqual(['connected', 'failed']);
  });

  it('close() DELETEs the captured WHEP session exactly once with the bearer token', async () => {
    fetchMock.mockResolvedValue(
      new Response(answerSdp, {
        status: 200,
        headers: { Location: '/cam-x/whep/sessions/abc' },
      }),
    );
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);

    client.close();
    client.close();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
    const deleteCalls = fetchMock.mock.calls.filter(
      ([, init]) => (init as RequestInit).method === 'DELETE',
    );
    expect(deleteCalls).toHaveLength(1);
    const [url, init] = deleteCalls[0]!;
    expect(url).toBe('http://mediamtx.test/cam-x/whep/sessions/abc');
    const headers = (init as RequestInit).headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer token');
    expect((init as RequestInit).keepalive).toBe(true);
  });

  it('close() without a captured session URL performs local teardown only', async () => {
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);
    fetchMock.mockClear();

    client.close();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('Aborting mid-connect leaves no live peer connection', async () => {
    fetchMock.mockImplementation(
      (_url: unknown, init?: RequestInit) =>
        new Promise((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () => reject(new Error('aborted')));
        }),
    );
    const controller = new AbortController();
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    const pending = client.connect(videoEl, controller.signal);
    await flushMicrotasks();

    controller.abort();

    await expect(pending).rejects.toMatchObject({ name: 'WhepError', kind: 'network' });
    expect(FakePeerConnection.lastInstance().closed).toBe(true);
  });

  it('Waits for ICE gathering completion before posting the offer', async () => {
    FakePeerConnection.initialIceGatheringState = 'gathering';
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    const pending = client.connect(videoEl);
    await flushMicrotasks();

    expect(fetchMock).not.toHaveBeenCalled();

    FakePeerConnection.lastInstance().completeIceGathering();
    await pending;

    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('Caps the ICE gathering wait at 250 ms', async () => {
    vi.useFakeTimers();
    FakePeerConnection.initialIceGatheringState = 'gathering';
    fetchMock.mockResolvedValue(new Response(answerSdp, { status: 200 }));
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    const pending = client.connect(videoEl);

    await vi.advanceTimersByTimeAsync(249);
    expect(fetchMock).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1);
    await pending;
    expect(fetchMock).toHaveBeenCalledOnce();
  });
});
