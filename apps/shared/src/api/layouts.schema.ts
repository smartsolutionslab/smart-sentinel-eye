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
});

export type CreateLayoutDraftInput = z.infer<typeof createLayoutDraftSchema>;
