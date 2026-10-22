import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';

const renewSession = vi.hoisted(() => vi.fn<() => Promise<string | undefined>>());
vi.mock('@smart-sentinel-eye/shared/api/gateway', () => ({ renewSession }));

const { useWallCredential } = await import('./useWallCredential.js');
const { recordRenewalAnswer } = await import('../../app/renewalAnswer.js');

/**
 * Spec 439 (issue #2781) T004 — RED. The wall's credential state machine:
 * idle -> renewing -> (recovered | refused), one renewal attempt per episode.
 *
 * Review of #2781 changed five expectations below (disclosed in the commit):
 * a renewer that returns no token keeps the first state unless the provider
 * answered with a user holding no credential (item 2); a renewed token is
 * held until the access token actually changes (item 3); and the latch is
 * per episode, not per mount (item 4).
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
    recordRenewalAnswer(false);
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

  it('Holds the remount until the access token actually changes to the renewed one', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');

    await completeRenewal('new-token');

    expect(hook.result.current.credentialGeneration, 'remounted tiles would still read the old bearer').toBe(0);
    expect(hook.result.current.state).toBe('renewing');

    hook.rerender({ tileKeys: ['a', 'b'], token: 'new-token' });

    expect(hook.result.current.state).toBe('idle');
    expect(hook.result.current.credentialGeneration).toBe(1);
  });

  it('Remounts at once when the renewed token already reached the access token before the renewal resolved', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    hook.rerender({ tileKeys: ['a', 'b'], token: 'new-token' });

    await completeRenewal('new-token');

    expect(hook.result.current.state).toBe('idle');
    expect(hook.result.current.credentialGeneration).toBe(1);
  });

  it('Shows the refused state when the provider answered the renewal with a user holding no credential', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    recordRenewalAnswer(true);

    await completeRenewal(undefined);

    expect(hook.result.current.state).toBe('refused');
    expect(hook.result.current.credentialGeneration).toBe(0);
  });

  it('Keeps the first state when the renewer returns no token because the provider is unreachable', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');

    await completeRenewal(undefined);

    expect(hook.result.current.state, 'an unreachable provider is not a refusal').toBe('renewing');
    expect(hook.result.current.credentialGeneration).toBe(0);
  });

  it('Treats an empty token as a failed renewal', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');

    await completeRenewal('');

    expect(hook.result.current.state).toBe('renewing');
    expect(hook.result.current.credentialGeneration).toBe(0);
  });

  it('Remounts and clears the state when the retry that follows an unreachable provider delivers a token', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    await completeRenewal(undefined);

    hook.rerender({ tileKeys: ['a', 'b'], token: 'recovered-token' });

    expect(hook.result.current.state).toBe('idle');
    expect(hook.result.current.credentialGeneration).toBe(1);
  });

  it('Makes no second renewal when a later burst of refusals arrives after the first failed', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    recordRenewalAnswer(true);
    await completeRenewal(undefined);

    refuse(hook, 'a', 'unauthorized', 50_000);
    refuse(hook, 'b', 'unauthorized', 50_100);

    expect(renewSession).toHaveBeenCalledTimes(1);
    expect(hook.result.current.state).toBe('refused');
  });

  it('Makes no second renewal for a burst after an unreachable-provider failure either', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    await completeRenewal(undefined);

    refuse(hook, 'a', 'unauthorized', 50_000);
    refuse(hook, 'b', 'unauthorized', 50_100);

    expect(renewSession).toHaveBeenCalledTimes(1);
  });

  it('Gives no wall state and no second renewal when the tiles are refused again before any tile is live', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    await completeRenewal('new-token');
    hook.rerender({ tileKeys: ['a', 'b'], token: 'new-token' });

    refuse(hook, 'a', 'unauthorized', 60_000);
    refuse(hook, 'b', 'unauthorized', 60_100);

    expect(hook.result.current.state).toBe('idle');
    expect(renewSession).toHaveBeenCalledTimes(1);
  });

  it('Renews exactly once for a second episode after the remounted tiles went live', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    await completeRenewal('new-token');
    hook.rerender({ tileKeys: ['a', 'b'], token: 'new-token' });
    act(() => {
      hook.result.current.reportLive('a');
      hook.result.current.reportLive('b');
    });
    expect(renewSession).toHaveBeenCalledTimes(1);

    refuse(hook, 'a', 'unauthorized', 900_000);
    refuse(hook, 'b', 'unauthorized', 900_100);

    expect(renewSession).toHaveBeenCalledTimes(2);
    expect(hook.result.current.state).toBe('renewing');
    await completeRenewal('newer-token');
    hook.rerender({ tileKeys: ['a', 'b'], token: 'newer-token' });
    refuse(hook, 'a', 'unauthorized', 950_000);
    refuse(hook, 'b', 'unauthorized', 950_100);
    expect(renewSession, 'at most one renewal per episode').toHaveBeenCalledTimes(2);
  });

  it('Makes no second renewal in a burst after a failed renewal even though a tile was live earlier', async () => {
    const hook = setup(['a', 'b']);
    act(() => hook.result.current.reportLive('a'));
    refuse(hook, 'a');
    refuse(hook, 'b');
    await completeRenewal(undefined);

    refuse(hook, 'a', 'unauthorized', 50_000);
    refuse(hook, 'b', 'unauthorized', 50_100);

    expect(renewSession).toHaveBeenCalledTimes(1);
  });

  it('Does not count a tile that left refusal when its neighbours are refused', () => {
    const hook = setup(['a', 'b', 'c']);
    refuse(hook, 'a');
    refuse(hook, 'b');

    act(() => hook.result.current.clearRefusal('a'));
    refuse(hook, 'c', 'unauthorized', 1_000);

    expect(hook.result.current.state).toBe('idle');
    expect(renewSession).not.toHaveBeenCalled();
  });

  it('Does not count a tile that went live when its neighbours are refused', () => {
    const hook = setup(['a', 'b', 'c']);
    refuse(hook, 'a');
    refuse(hook, 'b');

    act(() => hook.result.current.reportLive('a'));
    refuse(hook, 'c', 'unauthorized', 1_000);

    expect(hook.result.current.state).toBe('idle');
    expect(renewSession).not.toHaveBeenCalled();
  });

  it('Lifts the refused state when a tile goes live without the access token changing', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    recordRenewalAnswer(true);
    await completeRenewal(undefined);
    expect(hook.result.current.state).toBe('refused');

    act(() => hook.result.current.reportLive('a'));

    expect(hook.result.current.state).toBe('idle');
  });

  it('Clears the refused state and remounts once when the access token changes', async () => {
    const hook = setup(['a', 'b']);
    refuse(hook, 'a');
    refuse(hook, 'b');
    recordRenewalAnswer(true);
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
