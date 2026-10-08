import {
  systemVariablesApi,
  useArchiveVariableMutation,
  useListVariablesQuery,
  useSetVariableValueMutation,
  type Variable,
  type VariableState,
} from '@smart-sentinel-eye/shared/api/systemVariables.api';
import {
  CONFLICT_FALLBACK,
  isConflict,
  isStaleConflict,
  problemDetail,
} from '@smart-sentinel-eye/shared/api/problemDetail';
import { FaultNotice } from '@smart-sentinel-eye/shared/ui/composites/FaultNotice';
import { RetryBanner } from '@smart-sentinel-eye/shared/ui/composites/RetryBanner';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { useRevocationFallback } from '@smart-sentinel-eye/shared/hooks';
import { useState } from 'react';
import { ArchiveConfirmation } from '../ArchiveConfirmation';
import { SystemVariableDialog } from './SystemVariableDialog.js';

const STATE_FILTERS: ReadonlyArray<VariableState | 'All'> = ['All', 'Defined', 'Archived'];

export function SystemVariablesPage() {
  const [dialogOpen, setDialogOpen] = useState(false);
  // Defaults to Defined, matching what the server now returns without asking
  // (#2015). 'All' was the default when archiving hid nothing, so the tab and
  // the listing agreed by doing nothing.
  const [filter, setFilter] = useState<VariableState | 'All'>('Defined');
  // Keyed on the identifier, not the name. Two fabs may hold the same name
  // (spec 014), and a name-keyed buffer would show one row's typing in the
  // other and submit it against the wrong fab.
  const [pendingEdit, setPendingEdit] = useState<Record<string, string>>({});
  // Spec 036. Nullable subject, not a boolean: this page already holds a dialog
  // and per-row edit state, and the subject carries what the wording needs.
  const [archiveFor, setArchiveFor] = useState<{ name: string; version: number; fab: string } | null>(null);

  // Asked of the server rather than filtered here (#2015). Archived variables
  // no longer come back by default, so a client-side filter over an unfiltered
  // fetch would leave the Archived and All tabs permanently empty — and it was
  // pulling every row to the browser to do it, which against 1618 of them is
  // its own problem.
  const variablesArgs = filter === 'All' ? { includeArchived: true } : { state: filter };
  // Spec 317 (#2751): strikes need a refresh, and a passive tab otherwise
  // never refetches on its own.
  const {
    data: fetched,
    currentData,
    isLoading,
    isFetching,
    error,
    refetch,
    requestId,
  } = useListVariablesQuery(variablesArgs, { refetchOnFocus: true });

  // Spec 310 (#2725). Three consecutive 403 refreshes of this argument set
  // take the stale rows off screen, leaving only the existing failure banner.
  const refused = useRevocationFallback(
    JSON.stringify(variablesArgs),
    { error, isFetching, requestId },
    {
      endpoint: systemVariablesApi.endpoints.listVariables,
      args: variablesArgs,
    },
  );

  // `fetched` (`data`) can still hold a *previous* argument set's rows for a
  // moment after the filter changes — RTK Query's `lastResult` fallback
  // carries them forward with no refetch in between — while `currentData` is
  // only ever the cache entry for the argument set being requested right
  // now. Mirrors `CameraDetailPage`'s `record` (spec 211): below the
  // threshold, a failed refresh of THIS argument set still shows its own
  // stale rows (`currentData`); it must not show a DIFFERENT argument set's
  // rows carried over by `fetched` alone.
  const data = refused ? undefined : error !== undefined ? currentData : fetched;

  const [setVariableValue, setValueState] = useSetVariableValueMutation();
  const [archiveVariable, archiveState] = useArchiveVariableMutation();

  const { isLoading: saving } = setValueState;
  const { isLoading: archiving } = archiveState;
  const mutationError = setValueState.error ?? archiveState.error;

  const variables = data ?? [];

  const onValueSubmit = async (variable: Variable) => {
    const raw = pendingEdit[variable.variableIdentifier];
    if (raw === undefined || raw === '') return;
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
                ? 'rounded-md border border-accent bg-accent-subtle px-3 py-1 text-sm text-accent'
                : 'rounded-md border border-fg-muted/30 px-3 py-1 text-sm text-fg-muted'
            }
          >
            {option}
          </button>
        ))}
      </div>

      {error !== undefined && <RetryBanner message="Could not load variables." onRetry={() => void refetch()} />}

      {mutationError !== undefined && (
        <FaultNotice>
          {problemDetail(
            mutationError,
            isStaleConflict(mutationError) ? CONFLICT_FALLBACK : 'Could not apply that change.',
          )}{' '}
          {isConflict(mutationError) && (
            <button type="button" className="underline" onClick={() => void refetch()}>
              Reload
            </button>
          )}
        </FaultNotice>
      )}

      {(isLoading || isFetching) && <p className="text-sm text-fg-muted">Loading…</p>}

      {!isLoading && variables.length === 0 && <p className="text-sm text-fg-muted">No system variables to show.</p>}

      <ul className="flex flex-col gap-2">
        {variables.map((variable) => {
          const inProgress = saving;
          const editValue = pendingEdit[variable.variableIdentifier];
          const archiveUnavailable = inProgress || archiving;
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
                    unavailable={inProgress || editValue === undefined || editValue === ''}
                    onClick={() => {
                      // ADR-0151: `unavailable` keeps Set value focusable and
                      // clickable while a save is already in flight, so this
                      // guard is what refuses a second submit — before the
                      // empty-value check too, since nothing should be
                      // re-validated mid-request.
                      if (inProgress) return;
                      void onValueSubmit(variable);
                    }}
                  >
                    Set value
                  </Button>
                  <Button
                    variant="secondary"
                    unavailable={archiveUnavailable}
                    onClick={() => {
                      // ADR-0151: focus-return (ConfirmDialog reopens on this
                      // button once archiving natively disabled it before);
                      // refuse a second confirmation while one archive is
                      // already in flight.
                      if (archiveUnavailable) return;
                      setArchiveFor({
                        name: variable.name,
                        version: variable.version,
                        fab: variable.fab,
                      });
                    }}
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
          The variable&rsquo;s current value is cleared, and it can never be given another. Anything that sets it will
          be refused from now on.
        </p>
      </ArchiveConfirmation>

      <SystemVariableDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </section>
  );
}
