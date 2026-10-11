import { describe, expect, it } from 'vitest';
import { parseNavManifest } from '@smart-sentinel-eye/shared/navigation/navManifest';
import manifest from '../public/nav-manifest.json';

/** Mirrors apps/management-cameras/src/navManifest.published.test.ts's local type. */
interface PublishedNavEntry {
  readonly path: string;
  readonly order: number;
}

/**
 * Issue #2780 — a remote unit test parsing its own published manifest, so
 * this remote cannot ship one its own CI would reject. Mirrors
 * apps/management-cameras/src/navManifest.published.test.ts.
 */
describe('The published event-ingestion nav manifest parses (issue #2780)', () => {
  it('Parses successfully with the shared parseNavManifest', () => {
    const result = parseNavManifest(manifest);

    expect(result.ok).toBe(true);
  });

  it('Declares basePath "/event-ingestion"', () => {
    const result = parseNavManifest(manifest);

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.manifest.basePath).toBe('/event-ingestion');
    }
  });

  it('Declares order 80 for its "/event-ingestion" entry', () => {
    const result = parseNavManifest(manifest);

    expect(result.ok).toBe(true);
    if (result.ok) {
      const entry = result.manifest.entries.find(
        (candidate: PublishedNavEntry) => candidate.path === '/event-ingestion',
      );
      expect(entry?.order).toBe(80);
    }
  });
});
