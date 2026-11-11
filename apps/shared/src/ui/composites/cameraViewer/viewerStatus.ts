import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import type { CameraViewerStatus } from '../useWhepSession';

/**
 * The text a screen reader hears for the current stream state — empty while
 * live, otherwise the same label *and* hint {@link ViewerOverlay} paints,
 * joined the same way the overlay lays them out visually (spec 228 US2,
 * FR-004/FR-005). Reads the precomputed {@link statusInfoFor} result — see
 * the call site — so the announced text and the visible text cannot drift.
 *
 * <p>
 * <b>Also empty for `idle` while the read has not failed</b> —
 * `useWhepSession` initializes `status` to `idle`, so that is the value the
 * status region is born holding on first paint, in the same commit that
 * creates the DOM node. Treating it the same as `live` (empty) rather than
 * announcing "Idle" is the same #2346 lesson applied to the initial mount,
 * not just a later update: a screen reader attaching after mount never hears
 * an announcement for content the node was born holding. `failedRead` still
 * overrides it — a stream read that fails outright never gets a session, so
 * `status` never leaves `idle`, and that case must still be announced.
 * </p>
 */
export function announcementFor(
  status: CameraViewerStatus,
  message: string | null,
  queryError: unknown,
  failedRead: boolean,
  label: string,
): string {
  if (status === 'live' || (status === 'idle' && !failedRead)) return '';
  const hint = hintFor(message, queryError);
  return hint === null ? label : `${label}. ${hint}`;
}

// FR-005: the read for the current camera has failed and no stream has been
// received for it — an explicit error, never "Connecting…", never "Idle",
// and never a picture. `status` stays the session state machine (no new
// member added, per plan §2a); `offline` already has its own read of stream
// state from a read that *succeeded*, so it is excluded here rather than
// overridden.
//
// Called once, at the top of `CameraViewer`'s render, and the result passed
// down to `announcementFor` and `ViewerOverlay` rather than each re-deriving
// it — so the predicate itself cannot drift between the announced text and
// the painted one, and a non-live tile does not pay for the same computation
// twice on every render (5s stream poll, decode/lag sampler effects — across
// up to 250 wall tiles).
export function statusInfoFor(
  status: CameraViewerStatus,
  stream: StreamHealth | undefined,
  queryError: unknown,
): { failedRead: boolean; label: string } {
  const failedRead = stream === undefined && queryError !== undefined && status !== 'offline';
  return { failedRead, label: failedRead ? 'Viewer error' : labelFor(status, stream) };
}

/**
 * The second line under the label — the WHEP session's own error, or a
 * generic fallback when the stream read itself failed with nothing more
 * specific to say. Shared by the status region and the visible overlay so
 * neither can say more, or less, than the other (FR-005).
 */
export function hintFor(message: string | null, queryError: unknown): string | null {
  return message ?? (queryError !== undefined ? 'Could not reach the streaming service.' : null);
}

function labelFor(status: CameraViewerStatus, stream: StreamHealth | undefined): string {
  if (status === 'live') return 'Live';
  if (status === 'connecting') return 'Connecting…';
  if (status === 'reconnecting') return 'Reconnecting…';
  if (status === 'offline') return 'Stream is offline';
  if (status === 'error') return 'Access refused';
  if (stream?.state === 'Provisioning') return 'Provisioning stream…';
  return 'Idle';
}
