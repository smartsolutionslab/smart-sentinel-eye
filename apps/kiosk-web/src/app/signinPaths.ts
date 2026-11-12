export function redirectUri(origin: string, base: string): string {
  return `${origin}${base}oidc/callback`;
}

// `//host` and `/\host` are read by browsers as another origin, which holds even when the base is `/`.
const otherOrigin = /^\/[/\\]/;

export function returnPath(stored: unknown, base: string): string {
  return typeof stored === 'string' && stored.startsWith(base) && !otherOrigin.test(stored) ? stored : base;
}

export function routerBasename(base: string): string {
  return base.replace(/\/$/, '');
}
