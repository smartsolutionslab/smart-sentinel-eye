import { useCameraStream } from '@smart-sentinel-eye/shared/hooks';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';
import type { CameraViewerProps } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

/**
 * `CameraViewer` stopped calling `useGetStreamQuery` itself (ADR-0169,
 * #2846): a composite under `apps/shared/src/ui/composites/**` may not
 * depend on a Redux store. This is `CameraDetailPage`'s seam that supplies
 * it instead — the kiosk-web equivalent is `CameraTile`
 * (`apps/kiosk-web/src/features/cell/CameraTile.tsx`).
 */
export function CameraDetailViewer(props: Omit<CameraViewerProps, 'stream' | 'queryError'>) {
  const { stream, error } = useCameraStream(props.cameraIdentifier);
  return <CameraViewer {...props} stream={stream} queryError={error} />;
}
