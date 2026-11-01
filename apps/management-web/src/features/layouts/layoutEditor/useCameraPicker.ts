import { useState } from 'react';
import { useListAllCameraChoicesQuery } from '@smart-sentinel-eye/shared/api/cameras.api';
import { useDebouncedValue } from '@smart-sentinel-eye/shared/hooks';
import { describeCameraFilter } from './describeCameraFilter.js';
import { useKnownCameras } from './useKnownCameras.js';

export function useCameraPicker(open: boolean) {
  // Every camera the operator may choose, not the first page of them. The
  // picker used to ask for fifty and render them as the whole set, so past the
  // fiftieth a camera was absent with nothing said — and an absent option is
  // indistinguishable from a camera that was never registered (spec 048).
  // Spec 055: the fragment narrows on the server, so `cameras` is the matches
  // and `count` is their number. Filtering the gathered array here instead would
  // be a second implementation of "matches" that an operator could not tell
  // apart from the first when the two stopped agreeing.
  const [cameraFilter, setCameraFilter] = useState('');

  // **Debounced, because this query is a page walk rather than a request.**
  // `useListAllCameraChoicesQuery` fetches up to five pages of 200 to assemble
  // the whole choice list, so keying it on every keystroke meant up to five
  // round trips per letter — about thirty-five for "furnace" — and left a cache
  // entry per prefix, each of which a camera mutation would then refetch.
  //
  // The input keeps reading the raw state; only the query and the status read
  // the settled one, or the field would drop characters.
  const settledFilter = useDebouncedValue(cameraFilter.trim());
  const {
    data: cameras,
    isLoading: camerasLoading,
    isFetching: camerasFetching,
    isError: camerasFailed,
  } = useListAllCameraChoicesQuery({ name: settledFilter || undefined });

  const knownCameras = useKnownCameras(cameras);

  // The filter is cleared on close; the retained cameras are not. Both dialogs
  // stay mounted, so this state outlives a close either way, and accumulating
  // costs one entry per camera seen while being read only for the camera a tile
  // currently holds.
  //
  // Clearing it *was* a defect while the merge above keyed on the array's
  // identity: the reopened dialog read the same cache entry it had already
  // recorded, so nothing refilled the map and it was empty at exactly the moment
  // a fragment excluded a camera a tile held. The contents-based merge closed
  // that off — the map refills on the next render regardless — so this is now
  // intent rather than the fix.
  const [wasOpen, setWasOpen] = useState(open);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (!open) {
      setCameraFilter('');
    }
  }

  const cameraItems = cameras?.items ?? [];
  // Only ever true when the source says more cameras exist than were gathered.
  // The copy states the two numbers and stops there, deliberately: the gap can
  // also come from a camera retired between requests, and telling an operator
  // "the rest cannot be chosen" would be wrong in that case. A notice that
  // overclaims is how a notice stops being believed.
  // A notice that appeared whenever the list loaded would carry no information
  // and operators would learn to ignore it, so its absence is what gives it
  // meaning — and is tested as such.
  const camerasTruncated = cameras !== undefined && !cameras.complete;

  const filtering = settledFilter !== '';

  // Typed something the query has not been asked yet. Without this the status
  // reports the *previous* fragment's result for a quarter-second after each
  // keystroke — "3 of 250 cameras match" about a search that is no longer the
  // one in the box.
  const filterSettling = cameraFilter.trim() !== settledFilter;

  const cameraFilterStatus = describeCameraFilter({
    camerasFetching,
    filterSettling,
    camerasFailed,
    filtering,
    camerasTruncated,
    cameraItems,
    cameraFilter,
    cameras,
  });
  const cameraNoticeId = camerasTruncated ? 'layout-camera-truncation' : undefined;

  return {
    cameraFilter,
    setCameraFilter,
    cameras,
    cameraItems,
    camerasLoading,
    camerasFailed,
    camerasTruncated,
    knownCameras,
    filtering,
    cameraFilterStatus,
    cameraNoticeId,
  };
}
