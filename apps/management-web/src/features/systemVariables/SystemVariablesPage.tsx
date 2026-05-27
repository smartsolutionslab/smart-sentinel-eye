import {
  useListVariablesQuery,
  useSetVariableValueMutation,
  type Variable,
  type VariableState,
} from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useState } from 'react';
import { SystemVariableDialog } from './SystemVariableDialog.js';

const STATE_FILTERS: ReadonlyArray<VariableState | 'All'> = ['All', 'Defined', 'Archived'];

export function SystemVariablesPage() {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [filter, setFilter] = useState<VariableState | 'All'>('All');
  const [pendingEdit, setPendingEdit] = useState<Record<string, string>>({});

  const { data, isLoading, isFetching, error, refetch } = useListVariablesQuery(undefined);
  const [setVariableValue, { isLoading: saving }] = useSetVariableValueMutation();

  const variables = data ?? [];
  const visible = filter === 'All' ? variables : variables.filter((v) => v.state === filter);

  const onValueSubmit = async (variable: Variable) => {
    const raw = pendingEdit[variable.name];
    if (raw === undefined) return;
    await setVariableValue({ name: variable.name, value: raw });
    setPendingEdit((prev) => {
      const next = { ...prev };
      delete next[variable.name];
      return next;
    });
  };

  return (
    <section className="p-6">
      <header className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold">System variables</h1>
        <Button onClick={() => setDialogOpen(true)}>New variable</Button>
      </header>

      <div className="mb-4 flex gap-2">
        {STATE_FILTERS.map((option) => (
          <button
            key={option}
            type="button"
            onClick={() => setFilter(option)}
            className={
              option === filter
                ? 'rounded-md border border-accent-active bg-accent-active/10 px-3 py-1 text-sm text-accent-active'
                : 'rounded-md border border-fg-muted/30 px-3 py-1 text-sm text-fg-muted'
            }
          >
            {option}
          </button>
        ))}
      </div>

      {error !== undefined && (
        <div
          role="alert"
          className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
        >
          Could not load variables.{' '}
          <button type="button" className="underline" onClick={() => void refetch()}>
            Retry
          </button>
        </div>
      )}

      {(isLoading || isFetching) && <p className="text-sm text-fg-muted">Loading…</p>}

      {!isLoading && visible.length === 0 && (
        <p className="text-sm text-fg-muted">No system variables to show.</p>
      )}

      <ul className="flex flex-col gap-2">
        {visible.map((variable) => {
          const inProgress = saving;
          const editValue = pendingEdit[variable.name];
          return (
            <li
              key={variable.variableIdentifier}
              className="rounded-md border border-fg-muted/30 bg-bg-elevated px-4 py-3"
            >
              <header className="flex items-center justify-between">
                <h2 className="text-lg font-medium">{variable.name}</h2>
                <span className="text-xs text-fg-muted">
                  {variable.type} · {variable.state}
                </span>
              </header>
              <p className="mt-1 text-sm text-fg-muted">
                Current: <span className="font-mono">{variable.value ?? '(unset)'}</span>
              </p>
              {variable.state === 'Defined' && (
                <div className="mt-3 flex gap-2">
                  <input
                    type="text"
                    placeholder="New value"
                    value={editValue ?? ''}
                    onChange={(e) =>
                      setPendingEdit((prev) => ({ ...prev, [variable.name]: e.target.value }))
                    }
                    className="flex-1 rounded-md border border-fg-muted/40 bg-bg-base px-3 py-1.5 text-sm text-fg-primary"
                  />
                  <Button
                    variant="secondary"
                    disabled={inProgress || editValue === undefined || editValue === ''}
                    onClick={() => void onValueSubmit(variable)}
                  >
                    Set value
                  </Button>
                </div>
              )}
            </li>
          );
        })}
      </ul>

      <SystemVariableDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </section>
  );
}
