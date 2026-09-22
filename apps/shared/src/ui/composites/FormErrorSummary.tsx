// Structurally compatible with react-hook-form's `FieldErrors` without
// depending on the package: this app has no other reason to import RHF
// (see FormField's own comment, which drives validation through the parent
// instead), and a type-only import still requires the package resolvable
// for `tsc`. Any object keyed by field name, each entry optionally carrying
// a string `message`, satisfies both shapes.
type FieldErrorLike = Readonly<Record<string, { message?: unknown } | undefined>>;

export interface FormErrorSummaryProps {
  errors: FieldErrorLike;
  renderedFields: readonly string[];
}

// Safety net for spec 212 (issue #2430) US3: a conditional branch that is
// not rendered can still leave an error in form state (e.g. pathed to a
// field a hidden branch owns). Every `FormField` already surfaces its own
// error inline; this composite exists only for the remainder — errors whose
// field is not among `renderedFields` and therefore has nowhere to display
// them. Presentational only: no state, no `useFormContext`, no RHF import.
// Only reads a top-level entry's own `message` — a nested or array field
// error (react-hook-form's shape for a field array or object field with no
// message of its own) is silently skipped. Both dialogs this guards are
// flat, so it doesn't apply yet; a future nested form reaching for this
// component would need to walk it recursively first.
export function FormErrorSummary({ errors, renderedFields }: FormErrorSummaryProps) {
  const rendered = new Set(renderedFields);
  const messages = Object.entries(errors)
    .filter(([field]) => !rendered.has(field))
    .map(([field, error]) => ({ field, message: error?.message }))
    .filter((entry): entry is { field: string; message: string } => typeof entry.message === 'string');

  if (messages.length === 0) return null;

  return (
    <div role="alert" className="text-sm text-accent-fault">
      {messages.map(({ field, message }) => (
        <p key={field}>{message}</p>
      ))}
    </div>
  );
}
