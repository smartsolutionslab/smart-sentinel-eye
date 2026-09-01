import { useListAllCameraChoicesQuery, type CameraSummary } from '@smart-sentinel-eye/shared/api/cameras.api';
import {
  useCreateLayoutDraftMutation,
  useEditDraftRevisionMutation,
  useGetLayoutQuery,
  type LayoutTile,
} from '@smart-sentinel-eye/shared/api/layouts.api';
import { skipToken } from '@reduxjs/toolkit/query/react';
import { useListOverlaysQuery } from '@smart-sentinel-eye/shared/api/overlays.api';
import { CONFLICT_FALLBACK, isStaleConflict, problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { useEffect, useMemo, useState } from 'react';
import { useDebouncedValue } from '@smart-sentinel-eye/shared/hooks';
import { useForm } from 'react-hook-form';
import { GridDesigner } from './GridDesigner.js';
import {
  buildCells,
  cellsFromTiles,
  createGridDesignerResolver,
  tilesFromCells,
  type GridDesignerValue,
} from './gridDesignerModel.js';

/**
 * The revision the dialog edits in edit-after-publish (US4). The page branches
 * a new draft off the Published chain first, then hands the new draft's
 * revision number plus the baseline grid+tiles (branch copies them verbatim)
 * so the designer opens pre-loaded.
 */
export interface LayoutEditTarget {
  layoutIdentifier: string;
  revisionNumber: number;
  name: string;
  grid: { rows: number; cols: number };
  tiles: LayoutTile[];
}

export interface LayoutEditorDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** When set the dialog edits an existing draft (US4); otherwise it creates. */
  editTarget?: LayoutEditTarget;
}

const EMPTY_CREATE: GridDesignerValue = {
  name: '',
  grid: { rows: 1, cols: 1 },
  cells: buildCells(1, 1),
};

