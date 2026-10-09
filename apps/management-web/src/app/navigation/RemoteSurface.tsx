import { useEffect, useState, type ComponentType } from 'react';
import { loadRemote, registerRemotes } from '@module-federation/runtime';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { RemoteLoadFailure } from './RemoteLoadFailure.js';
import { onSharedVersionMismatch } from './mismatchRuntimePlugin.js';
import { remotes } from './remotes.js';

export interface RemoteSurfaceProps {
  readonly remote: string;
}

/**
 * Plan 316 §2.2: the shape every remote's exposed surface module must have.
 * Stays local until T011, which moves the nav-manifest contract (and this
 * type with it) to `apps/shared/src/navigation/remoteSurface.ts` — nothing
 * there yet to import from.
 */
type RemoteSurfaceModule = { readonly default?: unknown };

export { RemoteLoadFailure };

// Module scope, not component state (plan.md's 2026-10-08 decision, T001
// finding item 4): a retry re-navigates to the same path, which remounts
// this component fresh (SurfaceCrash's own established pattern — re-navigating
// resets the router's error boundary). A counter held in this component's own
// state would reset to the same value on every remount, defeating the one
// thing that actually busts the browser's own dynamic-import() cache for the
// literal entry URL (T001: `force: true` alone is necessary but not
// sufficient). One counter per remote name, surviving every remount.
const attemptCounters = new Map<string, number>();

function nextAttempt(remote: string): number {
  const attempt = (attemptCounters.get(remote) ?? 0) + 1;
  attemptCounters.set(remote, attempt);
  return attempt;
}

// B4 fix (spec 316 phase-6 review): a successful load's component, cached so
// a remount after success "incurs no fetch" (plan §5.3) — the index route and
// `cameras/*` are two separate route objects, so navigating between them
// remounts this component on every click, not only on an actual retry.
// `failedRemotes` is the other half: only a remote whose *previous* attempt
// genuinely failed re-registers with `force: true` and a bumped `?retry=`
// counter: a fresh or successful remote never pays either cost.
const resolvedComponents = new Map<string, ComponentType>();
const failedRemotes = new Set<string>();

// ADR-0168 §4's naming convention: a remote named "cameras" exposes
// "./CamerasSurface" (plan §5.1). One remote exists today — this is the
// naming the first one already follows, not a lookup table for a need that
// doesn't exist yet (CLAUDE.md: no speculative generality).
function exposedModuleKey(remote: string): string {
  return `${remote}/${remote.charAt(0).toUpperCase()}${remote.slice(1)}Surface`;
}

