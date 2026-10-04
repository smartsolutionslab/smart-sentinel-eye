// Spec 150 (issue #2345), T017. Two hand-copied bounds — the Zod `.max(...)`
// here and the domain `const` on the backend — is exactly how #2361 happened
// (spec.md "How #2353 and #2361 interact with this"). This test reads the
// backend source directly rather than asserting a literal, so a future
// ADR-0164 revision that bumps one and not the other fails here instead of
// silently drifting.
/// <reference types="node" />
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { MAX_LABELS, createOverlayDraftSchema, overlayLabelSchema } from './overlays.schema.js';

const here = path.dirname(fileURLToPath(import.meta.url));
// apps/shared/src/api -> repo root -> src/OverlayDesigner/Domain/Overlay/Label.cs
const labelCsPath = path.resolve(here, '..', '..', '..', '..', 'src/OverlayDesigner/Domain/Overlay/Label.cs');

function oneLabel(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    text: 'Line 1',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    fontSizePx: 32,
    ...overrides,
  };
}

describe('MAX_LABELS pinned against the domain const (spec 150 T017)', () => {
  it('equals Label.MaxLabels read straight from the backend source', () => {
    expect(existsSync(labelCsPath), `expected ${labelCsPath} to exist`).toBe(true);

    const source = readFileSync(labelCsPath, 'utf8');
    const match = source.match(/public const int MaxLabels = (\d+);/);

    expect(
      match,
      'Label.cs no longer declares `public const int MaxLabels = <n>;` in the form this test parses',
    ).not.toBeNull();

    const domainMaxLabels = Number(match![1]);
    expect(MAX_LABELS).toBe(domainMaxLabels);
  });

  it('rejects an empty label set', () => {
    const result = createOverlayDraftSchema.safeParse({ name: 'Line-1', labels: [] });
    expect(result.success).toBe(false);
  });

  it(`accepts exactly ${MAX_LABELS} labels`, () => {
    const labels = Array.from({ length: MAX_LABELS }, () => oneLabel());
    const result = createOverlayDraftSchema.safeParse({ name: 'Line-1', labels });
    expect(result.success).toBe(true);
  });

  it(`rejects ${MAX_LABELS + 1} labels`, () => {
    const labels = Array.from({ length: MAX_LABELS + 1 }, () => oneLabel());
    const result = createOverlayDraftSchema.safeParse({ name: 'Line-1', labels });
    expect(result.success).toBe(false);
  });

  it('still validates each label the way it always has', () => {
    expect(overlayLabelSchema.safeParse(oneLabel()).success).toBe(true);
    expect(overlayLabelSchema.safeParse(oneLabel({ text: '' })).success).toBe(false);
  });
});
