// Spec 300 (issue #2349), ADR-0165, T017. Replaces spec 150 T017's
// MAX_LABELS pin (the schema itself moves from a flat label array to a
// discriminated `kind` union — ADR-0165's element model). Two hand-copied
// bounds (the Zod `.max(...)` here and the domain `const` on the backend)
// is exactly how #2361 happened (spec.md "How #2353 and #2361 interact with
// this"); these tests read the backend source directly rather than
// asserting a literal, so a future revision that bumps one and not the
// other fails here instead of silently drifting (FR-020).
//
// `overlays.schema.ts` does not export any of these names yet — RED by
// design (T017 is red-first). `OverlayElement.cs`, `OverlayColor.cs`,
// `TextLegibility.cs` and `ElementKind.cs` do not exist on disk yet either
// (the backend half of this spec is also red-first, T005/T006): the pin
// tests below fail on a missing file, which is the expected red, not a
// weaker substitute for it.
/// <reference types="node" />
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  DEFAULT_OVERLAY_COLOR,
  MAX_ELEMENTS,
  MINIMUM_TEXT_CONTRAST,
  OVERLAY_COLOR_PATTERN,
  boxSchema,
  createOverlayDraftSchema,
  ellipseSchema,
  overlayElementSchema,
  textSchema,
} from './overlays.schema.js';

const here = path.dirname(fileURLToPath(import.meta.url));
// apps/shared/src/api -> repo root -> src/OverlayDesigner/Domain/Overlay/*.cs
const domainOverlayDir = path.resolve(here, '..', '..', '..', '..', 'src/OverlayDesigner/Domain/Overlay');

function readDomainSource(fileName: string): string {
  const filePath = path.join(domainOverlayDir, fileName);
  expect(existsSync(filePath), `expected ${filePath} to exist`).toBe(true);
  return readFileSync(filePath, 'utf8');
}

function geometry(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    ...overrides,
  };
}

function textElement(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    kind: 'Text',
    color: DEFAULT_OVERLAY_COLOR,
    text: 'Line 1',
    fontSizePx: 32,
    ...geometry(),
    ...overrides,
  };
}

function boxElement(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    kind: 'Box',
    color: '#D32F2FFF',
    ...geometry(),
    ...overrides,
  };
}

function ellipseElement(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    kind: 'Ellipse',
    color: '#FFA000CC',
    ...geometry(),
    ...overrides,
  };
}

describe('backend pins (FR-020) — a drift between the Zod schema and the domain const fails here', () => {
  it('MAX_ELEMENTS equals OverlayElement.MaxElements read straight from the backend source', () => {
    const source = readDomainSource('OverlayElement.cs');
    const match = source.match(/public const int MaxElements = (\d+);/);

    expect(
      match,
      'OverlayElement.cs no longer declares `public const int MaxElements = <n>;` in the form this test parses',
    ).not.toBeNull();

    expect(MAX_ELEMENTS).toBe(Number(match![1]));
  });

  it('DEFAULT_OVERLAY_COLOR equals OverlayColor.DefaultValue read straight from the backend source', () => {
    const source = readDomainSource('OverlayColor.cs');
    const match = source.match(/public const string DefaultValue = "([^"]*)";/);

    expect(
      match,
      'OverlayColor.cs no longer declares `public const string DefaultValue = "...";` in the form this test parses',
    ).not.toBeNull();

    expect(DEFAULT_OVERLAY_COLOR).toBe(match![1]);
  });

  it('OVERLAY_COLOR_PATTERN.source equals OverlayColor.Pattern read straight from the backend source', () => {
    const source = readDomainSource('OverlayColor.cs');
    const match = source.match(/public const string Pattern = "([^"]*)";/);

    expect(
      match,
      'OverlayColor.cs no longer declares `public const string Pattern = "...";` in the form this test parses',
    ).not.toBeNull();

    expect(OVERLAY_COLOR_PATTERN.source).toBe(match![1]);
  });

  it('MINIMUM_TEXT_CONTRAST equals TextLegibility.MinimumContrast read straight from the backend source', () => {
    const source = readDomainSource('TextLegibility.cs');
    const match = source.match(/public const double MinimumContrast = ([\d.]+);/);

    expect(
      match,
      'TextLegibility.cs no longer declares `public const double MinimumContrast = <n>;` in the form this test parses',
    ).not.toBeNull();

    expect(MINIMUM_TEXT_CONTRAST).toBe(Number(match![1]));
  });

  it("the union's kind values equal ElementKind's canonical values read straight from the backend source", () => {
    const source = readDomainSource('ElementKind.cs');
    // Mirrors OverlayRevisionState.cs's `From` switch shape: `"Draft" => Draft,`.
    const matches = [...source.matchAll(/"(\w+)"\s*=>\s*\w+,/g)].map((m) => m[1]);

    expect(
      matches.length,
      'ElementKind.cs\'s `From` switch has no `"Value" => Singleton,` arms in the form this test parses',
    ).toBeGreaterThan(0);

    const domainKinds = [...new Set(matches)].sort();
    const schemaKinds = (overlayElementSchema.options as unknown as { shape: Record<string, { value: unknown }> }[])
      .map((option) => option.shape.kind?.value as string)
      .sort();

    expect(schemaKinds).toEqual(domainKinds);
  });
});