export function LayoutEditorDialog({ open, onOpenChange, editTarget }: LayoutEditorDialogProps) {
  const isEdit = editTarget !== undefined;
  const [createLayoutDraft, createState] = useCreateLayoutDraftMutation();
  const [editDraftRevision, editState] = useEditDraftRevisionMutation();

  // The If-Match version has to be the chain's *current* one (ADR-0113).
  // The page branches a new draft before opening this dialog, and that branch
  // is itself a write, so the version the page held is already one behind.
  // Reading it back rather than inferring "+1" keeps the client from doing
  // arithmetic on server state -- and still fails correctly if another
  // operator moves the chain while the dialog is open.
  const { data: currentChain, refetch: refetchChain } = useGetLayoutQuery(editTarget?.layoutIdentifier ?? skipToken);
  const { isLoading, error, reset: resetMutationState } = isEdit ? editState : createState;

  // Drop any prior backend error when the dialog closes so a stale banner
  // doesn't greet the operator on the next open.
  useEffect(() => {
    if (!open) resetMutationState();
  }, [open, resetMutationState]);

  // Every camera the operator may choose, not the first page of them. The
  // picker used to ask for fifty and render them as the whole set, so past the
  // fiftieth a camera was absent with nothing said — and an absent option is
  // indistinguishable from a camera that was never registered (spec 048).
  // Spec 055: the fragment narrows on the server, so `cameras` is the matches
  // and `count` is their number. Filtering the gathered array here instead would
  // be a second implementation of "matches" that an operator could not tell
  // apart from the first when the two stopped agreeing.
  const [cameraFilter, setCameraFilter] = useState('');

  // **Debounced, because this query is a page walk rather than a request.**
  // `useListAllCameraChoicesQuery` fetches up to five pages of 200 to assemble
  // the whole choice list, so keying it on every keystroke meant up to five
  // round trips per letter — about thirty-five for "furnace" — and left a cache
  // entry per prefix, each of which a camera mutation would then refetch.
  //
  // The input keeps reading the raw state; only the query and the status read
  // the settled one, or the field would drop characters.
  const settledFilter = useDebouncedValue(cameraFilter.trim());
  const {
    data: cameras,
    isLoading: camerasLoading,
    isFetching: camerasFetching,
    isError: camerasFailed,
  } = useListAllCameraChoicesQuery({ name: settledFilter || undefined });

  // **Every camera seen so far, so a filter cannot blank a tile that is already
  // assigned.** The options come from the server's matches; a tile holding a
  // camera the current fragment excludes would render a select whose value has
  // no option — blank on screen while the form still carries it. The operator
  // reads that as lost, and the next thing they do is reassign it.
  //
  // Keeping what has been seen is what lets that tile keep showing its own
  // camera by name. Accumulated for the life of the component — see the reset
  // block below for why clearing it on close was a defect.
  // State rather than a ref, and adjusted during render rather than in an
  // effect — React's documented pattern for deriving from a changed prop. A ref
  // read during render, or a setState inside an effect, both produce the
  // cascading-render bug the hooks rules exist to stop, and both were the first
  // thing written here.
  const [knownCameras, setKnownCameras] = useState<ReadonlyMap<string, CameraSummary>>(() => new Map());

  // **Convergence is on the contents, not on the array's identity.**
  //
  // This first compared `cameras.items` against the previously seen array and
  // merged when the reference changed. That terminates only while the query
  // hands back a reference-stable array — and when it does not, the merge sets
  // state on every render and React aborts the component with "too many
  // re-renders". A crashed dialog, from a detail of someone else's caching.
  //
  // Asking which cameras are not yet known cannot loop: the merge only runs
  // while something is missing, and it adds exactly what was missing.
  const unknownCameras = (cameras?.items ?? []).filter((camera) => !knownCameras.has(camera.cameraIdentifier));
  if (unknownCameras.length > 0) {
    const merged = new Map(knownCameras);
    for (const camera of unknownCameras) {
      merged.set(camera.cameraIdentifier, camera);
    }
    setKnownCameras(merged);
  }

  // The filter is cleared on close; the retained cameras are not. Both dialogs
  // stay mounted, so this state outlives a close either way, and accumulating
  // costs one entry per camera seen while being read only for the camera a tile
  // currently holds.
  //
  // Clearing it *was* a defect while the merge above keyed on the array's
  // identity: the reopened dialog read the same cache entry it had already
  // recorded, so nothing refilled the map and it was empty at exactly the moment
  // a fragment excluded a camera a tile held. The contents-based merge closed
  // that off — the map refills on the next render regardless — so this is now
  // intent rather than the fix.
  const [wasOpen, setWasOpen] = useState(open);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (!open) {
      setCameraFilter('');
    }
  }
  const { data: overlays, isLoading: overlaysLoading } = useListOverlaysQuery('Published');

  const defaultValues = useMemo<GridDesignerValue>(() => {
    if (editTarget === undefined) return EMPTY_CREATE;
    return {
      name: editTarget.name,
      grid: editTarget.grid,
      cells: cellsFromTiles(editTarget.grid.rows, editTarget.grid.cols, editTarget.tiles),
    };
  }, [editTarget]);

  const form = useForm<GridDesignerValue>({
    resolver: createGridDesignerResolver(isEdit ? 'edit' : 'create'),
    defaultValues,
  });
  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = form;

  // Re-seed when the target (or create/edit mode) changes between opens.
  useEffect(() => {
    reset(defaultValues);
  }, [defaultValues, reset]);

  const onSubmit = handleSubmit(async (value) => {
    const tiles = tilesFromCells(value.cells);
    if (editTarget !== undefined) {
      if (currentChain === undefined) return;
      const result = await editDraftRevision({
        layoutIdentifier: editTarget.layoutIdentifier,
        revisionNumber: editTarget.revisionNumber,
        version: currentChain.version,
        grid: value.grid,
        tiles,
      });
      if (!('error' in result)) {
        reset(defaultValues);
        onOpenChange(false);
      }
      return;
    }
    const result = await createLayoutDraft({ name: value.name, grid: value.grid, tiles });
    if (!('error' in result)) {
      reset(EMPTY_CREATE);
      onOpenChange(false);
    }
  });

  // "Try again" is the wrong advice on a stale conflict — resubmitting replays
  // the same stale intent over whoever wrote in between, which is the overwrite
  // this whole mechanism exists to prevent. A name collision keeps it, because
  // there retrying with a different name is exactly what the operator should do.
  const staleConflict = isStaleConflict(error);
  const backendError = problemDetail(
    error,
    staleConflict ? CONFLICT_FALLBACK : 'Could not save the layout. Try again.',
  );
  const cameraItems = cameras?.items ?? [];
  // Only ever true when the source says more cameras exist than were gathered.
  // The copy states the two numbers and stops there, deliberately: the gap can
  // also come from a camera retired between requests, and telling an operator
  // "the rest cannot be chosen" would be wrong in that case. A notice that
  // overclaims is how a notice stops being believed.
  // A notice that appeared whenever the list loaded would carry no information
  // and operators would learn to ignore it, so its absence is what gives it
  // meaning — and is tested as such.
  const camerasTruncated = cameras !== undefined && !cameras.complete;

  const cameraFilterStatusId = 'layout-camera-filter-status';
  const filtering = settledFilter !== '';

  // Typed something the query has not been asked yet. Without this the status
  // reports the *previous* fragment's result for a quarter-second after each
  // keystroke — "3 of 250 cameras match" about a search that is no longer the
  // one in the box.
  const filterSettling = cameraFilter.trim() !== settledFilter;

  // The order matters: "searching" before "nothing matched", or a slow response
  // shows an operator "no cameras match" about a search still running — the one
  // wrong answer this state exists to prevent.
  const cameraFilterStatus = (() => {
    if (camerasFetching || filterSettling) return 'Searching…';
    if (camerasFailed) return 'The camera list could not be loaded.';
    // Unfiltered, this line said "N cameras." directly beneath a truncation
    // notice already reading "Showing 200 of 600 cameras." — two totals for one
    // list, adjacent, disagreeing. The notice is the more informative of the
    // two, so this one stands down rather than competing with it.
    if (!filtering) return camerasTruncated ? '' : `${cameraItems.length} cameras.`;
    if (cameraItems.length === 0) return `No camera matches “${cameraFilter.trim()}”.`;

    // **Not `cameras.count`.** On a mid-walk page failure the client sets that
    // to a deliberate sentinel (`gathered + 1`) meaning "there is more, and I do
    // not know how much" — rendering it here would read as a match count and
    // put a fabricated number in front of an operator. When the walk is
    // incomplete the honest statement is how many were found, with no total.
    return cameras?.complete === true
      ? `${cameraItems.length} of ${cameras.count} cameras match.`
      : `${cameraItems.length} matches so far — the list is incomplete.`;
  })();
  const cameraNoticeId = camerasTruncated ? 'layout-camera-truncation' : undefined;
  const overlayItems = overlays?.published ?? [];

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) reset(defaultValues);
        onOpenChange(next);
      }}
      title={isEdit ? 'Edit layout' : 'New layout'}
      description={
        isEdit
          ? 'Adjust the grid and tiles, then save the draft.'
          : 'Name the wall, pick a grid size, and assign a camera to each tile. It starts as a draft.'
      }
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-4">
        {!isEdit && (
          <FormField label="Name" htmlFor="layout-name" error={errors.name?.message}>
            <Input id="layout-name" autoFocus {...register('name')} />
          </FormField>
        )}
        {/*
          Spec 048's truncation notice. It says how many of how many, and stops
          there — the gap can also come from a camera retired between requests,
          so "the rest cannot be chosen" would sometimes be wrong.
        */}
        {camerasTruncated && (
          <p id={cameraNoticeId} className="text-sm text-fg-muted">
            Showing {cameraItems.length} of {cameras.count} cameras.
          </p>
        )}
        {/*
          Spec 055. A field beside the native lists rather than a combobox
          replacing them: the selects already carry role and value
          announcement, arrow-key movement, Escape and start-of-name
          type-ahead. Replacing them would mean re-implementing all of it, and
          losing any of it is invisible to anyone testing with a mouse.
        */}
        <FormField label="Find a camera" htmlFor="layout-camera-filter">
          <Input
            id="layout-camera-filter"
            type="search"
            placeholder="Part of a name, anywhere in it"
            value={cameraFilter}
            onChange={(event) => setCameraFilter(event.target.value)}
            aria-describedby={cameraFilterStatusId}
          />
        </FormField>
        {/*
          **Three states, told apart.** An operator who cannot distinguish
          "nothing matched" from "still loading" concludes the camera is gone
          and registers a duplicate — which is refused, because names are
          unique, so they are then stuck. `aria-live` because the list shrinking
          silently is the same problem for anyone not watching it.
        */}
        <p id={cameraFilterStatusId} aria-live="polite" className="text-sm text-fg-muted">
          {cameraFilterStatus}
        </p>
        <GridDesigner
          form={form}
          cameras={cameraItems}
          knownCameras={knownCameras}
          overlays={overlayItems}
          camerasLoading={camerasLoading}
          overlaysLoading={overlaysLoading}
          camerasFailed={camerasFailed}
          cameraFilterActive={filtering}
          cameraNoticeId={cameraNoticeId}
        />
        {backendError !== null && (
          <p role="alert" className="text-sm text-accent-fault">
            {backendError}{' '}
            {staleConflict && (
              // Reload, never retry. Refetching the chain replaces the version
              // the dialog would resubmit with the one the other writer left,
              // so the operator reapplies against what is actually stored.
              <button type="button" className="underline" onClick={() => void refetchChain()}>
                Reload
              </button>
            )}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          {/*
            Disabled when there is nothing to assign — which is **not** the same
            as "nothing matched". Reading `cameraItems` here meant a search
            matching nothing disabled Save with no explanation, on a form whose
            tiles may all already be filled, and the operator had to clear the
            box to discover why. It also flickered on every keystroke, because
            the query yields no data while each new fragment is in flight.

            `knownCameras` only ever grows, so it answers the question actually
            being asked: has this dialog ever seen a camera.
          */}
          <Button type="submit" disabled={isLoading || knownCameras.size === 0}>
            {isLoading ? 'Saving…' : isEdit ? 'Save draft' : 'Save as draft'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
