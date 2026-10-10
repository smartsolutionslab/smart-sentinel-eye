import { describe, expect, it } from 'vitest';
import { parseNavManifest } from '@smart-sentinel-eye/shared/navigation/navManifest';
import manifest from '../public/nav-manifest.json';

/** Mirrors plan.md §2.1's NavEntry shape, for the find() below's parameter type. */
interface PublishedNavEntry {
  readonly path: string;
  readonly order: number;
}

/**
 * Spec 316 (issue superseding #1008) T010 — RED (ADR-0139/0144), plan.md §5.4
 * / §6 test 8. Neither the `./navigation/navManifest` subpath export nor the
 * module it points at exists yet in `apps/shared/package.json` (T011, US2),
 * so this fails to resolve today — before the real `public/nav-manifest.json`
 * (already published, plan.md §5.4) is ever read. A remote unit test parsing
 * its own published manifest so the remote cannot ship one its own CI would
 * reject.
 */
describe('The published cameras nav manifest parses (spec 316 plan.md §5.4)', () => {
  it('Parses successfully with the shared parseNavManifest', () => {
    const result = parseNavManifest(manifest);

    expect(result.ok).toBe(true);
  });

  it('Declares basePath "/cameras"', () => {
    const result = parseNavManifest(manifest);

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.manifest.basePath).toBe('/cameras');
    }
  });

  it('Declares order 10 for its "/cameras" entry', () => {
    const result = parseNavManifest(manifest);

    expect(result.ok).toBe(true);
    if (result.ok) {
      const entry = result.manifest.entries.find((candidate: PublishedNavEntry) => candidate.path === '/cameras');
      expect(entry?.order).toBe(10);
    }
  });
});
