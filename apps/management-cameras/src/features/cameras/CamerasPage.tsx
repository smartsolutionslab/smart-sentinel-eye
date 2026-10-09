import {
  camerasApi,
  useListCamerasQuery,
  type CameraSortField,
  type CameraSortOrder,
  type CameraSummary,
} from '@smart-sentinel-eye/shared/api/cameras.api';
import { useListStreamsQuery, type StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import { RetryBanner } from '@smart-sentinel-eye/shared/ui/composites/RetryBanner';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import {
  DataTable,
  type DataTableColumn,
  type DataTableSort,
} from '@smart-sentinel-eye/shared/ui/composites/DataTable';
import { useMemo, useState } from 'react';
import { useDebouncedValue, useRevocationFallback } from '@smart-sentinel-eye/shared/hooks';
import { Link } from 'react-router-dom';
import { RegisterCameraDialog } from './RegisterCameraDialog.js';
import { StreamHealthBadge } from './StreamHealthBadge.js';

const PAGE_SIZE = 50;
const STREAM_POLL_MS = 5000;

export function CamerasPage() {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [sort, setSort] = useState<DataTableSort<CameraSortField>>({
    field: 'registeredAt',
    direction: 'desc',
  });
  const [offset, setOffset] = useState(0);
  const [nameFilter, setNameFilter] = useState('');

  // Settled before it reaches the query, so a request goes out per search
  // rather than per keystroke. One page here rather than the picker's five, but
  // the same hook so the two boxes in this app behave alike.
  const fragment = useDebouncedValue(nameFilter.trim());

  // **Back to the first page whenever the fragment changes**, adjusted during
  // render as React documents rather than in an effect. Without it an operator
  // on page three who narrows to two matches is shown an empty table and a
  // pager that says "Showing 0–0 of 2" — the offset outliving the population it
  // was an offset into.
  const [lastFragment, setLastFragment] = useState(fragment);
  if (fragment !== lastFragment) {
    setLastFragment(fragment);
    setOffset(0);
  }

  const listArgs = {
    sort: sort.field,
    order: sort.direction as CameraSortOrder,
    offset,
    limit: PAGE_SIZE,
    name: fragment === '' ? undefined : fragment,
  };
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
  } = useListCamerasQuery(listArgs, { refetchOnFocus: true });

  // Spec 310 (#2725). Three consecutive 403 refreshes of this argument set
  // take the stale rows off screen, leaving only the existing failure banner
  // — the same render a first-load refusal produces.
  const refused = useRevocationFallback(
    JSON.stringify(listArgs),
    { error, isFetching, requestId },
    {
      endpoint: camerasApi.endpoints.listCameras,
      args: listArgs,
    },
  );

  // `fetched` (`data`) can still hold a *previous* argument set's rows for a
  // moment after the filter changes — RTK Query's `lastResult` fallback
  // carries them forward with no refetch in between — while `currentData` is
  // only ever the cache entry for the argument set being requested right
  // now. Mirrors `CameraDetailPage`'s `record` (spec 211): below the
  // threshold, a failed refresh of THIS argument set still shows its own
  // stale rows (`currentData`, kept by RTK across repeated failures of the
  // same args); it must not show a DIFFERENT argument set's rows carried
  // over by `fetched` alone.
  const data = refused ? undefined : error !== undefined ? currentData : fetched;

  const items = useMemo(() => data?.items ?? [], [data?.items]);
  const totalCount = data?.count ?? 0;
  const showingFrom = totalCount === 0 ? 0 : offset + 1;
  const showingTo = Math.min(offset + items.length, totalCount);
  // ADR-0151: hoisted once each so Previous/Next's `unavailable` prop and its
  // guard read the same value.
  const previousUnavailable = offset === 0 || isFetching;
  const nextUnavailable = offset + items.length >= totalCount || isFetching;

  const visibleCameraIds = useMemo(() => items.map((row) => row.cameraIdentifier), [items]);

  const { data: streams } = useListStreamsQuery(visibleCameraIds, {
    pollingInterval: STREAM_POLL_MS,
    skip: visibleCameraIds.length === 0,
  });

  const streamsByCamera = useMemo(() => {
    const map = new Map<string, StreamHealth>();
    for (const stream of streams ?? []) {
      map.set(stream.cameraIdentifier, stream);
    }
    return map;
  }, [streams]);

  const columns = useMemo<DataTableColumn<CameraSummary, CameraSortField>[]>(
    () => [
      {
        id: 'name',
        header: 'Name',
        // The way into one camera (FR-001). A link rather than a row click, so it
        // can be opened in a new tab and copied — the same reason the nav is
        // links.
        cell: (row) => <Link to={`/cameras/${row.cameraIdentifier}`}>{row.name}</Link>,
        sortKey: 'name',
      },
      // A multi-fab operator's listing can hold two rows of one name; without
      // this column they are indistinguishable (spec 015 FR-013).
      { id: 'fab', header: 'Fab', cell: (row) => row.fab },
      {
        id: 'rtspUrl',
        header: 'RTSP URL',
        cell: (row) => <code className="text-xs text-fg-muted">{row.rtspUrl}</code>,
      },
      {
        id: 'stream',
        header: 'Stream',
        cell: (row) => <StreamHealthBadge stream={streamsByCamera.get(row.cameraIdentifier)} />,
      },
      {
        id: 'registeredAt',
        header: 'Registered',
        cell: (row) => new Date(row.registeredAt).toLocaleString(),
        sortKey: 'registeredAt',
      },
    ],
    [streamsByCamera],
  );

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

      {error !== undefined && <RetryBanner message="Could not load cameras." onRetry={() => void refetch()} />}

      {/*
        Spec 055. A search input above the table rather than a control that
        replaces it: the table keeps its own sorting and paging, and this only
        narrows what they operate on.
      */}
      <div className="mb-3 max-w-sm">
        <label htmlFor="camera-name-filter" className="mb-1 block text-sm font-medium text-fg-primary">
          Find a camera
        </label>
        <Input
          id="camera-name-filter"
          type="search"
          placeholder="Part of a name, anywhere in it"
          value={nameFilter}
          onChange={(event) => setNameFilter(event.target.value)}
        />
      </div>

      <DataTable
        columns={columns}
        rows={items}
        getRowKey={(row) => row.cameraIdentifier}
        sort={sort}
        onSortChange={onSortChange}
        isLoading={isLoading || isFetching}
        emptyMessage={
          /*
            **A miss and an empty catalogue are different facts.** An operator
            told "no cameras registered yet" while filtering concludes the fab
            is empty; one told nothing at all concludes the camera is gone and
            registers a duplicate, which is refused because names are unique.
          */
          fragment === '' ? 'No cameras registered yet.' : `No camera matches “${fragment}”.`
        }
        caption="Registered cameras"
      />

      <footer className="mt-3 flex items-center justify-between text-sm text-fg-muted">
        <span>{totalCount === 0 ? 'No cameras' : `Showing ${showingFrom}–${showingTo} of ${totalCount}`}</span>
        <div className="flex gap-2">
          <Button
            variant="secondary"
            unavailable={previousUnavailable}
            onClick={() => {
              if (previousUnavailable) return;
              setOffset(Math.max(0, offset - PAGE_SIZE));
            }}
          >
            Previous
          </Button>
          <Button
            variant="secondary"
            unavailable={nextUnavailable}
            onClick={() => {
              if (nextUnavailable) return;
              setOffset(offset + PAGE_SIZE);
            }}
          >
            Next
          </Button>
        </div>
      </footer>

      <RegisterCameraDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </section>
  );
}
