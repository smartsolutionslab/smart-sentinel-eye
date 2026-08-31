import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import type { AuthContextProps } from 'react-oidc-context';
import { ErrorResponse } from 'oidc-client-ts';

const renewer = vi.hoisted(() => ({ current: undefined as (() => Promise<boolean>) | undefined }));

vi.mock('@smart-sentinel-eye/shared/api/gateway', () => ({
  setSessionRenewer: (fn: () => Promise<boolean>) => {
    renewer.current = fn;
  },
  setOnSessionExpired: () => undefined,
  setAccessTokenProvider: () => undefined,
}));

const { useSessionExpiry, WAS_AUTHENTICATED_STORAGE_KEY } = await import('./useSessionExpiry.js');

const noopUnsubscribe = () => undefined;

/** Auth as the library reports it for a screen whose session has gone. */
const authWith = (overrides: Partial<AuthContextProps> = {}) =>
  ({
    isAuthenticated: false,
    isLoading: false,
    error: undefined,
    user: undefined,
    activeNavigator: undefined,
    signinRedirect: vi.fn(() => Promise.resolve()),
    signinSilent: vi.fn(() => Promise.resolve(null)),
    events: {
      addSilentRenewError: vi.fn(() => noopUnsubscribe),
      addAccessTokenExpired: vi.fn(() => noopUnsubscribe),
    },
    ...overrides,
  }) as unknown as AuthContextProps;

describe('The renewer keeps its contract (spec 051 T011)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    renewer.current = undefined;
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => vi.restoreAllMocks());

  /**
   * **The gateway's 401 path depends on this boolean and can see none of the
   * screens this feature adds.**
   *
   * <p>
   * Classifying the rejection means touching the one line that used to discard
   * it. If that changed what the renewer resolves to, every authenticated
   * request would misread a failed renewal — and nothing on any screen would
   * show it.
   * </p>
   */
  it('Still resolves false when renewal fails, having classified the cause on the way past', async () => {
    const auth = authWith({
      signinSilent: vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))),
    } as Partial<AuthContextProps>);

    renderHook(() => useSessionExpiry(auth));

    expect(renewer.current).toBeDefined();
    await expect(renewer.current?.()).resolves.toBe(false);
  });

  it('Still resolves true when renewal succeeds', async () => {
    const auth = authWith({
      signinSilent: vi.fn(() => Promise.resolve({ access_token: 'a' })),
    } as unknown as Partial<AuthContextProps>);

    renderHook(() => useSessionExpiry(auth));

    await expect(renewer.current?.()).resolves.toBe(true);
  });

  it('Still resolves false when renewal returns no user', async () => {
    renderHook(() => useSessionExpiry(authWith()));

    await expect(renewer.current?.()).resolves.toBe(false);
  });
});

describe('A refused screen is never redirected (spec 051 US2)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => vi.restoreAllMocks());

  /**
   * **The mechanism behind FR-007, asserted directly.** Redirecting a refused
   * screen is what puts the provider's login form on a factory wall. The verdict
   * has to be known before that call, and this fails if the call happens anyway.
   */
  it('Reaches the refused verdict without calling signinRedirect', async () => {
    window.localStorage.setItem(WAS_AUTHENTICATED_STORAGE_KEY, 'true');
    const auth = authWith({
      signinSilent: vi.fn(() =>
        Promise.reject(new ErrorResponse({ error: 'invalid_grant', error_description: 'User disabled' })),
      ),
    } as Partial<AuthContextProps>);

    const { result } = renderHook(() => useSessionExpiry(auth));

    await waitFor(() => expect(result.current.identityFailure).toBe('refused'));
    expect(auth.signinRedirect).not.toHaveBeenCalled();
  });

  /**
   * The other side of it: an absent provider must not be mistaken for a refusal,
   * and must start the retry loop rather than asking for a person.
   */
  it('Reaches the recoverable verdict for an absent provider, and starts trying', async () => {
    window.localStorage.setItem(WAS_AUTHENTICATED_STORAGE_KEY, 'true');
    const auth = authWith({
      signinSilent: vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))),
    } as Partial<AuthContextProps>);

    const { result } = renderHook(() => useSessionExpiry(auth));

    await waitFor(() => expect(result.current.identityFailure).toBe('recoverable'));
    expect(auth.signinRedirect).not.toHaveBeenCalled();
    await waitFor(() => expect(result.current.attempt).toBeGreaterThanOrEqual(1));
  });

  /**
   * **The wall coming back, with nothing touched** (US1). The provider starts
   * refusing, then recovers; the verdict must clear without any manual call.
   */
  it('Clears the failure by itself once the provider answers again', async () => {
    window.localStorage.setItem(WAS_AUTHENTICATED_STORAGE_KEY, 'true');
    let healthy = false;
    const auth = authWith({
      signinSilent: vi.fn(() =>
        healthy ? Promise.resolve({ access_token: 'a' }) : Promise.reject(new TypeError('Failed to fetch')),
      ),
    } as unknown as Partial<AuthContextProps>);

    const { result } = renderHook(() => useSessionExpiry(auth));
    await waitFor(() => expect(result.current.identityFailure).toBe('recoverable'));

    healthy = true;

    // **Nothing is touched.** No button is pressed and `retryNow` is not called
    // — the loop has to arrive on its own, which is the entire claim. An earlier
    // version of this test called `retryNow` to shorten the wait, and would have
    // passed against a screen that only ever recovers when somebody presses
    // something. That screen is the defect.
    await waitFor(() => expect(result.current.identityFailure).toBeUndefined(), { timeout: 10_000 });
    expect(auth.signinRedirect).not.toHaveBeenCalled();
  });

  /**
   * The manual control still works, and resetting the schedule is all it does —
   * there is no second loop to start (FR-013).
   */
  it('Lets a person shorten the wait without starting a second loop', async () => {
    window.localStorage.setItem(WAS_AUTHENTICATED_STORAGE_KEY, 'true');
    let healthy = false;
    const silent = vi.fn(() =>
      healthy ? Promise.resolve({ access_token: 'a' }) : Promise.reject(new TypeError('Failed to fetch')),
    );
    const auth = authWith({ signinSilent: silent } as unknown as Partial<AuthContextProps>);

    const { result } = renderHook(() => useSessionExpiry(auth));
    await waitFor(() => expect(result.current.identityFailure).toBe('recoverable'));

    healthy = true;
    const before = silent.mock.calls.length;
    act(() => result.current.retryNow());

    await waitFor(() => expect(result.current.identityFailure).toBeUndefined());
    // One replacement attempt, not a second loop running alongside the first.
    expect(silent.mock.calls.length).toBeLessThanOrEqual(before + 2);
  });
});
