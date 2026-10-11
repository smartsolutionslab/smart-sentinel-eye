import { useGetStreamQuery } from '../api/streams.api.js';
import type { StreamHealth } from '../api/streams.api.js';

/**
 * The exact `useGetStreamQuery` call `CameraViewer` used to make internally
 * (issue #2846 / ADR-0169): a 5 s poll, and `currentData` rather than `data`.
 *
 * `currentData`, not `data` (spec 157 FR-001): `data` is the last successful
 * result for ANY argument this hook instance has ever been called with, so a
 * camera swap at the same call site would keep answering the previous
 * camera's stream — and therefore its `whepUrl` — until the new camera's own
 * read succeeds, or forever if it never does. `currentData` resets to
 * `undefined` the instant the argument changes.
 */
export function useCameraStream(cameraIdentifier: string): { stream: StreamHealth | undefined; error: unknown } {
  const { currentData: stream, error } = useGetStreamQuery(cameraIdentifier, { pollingInterval: 5000 });
  return { stream, error };
}
