import { z } from 'zod';

// Mirrors the Label value object rules (spec 004 FR-005 / FR-008):
// non-empty trim ≤ 256, normalized [0,1] with positive width/height,
// font size 8-256.
export const overlayLabelSchema = z.object({
  text: z
    .string()
    .trim()
    .min(1, 'Text is required')
    .max(256, 'Text must be 256 characters or fewer'),
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
  label: overlayLabelSchema,
});

export type CreateOverlayDraftInput = z.infer<typeof createOverlayDraftSchema>;
