/**
 * Thrown by `RemoteSurface` during render once it has observed a load
 * failure (plan 316 §4.4, FR-007/FR-008) — `ShellLayout`'s `SurfaceCrash`
 * branches on it to show "this surface could not be loaded" instead of the
 * generic crash panel.
 *
 * <p>
 * Deliberately its own file, not declared alongside `RemoteSurface`: a
 * static `import` is evaluated — depth-first, before the importing module's
 * own top-level code runs — the instant anything imports it, and
 * `RemoteSurface.tsx` statically imports `./remotes.ts`, which reads
 * `VITE_CAMERAS_REMOTE_URL` from `import.meta.env` at module scope.
 * `ShellLayout.tsx` needs this class only for the `instanceof` check below,
 * never the remote registry — pulling `RemoteSurface.tsx` in just for that
 * would make `ShellLayout.tsx`'s own static import chain read that
 * environment variable before any test gets a chance to stub it (observed
 * directly: `RemoteSurface.test.tsx` imports `ShellLayout` statically, then
 * calls `vi.stubEnv` — too late once a static import chain already ran).
 * </p>
 */
export class RemoteLoadFailure extends Error {
  readonly remote: string;

  constructor(remote: string, reason: string) {
    super(`The "${remote}" remote could not be loaded: ${reason}`);
    this.name = 'RemoteLoadFailure';
    this.remote = remote;
  }
}
