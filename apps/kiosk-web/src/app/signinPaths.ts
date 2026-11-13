export function redirectUri(origin: string, base: string): string {
  return `${origin}${base}oidc/callback`;
}

// Resolved before the check: the browser collapses dot segments, so `/kiosk/../wall/x` would leave the base.
export function returnPath(stored: unknown, base: string, origin: string): string {
  if (typeof stored !== 'string') {
    return base;
  }
  const resolved = new URL(stored, origin);
  return resolved.origin === origin && resolved.pathname.startsWith(base) ? resolved.pathname + resolved.search : base;
}

export function routerBasename(base: string): string {
  return base.replace(/\/$/, '');
}
