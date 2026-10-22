import { renewSession } from '@smart-sentinel-eye/shared/api/gateway';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { renewalAnsweredWithoutCredential } from '../../app/renewalAnswer.js';
import { isWallCredentialRefusal, WALL_CREDENTIAL_WINDOW_MILLISECONDS, type WallTile } from './wallCredential.js';

export type WallCredentialState = 'idle' | 'renewing' | 'refused';

export interface WallCredential {
  state: WallCredentialState;
  credentialGeneration: number;
  reportRefusal: (key: string, kind: 'unauthorized' | 'forbidden') => void;
  /** A tile's session went live: its refusal record goes, and the episode ends. */
  reportLive: (key: string) => void;
  /** A tile left refusal without going live (reconnect or unmount): its record goes. */
  clearRefusal: (key: string) => void;
}

type Refusals = Readonly<Record<string, { kind: 'unauthorized' | 'forbidden'; at: number }>>;

const without = (refusals: Refusals, key: string): Refusals => {
  if (!(key in refusals)) return refusals;
  const rest = { ...refusals };
  delete rest[key];
  return rest;
};

/**
 * The wall's credential state (spec 439): when every mounted tile is refused
 * 401 together, ask the renewer once per episode. An episode ends when a tile
 * is live again.
 *
 * <p>
 * A renewed token remounts the refused tiles only once it has reached the
 * access token, because the remounted tiles read their bearer from it. A
 * renewer that returns no token keeps the first state while the session
 * machine's own retry runs; the second state is reserved for a provider that
 * answered with a user holding no credential. Either way, a new access token
 * is what lifts the state.
 * </p>
 */
export const useWallCredential = (tileKeys: readonly string[], accessToken: string | undefined): WallCredential => {
  const [refusals, setRefusals] = useState<Refusals>({});
  const [state, setState] = useState<WallCredentialState>('idle');
  const [credentialGeneration, setCredentialGeneration] = useState(0);
  const [awaitingToken, setAwaitingToken] = useState(false);
  const attempted = useRef(false);
  const renewalInFlight = useRef(false);
  const mounted = useRef(true);
  const latestToken = useRef(accessToken);
  const [previousToken, setPreviousToken] = useState(accessToken);

  useEffect(() => {
    latestToken.current = accessToken;
  });

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const reportRefusal = useCallback((key: string, kind: 'unauthorized' | 'forbidden') => {
    setRefusals((current) => ({ ...current, [key]: { kind, at: performance.now() } }));
  }, []);

  const clearRefusal = useCallback((key: string) => {
    setRefusals((current) => without(current, key));
  }, []);

  const reportLive = useCallback((key: string) => {
    setRefusals((current) => without(current, key));
    if (renewalInFlight.current) return;
    attempted.current = false;
    setAwaitingToken(false);
    setState('idle');
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

  const settle = () => {
    setRefusals({});
    setCredentialGeneration((generation) => generation + 1);
    setAwaitingToken(false);
    setState('idle');
  };

  useEffect(() => {
    if (attempted.current || !isWallCredentialRefusal(tiles, WALL_CREDENTIAL_WINDOW_MILLISECONDS)) return;
    attempted.current = true;
    renewalInFlight.current = true;
    logResilienceEvent('session', 'wall-credential→renewing');
    setState('renewing');
    renewSession().then((token) => {
      renewalInFlight.current = false;
      if (!mounted.current) return;
      if (token === undefined || token === '') {
        if (renewalAnsweredWithoutCredential()) {
          logResilienceEvent('session', 'wall-credential→refused');
          setState('refused');
          return;
        }
        logResilienceEvent('session', 'wall-credential→retrying');
        setAwaitingToken(true);
        return;
      }
      logResilienceEvent('session', 'wall-credential→recovered');
      if (latestToken.current === token) {
        settle();
        return;
      }
      setAwaitingToken(true);
    });
  }, [tiles]);

  // A new access token is the only thing that lifts a failed or pending
  // renewal (spec 439 A3). Adjusted during render, the idiom this app already
  // uses.
  if (previousToken !== accessToken) {
    setPreviousToken(accessToken);
    if (state === 'refused' || awaitingToken) settle();
  }

  return { state, credentialGeneration, reportRefusal, reportLive, clearRefusal };
};
