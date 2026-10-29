import { grantedScopes } from '@smart-sentinel-eye/shared/navigation/grantedScopes';
import { useAuth } from 'react-oidc-context';

const OVERLAY_WRITE_SCOPE = 'sse.overlays.write';

/**
 * Whether the session may change overlays, read the way the shell gates a nav entry: from the
 * token response's `scope`. The server stays the authority; this only avoids offering an edit
 * that would be refused. `auth?.`: tests mount without an `<AuthProvider>`, where `useAuth()`
 * returns undefined.
 */
export function useCanEditOverlays(): boolean {
  const auth = useAuth();
  return grantedScopes({ scope: auth?.user?.scope }).has(OVERLAY_WRITE_SCOPE);
}
