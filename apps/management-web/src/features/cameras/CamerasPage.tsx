import {
  useListCamerasQuery,
  type CameraSortField,
  type CameraSortOrder,
  type CameraSummary,
} from '@smart-sentinel-eye/shared/api/cameras.api';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import {
  DataTable,
  type DataTableColumn,
  type DataTableSort,
} from '@smart-sentinel-eye/shared/ui/composites/DataTable';
import { useState } from 'react';
import { RegisterCameraDialog } from './RegisterCameraDialog.js';

const PAGE_SIZE = 50;

const COLUMNS: DataTableColumn<CameraSummary, CameraSortField>[] = [
  {
    id: 'name',
    header: 'Name',
    cell: (row) => row.name,
    sortKey: 'name',
  },
  {
    id: 'rtspUrl',
    header: 'RTSP URL',
    cell: (row) => <code className="text-xs text-fg-muted">{row.rtspUrl}</code>,
  },
  {
    id: 'registeredAt',
    header: 'Registered',
    cell: (row) => new Date(row.registeredAt).toLocaleString(),
    sortKey: 'registeredAt',
  },
];

export function CamerasPage() {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [sort, setSort] = useState<DataTableSort<CameraSortField>>({
    field: 'registeredAt',
    direction: 'desc',
  });
  const [offset, setOffset] = useState(0);

  const { data, isLoading, isFetching, error, refetch } = useListCamerasQuery({
    sort: sort.field,
    order: sort.direction as CameraSortOrder,
    offset,
    limit: PAGE_SIZE,
  });

  const items = data?.items ?? [];
  const totalCount = data?.count ?? 0;
  const showingFrom = totalCount === 0 ? 0 : offset + 1;
  const showingTo = Math.min(offset + items.length, totalCount);

  const onSortChange = (next: DataTableSort<CameraSortField>) => {
    setSort(next);
    setOffset(0);
  };

  return (
    <section className="p-6">
      <header className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-semibold">Cameras</h1>
        <Button onClick={() => setDialogOpen(true)}>Register camera</Button>
      </header>

      {error !== undefined && (
        <div
          role="alert"
          className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
        >
          Could not load cameras.{' '}
          <button type="button" className="underline" onClick={() => void refetch()}>
            Retry
          </button>
        </div>
      )}

      <DataTable
        columns={COLUMNS}
        rows={items}
        getRowKey={(row) => row.cameraIdentifier}
        sort={sort}
        onSortChange={onSortChange}
        isLoading={isLoading || isFetching}
        emptyMessage="No cameras registered yet."
        caption="Registered cameras"
      />

      <footer className="mt-3 flex items-center justify-between text-sm text-fg-muted">
        <span>
          {totalCount === 0
            ? 'No cameras'
            : `Showing ${showingFrom}–${showingTo} of ${totalCount}`}
        </span>
        <div className="flex gap-2">
          <Button
            variant="secondary"
            disabled={offset === 0 || isFetching}
            onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}
          >
            Previous
          </Button>
          <Button
            variant="secondary"
            disabled={offset + items.length >= totalCount || isFetching}
            onClick={() => setOffset(offset + PAGE_SIZE)}
          >
            Next
          </Button>
        </div>
      </footer>

      <RegisterCameraDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </section>
  );
}
