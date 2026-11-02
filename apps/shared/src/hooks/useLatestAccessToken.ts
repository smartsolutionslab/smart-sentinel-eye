import { useCallback, useRef } from 'react';

/**
 * A `getToken` that never changes identity and always resolves the newest token.
 *
 * <p>
 * Stable identity, holding the newest token behind a ref. Callers put the getter
 * into effect dependency arrays and hand it to children that do the same: the
 * kiosk tile's overlay-draw measurement and decode sampler, `CameraViewer`'s
 * sampler, a WHEP session's connect effect, the layout editor's tile view. A
 * fresh function each render rebuilds those effects: the draw measurement then
 * times renders that changed nothing, the sampler's interval is cleared before
 * it can take a second sample (issues 1888, 1889) and a session reconnects on
 * every render rather than only when the camera changes. Nothing visible fails,
 * which is why each caller's test asserts the identity rather than trusting this
 * comment.
 * </p>
 *
 * <p>
 * Empty dependencies, not `[accessToken]`: the ref already carries the newest
 * token, so keying on its value would restart all of that on every silent renew
 * for no gain. The callback reads the ref when it is called, not when it was
 * created, so a call after a renew sees the new token.
 * </p>
 *
 * <p>
 * Takes the token value, not the auth context: each caller already holds its
 * own `useAuth()` result and its own tolerance for it (`auth?.` where tests
 * mount no provider), and this module stays free of the OIDC provider.
 * </p>
 */
export function useLatestAccessToken(accessToken: string | undefined): () => Promise<string | null> {
  const accessTokenRef = useRef(accessToken);
  // Deliberate, for the reason above (judged in #2118): assigning in render is
  // what lets the getter stay stable and still read the newest token.
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = accessToken;
  return useCallback(() => Promise.resolve(accessTokenRef.current ?? null), []);
}
