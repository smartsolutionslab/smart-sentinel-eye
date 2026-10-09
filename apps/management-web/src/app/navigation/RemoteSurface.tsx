import { useEffect, useState, type ComponentType } from 'react';
import { getInstance, loadRemote, registerRemotes } from '@module-federation/runtime';
import { logResilienceEvent } from '@smart-sentinel-eye/shared/observability/resilienceLog';
import { RemoteLoadFailure } from './RemoteLoadFailure.js';
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
 * decision 2026-10-08): the ordinary `loadRemote` rejection / bad-module-shape
 * path, caught directly below, and the runtime's own `errorLoadRemote`
 * lifecycle hook — the only signal that fires for the realistic
 * shared-dependency mismatch case, where `loadRemote`'s promise never settles
 * at all. Whichever fires first wins; the `settled` guard discards the other.
 * </p>
 */
export function RemoteSurface({ remote }: RemoteSurfaceProps) {
  const [failure, setFailure] = useState<RemoteLoadFailure | null>(null);
  const [Component, setComponent] = useState<ComponentType | null>(null);

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
      logResilienceEvent('crash', 'remote-load-failed', { remote, message: reason });
      setFailure(new RemoteLoadFailure(remote, reason));
    };

    const entry = remotes.find((candidate) => candidate.name === remote);
    if (entry === undefined) {
      fail(`no registered remote named "${remote}"`);
      return undefined;
    }

    // Real @module-federation/runtime 2.9.2: `errorLoadRemote` lives on the
    // REMOTE handler's own hooks (`ModuleFederation.remoteHandler.hooks
    // .lifecycle.errorLoadRemote`) — confirmed by reading the installed
    // package's `core.d.ts`/`core.js`, not on the top-level instance's own
    // `hooks` (which carries only beforeInit/init/beforeInitContainer
    // /initContainer). Optional chaining throughout: a missing instance or
    // hook degrades to "this failure mode goes unobserved", never a crash.
    const errorLoadRemoteHook = getInstance()?.remoteHandler.hooks.lifecycle.errorLoadRemote;
    const onErrorLoadRemote = (payload: { id: string; error: unknown }) => {
      if (payload.id !== remote) return;
      fail(describeError(payload.error));
    };
    errorLoadRemoteHook?.on(onErrorLoadRemote);

    const attempt = nextAttempt(remote);
    // force: true clears the federation runtime's own bookkeeping; the
    // ?retry= query parameter busts the browser's dynamic-import() cache for
    // the literal URL, which force: true alone does not (T001 finding item
    // 4) — both are necessary, neither alone is sufficient.
    //
    // type: 'module' is load-bearing, not a default: with no `type`, the
    // runtime injects remoteEntry.js as a classic <script>, and Vite's dev
    // server serves it as a real ES module (`import ... from ...` lines,
    // T001 finding item 1) — a classic script can't parse those, and the
    // browser throws "Cannot use import statement outside a module" (seen
    // live, diagnosed directly against the real dev server, not predicted).
    registerRemotes([{ name: remote, type: 'module', entry: `${entry.baseUrl}/remoteEntry.js?retry=${attempt}` }], {
      force: true,
    });

    loadRemote<RemoteSurfaceModule>(exposedModuleKey(remote))
      .then((module) => {
        if (settled) return;
        if (module === null || typeof module.default !== 'function') {
          fail('the remote module has no default export');
          return;
        }
        settled = true;
        setComponent(() => module.default as ComponentType);
      })
      .catch((error: unknown) => {
        fail(describeError(error));
      });

    return () => {
      settled = true;
      errorLoadRemoteHook?.remove?.(onErrorLoadRemote);
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
