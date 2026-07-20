import { useEffect, useState } from 'react';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';

export const CRASH_COUNT_STORAGE_KEY = 'sse.crash.count';
export const CRASH_LAST_AT_STORAGE_KEY = 'sse.crash.lastAt';

// Reload watchdog ladder (spec 011 research R8, data-model §3): 5/15/60 s by
// crash count, capped at 60 s so a persistent crash never hot-loops the device.
const RELOAD_DELAY_LADDER_MS: readonly number[] = [5_000, 15_000, 60_000];
const STABILITY_WINDOW_MS = 5 * 60_000;

interface CrashState {
  count: number;
  delayMs: number;
}

// Pure read so the lazy state initializer stays side-effect free (StrictMode
// double-invokes it); the effect below persists the computed count.
function readCrashState(): CrashState {
  const lastAtRaw = window.sessionStorage.getItem(CRASH_LAST_AT_STORAGE_KEY);
  const stableAgain = lastAtRaw !== null && Date.now() - Number(lastAtRaw) > STABILITY_WINDOW_MS;
  const previousCount = stableAgain ? 0 : Number(window.sessionStorage.getItem(CRASH_COUNT_STORAGE_KEY)) || 0;
  const count = previousCount + 1;
  const delayMs = RELOAD_DELAY_LADDER_MS[Math.min(count, RELOAD_DELAY_LADDER_MS.length) - 1] ?? 60_000;
  return { count, delayMs };
}

// The dev crash trigger (?crash=render) must not survive the reload, or the
// recovered page would crash again immediately.
function stripCrashTriggerParam(): void {
  const url = new URL(window.location.href);
  if (!url.searchParams.has('crash')) {
    return;
  }
  url.searchParams.delete('crash');
  window.history.replaceState(window.history.state, '', url);
}

/**
 * ErrorBoundary fallback for the kiosk (spec 011 FR-016): an unattended wall
 * cannot wait for a human, so it reloads itself back to the same URL after a
 * crash-count backoff, clearing whatever corrupted client state threw.
 */
export function KioskCrashRecovery() {
  const [{ count, delayMs }] = useState(readCrashState);

  useEffect(() => {
    window.sessionStorage.setItem(CRASH_COUNT_STORAGE_KEY, String(count));
    window.sessionStorage.setItem(CRASH_LAST_AT_STORAGE_KEY, String(Date.now()));
    logResilienceEvent('crash', 'reload-scheduled', { delayMs, count });
    const timer = setTimeout(() => {
      stripCrashTriggerParam();
      window.location.reload();
    }, delayMs);
    return () => clearTimeout(timer);
  }, [count, delayMs]);

  return (
    <main
      role="alert"
      className="flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base text-fg-primary"
    >
      <h1 className="text-3xl font-semibold">Recovering…</h1>
      <p className="text-fg-muted">
        The display hit an unexpected error and will reload in {Math.round(delayMs / 1000)} seconds.
      </p>
    </main>
  );
}
