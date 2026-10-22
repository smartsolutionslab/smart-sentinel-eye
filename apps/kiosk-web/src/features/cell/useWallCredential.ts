import { renewSession } from '@smart-sentinel-eye/shared/api/gateway';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { isWallCredentialRefusal, WALL_CREDENTIAL_WINDOW_MILLISECONDS, type WallTile } from './wallCredential.js';

export type WallCredentialState = 'idle' | 'renewing' | 'refused';

export interface WallCredential {
  state: WallCredentialState;
  credentialGeneration: number;
  reportRefusal: (key: string, kind: 'unauthorized' | 'forbidden') => void;
}

type Refusals = Readonly<Record<string, { kind: 'unauthorized' | 'forbidden'; at: number }>>;

/**
 * The wall's credential state (spec 439): when every mounted tile is refused
 * 401 together, ask the renewer once. Success remounts the refused tiles;
 * failure shows the refused state until the access token changes.
 */
export const useWallCredential = (tileKeys: readonly string[], accessToken: string | undefined): WallCredential => {
  const [refusals, setRefusals] = useState<Refusals>({});
  const [state, setState] = useState<WallCredentialState>('idle');
  const [credentialGeneration, setCredentialGeneration] = useState(0);
  const attempted = useRef(false);
  const [previousToken, setPreviousToken] = useState(accessToken);

  const reportRefusal = useCallback((key: string, kind: 'unauthorized' | 'forbidden') => {
    setRefusals((current) => ({ ...current, [key]: { kind, at: performance.now() } }));
  }, []);

  const keysSignature = tileKeys.join('\n');
  const tiles = useMemo<WallTile[]>(
    () =>
      (keysSignature === '' ? [] : keysSignature.split('\n')).map((key) => {
        const refusal = refusals[key];
        return refusal === undefined
          ? { key, status: 'connecting' }
          : { key, status: refusal.kind, refusedAt: refusal.at };
      }),
    [keysSignature, refusals],
  );

  useEffect(() => {
    if (attempted.current || !isWallCredentialRefusal(tiles, WALL_CREDENTIAL_WINDOW_MILLISECONDS)) return;
    attempted.current = true;
    logResilienceEvent('session', 'wall-credential→renewing');
    setState('renewing');
    const recover = (token: string | undefined) => {
      if (token === undefined) {
        logResilienceEvent('session', 'wall-credential→refused');
        setState('refused');
        return;
      }
      logResilienceEvent('session', 'wall-credential→recovered');
      setRefusals({});
      setCredentialGeneration((generation) => generation + 1);
      setState('idle');
    };
    renewSession().then(recover, () => recover(undefined));
  }, [tiles]);

  // A new access token is the only thing that lifts the refused state (spec
  // 439 A3). Adjusted during render, the idiom this app already uses.
  if (previousToken !== accessToken) {
    setPreviousToken(accessToken);
    if (state === 'refused') {
      setRefusals({});
      setCredentialGeneration((generation) => generation + 1);
      setState('idle');
    }
  }

  return { state, credentialGeneration, reportRefusal };
};