function describeError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * Loads one federated remote's exposed surface into the shell (plan 316 §4.4,
 * FR-003/FR-007/FR-008). Registers `registerRemotes`/`loadRemote` only once
 * rendered — because `<Gated>` wraps every remote route (T011), an
 * unentitled session never mounts this at all, so it never registers and
 * never fetches (FR-013).
 *
 * <p>
 * Two independent failure sources feed one state (T001 finding item 3,
 * decision 2026-10-08, revised B3 — spec 316 phase-6 review): the ordinary
 * `loadRemote` rejection / bad-module-shape path, caught directly below, and
 * a `resolveShare` runtime plugin (`mismatchRuntimePlugin.ts`, registered via
 * the shell's `vite.config.ts` `runtimePlugins`) — the one signal confirmed
 * to fire for the realistic shared-dependency mismatch case, where
 * `loadRemote`'s promise never settles at all. The remote handler's own
 * `errorLoadRemote` lifecycle hook was tried first and found dead for this:
 * its payload's `id` is never the bare remote name, so the filter this
 * component used to apply could never match. Whichever of the two sources
 * fires first wins; the `settled` guard discards the other.
 * </p>
 */
export function RemoteSurface({ remote }: RemoteSurfaceProps) {
  const [failure, setFailure] = useState<RemoteLoadFailure | null>(null);
  // B4 fix (spec 316 phase-6 review): a lazy initializer, not a setState call
  // inside the effect below — react-hooks/set-state-in-effect flags a
  // synchronous setState in an effect body, and this cache hit needs none:
  // the cached component is already known at the first render.
  const [Component, setComponent] = useState<ComponentType | null>(() => resolvedComponents.get(remote) ?? null);

  useEffect(() => {
    // No reset of `failure`/`Component` here: `remote` is a stable literal
    // prop (one remote exists today) and a retry remounts this component
    // fresh via `SurfaceCrash`'s navigate — so this effect body runs exactly
    // once per mounted instance, always starting from each `useState`'s own
    // initial `null`. Resetting here too would be a synchronous setState in
    // an effect body for a case this component never actually hits.
    let settled = false;

    const fail = (reason: string) => {
      if (settled) return;
      settled = true;
      failedRemotes.add(remote);
      logResilienceEvent('crash', 'remote-load-failed', { remote, message: reason });
      setFailure(new RemoteLoadFailure(remote, reason));
    };

    const entry = remotes.find((candidate) => candidate.name === remote);
    if (entry === undefined) {
      fail(`no registered remote named "${remote}"`);
      return undefined;
    }

    // B4 fix (spec 316 phase-6 review): a remote already resolved once never
    // re-registers or re-fetches on a later mount — this is the cache that
    // makes a repeat visit "incur no fetch" (plan §5.3). The cached value
    // itself was already applied by useState's lazy initializer above; no
    // setState call belongs here.
    if (resolvedComponents.has(remote)) {
      return undefined;
    }

    // B3 fix (spec 316 phase-6 review): `resolveShare` fires on every
    // `loadShare` resolution in the share scope, not only this remote's —
    // acceptable today because exactly one remote exists (no speculative
    // generality for a filter this repo has no second remote to exercise).
    const unsubscribeMismatch = onSharedVersionMismatch(({ pkgName, version, requiredVersion }) => {
      fail(
        `shared module "${pkgName}" resolved to ${version}, which does not satisfy required version ${requiredVersion}`,
      );
    });

    // B4 fix (spec 316 phase-6 review): only a genuine retry — the
    // *previous* attempt for this remote actually failed — bumps the
    // `?retry=` counter and forces re-registration. An ordinary first mount
    // (never attempted, or still in flight when this instance unmounted)
    // pays for neither: `force: true` clears bookkeeping a fresh remote
    // doesn't have yet, and the cache-busting query parameter (T001 finding
    // item 4) only matters once the browser's dynamic-import() cache
    // already holds a failed entry for the literal URL.
    //
    // type: 'module' is load-bearing, not a default: with no `type`, the
    // runtime injects remoteEntry.js as a classic <script>, and Vite's dev
    // server serves it as a real ES module (`import ... from ...` lines,
    // T001 finding item 1) — a classic script can't parse those, and the
    // browser throws "Cannot use import statement outside a module" (seen
    // live, diagnosed directly against the real dev server, not predicted).
    const isRetry = failedRemotes.has(remote);
    const entryUrl = isRetry
      ? `${entry.baseUrl}/remoteEntry.js?retry=${nextAttempt(remote)}`
      : `${entry.baseUrl}/remoteEntry.js`;

    registerRemotes([{ name: remote, type: 'module', entry: entryUrl }], isRetry ? { force: true } : undefined);

    loadRemote<RemoteSurfaceModule>(exposedModuleKey(remote))
      .then((module) => {
        if (settled) return;
        if (module === null || typeof module.default !== 'function') {
          fail('the remote module has no default export');
          return;
        }
        settled = true;
        failedRemotes.delete(remote);
        const LoadedComponent = module.default as ComponentType;
        resolvedComponents.set(remote, LoadedComponent);
        setComponent(() => LoadedComponent);
      })
      .catch((error: unknown) => {
        fail(describeError(error));
      });

    return () => {
      settled = true;
      unsubscribeMismatch();
    };
  }, [remote]);

  if (failure !== null) {
    throw failure;
  }

  if (Component !== null) {
    return <Component />;
  }

  return (
    <p className="p-6 text-fg-muted" role="status">
      Loading…
    </p>
  );
}
