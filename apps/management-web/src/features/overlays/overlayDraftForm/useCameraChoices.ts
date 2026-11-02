import { useListAllCameraChoicesQuery } from '@smart-sentinel-eye/shared/api/cameras.api';

/**
 * ADR-0169 (issue #2847): `BackdropControls`' camera picker needs this
 * query, and for the same reason as `useResolvedPreview`, it cannot live
 * in `OverlayEditor` — `OverlayEditorCharacterisation.test.tsx` and its
 * siblings render that component bare, with no Redux `<Provider>`. Every
 * draft mounts the editor now (spec 457), so the query is never skipped.
 *
 * `data` carries its own `complete` — spec
 * 048's truncation signal for this bounded response. It is forwarded
 * whole, unread here, as `cameras.data` to `OverlayEditor` and on to
 * `BackdropControls`; `CameraCaptureSection` (`BackdropControls.tsx`) is
 * what actually reads `complete` and renders the "Showing N of M cameras"
 * notice, because it is the component that owns the picker UI this query
 * feeds.
 */
export function useCameraChoices() {
  const { data, isLoading, isError } = useListAllCameraChoicesQuery(undefined, { skip: false });
  return { data, isLoading, isError };
}
