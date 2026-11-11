/**
 * The shell's dev-server proxy table (ADR-0192): `/api` and `/remotes/<name>` as the gateway answers them on the one origin, plus
 * the bare `/<context>/...` REST paths the Playwright suite calls on the origin it captured. A bare path is proxied only when the
 * request is not a page navigation, the gateway's own rule, because two shell pages share a first segment with a REST route.
 */

export const REST_CONTEXTS = [
  'camera-catalog',
  'stream-distribution',
  'layout-composition',
  'event-ingestion',
  'overlay-designer',
  'system-variables',
  'audit-observability',
  'automation',
  'identity',
] as const;

interface ProxyRequest {
  readonly url?: string;
  readonly headers: Readonly<Record<string, string | string[] | undefined>>;
}

export interface DevProxyEntry {
  readonly target: string;
  readonly changeOrigin: true;
  readonly ws?: true;
  readonly bypass?: (request: ProxyRequest) => string | undefined;
}

export interface DevProxyRemotes {
  readonly cameras: string;
  readonly eventIngestion: string;
}

const isPageNavigation = (request: ProxyRequest): boolean => request.headers['sec-fetch-dest'] === 'document';

// Vite treats a returned string as "serve this path yourself" and skips the proxy; `undefined` proxies the request.
const proxyUnlessNavigation = (request: ProxyRequest): string | undefined =>
  isPageNavigation(request) ? request.url : undefined;

/** The dev-server proxy table, or `undefined` when there is no gateway to proxy to (unit tests, a preview build). */
export function devProxy(
  gatewayOrigin: string | undefined,
  remotes: DevProxyRemotes,
): Record<string, DevProxyEntry> | undefined {
  if (gatewayOrigin === undefined || gatewayOrigin === '') {
    return undefined;
  }

  const table: Record<string, DevProxyEntry> = {
    '/api': { target: gatewayOrigin, changeOrigin: true },
    '/remotes/cameras': { target: remotes.cameras, changeOrigin: true, ws: true },
    '/remotes/event-ingestion': { target: remotes.eventIngestion, changeOrigin: true, ws: true },
  };

  for (const context of REST_CONTEXTS) {
    // A leading `^` makes Vite read the key as a regular expression: the context followed by a slash or the end of the path,
    // so `/event-ingestion-x` is not taken for `/event-ingestion`.
    table[`^/${context}(/|$)`] = { target: gatewayOrigin, changeOrigin: true, bypass: proxyUnlessNavigation };
  }

  return table;
}
