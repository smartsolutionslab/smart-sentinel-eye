import { z } from 'zod';

/**
 * PD-5 (spec 258): a wall with fewer than 2 scenes has nothing to switch
 * between, and the upper bound keeps the set, the editor and the audit
 * payload bounded. Both are UI-scale guesses with no latency consequence
 * (unlike `MAX_TILES`/`MAX_CELLS`), and the backend re-validates
 * authoritatively — this is the inline-feedback copy, per ADR-0112 §4's
 * precedent for `layouts.schema.ts`.
 */
export const MIN_SCENES = 2;
export const MAX_SCENES = 8;

/**
 * A scene is a `LayoutIdentifier` (PD-3). Deliberately not constrained to the
 * Guid shape `layouts.schema.ts`'s tile fields use: the picker builds this
 * array from whatever the caller's `useListLayoutsQuery('Published')` result
 * hands back, and pinning the wire format here would duplicate a check the
 * backend already owns for no benefit — this schema's job is the *set*
 * invariants (count, no duplicates), not the identifier's shape.
 */
export const createWallSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, 'Name is required')
      .max(80, 'Name must be 80 characters or fewer')
      .refine((s) => !/[\r\n]/.test(s), 'Name must not contain a line break'),
    scenes: z
      .array(z.string().min(1))
      .min(MIN_SCENES, `A wall needs at least ${MIN_SCENES} scenes`)
      .max(MAX_SCENES, `A wall may have at most ${MAX_SCENES} scenes`),
  })
  .superRefine((value, ctx) => {
    const seen = new Set<string>();
    value.scenes.forEach((scene, index) => {
      if (seen.has(scene)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['scenes', index],
          message: 'Each scene may appear only once',
        });
      }
      seen.add(scene);
    });
  });

export type CreateWallInput = z.infer<typeof createWallSchema>;
