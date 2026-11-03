import type { FormEvent } from 'react';
import { renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useSaveGate } from './layoutEditor/useSaveGate.js';

/**
 * Spec 482 T003. What the layout editor's Save gate does with the form's submit event. The dialog tests pin that a blocked Save
 * does not submit; they do not pin that the blocked event is prevented, that the event is handed to `onSubmit` untouched when the
 * gate is open, nor that the gate leaves the event alone then. Green on today's code.
 */
function formEvent() {
  const preventDefault = vi.fn();
  return { event: { preventDefault } as unknown as FormEvent, preventDefault };
}

const open = {
  isLoading: false,
  knownCameras: new Map([['camera-1', {}]]),
  isEdit: false,
  currentChain: undefined,
  chainFetching: false,
  chainFailed: false,
};

describe('layout useSaveGate — the submit handler', () => {
  it('refuses a blocked submit: prevents the default and does not call onSubmit', () => {
    const onSubmit = vi.fn(async () => {});
    const { result } = renderHook(() => useSaveGate({ ...open, isLoading: true, onSubmit }));
    const { event, preventDefault } = formEvent();

    result.current.handleFormSubmit(event);

    expect(preventDefault).toHaveBeenCalledTimes(1);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('hands the very event to onSubmit once when the gate is open, and does not prevent the default itself', () => {
    const onSubmit = vi.fn(async () => {});
    const { result } = renderHook(() => useSaveGate({ ...open, onSubmit }));
    const { event, preventDefault } = formEvent();

    result.current.handleFormSubmit(event);

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith(event);
    expect(preventDefault).not.toHaveBeenCalled();
  });

  it('follows the gate across re-renders: blocked, then open', () => {
    const onSubmit = vi.fn(async () => {});
    const { result, rerender } = renderHook(({ isLoading }) => useSaveGate({ ...open, isLoading, onSubmit }), {
      initialProps: { isLoading: true },
    });
    result.current.handleFormSubmit(formEvent().event);
    expect(onSubmit).not.toHaveBeenCalled();

    rerender({ isLoading: false });
    result.current.handleFormSubmit(formEvent().event);

    expect(onSubmit).toHaveBeenCalledTimes(1);
  });
});
