import { z } from 'zod';

// Mirrors the VariableName grammar from FR-001:
//   ^[A-Za-z][A-Za-z0-9_]{0,63}$
// Case-sensitive; max 64 chars; alphanumeric + underscore; must start with a letter.
const variableNameSchema = z
  .string()
  .min(1, 'Name is required')
  .max(64, 'Name must be 64 characters or fewer')
  .regex(
    /^[A-Za-z][A-Za-z0-9_]{0,63}$/,
    'Name must start with a letter and contain only letters, digits, and underscores',
  );

export const defineVariableSchema = z
  .object({
    name: variableNameSchema,
    type: z.enum(['String', 'Number', 'Boolean']),
    /** Optional initial value as a wire-string. */
    initialValue: z.string().optional(),
    truthyLabel: z.string().min(1).max(64).optional(),
    falsyLabel: z.string().min(1).max(64).optional(),
  })
  .superRefine((value, ctx) => {
    if (value.type === 'Boolean') {
      if (value.truthyLabel === undefined || value.falsyLabel === undefined) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['truthyLabel'],
          message: 'BooleanLabels are required when Type is Boolean.',
        });
      }
    } else if (value.truthyLabel !== undefined || value.falsyLabel !== undefined) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['truthyLabel'],
        message: 'BooleanLabels can only be set on Boolean variables.',
      });
    }
  });

export type DefineVariableInput = z.infer<typeof defineVariableSchema>;

export const setVariableValueSchema = z.object({
  value: z.string(),
});

export type SetVariableValueInput = z.infer<typeof setVariableValueSchema>;
