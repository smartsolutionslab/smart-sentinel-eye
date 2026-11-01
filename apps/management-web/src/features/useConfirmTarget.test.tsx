import { act, renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useConfirmTarget } from './useConfirmTarget.js';

interface Target {
  name: string;
  version: number;
}

describe('useConfirmTarget', () => {
  it('Starts with nothing to confirm', () => {
    const { result } = renderHook(() => useConfirmTarget<Target>());

    expect(result.current.target).toBeNull();
  });

  it('Holds the target it is asked about, and drops it on cancel', () => {
    const { result } = renderHook(() => useConfirmTarget<Target>());

    act(() => result.current.ask({ name: 'a', version: 3 }));
    expect(result.current.target).toEqual({ name: 'a', version: 3 });

    act(() => result.current.cancel());
    expect(result.current.target).toBeNull();
  });

  it('Runs the action with the target, then clears it', () => {
    const { result } = renderHook(() => useConfirmTarget<Target>());
    const run = vi.fn();
    act(() => result.current.ask({ name: 'a', version: 3 }));

    act(() => result.current.confirm(run)());

    expect(run).toHaveBeenCalledTimes(1);
    expect(run).toHaveBeenCalledWith({ name: 'a', version: 3 });
    expect(result.current.target).toBeNull();
  });

  it('Keeps the target when the action throws, because it clears only after the action ran', () => {
    const { result } = renderHook(() => useConfirmTarget<Target>());
    const run = vi.fn(() => {
      throw new Error('refused');
    });
    act(() => result.current.ask({ name: 'a', version: 3 }));
    let thrown: unknown;

    act(() => {
      try {
        result.current.confirm(run)();
      } catch (error) {
        thrown = error;
      }
    });

    expect(thrown).toEqual(new Error('refused'));
    expect(result.current.target).toEqual({ name: 'a', version: 3 });
  });

  it('Does nothing when there is no target', () => {
    const { result } = renderHook(() => useConfirmTarget<Target>());
    const run = vi.fn();

    act(() => result.current.confirm(run)());

    expect(run).not.toHaveBeenCalled();
    expect(result.current.target).toBeNull();
  });

  it('Confirms the latest target, not the one held when the handler was first built', () => {
    const { result } = renderHook(() => useConfirmTarget<Target>());
    const run = vi.fn();
    act(() => result.current.ask({ name: 'first', version: 1 }));
    act(() => result.current.ask({ name: 'second', version: 2 }));

    act(() => result.current.confirm(run)());

    expect(run).toHaveBeenCalledWith({ name: 'second', version: 2 });
  });
});
