import { logResilienceEvent } from '../observability/resilienceLog.js';

export type WhepErrorKind = 'unauthorized' | 'forbidden' | 'stream-unavailable' | 'network' | 'sdp';

/**
 * What `setPlayoutTarget` found, in place of the single `boolean` it used to
 * collapse three distinct causes into (#2198 item 2, spec 142 FR-006):
 *
 * - `'applied'` — at least one video receiver accepted the assignment.
 * - `'not-connected'` — no peer connection, no `getReceivers`, or no video
 *   receiver at all. A transient every tile passes through on its way up.
 * - `'unsupported'` — video receivers exist, but none carries
 *   `jitterBufferTarget`. Permanent for this browser engine.
 * - `'refused'` — a video receiver carries the property and every assignment
 *   attempt threw. Never raised to a caller (FR-008); only made
 *   distinguishable in the return value.
 */
export type PlayoutTargetOutcome = 'applied' | 'not-connected' | 'unsupported' | 'refused';

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
    // The stream this session has attached to the element: the one the SFU
    // supplied, or one built here for tracks that arrived without an `msid`.
    // Held for this call only — a reconnect runs a fresh `connect` against the
    // same element, and reading `srcObject` back instead would splice the
    // previous session's already-stopped tracks into the new one.
    let sessionStream: MediaStream | null = null;
    pc.ontrack = (event) => {
      const stream = event.streams[0];
      if (stream) {
        // `msid` is per-`m=`-section, so one answer can mix the two shapes.
        // Whatever this session already attached is carried into the stream
        // being attached, because assigning over it would drop those tracks —
        // the same eviction the branch below avoids, met from the other side.
        if (sessionStream !== null && sessionStream !== stream) {
          for (const attached of sessionStream.getTracks()) {
            stream.addTrack(attached);
          }
        }
        sessionStream = stream;
        videoEl.srcObject = stream;
        return;
      }
      // `msid` is negotiated, not guaranteed. Dropping the track here leaves a
      // black tile that still labels itself Live, because `live` follows the
      // connection state — so the track is attached anyway, joining whatever
      // this session already attached. Two recvonly transceivers deliver two
      // events in an order nobody controls; a fresh stream per event would let
      // the second evict the first.
      logResilienceEvent('stream', 'track-without-stream', { kind: event.track.kind });
      sessionStream ??= new MediaStream();
      sessionStream.addTrack(event.track);
      videoEl.srcObject = sessionStream;
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
   * The receiver statistics for this session, or null before there is a
   * connection (spec 040).
   *
   * <p>
   * <b>Read-only, and deliberately the only thing added here.</b> Two legs of
   * the latency budget can be observed nowhere but in the browser, and
   * `inbound-rtp` is where one of them lives — but nothing about reading it
   * changes what this client does. The session, the transceivers, the
   * reconnection and the teardown are untouched (spec 040 FR-011).
   * </p>
   */
  stats(): Promise<RTCStatsReport> | null {
    // `getStats` is checked rather than assumed. A peer connection may not
    // offer it — an older engine, or a test double standing in for one — and
    // an observer that throws where it is unsupported would break the very
    // thing it observes. Null means "no measurement", which every caller
    // already handles, rather than an exception nobody asked for.
    const pc = this.pc;
    if (pc === null || typeof pc.getStats !== 'function') {
      return null;
    }
    return pc.getStats();
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

  /**
   * Sets this session's playout target in milliseconds — how far behind live
   * the receiver holds frames before presenting them (spec 045, ADR-0128).
   *
   * <p>
   * <b>The actuator for wall alignment.</b> Tiles of a wall settle at whatever
   * depth their own jitter buffers chose; raising the target on the leading
   * tiles is how they are brought to a common instant.
   * </p>
   *
   * <p>
   * <b>Write-only, and that matters.</b> `jitterBufferTarget` is not reported
   * back by `getStats`, so the setpoint and what actually happened are two
   * different numbers. Never report this value as a measurement — read the
   * achieved figure from the statistics instead (spec 045 FR-007).
   * </p>
   *
   * <p>
   * Like `stats()`, this reaches an object the client already owns and changes
   * nothing about the session: the transceivers, the reconnection and the
   * teardown are untouched. Support is checked rather than assumed — an older
   * engine or a test double may not offer the property, and a controller that
   * threw where it is unsupported would break the wall it is aligning
   * (FR-013). Returns which of four things happened —
   * {@link PlayoutTargetOutcome}: `'applied'` to at least one video receiver;
   * `'not-connected'`, a transient with no receivers to act on yet;
   * `'unsupported'`, receivers exist but none carries the property; or
   * `'refused'`, a receiver carries it and every assignment threw.
   * </p>
   */
  setPlayoutTarget(milliseconds: number): PlayoutTargetOutcome {
    const pc = this.pc;
    if (pc === null || typeof pc.getReceivers !== 'function') {
      return 'not-connected';
    }

    let outcome: PlayoutTargetOutcome = 'not-connected';
    for (const receiver of pc.getReceivers()) {
      if (receiver.track?.kind !== 'video') continue;
      if (outcome === 'not-connected') outcome = 'unsupported';
      if (!('jitterBufferTarget' in receiver)) continue;
      try {
        // Cast through `unknown`: `jitterBufferTarget` is a WebRTC extension
        // and is absent from the standard receiver type, so there is nothing
        // to intersect with.
        (receiver as unknown as { jitterBufferTarget: number | null }).jitterBufferTarget = milliseconds;
        outcome = 'applied';
      } catch {
        // Swallowed deliberately: an engine may refuse a value or drop the
        // property, and a tile that cannot be aligned must carry on showing
        // video rather than surface an alignment fault to an operator watching
        // a fab (FR-013). Named 'refused' rather than raised — still not
        // surfaced to any caller, only distinguishable in the return value
        // (spec 142 FR-008).
        if (outcome !== 'applied') outcome = 'refused';
      }
    }
    return outcome;
  }

  private releaseSession(): void {
    if (this.sessionUrl === null) return;
    const sessionUrl = this.sessionUrl;
    this.sessionUrl = null;
    // WHEP DELETE (draft-ietf-wish-whep session resource). Fire-and-forget:
    // teardown must never depend on the server still being alive, and
    // `keepalive` lets the release survive page navigation. On page unload
    // the continuation below never runs and nothing is logged — the accepted
    // cost of `keepalive`, not designed around (spec 142 FR-003).
    //
    // Reported, never retried (spec 142 FR-004): MediaMTX 1.21.0 has no
    // idle-session timeout of its own — reclaim is ICE-driven, at roughly 30 s
    // (pion/ice's defaults; no MediaMTX setting bounds it any tighter), so a
    // retry could not recover more than that already-bounded transient, and
    // the dominant failure is a 401 from an expired token — `getToken()` is
    // re-resolved above, at release time, so a retry would only re-present the
    // same dead credential (ADR-0143).
    void this.opts
      .getToken()
      .then((token) => {
        const headers: Record<string, string> = {};
        if (token) {
          headers.Authorization = `Bearer ${token}`;
        }
        return fetch(sessionUrl, { method: 'DELETE', headers, keepalive: true });
      })
      .then((response) => {
        if (!response.ok) {
          logResilienceEvent('stream', 'session-release-failed', { status: response.status });
        }
      })
      .catch((cause: unknown) => {
        logResilienceEvent('stream', 'session-release-failed', { error: String(cause) });
      });
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
