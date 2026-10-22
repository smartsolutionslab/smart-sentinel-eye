import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';

const renewSession = vi.hoisted(() => vi.fn<() => Promise<string | undefined>>());
vi.mock('@smart-sentinel-eye/shared/api/gateway', () => ({ renewSession }));

const { useWallCredential } = await import('./useWallCredential.js');

/**
 * Spec 439 (issue #2781) T004 — RED. The wall's credential state machine:
 * idle -> renewing -> (recovered | refused), one renewal attempt per mount.
 */

let clock = 0;
let resolveRenewal: (token: string | undefined) => void = () => undefined;
let pendingRenewal: Promise<string | undefined> = Promise.resolve(undefined);

const setup = (keys: string[], accessToken: string | undefined = 'old-token') =>
  renderHook(({ tileKeys, token }) => useWallCredential(tileKeys, token), {
    initialProps: { tileKeys: keys, token: accessToken },
  });

const refuse = (
  hook: ReturnType<typeof setup>,
  key: string,
  kind: 'unauthorized' | 'forbidden' = 'unauthorized',
  at = clock,
) => {
  clock = at;
  act(() => {
    hook.result.current.reportRefusal(key, kind);
  });
};

const completeRenewal = async (token: string | undefined) => {
  await act(async () => {
    resolveRenewal(token);
    await pendingRenewal;
  });
};

describe('useWallCredential', () => {
  beforeEach(() => {
    clock = 0;
    vi.spyOn(performance, 'now').mockImplementation(() => clock);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
    renewSession.mockReset();
    renewSession.mockImplementation(() => {
      pendingRenewal = new Promise<string | undefined>((resolve) => {
        resolveRenewal = resolve;
      });
      return pendingRenewal;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('Starts idle at generation zero', () => {
    const hook = setup(['a', 'b']);

    expect(hook.result.current.state).toBe('idle');
    expect(hook.result.current.credentialGeneration).toBe(0);
  });

  it('Asks the renewer exactly once and shows renewing when both tiles are refused 401 within the window', () => {
    const hook = setup(['a', 'b']);

    refuse(hook, 'a', 'unauthorized', 0);
    expect(hook.result.current.state).toBe('idle');
    refuse(hook, 'b', 'unauthorized', 10_000);

    expect(hook.result.current.state).toBe('renewing');
    expect(renewSession).toHaveBeenCalledTimes(1);
  });

  it('Remounts the tiles by bumping the generation and clears the state when renewal yields a token', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');

    await completeRenewal('new-token');

    expect(hook.result.current.state).toBe('idle');
    expect(hook.result.current.credentialGeneration).toBe(1);
  });

  it('Shows the refused state when the renewer resolves no usable token', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');

    await completeRenewal(undefined);

    expect(hook.result.current.state).toBe('refused');
    expect(hook.result.current.credentialGeneration).toBe(0);
  });

  it('Makes no second renewal when a later burst of refusals arrives after the first failed', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    await completeRenewal(undefined);

    refuse(hook, 'a', 'unauthorized', 50_000);
    refuse(hook, 'b', 'unauthorized', 50_100);

    expect(renewSession).toHaveBeenCalledTimes(1);
    expect(hook.result.current.state).toBe('refused');
  });

  it('Gives no wall state and no second renewal when the tiles are refused again after a successful renewal', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    await completeRenewal('new-token');

    refuse(hook, 'a', 'unauthorized', 60_000);
    refuse(hook, 'b', 'unauthorized', 60_100);

    expect(hook.result.current.state).toBe('idle');
    expect(renewSession).toHaveBeenCalledTimes(1);
  });

  it('Clears the refused state and remounts once when the access token changes', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    await completeRenewal(undefined);
    expect(hook.result.current.state).toBe('refused');

    hook.rerender({ tileKeys: ['a', 'b'], token: 'recovered-token' });

    expect(hook.result.current.state).toBe('idle');
    expect(hook.result.current.credentialGeneration).toBe(1);
  });

  it('Does not renew for a single refused tile', () => {
    const hook = setup(['a']);

    refuse(hook, 'a');

    expect(hook.result.current.state).toBe('idle');
    expect(renewSession).not.toHaveBeenCalled();
  });

  it('Does not renew when one tile is refused 401 and the other 403', () => {
    const hook = setup(['a', 'b']);

    refuse(hook, 'a', 'unauthorized');
    refuse(hook, 'b', 'forbidden');

    expect(hook.result.current.state).toBe('idle');
    expect(renewSession).not.toHaveBeenCalled();
  });

  it('Does not renew when the refusals are more than ten seconds apart', () => {
    const hook = setup(['a', 'b']);

    refuse(hook, 'a', 'unauthorized', 0);
    refuse(hook, 'b', 'unauthorized', 10_001);

    expect(hook.result.current.state).toBe('idle');
    expect(renewSession).not.toHaveBeenCalled();
  });

  it('Waits for a tile that mounted but has not refused, and renews when it also refuses 401', () => {
    const hook = setup(['a', 'b', 'c']);

    refuse(hook, 'a');
    refuse(hook, 'b');
    expect(hook.result.current.state).toBe('idle');
    expect(renewSession).not.toHaveBeenCalled();

    refuse(hook, 'c', 'unauthorized', 5_000);

    expect(hook.result.current.state).toBe('renewing');
    expect(renewSession).toHaveBeenCalledTimes(1);
  });

  it('Renews when the one tile that was not refused is unmounted', () => {
    const hook = setup(['a', 'b', 'c']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    expect(renewSession).not.toHaveBeenCalled();

    hook.rerender({ tileKeys: ['a', 'b'], token: 'old-token' });

    expect(hook.result.current.state).toBe('renewing');
    expect(renewSession).toHaveBeenCalledTimes(1);
  });
});
