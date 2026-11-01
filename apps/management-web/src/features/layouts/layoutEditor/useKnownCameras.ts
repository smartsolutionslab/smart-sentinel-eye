import { useState } from 'react';
import type { CameraSummary } from '@smart-sentinel-eye/shared/api/cameras.api';

export function useKnownCameras(
  cameras: { items: readonly CameraSummary[] } | undefined,
): ReadonlyMap<string, CameraSummary> {
  // **Every camera seen so far, so a filter cannot blank a tile that is already
  // assigned.** The options come from the server's matches; a tile holding a
  // camera the current fragment excludes would render a select whose value has
  // no option — blank on screen while the form still carries it. The operator
  // reads that as lost, and the next thing they do is reassign it.
  //
  // Keeping what has been seen is what lets that tile keep showing its own
  // camera by name. Accumulated for the life of the component — see the
  // `wasOpen` block in useCameraPicker for why clearing it on close was a defect.
  // State rather than a ref, and adjusted during render rather than in an
  // effect — React's documented pattern for deriving from a changed prop. A ref
  // read during render, or a setState inside an effect, both produce the
  // cascading-render bug the hooks rules exist to stop, and both were the first
  // thing written here.
  const [knownCameras, setKnownCameras] = useState<ReadonlyMap<string, CameraSummary>>(() => new Map());

  // **Convergence is on the contents, not on the array's identity.**
  //
  // This first compared `cameras.items` against the previously seen array and
  // merged when the reference changed. That terminates only while the query
  // hands back a reference-stable array — and when it does not, the merge sets
  // state on every render and React aborts the component with "too many
  // re-renders". A crashed dialog, from a detail of someone else's caching.
  //
  // Asking which cameras are not yet known cannot loop: the merge only runs
  // while something is missing, and it adds exactly what was missing.
  const unknownCameras = (cameras?.items ?? []).filter((camera) => !knownCameras.has(camera.cameraIdentifier));
  if (unknownCameras.length > 0) {
    const merged = new Map(knownCameras);
    for (const camera of unknownCameras) {
      merged.set(camera.cameraIdentifier, camera);
    }
    setKnownCameras(merged);
  }
  return knownCameras;
}
