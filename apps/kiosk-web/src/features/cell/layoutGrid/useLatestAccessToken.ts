import { useCallback, useRef } from 'react';

export function useLatestAccessToken(accessToken: string | undefined): () => Promise<string | null> {
  // Stable identity, holding the newest token behind a ref. Every tile puts this
  // into effect dependency arrays, so a fresh function each render rebuilds those
  // effects: the overlay-draw measurement then times renders that changed nothing,
  // and the decode sampler's interval is cleared before it can take a second
  // sample (issues 1888, 1889). `useWhepSession` already guards its own use of
  // this prop the same way, and says why.
  const accessTokenRef = useRef(accessToken);
  // Deliberate, for the reason above: keying the effects on the token value
  // restarts them on every silent renew, which is precisely what broke the
  // overlay-draw and decode-leg measurements (issues 1888, 1889).
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = accessToken;
  const getToken = useCallback(() => Promise.resolve(accessTokenRef.current ?? null), []);
  return getToken;
}
