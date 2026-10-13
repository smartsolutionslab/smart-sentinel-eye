import { useCameraStream } from '@smart-sentinel-eye/shared/hooks/useCameraStream';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import type { CameraViewerProps } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

/**
 * `CameraViewer` stopped calling `useGetStreamQuery` itself (ADR-0169, #2846):
 * a composite under `apps/shared/src/ui/composites/**` may not depend on a
 * Redux store. This is the per-tile seam that supplies it instead — a thin
 * wrapper so the 5 s stream poll re-renders only this component, not `Tile`,
 * keeping the render topology a 250-tile wall had before the split.
 */
export function CameraTile(props: Omit<CameraViewerProps, 'stream' | 'queryError'>) {
  const { stream, error } = useCameraStream(props.cameraIdentifier);
  return <CameraViewer {...props} stream={stream} queryError={error} />;
}
