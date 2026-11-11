import { describe, expect, it } from 'vitest';
import { REST_CONTEXTS, devProxy } from './devProxy.js';

/**
 * Spec 491 (ADR-0192). The shell's dev server stands in for the gateway at :5173: `/api` and `/remotes/*` are proxied, and so are
 * the bare `/<context>/...` REST paths, except for a page navigation, which Vite must serve as the shell.
 */
const GATEWAY = 'http://gateway.test:5180';
const REMOTES = { cameras: 'http://localhost:5176', eventIngestion: 'http://localhost:5177' };

describe('The shell dev-server proxy (spec 491)', () => {
  it('Is absent when there is no gateway to proxy to', () => {
    expect(devProxy(undefined, REMOTES)).toBeUndefined();
    expect(devProxy('', REMOTES)).toBeUndefined();
  });

  it('Proxies /api to the gateway, and each remote prefix to its own dev server with websockets', () => {
    const table = devProxy(GATEWAY, REMOTES)!;

    expect(table['/api']).toMatchObject({ target: GATEWAY, changeOrigin: true });
    expect(table['/api']?.bypass).toBeUndefined();
    expect(table['/remotes/cameras']).toMatchObject({ target: REMOTES.cameras, ws: true });
    expect(table['/remotes/event-ingestion']).toMatchObject({ target: REMOTES.eventIngestion, ws: true });
  });

  it('Proxies each bare REST context to the gateway, as the key a path with a slash or at its end matches', () => {
    const table = devProxy(GATEWAY, REMOTES)!;

    for (const context of REST_CONTEXTS) {
      const key = `^/${context}(/|$)`;
      expect(table[key], context).toMatchObject({ target: GATEWAY });
      const pattern = new RegExp(key);
      expect(pattern.test(`/${context}`), `${context} bare`).toBe(true);
      expect(pattern.test(`/${context}/things`), `${context} with a path`).toBe(true);
      expect(pattern.test(`/${context}-other`), `${context} as a prefix of another word`).toBe(false);
    }
  });

  it('Proxies a bare REST path for fetch and scripts, and hands a page navigation back to Vite', () => {
    const bypass = devProxy(GATEWAY, REMOTES)!['^/system-variables(/|$)']!.bypass!;

    expect(bypass({ url: '/system-variables/x', headers: {} })).toBeUndefined();
    expect(bypass({ url: '/system-variables/x', headers: { 'sec-fetch-dest': 'empty' } })).toBeUndefined();
    expect(bypass({ url: '/system-variables', headers: { 'sec-fetch-dest': 'document' } })).toBe('/system-variables');
    expect(bypass({ url: '/event-ingestion/types', headers: { 'sec-fetch-dest': 'document' } })).toBe(
      '/event-ingestion/types',
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
