import { z } from 'zod';

// Schema mirrors spec/003-layout-composition FR-006 + the LayoutName
// value-object rules (≤ 80 chars, non-empty, no newlines, trimmed).
export const createLayoutDraftSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'Name is required')
    .max(80, 'Name must be 80 characters or fewer')
    .refine((s) => !/[\r\n]/.test(s), 'Name must not contain a line break'),
  cameraIdentifier: z.string().uuid('cameraIdentifier must be a Guid'),
  /**
   * Empty string == "(none) — no overlay binding". A valid Guid binds
   * the layout to that overlay (spec 004 PR B'). The wire-level field
   * is fully optional; the form coalesces empty → omitted.
   */
  overlayIdentifier: z
    .string()
    .refine(
      (s) =>
        s === '' ||
        /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(s),
      'overlayIdentifier must be a Guid or empty',
    )
    .optional(),
});

export type CreateLayoutDraftInput = z.infer<typeof createLayoutDraftSchema>;
