import { z } from 'zod';
import { isLegibleTextColor, minimumTextAlpha } from '../ui/composites/textLegibility.js';

// Spec 300 (issue #2349), ADR-0165. A revision carries an ordered,
// non-empty set of 1..MAX_ELEMENTS elements, each a Text label, a Box or an
// Ellipse. MAX_ELEMENTS mirrors the backend domain `const`
// (`OverlayElement.MaxElements`, src/OverlayDesigner/Domain/Overlay/OverlayElement.cs)
// — the one justified cross-tier duplication (browser feedback vs.
// authoritative validation), pinned equal to it by a test
// (`overlays.schema.test.ts`) rather than left as two hand-copied numbers:
// issue #2361 is the precedent for why that drifts.
export const MAX_ELEMENTS = 8;

// `OverlayColor.DefaultValue` / `.Pattern` (OverlayColor.cs), pinned equal
// by `overlays.schema.test.ts` (FR-020).
export const DEFAULT_OVERLAY_COLOR = '#FFFFFFD9';
export const OVERLAY_COLOR_PATTERN = /^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$/;

// `TextLegibility.MinimumContrast` (TextLegibility.cs), pinned equal by
// `overlays.schema.test.ts` (FR-020).
export const MINIMUM_TEXT_CONTRAST = 4.5;

/**
 * A six- or eight-digit hex colour, canonicalised to an opaque, upper-case
 * eight-digit form the way `OverlayColor.From` does (appends `FF` to six
 * digits, then upper-cases). Shared by every element kind (ADR-0165 §2 —
 * `OverlayColor` itself is kind-agnostic).
 */
const colorSchema = z
  .string()
  .regex(OVERLAY_COLOR_PATTERN, 'Colour must be a six- or eight-digit hex colour, e.g. #RRGGBB or #RRGGBBAA.')
  .transform(canonicalColor);

function canonicalColor(value: string): string {
  const digits = value.slice(1);
  const eightDigits = digits.length === 6 ? `${digits}FF` : digits;
  return `#${eightDigits.toUpperCase()}`;
}

const geometrySchema = {
  normalizedX: z.number().min(0).max(1),
  normalizedY: z.number().min(0).max(1),
  normalizedWidth: z.number().gt(0).max(1),
  normalizedHeight: z.number().gt(0).max(1),
};

// Mirrors the TextContent value object rules (spec 004 FR-005 / FR-008):
// non-empty trim <= 256, font size 8-256.
export const textSchema = z
  .object({
    kind: z.literal('Text'),
    color: colorSchema,
    text: z.string().trim().min(1, 'Text is required').max(256, 'Text must be 256 characters or fewer'),
    fontSizePx: z.number().int().min(8).max(256),
    ...geometrySchema,
  })
  .superRefine((value, ctx) => {
    if (!isLegibleTextColor(value.color)) {
      const rgb = value.color.slice(1, 7);
      ctx.addIssue({
        code: 'custom',
        path: ['color'],
        message: `Needs opacity of at least ${minimumTextAlpha(rgb)} to stay readable over any video.`,
      });
    }
  });

// Strokes carry no readability floor (ADR-0165 §2) — any alpha, including
// fully transparent.
export const boxSchema = z.object({
  kind: z.literal('Box'),
  color: colorSchema,
  ...geometrySchema,
});

export const ellipseSchema = z.object({
  kind: z.literal('Ellipse'),
  color: colorSchema,
  ...geometrySchema,
});

export const overlayElementSchema = z.discriminatedUnion('kind', [textSchema, boxSchema, ellipseSchema]);

/**
 * The pre-spec-300 label schema (no `kind`/`color`), kept only because
 * `OverlayEditorCharacterisation.test.tsx` — one of the four `OverlayEditor*`
 * guards this spec must leave byte-identical (FR-016) — validates a label
 * built by `OverlayEditor.tsx` itself against it, and that component never
 * adds `kind`/`color` (they are optional on `OverlayLabel`, see
 * `overlays.api.ts`). Not used by `overlayElementSchema`/`textSchema` above.
 */
export const overlayLabelSchema = z.object({
  text: z.string().trim().min(1, 'Text is required').max(256, 'Text must be 256 characters or fewer'),
  normalizedX: z.number().min(0).max(1),
  normalizedY: z.number().min(0).max(1),
  normalizedWidth: z.number().gt(0).max(1),
  normalizedHeight: z.number().gt(0).max(1),
  fontSizePx: z.number().int().min(8).max(256),
});

export const createOverlayDraftSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(80, 'Name must be 80 characters or fewer')
    .refine((s) => !/[\r\n]/.test(s), 'Name must not contain a line break'),
  elements: z
    .array(overlayElementSchema)
    .min(1, 'A revision must carry at least one element')
    .max(MAX_ELEMENTS, `A revision may carry at most ${MAX_ELEMENTS} elements`),
});

export type CreateOverlayDraftInput = z.infer<typeof createOverlayDraftSchema>;
