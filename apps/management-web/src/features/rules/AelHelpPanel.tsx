/**
 * Inline AEL reference (spec 007 T096). Rules are authored as free text, so
 * the grammar has to be discoverable at the point of writing — sending an
 * operator to a separate doc to remember whether it is `AND` or `&&` is how
 * predicates end up wrong.
 *
 * Kept deliberately terse: the field paths and operators the interpreter
 * actually supports, and nothing aspirational.
 */
const OPERATORS: ReadonlyArray<{ syntax: string; meaning: string }> = [
  { syntax: '==  !=', meaning: 'equal / not equal' },
  { syntax: '<  <=  >  >=', meaning: 'numeric comparison' },
  { syntax: 'and  or  not', meaning: 'boolean combination (short-circuiting)' },
  { syntax: 'contains', meaning: 'substring match on strings' },
  { syntax: '+  -  *  /', meaning: 'arithmetic' },
  { syntax: '( )', meaning: 'grouping' },
];

const FIELDS: ReadonlyArray<{ path: string; meaning: string }> = [
  { path: '$.source', meaning: 'event source, e.g. plc, inference, manual, webhook' },
  { path: '$.kind', meaning: 'event kind, e.g. PlcCycleStart' },
  { path: '$.device', meaning: 'emitting device identifier' },
  { path: '$.payload.<field>', meaning: 'any field from the event payload' },
];

const EXAMPLES: ReadonlyArray<{ expression: string; meaning: string }> = [
  { expression: '$.payload.cycleTime <= 30', meaning: 'fires on a fast cycle' },
  { expression: '$.payload.temp > 900 and $.device == "press-1"', meaning: 'two conditions' },
  { expression: '$.payload.message contains "jam"', meaning: 'substring match' },
  { expression: '100 - $.payload.cycleTime * 2', meaning: 'a value expression, not a predicate' },
];

export function AelHelpPanel() {
  return (
    <details data-testid="ael-help" className="rounded-md border border-fg-muted/30 p-3 text-sm">
      <summary className="cursor-pointer font-medium">AEL syntax reference</summary>

      <div className="mt-3 space-y-4">
        <p className="text-xs text-fg-muted">
          A predicate must evaluate to a boolean — anything else counts as no match. A value
          expression may return any type; it is converted to a string when the variable is written.
        </p>

        <Section title="Fields" rows={FIELDS.map((f) => [f.path, f.meaning])} />
        <Section title="Operators" rows={OPERATORS.map((o) => [o.syntax, o.meaning])} />
        <Section title="Examples" rows={EXAMPLES.map((e) => [e.expression, e.meaning])} />
      </div>
    </details>
  );
}

function Section({ title, rows }: { title: string; rows: [string, string][] }) {
  return (
    <section>
      <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-fg-muted">{title}</h4>
      <dl className="space-y-1">
        {rows.map(([term, meaning]) => (
          <div key={term} className="flex flex-wrap gap-x-3">
            <dt className="font-mono text-xs">{term}</dt>
            <dd className="text-xs text-fg-muted">{meaning}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
