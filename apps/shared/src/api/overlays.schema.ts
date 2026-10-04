import { z } from 'zod';

// Mirrors the Label value object rules (spec 004 FR-005 / FR-008):
// non-empty trim ≤ 256, normalized [0,1] with positive width/height,
// font size 8-256.
export const overlayLabelSchema = z.object({
  text: z.string().trim().min(1, 'Text is required').max(256, 'Text must be 256 characters or fewer'),
  normalizedX: z.number().min(0).max(1),
  normalizedY: z.number().min(0).max(1),
  normalizedWidth: z.number().gt(0).max(1),
  normalizedHeight: z.number().gt(0).max(1),
  fontSizePx: z.number().int().min(8).max(256),
});

// Spec 150 / ADR-0164: a revision carries an ordered, non-empty set of 1..
// MAX_LABELS labels. MAX_LABELS mirrors the backend domain `const`
// (`Label.MaxLabels`, src/OverlayDesigner/Domain/Overlay/Label.cs) — the one
// justified cross-tier duplication (browser feedback vs. authoritative
// validation), pinned equal to it by a test
// (`overlays.schema.test.ts`) rather than left as two hand-copied numbers:
// issue #2361 is the precedent for why that drifts.
export const MAX_LABELS = 8;

export const createOverlayDraftSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(80, 'Name must be 80 characters or fewer')
    .refine((s) => !/[\r\n]/.test(s), 'Name must not contain a line break'),
  labels: z
    .array(overlayLabelSchema)
    .min(1, 'A revision must carry at least one label')
    .max(MAX_LABELS, `A revision may carry at most ${MAX_LABELS} labels`),
});

export type CreateOverlayDraftInput = z.infer<typeof createOverlayDraftSchema>;
