import { useState } from 'react';
import { useDryRunRuleMutation } from '@smart-sentinel-eye/shared/api/rules.api';
import { dryRunSampleSchema } from '@smart-sentinel-eye/shared/api/rules.schema';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';

const DEFAULT_SAMPLE = JSON.stringify(
  { source: 'plc', kind: 'PlcCycleStart', device: 'press-1', payload: { cycleTime: 20 } },
  null,
  2,
);

/**
 * Try a rule against a sample event without publishing it (spec 007 T095).
 *
 * The sample is the canonical evaluation root the live pipeline builds, so
 * what is tried here is what the rule will actually see. The JSON is parsed
 * locally first — a typo should read as "not valid JSON" beside the box, not
 * come back as a 400.
 */
export function DryRunPanel({ ruleName, fabId }: { ruleName: string; fabId: string }) {
  const [sample, setSample] = useState(DEFAULT_SAMPLE);
  const [jsonError, setJsonError] = useState<string | null>(null);
  const [dryRun, { data, error, isLoading, reset }] = useDryRunRuleMutation();

  const onRun = async () => {
    const parsed = dryRunSampleSchema.safeParse(sample);
    if (!parsed.success) {
      setJsonError(parsed.error.issues[0]?.message ?? 'Not valid JSON');
      reset();
      return;
    }
    setJsonError(null);
    await dryRun({ name: ruleName, sampleEvent: sample, fabId });
  };

  return (
    <section data-testid="dry-run-panel" className="space-y-2 rounded-md border border-fg-muted/30 p-3">
      <h3 className="text-sm font-medium">Dry run</h3>
      <p className="text-xs text-fg-muted">
        Evaluates this rule against the sample below. Nothing is saved and no action is taken.
      </p>

      <label className="block text-xs font-medium" htmlFor="dry-run-sample">
        Sample event
      </label>
      <textarea
        id="dry-run-sample"
        data-testid="dry-run-sample"
        className="h-40 w-full rounded-md border border-fg-muted/30 bg-transparent p-2 font-mono text-xs"
        value={sample}
        onChange={(event) => setSample(event.target.value)}
      />

      {jsonError !== null && (
        <p role="alert" className="text-xs text-accent-fault">
          {jsonError}
        </p>
      )}

      <Button type="button" onClick={onRun} disabled={isLoading}>
        {isLoading ? 'Running…' : 'Run'}
      </Button>

      {error !== undefined && (
        <p role="alert" data-testid="dry-run-error" className="text-xs text-accent-fault">
          {problemDetail(error, 'Could not run the rule.')}
        </p>
      )}

      {data !== undefined && (
        <div data-testid="dry-run-result" className="rounded-md bg-fg-muted/10 p-2 text-xs">
          <p className={data.matched ? 'font-medium text-accent-ok' : 'font-medium text-fg-muted'}>
            {data.matched ? 'Matched' : 'Did not match'}
          </p>
          {data.matched && data.evaluatedValue !== null && (
            <p className="mt-1">
              Would set: <span className="font-mono">{data.evaluatedValue}</span>
            </p>
          )}
          {data.matched && data.evaluatedValue === null && (
            <p className="mt-1 text-fg-muted">This action has no value to evaluate.</p>
          )}
        </div>
      )}
    </section>
  );
}
