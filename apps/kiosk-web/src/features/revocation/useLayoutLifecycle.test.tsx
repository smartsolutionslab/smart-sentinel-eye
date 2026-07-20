import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { Provider } from 'react-redux';
import type { ReactNode } from 'react';
import { layoutsApi } from '@smart-sentinel-eye/shared/api/layouts.api';
import { overlaysApi } from '@smart-sentinel-eye/shared/api/overlays.api';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import type {
  LayoutHubCallbacks,
  OverlayHighlightChangedMessage,
  ResolvedOverlayTextChangedMessage,
} from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { store } from '../../app/store.js';

// Capture the callbacks the hook hands to the (long-lived) hub so the test
// can fire an event after the consumer has re-rendered with new closures.
let capturedCallbacks: LayoutHubCallbacks | undefined;

vi.mock('@smart-sentinel-eye/shared/realtime/layoutHub', () => ({
  createLayoutHubClient: (_config: unknown, callbacks: LayoutHubCallbacks) => {
    capturedCallbacks = callbacks;
    return {
      start: () => Promise.resolve(),
      stop: () => Promise.resolve(),
      state: () => 'Connected',
    };
  },
}));

const { useLayoutLifecycle } = await import('./useLayoutLifecycle.js');

function wrapper({ children }: { children: ReactNode }) {
  return <Provider store={store}>{children}</Provider>;
}

describe('useLayoutLifecycle', () => {
  beforeEach(() => {
    capturedCallbacks = undefined;
  });

  // Regression: the hub connection is built once, but its callbacks must
  // invoke the LATEST options — mirrors CellPage, whose overlayIdentifier is
  // null at mount and only resolves after the layout query lands. Before the
  // fix the connection captured the mount-time closure and these events
  // silently no-op'd forever.
  it('invokes the latest callback after the consumer re-renders post-mount', () => {
    const accessTokenFactory = () => 'token';
    const first = vi.fn();
    const second = vi.fn();

    const { rerender } = renderHook(
      ({ onChanged }: { onChanged: (message: ResolvedOverlayTextChangedMessage) => void }) =>
        useLayoutLifecycle({ accessTokenFactory, onResolvedOverlayTextChanged: onChanged }),
      { wrapper, initialProps: { onChanged: first } },
    );

    // Consumer re-renders with a new closure (e.g. overlayIdentifier resolved).
    rerender({ onChanged: second });

    const message: ResolvedOverlayTextChangedMessage = {
      overlay: 'ovl-1',
      resolvedText: 'Live value',
      version: 2,
    };
    capturedCallbacks?.onResolvedOverlayTextChanged?.(message);

    expect(second).toHaveBeenCalledWith(message);
    expect(first).not.toHaveBeenCalled();
  });

  // Spec 010 US3: the new overlay-highlight frame is forwarded to the
  // consumer's `onOverlayHighlightChanged` through the same latest-options ref.
  it('forwards OverlayHighlightChanged frames to the latest callback', () => {
    const accessTokenFactory = () => 'token';
    const onHighlight = vi.fn();

    renderHook(
      ({ onChanged }: { onChanged: (message: OverlayHighlightChangedMessage) => void }) =>
        useLayoutLifecycle({ accessTokenFactory, onOverlayHighlightChanged: onChanged }),
      { wrapper, initialProps: { onChanged: onHighlight } },
    );

    const message: OverlayHighlightChangedMessage = { overlay: 'ovl-1', durationMs: 1500 };
    capturedCallbacks?.onOverlayHighlightChanged?.(message);

    expect(onHighlight).toHaveBeenCalledWith(message);
  });

  // Spec 011 FR-008: on reconnect the kiosk reconciles everything push can
  // change while disconnected — layout lifecycle (incl. revocation via the
  // list refetch), overlay list, mounted per-overlay queries (bare type),
  // and resolved-text snapshots.
  it('Dispatches all four reconciliation invalidations on reconnect', () => {
    // Record raw dispatches — spying on the invalidateTags creators themselves
    // would strip the `.match` RTK's middleware relies on.
    const dispatchSpy = vi.spyOn(store, 'dispatch');

    renderHook(() => useLayoutLifecycle({ accessTokenFactory: () => 'token' }), { wrapper });

    act(() => {
      capturedCallbacks?.onReconnected?.();
    });

    const dispatched: unknown[] = dispatchSpy.mock.calls.map(([action]) => action);
    const payloadsOf = (creator: { match: (action: unknown) => boolean }) =>
      dispatched.filter((action) => creator.match(action)).map((action) => (action as { payload: unknown }).payload);

    expect(payloadsOf(layoutsApi.util.invalidateTags)).toContainEqual([{ type: 'LayoutList', id: 'ALL' }]);
    expect(payloadsOf(overlaysApi.util.invalidateTags)).toContainEqual([{ type: 'OverlayList', id: 'ALL' }]);
    expect(payloadsOf(overlaysApi.util.invalidateTags)).toContainEqual(['Overlay']);
    expect(payloadsOf(systemVariablesApi.util.invalidateTags)).toContainEqual([{ type: 'OverlaySnapshot', id: 'ALL' }]);
  });

  // Spec 011 FR-007: the degraded flag drives the discreet badge; it flips
  // with the hub's state events and clears on reconnection.
  it('Flips degraded with the hub state events', () => {
    const { result } = renderHook(() => useLayoutLifecycle({ accessTokenFactory: () => 'token' }), { wrapper });

    expect(result.current.degraded).toBe(false);

    act(() => {
      capturedCallbacks?.onStateChange?.('degraded');
    });
    expect(result.current.degraded).toBe(true);

    act(() => {
      capturedCallbacks?.onStateChange?.('connected');
    });
    expect(result.current.degraded).toBe(false);
  });
});
