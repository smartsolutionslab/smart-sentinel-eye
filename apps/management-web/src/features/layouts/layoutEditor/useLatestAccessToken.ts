import { useCallback, useRef } from 'react';
import { useAuth } from 'react-oidc-context';

// `auth?.`: tests mount the dialog with no `<AuthProvider>`, where `useAuth()` returns undefined.
export function useLatestAccessToken(): () => Promise<string | null> {
  const auth = useAuth();
  const accessTokenRef = useRef(auth?.user?.access_token);
  // eslint-disable-next-line react-hooks/refs -- the callback must read the latest token, not the first
  accessTokenRef.current = auth?.user?.access_token;
  return useCallback(() => Promise.resolve(accessTokenRef.current ?? null), []);
}
