import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';

let auth: { user?: { scope?: string } } | undefined;

vi.mock('react-oidc-context', () => ({ useAuth: () => auth }));

const { useCanEditOverlays } = await import('./useCanEditOverlays');

describe('useCanEditOverlays', () => {
  beforeEach(() => {
    auth = undefined;
  });

  it('is true when the session was granted the overlay write scope', () => {
    auth = { user: { scope: 'openid sse.layouts.read sse.overlays.read sse.overlays.write' } };
    expect(renderHook(() => useCanEditOverlays()).result.current).toBe(true);
  });

  it('is false when the session only reads overlays', () => {
    auth = { user: { scope: 'openid sse.overlays.read sse.layouts.write' } };
    expect(renderHook(() => useCanEditOverlays()).result.current).toBe(false);
  });

  it('is false with no signed-in user or no AuthProvider', () => {
    expect(renderHook(() => useCanEditOverlays()).result.current).toBe(false);
    auth = { user: undefined };
    expect(renderHook(() => useCanEditOverlays()).result.current).toBe(false);
  });

  it('does not take a scope that merely contains the name', () => {
    auth = { user: { scope: 'sse.overlays.write.extra' } };
    expect(renderHook(() => useCanEditOverlays()).result.current).toBe(false);
  });
});
