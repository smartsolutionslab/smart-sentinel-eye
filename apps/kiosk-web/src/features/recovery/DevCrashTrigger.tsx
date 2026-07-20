/**
 * Dev-only crash injector (spec 011 quickstart §4): `?crash=render` throws
 * during render so the ErrorBoundary/watchdog path can be exercised by hand.
 * `import.meta.env.DEV` is statically false in production builds, so Vite
 * dead-code-eliminates the throw — it cannot ship active.
 */
export function DevCrashTrigger(): null {
  if (
    import.meta.env.DEV &&
    new URLSearchParams(window.location.search).get('crash') === 'render'
  ) {
    throw new Error('Dev crash trigger: ?crash=render');
  }
  return null;
}
