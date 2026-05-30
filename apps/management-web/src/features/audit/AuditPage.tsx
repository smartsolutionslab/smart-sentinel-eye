import { useMemo, useState, type FormEvent } from 'react';
import {
  useSearchAuditQuery,
  type AuditRow,
  type SearchAuditInput,
} from '@smart-sentinel-eye/shared/api/audit.api';
import {
  DataTable,
  type DataTableColumn,
} from '@smart-sentinel-eye/shared/ui/composites/DataTable';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';

const PAGE_SIZE = 50;

interface FilterDraft {
  fabId: string;
  eventKind: string;
  resourceKind: string;
  actorUsername: string;
  since: string;
  until: string;
}

const EMPTY_DRAFT: FilterDraft = {
  fabId: '',
  eventKind: '',
  resourceKind: '',
  actorUsername: '',
  since: '',
  until: '',
};

function toQuery(draft: FilterDraft): SearchAuditInput {
  const query: SearchAuditInput = { pageSize: PAGE_SIZE };
  if (draft.fabId !== '') query.fabId = draft.fabId;
  if (draft.eventKind !== '') query.eventKind = draft.eventKind;
  if (draft.resourceKind !== '') query.resourceKind = draft.resourceKind;
  if (draft.actorUsername !== '') query.actorUsername = draft.actorUsername;
  if (draft.since !== '') query.since = draft.since;
  if (draft.until !== '') query.until = draft.until;
  return query;
}

function formatPayload(payload: string): string {
  try {
    return JSON.stringify(JSON.parse(payload) as unknown, null, 2);
  } catch {
    return payload;
  }
}

function actorLabel(row: AuditRow): string {
  if (row.actorIsSystem) return 'system';
  return row.actorUsername ?? row.actorIdentifier;
}

// Audit page (spec 009 US2 / FR-008). Cross-cutting search over the audit
// trail with a filter bar, a results table, and a read-only payload panel.
export function AuditPage() {
  const [draft, setDraft] = useState<FilterDraft>(EMPTY_DRAFT);
  const [applied, setApplied] = useState<SearchAuditInput>({ pageSize: PAGE_SIZE });
  const [selected, setSelected] = useState<AuditRow | null>(null);

  const { data, isLoading, isFetching, error, refetch } = useSearchAuditQuery(applied);
  const rows = useMemo(() => data?.rows ?? [], [data?.rows]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    setApplied(toQuery(draft));
    setSelected(null);
  };

  const onClear = () => {
    setDraft(EMPTY_DRAFT);
    setApplied({ pageSize: PAGE_SIZE });
    setSelected(null);
  };

  const setField =
    (field: keyof FilterDraft) =>
    (event: { target: { value: string } }) =>
      setDraft((current) => ({ ...current, [field]: event.target.value }));

  const columns = useMemo<DataTableColumn<AuditRow>[]>(
    () => [
      { id: 'occurredAt', header: 'When', cell: (row) => new Date(row.occurredAt).toLocaleString() },
      { id: 'eventKind', header: 'Event', cell: (row) => row.eventKind },
      {
        id: 'resource',
        header: 'Resource',
        cell: (row) =>
          row.resourceKind === null
            ? '—'
            : `${row.resourceKind} / ${row.resourceIdentifier ?? ''}`,
      },
      { id: 'actor', header: 'Actor', cell: actorLabel },
      { id: 'fab', header: 'Fab', cell: (row) => row.fab ?? '—' },
      {
        id: 'details',
        header: '',
        cell: (row) => (
          <Button variant="ghost" onClick={() => setSelected(row)}>
            View
          </Button>
        ),
      },
    ],
    [],
  );

  return (
    <section className="p-6">
      <header className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Audit</h1>
      </header>

      <form onSubmit={onSubmit} className="mb-4 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <FormField label="Fab" htmlFor="audit-fab">
          <Input id="audit-fab" placeholder="e.g. munich" value={draft.fabId} onChange={setField('fabId')} />
        </FormField>
        <FormField label="Event kind" htmlFor="audit-kind">
          <Input id="audit-kind" placeholder="e.g. CameraRegisteredV1" value={draft.eventKind} onChange={setField('eventKind')} />
        </FormField>
        <FormField label="Resource kind" htmlFor="audit-resource">
          <Input id="audit-resource" placeholder="e.g. camera" value={draft.resourceKind} onChange={setField('resourceKind')} />
        </FormField>
        <FormField label="Actor" htmlFor="audit-actor">
          <Input id="audit-actor" placeholder="username" value={draft.actorUsername} onChange={setField('actorUsername')} />
        </FormField>
        <FormField label="Since" htmlFor="audit-since">
          <Input id="audit-since" type="datetime-local" value={draft.since} onChange={setField('since')} />
        </FormField>
        <FormField label="Until" htmlFor="audit-until">
          <Input id="audit-until" type="datetime-local" value={draft.until} onChange={setField('until')} />
        </FormField>
        <div className="flex items-end gap-2">
          <Button type="submit">Search</Button>
          <Button type="button" variant="secondary" onClick={onClear}>
            Clear
          </Button>
        </div>
      </form>

      {error !== undefined && (
        <div role="alert" className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault">
          Could not load the audit trail.{' '}
          <button type="button" className="underline" onClick={() => void refetch()}>
            Retry
          </button>
        </div>
      )}

      <DataTable
        columns={columns}
        rows={rows}
        getRowKey={(row) => row.auditIdentifier}
        isLoading={isLoading || isFetching}
        emptyMessage="No audit events match these filters."
        caption="Audit trail"
      />

      <footer className="mt-3 flex items-center justify-end gap-2 text-sm text-fg-muted">
        {applied.cursor !== undefined && (
          <Button
            variant="secondary"
            onClick={() => {
              setApplied((current: SearchAuditInput) => ({ ...current, cursor: undefined }));
              setSelected(null);
            }}
          >
            First page
          </Button>
        )}
        <Button
          variant="secondary"
          disabled={data?.nextCursor === null || data?.nextCursor === undefined}
          onClick={() => {
            setApplied((current: SearchAuditInput) => ({ ...current, cursor: data?.nextCursor ?? undefined }));
            setSelected(null);
          }}
        >
          Next
        </Button>
      </footer>

      {selected !== null && (
        <aside className="mt-4 rounded-md border border-fg-muted/30 bg-bg-elevated p-4">
          <div className="mb-2 flex items-center justify-between">
            <h2 className="text-sm font-medium">
              {selected.eventKind} — {new Date(selected.occurredAt).toLocaleString()}
            </h2>
            <Button variant="ghost" onClick={() => setSelected(null)}>
              Close
            </Button>
          </div>
          <pre className="overflow-x-auto text-xs text-fg-muted">
            <code>{formatPayload(selected.payload)}</code>
          </pre>
        </aside>
      )}
    </section>
  );
}
