import { z } from 'zod';

// Mirrors RuleName (spec 007 FR-001): lowercase kebab, 2-63 chars.
const ruleNameSchema = z
  .string()
  .trim()
  .min(2, 'Name must be at least 2 characters')
  .max(63, 'Name must be 63 characters or fewer')
  .regex(
    /^[a-z][a-z0-9-]*$/,
    'Name must be lowercase kebab-case: start with a letter, then letters, digits or hyphens',
  );

// RulePredicate: 1-4096 chars of AEL. The grammar itself is validated
// server-side by AelParser — duplicating a parser in the browser would be two
// implementations to keep in step, and the server already returns a parse
// error with its position.
const predicateSchema = z
  .string()
  .trim()
  .min(1, 'Predicate is required')
  .max(4096, 'Predicate must be 4096 characters or fewer');

export const createRuleSchema = z
  .object({
    name: ruleNameSchema,
    triggerSource: z.string().trim().min(1, 'Trigger source is required'),
    triggerKind: z.string().trim().min(1, 'Trigger kind is required'),
    predicate: predicateSchema,
    actionType: z.enum(['SetVariableValue', 'HighlightOverlay']),
    variableName: z.string().trim().max(64).optional(),
    valueExpression: z.string().trim().max(4096).optional(),
    overlayIdentifier: z.string().uuid('Choose an overlay').optional(),
    durationMs: z.number().int().min(500).max(60_000).optional(),
  })
  .superRefine((value, ctx) => {
    // The action tag decides which fields are required — the same rule the
    // server enforces when it builds the RuleAction variant.
    if (value.actionType === 'SetVariableValue') {
      if (!value.variableName) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['variableName'],
          message: 'Variable name is required for SetVariableValue',
        });
      }
      if (!value.valueExpression) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['valueExpression'],
          message: 'Value expression is required for SetVariableValue',
        });
      }
    } else {
      if (!value.overlayIdentifier) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['overlayIdentifier'],
          message: 'Overlay is required for HighlightOverlay',
        });
      }
      if (value.durationMs === undefined) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['durationMs'],
          message: 'Duration is required for HighlightOverlay',
        });
      }
    }
  });

export type CreateRuleInput = z.infer<typeof createRuleSchema>;

// Sample event for the dry-run panel: must be valid JSON before we ask the
// server to evaluate it, so a typo is caught locally rather than as a 400.
export const dryRunSampleSchema = z.string().superRefine((value, ctx) => {
  try {
    JSON.parse(value);
  } catch (error) {
    ctx.addIssue({
      code: z.ZodIssueCode.custom,
      message: `Not valid JSON: ${(error as Error).message}`,
    });
  }
});
