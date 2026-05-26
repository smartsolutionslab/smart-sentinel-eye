import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { CameraViewer } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

export interface CameraViewerPanelProps {
  cameraIdentifier: string | null;
  cameraName: string | null;
  onClose: () => void;
}

/**
 * Side panel that mounts the shared <CameraViewer> for the selected camera.
 * Unmounts the viewer on close so the WHEP peer connection is torn down.
 * Spec 002 US1.
 */
export function CameraViewerPanel({ cameraIdentifier, cameraName, onClose }: CameraViewerPanelProps) {
  if (cameraIdentifier === null) return null;

  return (
    <aside
      role="dialog"
      aria-label={`Live viewer for ${cameraName ?? 'camera'}`}
      className="fixed inset-y-0 right-0 z-40 flex w-full max-w-2xl flex-col gap-4 border-l border-fg-muted/30 bg-bg-base p-6 shadow-xl"
    >
      <header className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">{cameraName ?? cameraIdentifier}</h2>
        <Button variant="secondary" onClick={onClose}>
          Close
        </Button>
      </header>
      <CameraViewer cameraIdentifier={cameraIdentifier} getToken={getKeycloakAccessToken} />
      <p className="text-xs text-fg-muted">
        Live feed served via WebRTC (WHEP). Reconnects automatically on transient outages.
      </p>
    </aside>
  );
}

/**
 * Resolves the current operator's Keycloak access token. The real wiring
 * lands when react-oidc-context is added to the app shell (spec 002
 * Assumptions); for now the placeholder reads a session-storage key set by
 * the planned sign-in flow.
 */
async function getKeycloakAccessToken(): Promise<string | null> {
  if (typeof window === 'undefined') return null;
  return window.sessionStorage.getItem('keycloak:access_token');
}
