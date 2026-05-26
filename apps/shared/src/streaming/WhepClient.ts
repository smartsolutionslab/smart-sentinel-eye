export type WhepErrorKind =
  | 'unauthorized'
  | 'forbidden'
  | 'stream-unavailable'
  | 'network'
  | 'sdp';

export class WhepError extends Error {
  constructor(public readonly kind: WhepErrorKind, message: string) {
    super(message);
    this.name = 'WhepError';
  }
}

export interface WhepClientOptions {
  whepUrl: string;
  getToken: () => Promise<string | null>;
}

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

  constructor(private readonly opts: WhepClientOptions) {}

  async connect(videoEl: HTMLVideoElement, signal?: AbortSignal): Promise<void> {
    if (this.pc) {
      throw new Error('WhepClient already connected; create a new instance per session.');
    }

    const pc = new RTCPeerConnection({ iceServers: [] });
    this.pc = pc;
    pc.addTransceiver('video', { direction: 'recvonly' });
    pc.addTransceiver('audio', { direction: 'recvonly' });
    pc.ontrack = (event) => {
      if (event.streams[0]) {
        videoEl.srcObject = event.streams[0];
      }
    };

    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    if (!offer.sdp) {
      throw new WhepError('sdp', 'createOffer() returned no SDP.');
    }

    const token = await this.opts.getToken();
    const headers: Record<string, string> = {
      'Content-Type': 'application/sdp',
    };
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }

    let response: Response;
    try {
      response = await fetch(this.opts.whepUrl, {
        method: 'POST',
        headers,
        body: offer.sdp,
        signal,
      });
    } catch (cause) {
      throw new WhepError('network', `WHEP POST failed: ${String(cause)}`);
    }

    if (!response.ok) {
      const detail = await response.text().catch(() => '');
      if (response.status === 401) throw new WhepError('unauthorized', detail || 'unauthorized');
      if (response.status === 403) {
        const kind: WhepErrorKind = detail.toLowerCase().includes('unavailable')
          ? 'stream-unavailable'
          : 'forbidden';
        throw new WhepError(kind, detail || 'forbidden');
      }
      throw new WhepError('network', `WHEP returned ${response.status}: ${detail}`);
    }

    const answerSdp = await response.text();
    await pc.setRemoteDescription({ type: 'answer', sdp: answerSdp });
  }

  close(): void {
    if (this.pc) {
      this.pc.getReceivers().forEach((receiver) => receiver.track?.stop());
      this.pc.close();
      this.pc = null;
    }
  }
}
