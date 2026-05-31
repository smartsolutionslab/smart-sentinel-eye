import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { Provider } from 'react-redux';
import type { ReactNode } from 'react';
import type {
  LayoutHubCallbacks,
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
});
