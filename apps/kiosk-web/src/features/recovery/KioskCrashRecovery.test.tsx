import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CRASH_COUNT_STORAGE_KEY, CRASH_LAST_AT_STORAGE_KEY, KioskCrashRecovery } from './KioskCrashRecovery.js';

// jsdom's location.reload is own + non-configurable, so the whole location
// object is swapped for a stub (window's `location` property IS configurable).
const reloadMock = vi.fn();
const realLocationDescriptor = Object.getOwnPropertyDescriptor(window, 'location')!;

function stubLocation(href: string): void {
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: { href, reload: reloadMock },
  });
}

describe('KioskCrashRecovery reload watchdog', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-07-20T10:00:00Z'));
    stubLocation('http://localhost:3000/layouts/abc');
    vi.spyOn(window.history, 'replaceState').mockImplementation(() => undefined);
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
    reloadMock.mockClear();
    Object.defineProperty(window, 'location', realLocationDescriptor);
  });

  it('Shows the recovery notice and schedules the reload 5 s after the first crash', () => {
    render(<KioskCrashRecovery />);

    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByText(/reload in 5 seconds/i)).toBeInTheDocument();
    expect(window.sessionStorage.getItem(CRASH_COUNT_STORAGE_KEY)).toBe('1');
    expect(window.sessionStorage.getItem(CRASH_LAST_AT_STORAGE_KEY)).not.toBeNull();

    vi.advanceTimersByTime(4_999);
    expect(reloadMock).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(reloadMock).toHaveBeenCalledTimes(1);
  });

  it('Backs off to 15 s on the second crash and caps at 60 s from the third on', () => {
    const ladder: [previousCount: string, delayMs: number, notice: RegExp][] = [
      ['1', 15_000, /reload in 15 seconds/i],
      ['2', 60_000, /reload in 60 seconds/i],
      ['7', 60_000, /reload in 60 seconds/i],
    ];

    for (const [previousCount, delayMs, notice] of ladder) {
      window.sessionStorage.setItem(CRASH_COUNT_STORAGE_KEY, previousCount);
      window.sessionStorage.setItem(CRASH_LAST_AT_STORAGE_KEY, String(Date.now()));
      reloadMock.mockClear();

      render(<KioskCrashRecovery />);

      expect(screen.getByText(notice)).toBeInTheDocument();
      expect(window.sessionStorage.getItem(CRASH_COUNT_STORAGE_KEY)).toBe(String(Number(previousCount) + 1));
      vi.advanceTimersByTime(delayMs - 1);
      expect(reloadMock).not.toHaveBeenCalled();
      vi.advanceTimersByTime(1);
      expect(reloadMock).toHaveBeenCalledTimes(1);
      cleanup();
    }
  });

  it('Restarts the ladder at 5 s when the last crash is more than five minutes old', () => {
    window.sessionStorage.setItem(CRASH_COUNT_STORAGE_KEY, '5');
    window.sessionStorage.setItem(CRASH_LAST_AT_STORAGE_KEY, String(Date.now() - 6 * 60_000));

    render(<KioskCrashRecovery />);

    expect(screen.getByText(/reload in 5 seconds/i)).toBeInTheDocument();
    expect(window.sessionStorage.getItem(CRASH_COUNT_STORAGE_KEY)).toBe('1');

    vi.advanceTimersByTime(5_000);
    expect(reloadMock).toHaveBeenCalledTimes(1);
  });

  it('Strips the crash trigger param from the URL before reloading', () => {
    stubLocation('http://localhost:3000/layouts/abc?crash=render');
    const replaceState = vi.mocked(window.history.replaceState);

    render(<KioskCrashRecovery />);
    vi.advanceTimersByTime(5_000);

    expect(replaceState).toHaveBeenCalledTimes(1);
    const rewrittenUrl = new URL(String(replaceState.mock.calls[0]![2]));
    expect(rewrittenUrl.searchParams.has('crash')).toBe(false);
    expect(rewrittenUrl.pathname).toBe('/layouts/abc');
    expect(reloadMock).toHaveBeenCalledTimes(1);
    // The param must be gone BEFORE the reload navigates.
    expect(replaceState.mock.invocationCallOrder[0]!).toBeLessThan(reloadMock.mock.invocationCallOrder[0]!);
  });

  it('Leaves the URL untouched when there is no crash trigger param', () => {
    render(<KioskCrashRecovery />);
    vi.advanceTimersByTime(5_000);

    expect(window.history.replaceState).not.toHaveBeenCalled();
    expect(reloadMock).toHaveBeenCalledTimes(1);
  });
});
