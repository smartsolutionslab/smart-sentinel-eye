import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { parseNavManifest, type NavEntry } from '@smart-sentinel-eye/shared/navigation/navManifest';
import { remotes as defaultRemotes, type RemoteDescriptor } from './remotes.js';
import { shellEntries } from './shellEntries.js';

/**
 * Plan.md §4.3 — a bound, not a tuning knob: a down remote must not hold the
 * whole shell's navigation hostage. One constant, no configuration.
 */
export const MANIFEST_TIMEOUT_MS = 5_000;

interface NavigationContextValue {
  readonly status: 'loading' | 'ready';
  readonly entries: readonly NavEntry[];
  readonly remoteFor: (path: string) => string | undefined;
}

const NavigationContext = createContext<NavigationContextValue | undefined>(undefined);

export function useNavigation(): NavigationContextValue {
  const value = useContext(NavigationContext);
  if (value === undefined) {
    throw new Error('useNavigation must be used within a NavigationProvider');
  }
  return value;
}

export interface NavigationProviderProps {
  // Test seams (plan.md §4.3's registry and 5 s bound), not real runtime
  // configuration: production always falls back to the real remote registry
  // and the real bound.
  readonly remotes?: readonly RemoteDescriptor[];
  readonly manifestTimeoutMs?: number;
  readonly children: ReactNode;
}

function describeError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** Equal, or one a path-segment-wise prefix of the other (FR-015's collision rule). */
function pathsCollide(a: string, b: string): boolean {
  return a === b || a.startsWith(`${b}/`) || b.startsWith(`${a}/`);
}

type ManifestOutcome = ReturnType<typeof parseNavManifest>;

/**
 * Races the manifest fetch against its own timer rather than relying solely
 * on the request honouring an abort signal: a remote that never answers (or,
 * in a test, a fetch stub that ignores the signal entirely) must still
 * settle within `timeoutMs`.
 */
async function fetchManifest(remote: RemoteDescriptor, timeoutMs: number): Promise<ManifestOutcome> {
  const controller = new AbortController();
  const abortTimer = setTimeout(() => controller.abort(), timeoutMs);
  let raceTimer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<never>((_resolve, reject) => {
    raceTimer = setTimeout(() => reject(new Error(`manifest request timed out after ${timeoutMs}ms`)), timeoutMs);
  });

  try {
    const response = await Promise.race([
      fetch(`${remote.baseUrl}/nav-manifest.json`, { signal: controller.signal }),
      timeout,
    ]);
    if (!response.ok) {
      return { ok: false, reason: `manifest request failed with status ${response.status}` };
    }
    const body: unknown = await response.json();
    return parseNavManifest(body);
  } catch (error) {
    return { ok: false, reason: describeError(error) };
  } finally {
    clearTimeout(abortTimer);
    clearTimeout(raceTimer);
  }
}

/**
 * Plan.md §4.3 — fetches and validates every registered remote's manifest
 * once per session (mounted in `RoutedApp`, after authentication), fails
 * closed per manifest (FR-015), and exposes the composed in-shell + accepted
 * remote entries for `useVisibleEntries`/route gating to filter by scope.
 *
 * Remotes are processed in registration order, one at a time: a basePath
 * collision must have a deterministic winner (the earlier-registered
 * remote), not a race between concurrent fetches.
 */
export function NavigationProvider({
  remotes = defaultRemotes,
  manifestTimeoutMs = MANIFEST_TIMEOUT_MS,
  children,
}: NavigationProviderProps) {
  // Lazy initial state, not a reset at the top of the effect below: `remotes`
  // and `manifestTimeoutMs` are test seams (see the doc comment above), never
  // reassigned by production code after mount, so there is no real "the
  // registry changed under a mounted provider" case to resynchronise state
  // for — only the one-time fetch loop below needs to run.
  const [entries, setEntries] = useState<readonly NavEntry[]>(shellEntries);
  const [remoteForPath, setRemoteForPath] = useState<ReadonlyMap<string, string>>(new Map());
  const [status, setStatus] = useState<'loading' | 'ready'>(() => (remotes.length === 0 ? 'ready' : 'loading'));

  useEffect(() => {
    if (remotes.length === 0) {
      return undefined;
    }

    let cancelled = false;
    const acceptedBasePaths: string[] = [];

    void (async () => {
      for (const remote of remotes) {
        const result = await fetchManifest(remote, manifestTimeoutMs);
        if (cancelled) return;

        if (!result.ok) {
          logResilienceEvent('navigation', 'manifest-rejected', { remote: remote.name, reason: result.reason });
          continue;
        }

        const { manifest } = result;

        if (manifest.remote !== remote.name) {
          logResilienceEvent('navigation', 'manifest-rejected', {
            remote: remote.name,
            reason: `manifest declares remote "${manifest.remote}", which does not match the registered remote "${remote.name}"`,
          });
          continue;
        }

        const collides =
          shellEntries.some((entry) => pathsCollide(entry.path, manifest.basePath)) ||
          acceptedBasePaths.some((basePath) => pathsCollide(basePath, manifest.basePath));

        if (collides) {
          logResilienceEvent('navigation', 'manifest-rejected', {
            remote: remote.name,
            reason: `basePath "${manifest.basePath}" collides with an already-accepted entry`,
          });
          continue;
        }

        acceptedBasePaths.push(manifest.basePath);
        setEntries((previous) => [...previous, ...manifest.entries]);
        setRemoteForPath((previous) => {
          const next = new Map(previous);
          for (const entry of manifest.entries) next.set(entry.path, remote.name);
          return next;
        });
      }

      if (!cancelled) setStatus('ready');
    })();

    return () => {
      cancelled = true;
    };
  }, [remotes, manifestTimeoutMs]);

  const value = useMemo<NavigationContextValue>(
    () => ({ status, entries, remoteFor: (path: string) => remoteForPath.get(path) }),
    [status, entries, remoteForPath],
  );

  return <NavigationContext.Provider value={value}>{children}</NavigationContext.Provider>;
}
