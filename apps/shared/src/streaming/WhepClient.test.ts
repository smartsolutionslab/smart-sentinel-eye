import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WhepClient } from './WhepClient.js';

class FakePeerConnection {
  static instances: FakePeerConnection[] = [];
  static lastInstance(): FakePeerConnection {
    return FakePeerConnection.instances[FakePeerConnection.instances.length - 1]!;
  }
  ontrack: ((event: { streams: MediaStream[] }) => void) | null = null;
  transceivers: { direction: string; kind: string }[] = [];
  localDescription: RTCSessionDescriptionInit | null = null;
  remoteDescription: RTCSessionDescriptionInit | null = null;
  closed = false;
  receivers: { track: { stop: () => void } }[] = [];

  constructor() {
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
}

describe('WhepClient', () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let videoEl: HTMLVideoElement;

  beforeEach(() => {
    FakePeerConnection.instances = [];
    (globalThis as unknown as { RTCPeerConnection: typeof FakePeerConnection }).RTCPeerConnection =
      FakePeerConnection;
    videoEl = { srcObject: null } as unknown as HTMLVideoElement;
    fetchMock = vi.fn();
    globalThis.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('Posts an SDP offer with the bearer token and applies the answer', async () => {
    fetchMock.mockResolvedValue(
      new Response('v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n', {
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
    fetchMock.mockResolvedValue(
      new Response('v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n', { status: 200 }),
    );
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);

    client.close();

    expect(FakePeerConnection.lastInstance().closed).toBe(true);
  });

  it('Throws when reused without creating a new instance', async () => {
    fetchMock.mockResolvedValue(
      new Response('v=0\r\no=mediamtx 1 1 IN IP4 127.0.0.1\r\ns=-\r\n', { status: 200 }),
    );
    const client = new WhepClient({
      whepUrl: 'http://mediamtx.test/cam-x/whep',
      getToken: async () => 'token',
    });
    await client.connect(videoEl);

    await expect(client.connect(videoEl)).rejects.toThrow(/already connected/i);
  });
});

