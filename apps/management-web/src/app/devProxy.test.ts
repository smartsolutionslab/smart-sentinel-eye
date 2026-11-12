import { describe, expect, it } from 'vitest';
import { REST_CONTEXTS, devProxy, type DevProxyEntry } from './devProxy';

/**
 * Spec 491 (ADR-0192). The shell's dev server stands in for the gateway at :5173: `/api` and `/remotes/*` are proxied, and so are
 * the bare `/<context>/...` REST paths, except for a page navigation, which Vite must serve as the shell.
 */
const GATEWAY = 'http://gateway.test:5180';
const REMOTES = { cameras: 'http://localhost:5176', eventIngestion: 'http://localhost:5177' };

// Mirrors Vite's proxy matching: a key starting with ^ is a regular expression tested against the whole URL, any other key is a
// prefix, and the first entry in insertion order that matches wins.
const entryFor = (table: Record<string, DevProxyEntry>, url: string): DevProxyEntry | undefined => {
  const key = Object.keys(table).find((candidate) =>
    candidate.startsWith('^') ? new RegExp(candidate).test(url) : url.startsWith(candidate),
  );
  return key === undefined ? undefined : table[key];
};

describe('The shell dev-server proxy (spec 491)', () => {
  it('Is absent when there is no gateway to proxy to', () => {
    expect(devProxy(undefined, REMOTES)).toBeUndefined();
    expect(devProxy('', REMOTES)).toBeUndefined();
  });

  it('Proxies /api to the gateway, and each remote prefix to its own dev server with websockets', () => {
    const table = devProxy(GATEWAY, REMOTES)!;

    expect(entryFor(table, '/api/things')).toMatchObject({ target: GATEWAY, changeOrigin: true });
    expect(entryFor(table, '/api/things')?.bypass).toBeUndefined();
    expect(entryFor(table, '/remotes/cameras/remoteEntry.js')).toMatchObject({ target: REMOTES.cameras, ws: true });
    expect(entryFor(table, '/remotes/event-ingestion/remoteEntry.js')).toMatchObject({
      target: REMOTES.eventIngestion,
      ws: true,
    });
  });

  it('Proxies each bare REST context to the gateway, for a path with a slash or at its end', () => {
    const table = devProxy(GATEWAY, REMOTES)!;

    for (const context of REST_CONTEXTS) {
      expect(entryFor(table, `/${context}`), `${context} bare`).toMatchObject({ target: GATEWAY });
      expect(entryFor(table, `/${context}/things`), `${context} with a path`).toMatchObject({ target: GATEWAY });
      expect(entryFor(table, `/${context}-other`), `${context} as a prefix of another word`).toBeUndefined();
    }
  });

  it('Proxies a bare REST path for fetch and scripts, and hands a page navigation back to Vite', () => {
    const bypass = entryFor(devProxy(GATEWAY, REMOTES)!, '/system-variables/x')!.bypass!;

    expect(bypass({ url: '/system-variables/x', headers: {} })).toBeUndefined();
    expect(bypass({ url: '/system-variables/x', headers: { 'sec-fetch-dest': 'empty' } })).toBeUndefined();
    expect(bypass({ url: '/system-variables', headers: { 'sec-fetch-dest': 'document' } })).toBe('/system-variables');
    expect(bypass({ url: '/event-ingestion/types', headers: { 'sec-fetch-dest': 'document' } })).toBe(
      '/event-ingestion/types',
    );
  });

  it('Proxies a bare REST context followed straight by a query string', () => {
    const table = devProxy(GATEWAY, REMOTES)!;

    for (const context of REST_CONTEXTS) {
      expect(entryFor(table, `/${context}?limit=5`), context).toMatchObject({ target: GATEWAY });
    }
  });

  it('Does not proxy a path that only starts with a proxied word', () => {
    const table = devProxy(GATEWAY, REMOTES)!;

    for (const url of ['/api-tokens', '/apiary', '/remotes/cameras-x', '/remotes/event-ingestion-x']) {
      expect(entryFor(table, url), url).toBeUndefined();
    }
  });

  it('Keeps proxying the paths the gateway forwards, with or without a query', () => {
    const table = devProxy(GATEWAY, REMOTES)!;

    for (const url of ['/api', '/api?x=1', '/api/system-variables']) {
      expect(entryFor(table, url), url).toMatchObject({ target: GATEWAY });
    }
    for (const url of ['/remotes/cameras/remoteEntry.js', '/remotes/cameras?x=1']) {
      expect(entryFor(table, url), url).toMatchObject({ target: REMOTES.cameras });
    }
    for (const url of ['/remotes/event-ingestion/x?y', '/remotes/event-ingestion']) {
      expect(entryFor(table, url), url).toMatchObject({ target: REMOTES.eventIngestion });
    }
    for (const context of REST_CONTEXTS) {
      expect(entryFor(table, `/${context}/x?y`), context).toMatchObject({ target: GATEWAY });
    }
  });

  it('Hands a page navigation with a query string back to Vite', () => {
    const entry = entryFor(devProxy(GATEWAY, REMOTES)!, '/system-variables?tab=x');

    expect(entry?.bypass?.({ url: '/system-variables?tab=x', headers: { 'sec-fetch-dest': 'document' } })).toBe(
      '/system-variables?tab=x',
    );
  });

  it('Names exactly the nine contexts the gateway routes', () => {
    expect([...REST_CONTEXTS].sort()).toEqual(
      [
        'audit-observability',
        'automation',
        'camera-catalog',
        'event-ingestion',
        'identity',
        'layout-composition',
        'overlay-designer',
        'stream-distribution',
        'system-variables',
      ].sort(),
    );
  });
});
