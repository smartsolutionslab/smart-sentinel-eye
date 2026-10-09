import { satisfy, type ModuleFederationRuntimePlugin } from '@module-federation/runtime';

/**
 * B3 fix (spec 316 phase-6 review): the module-federation runtime hook that
 * actually fires for FR-008's realistic case. An earlier version relied on
 * the REMOTE handler's `errorLoadRemote` lifecycle hook, filtered by
 * `payload.id === remote` — confirmed dead for a shared-dependency mismatch,
 * because the runtime never sends the bare remote name on that payload.
 *
 * `resolveShare` (`SharedHandler.hooks.lifecycle.resolveShare`, a
 * `SyncWaterfallHook`) fires on every `loadShare` resolution — deterministic,
 * independent of `strictVersion` — confirmed live against the installed
 * `@module-federation/runtime-core@2.9.2`: `version` is the resolved
 * candidate, `shareInfo.shareConfig.requiredVersion` is this consumer's own
 * pin. A waterfall hook must return its argument unchanged for every other
 * registered plugin to still run.
 */
export interface SharedVersionMismatch {
  readonly pkgName: string;
  readonly scope: string;
  readonly version: string;
  readonly requiredVersion: string;
}

type MismatchListener = (mismatch: SharedVersionMismatch) => void;

// Module scope, not a constructor argument: this file's default export is
// invoked by module-federation's own generated runtime-init code (the
// `runtimePlugins` host config entry), which `RemoteSurface` never
// constructs directly — the only channel between the two is the module's
// own shared state, same pattern as `attemptCounters` in RemoteSurface.tsx.
const listeners = new Set<MismatchListener>();

export function onSharedVersionMismatch(listener: MismatchListener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export default function mismatchRuntimePlugin(): ModuleFederationRuntimePlugin {
  return {
    name: 'sse-shared-version-mismatch-plugin',
    resolveShare(args) {
      const { pkgName, scope, version, shareInfo } = args;
      const requiredVersion = shareInfo?.shareConfig?.requiredVersion;
      if (version && typeof requiredVersion === 'string' && !satisfy(version, requiredVersion)) {
        for (const listener of listeners) {
          listener({ pkgName, scope, version, requiredVersion });
        }
      }
      return args;
    },
  };
}
