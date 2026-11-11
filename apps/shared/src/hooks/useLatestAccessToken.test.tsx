// @vitest-environment jsdom
import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useLatestAccessToken } from './useLatestAccessToken';

describe('useLatestAccessToken', () => {
  it('keeps one function across re-renders and a token renewal', () => {
    const { result, rerender } = renderHook(({ token }) => useLatestAccessToken(token), {
      initialProps: { token: 'token-1' as string | undefined },
    });
    const first = result.current;

    rerender({ token: 'token-1' });
    expect(result.current).toBe(first);

    rerender({ token: 'token-2' });
    expect(result.current).toBe(first);
  });

  it('resolves the newest token when it is called, not the one from the render that created it', async () => {
    const { result, rerender } = renderHook(({ token }) => useLatestAccessToken(token), {
      initialProps: { token: 'token-1' as string | undefined },
    });
    const getToken = result.current;
    await expect(getToken()).resolves.toBe('token-1');

    rerender({ token: 'token-2' });

    await expect(getToken()).resolves.toBe('token-2');
  });

  it('resolves null, as a promise, when there is no token', async () => {
    const { result, rerender } = renderHook(({ token }) => useLatestAccessToken(token), {
      initialProps: { token: undefined as string | undefined },
    });

    const pending = result.current();
    expect(pending).toBeInstanceOf(Promise);
    await expect(pending).resolves.toBeNull();

    rerender({ token: 'token-1' });
    rerender({ token: undefined });
    await expect(result.current()).resolves.toBeNull();
  });
});
