import { describe, expect, it } from 'vitest';
import { parseNavManifest } from './navManifest.js';

/**
 * Spec 316 (issue superseding #1008) T010 — RED (ADR-0139/0144), plan.md §2.1
 * / §6 test 1. `apps/shared/src/navigation/navManifest.ts` does not exist
 * yet: T011 (US2) creates the zod schema and `parseNavManifest`, the shell's
 * fail-closed gate for a remote's published `nav-manifest.json` (FR-015).
 * Every case here fails on the missing module today, not on an assertion
 * mismatch against a working parser.
 *
 * `parseNavManifest` returns a discriminated union — `{ ok: true, manifest }`
 * or `{ ok: false, reason: string }` — rather than throwing, so the caller
 * (`NavigationProvider`, T011) can log `reason` through
 * `logResilienceEvent('navigation', 'manifest-rejected', { remote, reason })`
 * without a try/catch around every manifest fetch.
 */

const VALID_MANIFEST = {
  schemaVersion: 1,
  remote: 'cameras',
  basePath: '/cameras',
  entries: [{ path: '/cameras', label: 'Cameras', order: 10, requiredScopes: ['sse.cameras.read'] }],
};

describe('parseNavManifest (spec 316 FR-015, plan.md §2.1)', () => {
  it('Accepts a valid manifest', () => {
    const result = parseNavManifest(VALID_MANIFEST);

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.manifest).toEqual(VALID_MANIFEST);
    }
  });

  it('Rejects an unknown schemaVersion, with a reason', () => {
    const result = parseNavManifest({ ...VALID_MANIFEST, schemaVersion: 2 });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.reason).toBeTypeOf('string');
      expect(result.reason.length).toBeGreaterThan(0);
    }
  });

  it('Rejects an empty requiredScopes array, with a reason', () => {
    const result = parseNavManifest({
      ...VALID_MANIFEST,
      entries: [{ path: '/cameras', label: 'Cameras', order: 10, requiredScopes: [] }],
    });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.reason).toBeTypeOf('string');
      expect(result.reason.length).toBeGreaterThan(0);
    }
  });

  it('Rejects a scope that does not match the sse.<resource>.<verb> pattern, with a reason', () => {
    const result = parseNavManifest({
      ...VALID_MANIFEST,
      entries: [{ path: '/cameras', label: 'Cameras', order: 10, requiredScopes: ['cameras.read'] }],
    });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.reason).toBeTypeOf('string');
      expect(result.reason.length).toBeGreaterThan(0);
    }
  });

  it('Rejects a manifest with no basePath, with a reason', () => {
    const withoutBasePath: Record<string, unknown> = { ...VALID_MANIFEST };
    delete withoutBasePath.basePath;

    const result = parseNavManifest(withoutBasePath);

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.reason).toBeTypeOf('string');
      expect(result.reason.length).toBeGreaterThan(0);
    }
  });

  it('Rejects an entry whose path is outside the manifest basePath, with a reason', () => {
    const result = parseNavManifest({
      ...VALID_MANIFEST,
      entries: [{ path: '/layouts', label: 'Cameras', order: 10, requiredScopes: ['sse.cameras.read'] }],
    });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.reason).toBeTypeOf('string');
      expect(result.reason.length).toBeGreaterThan(0);
    }
  });

  it('Rejects a non-object input, with a reason', () => {
    const result = parseNavManifest(null);

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.reason).toBeTypeOf('string');
      expect(result.reason.length).toBeGreaterThan(0);
    }
  });
});
