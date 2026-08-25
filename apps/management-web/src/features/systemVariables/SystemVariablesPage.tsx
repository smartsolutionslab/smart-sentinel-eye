import {
  useArchiveVariableMutation,
  useListVariablesQuery,
  useSetVariableValueMutation,
  type Variable,
  type VariableState,
} from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { isConflict, problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useState } from 'react';
import { ArchiveConfirmation } from '../ArchiveConfirmation';
import { SystemVariableDialog } from './SystemVariableDialog.js';

const STATE_FILTERS: ReadonlyArray<VariableState | 'All'> = ['All', 'Defined', 'Archived'];

export function SystemVariablesPage() {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [filter, setFilter] = useState<VariableState | 'All'>('All');
  // Keyed on the identifier, not the name. Two fabs may hold the same name
  // (spec 014), and a name-keyed buffer would show one row's typing in the
  // other and submit it against the wrong fab.
  const [pendingEdit, setPendingEdit] = useState<Record<string, string>>({});
  // Spec 036. Nullable subject, not a boolean: this page already holds a dialog
  // and per-row edit state, and the subject carries what the wording needs.
  const [archiveFor, setArchiveFor] = useState<{ name: string; version: number; fab: string } | null>(null);

  const { data, isLoading, isFetching, error, refetch } = useListVariablesQuery(undefined);
  const [setVariableValue, setValueState] = useSetVariableValueMutation();
  const [archiveVariable, archiveState] = useArchiveVariableMutation();

  const { isLoading: saving } = setValueState;
  const { isLoading: archiving } = archiveState;
  const mutationError = setValueState.error ?? archiveState.error;

  const variables = data ?? [];
  const visible = filter === 'All' ? variables : variables.filter((v) => v.state === filter);

  const onValueSubmit = async (variable: Variable) => {
    const raw = pendingEdit[variable.variableIdentifier];
    if (raw === undefined) return;
    const result = await setVariableValue({
      name: variable.name,
      value: raw,
      version: variable.version,
      // The row's own fab. A name is unique per fab, not globally, so a
      // multi-fab operator can see the same name twice — without this the
      // write is ambiguous and the server refuses it (spec 014).
      fabId: variable.fab,
    });

    // Only drop the operator's typing once it has actually been stored. This
    // used to clear unconditionally, so a rejected write looked exactly like a
    // successful one: the value they typed vanished and the old one came back
    // with no explanation. On a conflict that would lose their work twice —
    // once to the other writer, once to the UI.
    if ('error' in result) return;

    setPendingEdit((prev) => {
      const next = { ...prev };
      delete next[variable.variableIdentifier];
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

      {mutationError !== undefined && (
        <div
          role="alert"
          className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
        >
          {problemDetail(mutationError, 'Could not apply that change.')}{' '}
          {isConflict(mutationError) && (
            <button type="button" className="underline" onClick={() => void refetch()}>
              Reload
            </button>
          )}
        </div>
      )}

      {(isLoading || isFetching) && <p className="text-sm text-fg-muted">Loading…</p>}

      {!isLoading && visible.length === 0 && (
        <p className="text-sm text-fg-muted">No system variables to show.</p>
      )}

      <ul className="flex flex-col gap-2">
        {visible.map((variable) => {
          const inProgress = saving;
          const editValue = pendingEdit[variable.variableIdentifier];
          return (
            <li
              key={variable.variableIdentifier}
              className="rounded-md border border-fg-muted/30 bg-bg-elevated px-4 py-3"
            >
              <header className="flex items-center justify-between">
                <h2 className="text-lg font-medium">{variable.name}</h2>
                <span className="text-xs text-fg-muted">
                  {variable.fab} · {variable.type} · {variable.state}
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
                      setPendingEdit((prev) => ({ ...prev, [variable.variableIdentifier]: e.target.value }))
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
                  <Button
                    variant="secondary"
                    disabled={inProgress || archiving}
                    onClick={() =>
                      setArchiveFor({
                        name: variable.name,
                        version: variable.version,
                        fab: variable.fab,
                      })
                    }
                  >
                    Archive
                  </Button>
                </div>
              )}
            </li>
          );
        })}
      </ul>

      {/* Spec 036 FR-006. Both halves are invisible from this page and neither
          is guessable: archiving clears the variable's current value, and
          nothing can give it another afterwards. Verified from
          Variable.Archive setting Value to Unset and SetValue refusing once
          archived. */}
      <ArchiveConfirmation
        subject={archiveFor === null ? null : `variable ${archiveFor.name}`}
        onCancel={() => setArchiveFor(null)}
        pending={archiving}
        onConfirm={() => {
          if (archiveFor === null) {
            return;
          }
          void archiveVariable({
            name: archiveFor.name,
            version: archiveFor.version,
            fabId: archiveFor.fab,
          });
          setArchiveFor(null);
        }}
      >
        <p>This cannot be undone.</p>
        <p>
          The variable&rsquo;s current value is cleared, and it can never be given another. Anything
          that sets it will be refused from now on.
        </p>
      </ArchiveConfirmation>

      <SystemVariableDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </section>
  );
}
