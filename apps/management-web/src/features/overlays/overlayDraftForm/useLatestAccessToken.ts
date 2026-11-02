import { useCallback, useRef } from 'react';
import { useAuth } from 'react-oidc-context';

/**
 * Spec 147 T010. Stable identity, holding the newest token behind a ref —
 * copied from `CameraDetailPage.tsx:20-38`, including its reasoning.
 * `OverlayEditor` puts this into a WHEP session's connect effect the same
 * way `CameraViewer` does, so a fresh function every render would tear that
 * session down and reconnect it on every render rather than only when the
 * camera changes — the same failure that silently killed the decode
 * sampler once already (issue 1889).
 *
 * `auth?.` rather than `auth.`: unlike `CameraDetailPage`, this form is
 * exercised in tests with no `<AuthProvider>` in the tree, where
 * `react-oidc-context`'s real `useAuth()` warns and returns `undefined`
 * rather than throwing.
 */
export function useLatestAccessToken(): () => Promise<string | null> {
  const auth = useAuth();
  const accessTokenRef = useRef(auth?.user?.access_token);
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = auth?.user?.access_token;
  return useCallback(() => Promise.resolve(accessTokenRef.current ?? null), []);
}