describe('overlayElementSchema: colour pattern + canonicalisation (shared by every kind)', () => {
  it.each(['red', '#F00', '#F00F', '#FF00', '#FF0000A', '#FF0000AAB', '#GG0000', ''])(
    'rejects the malformed colour %s on a Box element',
    (badColor) => {
      const result = overlayElementSchema.safeParse(boxElement({ color: badColor }));
      expect(result.success).toBe(false);
    },
  );

  it('canonicalises a six-digit colour to an opaque, upper-case eight-digit colour', () => {
    const result = overlayElementSchema.safeParse(boxElement({ color: '#d32f2f' }));
    expect(result.success).toBe(true);
    if (result.success) {
      expect((result.data as { color: string }).color).toBe('#D32F2FFF');
    }
  });

  it('canonicalises an already eight-digit colour to upper-case', () => {
    const result = overlayElementSchema.safeParse(boxElement({ color: '#ff000080' }));
    expect(result.success).toBe(true);
    if (result.success) {
      expect((result.data as { color: string }).color).toBe('#FF000080');
    }
  });
});

describe('overlayElementSchema: text legibility (textSchema.superRefine, Box/Ellipse exempt)', () => {
  it('rejects a Text element at #D32F2F80, the error naming the minimum alpha F9', () => {
    const result = overlayElementSchema.safeParse(textElement({ color: '#D32F2F80' }));
    expect(result.success).toBe(false);
    if (!result.success) {
      const messages = result.error.issues.map((issue: { message: string }) => issue.message).join(' ');
      expect(messages).toContain('F9');
    }
  });

  it('accepts a Text element at its colour floor (#D32F2FF9)', () => {
    const result = overlayElementSchema.safeParse(textElement({ color: '#D32F2FF9' }));
    expect(result.success).toBe(true);
  });

  it('accepts a Box element at #D32F2F00 — no legibility check for shapes', () => {
    const result = overlayElementSchema.safeParse(boxElement({ color: '#D32F2F00' }));
    expect(result.success).toBe(true);
  });

  it('accepts an Ellipse element at #FFA00040 — no legibility check for shapes', () => {
    const result = overlayElementSchema.safeParse(ellipseElement({ color: '#FFA00040' }));
    expect(result.success).toBe(true);
  });
});

describe('textSchema / boxSchema / ellipseSchema: per-kind presence (text/fontSizePx)', () => {
  it('textSchema requires text and fontSizePx', () => {
    expect(textSchema.safeParse(textElement()).success).toBe(true);
    expect(textSchema.safeParse(textElement({ text: '' })).success).toBe(false);
  });

  it('boxSchema does not require text or fontSizePx', () => {
    expect(boxSchema.safeParse(boxElement()).success).toBe(true);
  });

  it('ellipseSchema does not require text or fontSizePx', () => {
    expect(ellipseSchema.safeParse(ellipseElement()).success).toBe(true);
  });
});

// Guards against a false-positive pass: today's `createOverlayDraftSchema`
// still requires its OLD `labels` field, so a payload built with the NEW
// `elements` field fails validation regardless — but for the wrong reason
// (a missing `labels`, not an `elements` cardinality violation). Asserting
// `result.success === false` alone would pass today by accident and tell
// nobody anything. This helper pins the failure to the `elements` path
// specifically, so it stays red until the real cause (empty/too-many
// `elements`) exists.
function expectElementsIssue(result: { success: boolean; error?: { issues: { path: PropertyKey[] }[] } }) {
  expect(result.success).toBe(false);
  if (!result.success) {
    const elementsIssue = result.error?.issues.find((issue) => issue.path[0] === 'elements');
    expect(
      elementsIssue,
      `expected a Zod issue on the "elements" path; got: ${JSON.stringify(result.error?.issues)}`,
    ).toBeDefined();
  }
}

describe('createOverlayDraftSchema: cardinality (1..MAX_ELEMENTS, shapes count too — ADR-0165)', () => {
  it('rejects an empty element set, with the issue on the elements path (not a missing-labels complaint)', () => {
    const result = createOverlayDraftSchema.safeParse({ name: 'Line-1', elements: [] });
    expectElementsIssue(result as never);
  });

  it('accepts exactly MAX_ELEMENTS elements', () => {
    const elements = Array.from({ length: MAX_ELEMENTS }, () => boxElement());
    const result = createOverlayDraftSchema.safeParse({ name: 'Line-1', elements });
    expect(result.success).toBe(true);
  });

  it('rejects MAX_ELEMENTS + 1 elements, with the issue on the elements path', () => {
    const elements = Array.from({ length: MAX_ELEMENTS + 1 }, () => boxElement());
    const result = createOverlayDraftSchema.safeParse({ name: 'Line-1', elements });
    expectElementsIssue(result as never);
  });

  it('accepts a mix of Box and Text elements up to the cap (shapes count toward it too)', () => {
    const elements = [
      ...Array.from({ length: 5 }, () => boxElement()),
      ...Array.from({ length: 3 }, () => textElement()),
    ];
    expect(elements.length).toBe(MAX_ELEMENTS);
    const result = createOverlayDraftSchema.safeParse({ name: 'Line-1', elements });
    expect(result.success).toBe(true);
  });

  it('rejects 5 Box + 4 Text — nine elements over the cap, with the issue on the elements path', () => {
    const elements = [
      ...Array.from({ length: 5 }, () => boxElement()),
      ...Array.from({ length: 4 }, () => textElement()),
    ];
    const result = createOverlayDraftSchema.safeParse({ name: 'Line-1', elements });
    expectElementsIssue(result as never);
  });

  it('still validates each element the way it always has', () => {
    expect(overlayElementSchema.safeParse(textElement()).success).toBe(true);
    expect(overlayElementSchema.safeParse(textElement({ text: '' })).success).toBe(false);
  });
});
