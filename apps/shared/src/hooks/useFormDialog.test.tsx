import { renderHook } from '@testing-library/react';
import type { FormEvent } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { useClearOnClose, useResetOnOpen, useSubmitGuard } from './useFormDialog.js';

function formEvent() {
  const preventDefault = vi.fn();
  return { event: { preventDefault } as unknown as FormEvent, preventDefault };
}

describe('useClearOnClose', () => {
  it('Clears when it mounts closed', () => {
    const clear = vi.fn();

    renderHook(({ open }) => useClearOnClose(open, clear), { initialProps: { open: false } });

    expect(clear).toHaveBeenCalledTimes(1);
  });

  it('Does not clear while the dialog is open', () => {
    const clear = vi.fn();

    const { rerender } = renderHook(({ open }) => useClearOnClose(open, clear), { initialProps: { open: true } });
    rerender({ open: true });

    expect(clear).not.toHaveBeenCalled();
  });

  it('Clears when the parent flips open to false, which no onOpenChange handler would see', () => {
    const clear = vi.fn();
    const { rerender } = renderHook(({ open }) => useClearOnClose(open, clear), { initialProps: { open: true } });

    rerender({ open: false });

    expect(clear).toHaveBeenCalledTimes(1);
  });

  it('Does not fire again when only the callback changes identity (the issue-2624 property)', () => {
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = renderHook(({ clear }) => useClearOnClose(false, clear), { initialProps: { clear: first } });
    expect(first).toHaveBeenCalledTimes(1);

    rerender({ clear: second });

    expect(first).toHaveBeenCalledTimes(1);
    expect(second).not.toHaveBeenCalled();
  });

  it('Calls the latest callback when it fires, not the one from the render that scheduled it', () => {
    const stale = vi.fn();
    const latest = vi.fn();
    const { rerender } = renderHook(({ open, clear }) => useClearOnClose(open, clear), {
      initialProps: { open: true, clear: stale },
    });

    rerender({ open: false, clear: latest });

    expect(latest).toHaveBeenCalledTimes(1);
    expect(stale).not.toHaveBeenCalled();
  });
});

describe('useResetOnOpen', () => {
  it('Resets when it mounts open', () => {
    const reset = vi.fn();

    renderHook(({ open }) => useResetOnOpen(open, reset), { initialProps: { open: true } });

    expect(reset).toHaveBeenCalledTimes(1);
  });

  it('Resets when the dialog opens, and not while it stays closed', () => {
    const reset = vi.fn();
    const { rerender } = renderHook(({ open }) => useResetOnOpen(open, reset), { initialProps: { open: false } });
    expect(reset).not.toHaveBeenCalled();

    rerender({ open: true });

    expect(reset).toHaveBeenCalledTimes(1);
  });

  it('Does not reset when the dialog closes', () => {
    const reset = vi.fn();
    const { rerender } = renderHook(({ open }) => useResetOnOpen(open, reset), { initialProps: { open: true } });

    rerender({ open: false });

    expect(reset).toHaveBeenCalledTimes(1);
  });

  it('Does not fire again when only the callback changes identity (the issue-2624 property)', () => {
    const first = vi.fn();
    const second = vi.fn();
    const { rerender } = renderHook(({ reset }) => useResetOnOpen(true, reset), { initialProps: { reset: first } });
    expect(first).toHaveBeenCalledTimes(1);

    rerender({ reset: second });

    expect(first).toHaveBeenCalledTimes(1);
    expect(second).not.toHaveBeenCalled();
  });

  it('Calls the latest callback when it fires', () => {
    const stale = vi.fn();
    const latest = vi.fn();
    const { rerender } = renderHook(({ open, reset }) => useResetOnOpen(open, reset), {
      initialProps: { open: false, reset: stale },
    });

    rerender({ open: true, reset: latest });

    expect(latest).toHaveBeenCalledTimes(1);
    expect(stale).not.toHaveBeenCalled();
  });

  it('Fires again when the reset key changes while open (RenameCameraDialog resets on a new currentName)', () => {
    const reset = vi.fn();
    const { rerender } = renderHook(({ key }) => useResetOnOpen(true, reset, key), { initialProps: { key: 'Line-3' } });
    expect(reset).toHaveBeenCalledTimes(1);

    rerender({ key: 'Line-4' });

    expect(reset).toHaveBeenCalledTimes(2);
  });

  it('Does not fire again on a rerender when no key is given', () => {
    const reset = vi.fn();
    const { rerender } = renderHook(() => useResetOnOpen(true, reset));

    rerender();
    rerender();

    expect(reset).toHaveBeenCalledTimes(1);
  });
});

describe('useSubmitGuard', () => {
  it('Refuses a second submit while one is in flight: prevents the default and does not call onSubmit', () => {
    const onSubmit = vi.fn();
    const { result } = renderHook(() => useSubmitGuard(true, onSubmit));
    const { event, preventDefault } = formEvent();

    result.current(event);

    expect(preventDefault).toHaveBeenCalledTimes(1);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('Passes the event to onSubmit when nothing is in flight', () => {
    const onSubmit = vi.fn();
    const { result } = renderHook(() => useSubmitGuard(false, onSubmit));
    const { event, preventDefault } = formEvent();

    result.current(event);

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith(event);
    expect(preventDefault).not.toHaveBeenCalled();
  });

  it('Follows isLoading across rerenders', () => {
    const onSubmit = vi.fn();
    const { result, rerender } = renderHook(({ loading }) => useSubmitGuard(loading, onSubmit), {
      initialProps: { loading: true },
    });
    result.current(formEvent().event);
    expect(onSubmit).not.toHaveBeenCalled();

    rerender({ loading: false });
    result.current(formEvent().event);

    expect(onSubmit).toHaveBeenCalledTimes(1);
  });
});
