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
import { ChainRecoveryNotice } from '@smart-sentinel-eye/shared/ui/composites/ChainRecoveryNotice';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { useEffect, useMemo, useRef, useState, type ComponentRef, type FormEvent } from 'react';
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
  //
  // `currentData`, never `data` (spec 153): `data` is RTK Query's last
  // successful result for ANY argument this hook has ever been called with,
  // so it survives a `skipToken` step and an argument change. This dialog
  // stays permanently mounted in `LayoutsPage` and is driven
  // `editTarget: A -> undefined -> B` on the same component -- not an
  // unmount and a fresh mount -- so `data` would hand layout B's dialog
  // layout A's chain, including A's version, before B's own GET has ever
  // answered. `currentData` resets on both, which is what "re-read", not
  // "reused", requires.
  //
  // `refetchOnMountOrArgChange`: kept even though the only path into this
  // dialog already re-fetches without it -- `branchDraftRevision`'s
  // invalidation lands with zero subscribers (`LayoutsPage.onEdit` awaits it
  // before `setEditTarget`), so the entry is evicted outright before the
  // dialog ever reopens on the same layout (spec.md "Window 3, corrected in
  // phase 4a"). The option is what makes FR-003 true on its own terms -- a
  // reopen within the 60s cache window always asks the server again --
  // rather than resting on an eviction behaviour that is RTK Query's
  // implementation detail, not a contract, and could change under a library
  // upgrade.
  const {
    currentData: currentChain,
    isError: chainFailed,
    isFetching: chainFetching,
    refetch: refetchChain,
  } = useGetLayoutQuery(editTarget?.layoutIdentifier ?? skipToken, { refetchOnMountOrArgChange: true });
  const { isLoading, error } = isEdit ? editState : createState;

  // Drop any prior backend error when the dialog closes so a stale banner
  // doesn't greet the operator on the next open.
  //
  // Resets BOTH mutation states, not the one `isEdit` names (phase-6 review):
  // `LayoutsPage.tsx` drives `open={editTarget !== undefined}`, so `open`
  // and `isEdit` are the same boolean, and by the time this effect fires on
  // close (`!open`), `isEdit` has already gone false — a mode-selected reset
  // would always clear create's state, leaving a refused edit's error to
  // survive into the next open, on a different, never-refused draft.
  useEffect(() => {
    if (!open) {
      createState.reset();
      editState.reset();
    }
  }, [open, createState, editState]);

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
      // FR-002: kept as defence-in-depth alongside `saveBlocked` below (spec
      // 160 FR-002). It narrows `currentChain` for the `.version` read next,
      // and — now that the button is `aria-disabled` rather than natively
      // disabled — the form's submit guard, not this check, is what actually
      // keeps this unreached through the UI.
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

  // Spec 156 (issue #2372). The chain read is the only thing blocking Save
  // (FR-002 above), so an operator clicks Retry *in order to* Save — focus
  // lands there, not restored to wherever it was, when the operator's own
  // re-read succeeds.
  // `ComponentRef<'button'>`, not `HTMLButtonElement` (spec 154's own
  // `e2e/overlays.spec.ts` fix, bc30486f) — this app's eslint config has no
  // per-tag DOM lib globals, and naming the type literally trips `no-undef`;
  // widening the config would be the gate-weakening ADR-0144 rules out.
  const saveRef = useRef<ComponentRef<'button'>>(null);

  // Spec 160 (issue #2387) FR-002/FR-007/spec §11 A1. Computed once,
  // consumed by both the button's `aria-disabled` and the form's submit
  // guard below — one gate, one mechanism, rather than the two overlapping
  // conditions this used to state separately (here and in `onSubmit`'s own
  // `currentChain === undefined` check above).
  //
  // `knownCameras.size === 0`, not `cameraItems.length === 0` — that is
  // "nothing to assign" and not the same question as "nothing matched".
  // Reading `cameraItems` here disabled Save with no explanation on a form
  // whose tiles may all already be filled, and flickered on every keystroke
  // while each new filter fragment was in flight. `knownCameras` only ever
  // grows, so it answers the question actually being asked: has this dialog
  // ever seen a camera.
  //
  // `chainFetching` alongside `currentChain === undefined` (FR-002): a
  // version that has not been read, or is being re-read, must never be the
  // one Save submits. Its most common trigger is not the Reload button — it
  // is RTK Query applying `invalidatesTags` on a REJECTED mutation too: a
  // stale-version 409 from `editDraftRevision` starts a background chain
  // refetch while the dialog stays subscribed, exactly the moment an
  // operator is about to click Save again. Verified in phase 6 against a
  // real `LAYOUT_REVISION_STALE` 409: GET count 1 -> 2 immediately,
  // `currentData` still the old version, `isFetching` true, Save unavailable,
  // exactly one PATCH ever issued. Reload (`refetchChain()`) hits the same
  // gate but is the rarer path — both are cases where `currentData`
  // genuinely stays stale while a fetch for the same argument is in flight.
  // The Reload path is pinned by `LayoutEditorDialogChainRecovery.test.tsx`'s
  // FR-005 case; the REJECTED-mutation path is not pinned by a test in this
  // repo, so that half of the outcome is recorded here rather than asserted.
  const saveBlocked = isLoading || knownCameras.size === 0 || (isEdit && (currentChain === undefined || chainFetching));

  // FR-003: `aria-disabled` restores implicit form submission (a natively
  // disabled default button suppresses Enter-to-submit; `aria-disabled` does
  // not), so this guard — run on the form's submit event, before
  // `handleSubmit` — is the only thing left stopping a submit while
  // `saveBlocked` is true, on both routes: a click on Save, and Enter in a
  // text field.
  function handleFormSubmit(event: FormEvent) {
    if (saveBlocked) {
      event.preventDefault();
      return;
    }
    void onSubmit(event);
  }

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
      <form onSubmit={handleFormSubmit} className="flex flex-col gap-4">
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
        <ChainRecoveryNotice
          noun="layout"
          readFailed={chainFailed}
          reReading={chainFetching}
          onReRead={() => void refetchChain()}
          backendError={backendError}
          offerReload={staleConflict}
          onReadRecovered={() => saveRef.current?.focus()}
        />
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          {/*
            `aria-disabled`, not the native `disabled` attribute (spec 160,
            issue #2387) — a native disable blurs the focused element the
            instant it takes effect, the same finding this repo already
            shipped twice: `ChainRecoveryNotice.tsx:31-33` (spec 156) and
            `OverlayEditor.tsx:649-657` (spec 154). `saveBlocked` above is
            what defines the gate; `handleFormSubmit` on the form is what
            actually enforces it now that the button stays clickable.
          */}
          <Button
            ref={saveRef}
            type="submit"
            aria-disabled={saveBlocked}
            className="aria-disabled:opacity-50 aria-disabled:cursor-progress"
          >
            {isLoading ? 'Saving…' : isEdit ? 'Save draft' : 'Save as draft'}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
