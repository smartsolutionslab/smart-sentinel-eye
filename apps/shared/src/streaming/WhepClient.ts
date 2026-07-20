export type WhepErrorKind = 'unauthorized' | 'forbidden' | 'stream-unavailable' | 'network' | 'sdp';

export class WhepError extends Error {
  constructor(
    public readonly kind: WhepErrorKind,
    message: string,
  ) {
    super(message);
    this.name = 'WhepError';
  }
}

export interface WhepClientOptions {
  whepUrl: string;
  getToken: () => Promise<string | null>;
  /** Fires on every RTCPeerConnection connectionState change (spec 011 FR-001/002). */
  onConnectionStateChange?: (state: RTCPeerConnectionState) => void;
}

const ICE_GATHERING_CAP_MS = 250;

/**
 * Minimal WHEP-over-fetch client. Wraps `RTCPeerConnection` + a single POST
 * of the SDP offer against MediaMTX's WHEP endpoint. Browser-only — relies on
 * the global `RTCPeerConnection` constructor.
 *
 * On-prem fab assumption (spec 002 Assumptions): browser and MediaMTX share
 * the same L2 network, so ICE candidates are gathered locally without any
 * STUN/TURN server.
 */
export class WhepClient {
  private pc: RTCPeerConnection | null = null;
  private sessionUrl: string | null = null;
  private started = false;

  constructor(private readonly opts: WhepClientOptions) {}

  async connect(videoEl: HTMLVideoElement, signal?: AbortSignal): Promise<void> {
    if (this.started) {
      throw new Error('WhepClient already connected; create a new instance per session.');
    }
    this.started = true;

    const pc = new RTCPeerConnection({ iceServers: [] });
    this.pc = pc;
    pc.onconnectionstatechange = () => this.opts.onConnectionStateChange?.(pc.connectionState);
    pc.addTransceiver('video', { direction: 'recvonly' });
    pc.addTransceiver('audio', { direction: 'recvonly' });
    pc.ontrack = (event) => {
      if (event.streams[0]) {
        videoEl.srcObject = event.streams[0];
      }
    };

    try {
      const offer = await pc.createOffer();
      await pc.setLocalDescription(offer);
      await waitForIceGathering(pc);
      const sdp = pc.localDescription?.sdp ?? offer.sdp;
      if (!sdp) {
        throw new WhepError('sdp', 'createOffer() returned no SDP.');
      }
      const response = await this.postOffer(sdp, signal);
      const answerSdp = await response.text();
      await pc.setRemoteDescription({ type: 'answer', sdp: answerSdp });
    } catch (cause) {
      this.teardownLocally();
      throw cause;
    }
  }

  /**
   * Releases the WHEP session (fire-and-forget DELETE against the captured
   * `Location`) and then unconditionally tears the peer connection down
   * locally. Never throws and never awaits; safe to call repeatedly and
   * mid-connect (spec 011 FR-004).
   */
  close(): void {
    this.releaseSession();
    this.teardownLocally();
  }

  private async postOffer(sdp: string, signal?: AbortSignal): Promise<Response> {
    const token = await this.opts.getToken();
    const headers: Record<string, string> = {
      'Content-Type': 'application/sdp',
    };
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }

    let response: Response;
    try {
      response = await fetch(this.opts.whepUrl, { method: 'POST', headers, body: sdp, signal });
    } catch (cause) {
      throw new WhepError('network', `WHEP POST failed: ${String(cause)}`);
    }
    if (!response.ok) {
      throw await errorForResponse(response);
    }

    const location = response.headers.get('Location');
    this.sessionUrl = location === null ? null : new URL(location, this.opts.whepUrl).toString();
    return response;
  }

  private releaseSession(): void {
    if (this.sessionUrl === null) return;
    const sessionUrl = this.sessionUrl;
    this.sessionUrl = null;
    // WHEP DELETE (draft-ietf-wish-whep session resource). Fire-and-forget:
    // teardown must never depend on the server still being alive, and
    // `keepalive` lets the release survive page navigation.
    void this.opts
      .getToken()
      .then((token) => {
        const headers: Record<string, string> = {};
        if (token) {
          headers.Authorization = `Bearer ${token}`;
        }
        return fetch(sessionUrl, { method: 'DELETE', headers, keepalive: true });
      })
      .catch(() => undefined);
  }

  private teardownLocally(): void {
    if (!this.pc) return;
    this.pc.getReceivers().forEach((receiver) => receiver.track?.stop());
    this.pc.close();
    this.pc = null;
  }
}

async function errorForResponse(response: Response): Promise<WhepError> {
  const detail = await response.text().catch(() => '');
  if (response.status === 401) return new WhepError('unauthorized', detail || 'unauthorized');
  if (response.status === 403) {
    const kind: WhepErrorKind = detail.toLowerCase().includes('unavailable') ? 'stream-unavailable' : 'forbidden';
    return new WhepError(kind, detail || 'forbidden');
  }
  return new WhepError('network', `WHEP returned ${response.status}: ${detail}`);
}

function waitForIceGathering(pc: RTCPeerConnection): Promise<void> {
  if (pc.iceGatheringState === 'complete') {
    return Promise.resolve();
  }
  return new Promise((resolve) => {
    const finish = () => {
      clearTimeout(cap);
      pc.removeEventListener('icegatheringstatechange', onChange);
      resolve();
    };
    const onChange = () => {
      if (pc.iceGatheringState === 'complete') finish();
    };
    // Single-L2 fab networks gather host candidates in milliseconds; the cap
    // keeps connect latency bounded when a slow kiosk browser does not.
    const cap = setTimeout(finish, ICE_GATHERING_CAP_MS);
    pc.addEventListener('icegatheringstatechange', onChange);
  });
}
