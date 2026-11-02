import {
  useCreateOverlayDraftMutation,
  useEditDraftOverlayRevisionMutation,
  type OverlayElement,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import {
  createOverlayDraftSchema,
  MAX_ELEMENTS,
  type CreateOverlayDraftInput,
} from '@smart-sentinel-eye/shared/api/overlays.schema';
import { useGetStreamQuery } from '@smart-sentinel-eye/shared/api/streams.api';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { ChainRecoveryNotice } from '@smart-sentinel-eye/shared/ui/composites/ChainRecoveryNotice';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { OverlayEditor, type OverlayEditorValue } from '@smart-sentinel-eye/shared/ui/composites/OverlayEditor';
import { OverlayLayerList } from '@smart-sentinel-eye/shared/ui/composites/OverlayLayerList';
import {
  layerName,
  listPositionOf,
  newElement,
  ordinalAt,
  selectionAfterRemove,
  textOverlaps,
} from '@smart-sentinel-eye/shared/ui/composites/overlayLayers';
import { FrameGrabber } from '@smart-sentinel-eye/shared/ui/composites/FrameGrabber';
import { zodResolver } from '@hookform/resolvers/zod';
import { useCallback, useEffect, useMemo, useRef, useState, type ComponentRef, type FormEvent } from 'react';
import type { OverlayEditTarget } from './overlayDraftForm/overlayEditTarget.js';
import { useCameraChoices } from './overlayDraftForm/useCameraChoices.js';
import { useResolvedPreview } from './overlayDraftForm/useResolvedPreview.js';
import { useDraftChain } from './overlayDraftForm/useDraftChain.js';
import { describeSaveRefusal } from './overlayDraftForm/describeSaveRefusal.js';
import { useUnmountMutationReset } from './overlayDraftForm/useUnmountMutationReset.js';
import { useElementsChangeNotifier } from './overlayDraftForm/useElementsChangeNotifier.js';
import { useLatestAccessToken } from './overlayDraftForm/useLatestAccessToken.js';
import { Controller, useFieldArray, useForm, useWatch, type FieldErrors } from 'react-hook-form';

export type { OverlayEditTarget };

export interface OverlayDraftFormProps {
  /** When set the form edits an existing draft (spec 152); otherwise it creates. */
  editTarget?: OverlayEditTarget;
  /** Called once a save succeeds. */
  onDone: () => void;
  /** Called when the operator cancels. Never preceded by a request. */
  onCancel: () => void;
  /**
   * Spec 305 (#2350) FR-006. Forwarded verbatim to `OverlayEditor`'s own
   * `canvasWidthPx`/`canvasHeightPx` — this form owns no sizing of its own,
   * only the host page's `useCanvasFit` does.
   */
  canvasWidthPx?: number;
  canvasHeightPx?: number;
  /**
   * Phase-6 should-fix 4 (#2350). `OverlayEditPage` passes this while it is
   * still showing the PLACEHOLDER `editTarget` (the chain's first read is
   * in flight, nothing real to edit yet) — without it the placeholder's
   * text field and label are both interactive, and whatever the operator
   * types or drags into them is silently replaced the instant the real
   * seed lands and `reset(defaultValues)` runs. Save is already gated by
   * `saveBlocked` regardless of this prop (`currentChain === undefined`
   * during this same window), so this only closes the input side, not the
   * submit side.
   */
  readOnly?: boolean;
  /** Spec 466. The tile's camera, preselected in the editor's picker. Absent keeps today's empty start. */
  backdropCameraIdentifier?: string;
  /** Spec 466. Told the live element set after every edit, so a host can show it before it is saved. */
  onElementsChange?: (elements: OverlayElement[]) => void;
}

const DEFAULT_INPUT: CreateOverlayDraftInput = {
  name: '',
  elements: [newElement('Text')],
};

/**
 * Spec 300 (#2349) FR-019: the form edits the Text element wherever it
 * sits in the set, never index 0 — a mixed `[Box, Text]` draft must not
 * have its Box coerced into a label. `-1` when the set carries no Text
 * element at all (shapes only), which the render below turns into a
 * notice rather than a crash.
 */
function textIndexOf(elements: readonly OverlayElement[]): number {
  return elements.findIndex((element) => element.kind === 'Text');
}

/** Phase-6 should-fix 4 (#2350) — `readOnly`'s `OverlayEditor.onChange` override. */
function noop(): void {}

/**
 * ADR-0169 (issue #2847): `FrameGrabber` takes `stream` as a prop rather than
 * calling `useGetStreamQuery` itself, so this wrapper — which does have a
 * `<Provider>` above it — is where that call happens. `currentData`, not
 * `data` (spec 157 FR-002): this component is mounted per capture and never
 * re-propped, so its `cameraIdentifier` cannot change on a mounted instance
 * and the two spellings are indistinguishable today. Closed anyway so no
 * instance of the `data`-with-a-variable-argument shape stays live in this
 * file family. Module scope, not inline in `OverlayDraftForm`'s render — a
 * component defined inside another component's body would remount every
 * render, dropping its own WHEP session.
 */
function OverlayDraftFormFrameGrabber({
  cameraIdentifier,
  getToken,
  onCaptured,
  onFailed,
}: {
  cameraIdentifier: string;
  getToken: () => Promise<string | null>;
  onCaptured: (dataUrl: string) => void;
  onFailed: () => void;
}) {
  const { currentData: stream } = useGetStreamQuery(cameraIdentifier);
  return (
    <FrameGrabber
      cameraIdentifier={cameraIdentifier}
      stream={stream}
      getToken={getToken}
      onCaptured={onCaptured}
      onFailed={onFailed}
    />
  );
}

export function OverlayDraftForm({
  editTarget,
  onDone,
  onCancel,
  canvasWidthPx,
  canvasHeightPx,
  readOnly = false,
  backdropCameraIdentifier,
  onElementsChange,
}: OverlayDraftFormProps) {
  const isEdit = editTarget !== undefined;
  const [createOverlayDraft, createState] = useCreateOverlayDraftMutation();
  const [editDraftOverlayRevision, editState] = useEditDraftOverlayRevisionMutation();

  const { currentChain, chainFailed, chainFetching, refetchChain } = useDraftChain(editTarget?.overlayIdentifier);
  const { isLoading, error } = isEdit ? editState : createState;

  const getToken = useLatestAccessToken();

  useUnmountMutationReset(createState, editState);

  // The create seed (`DEFAULT_INPUT`) is untouched; edit seeds a second value
  // computed from `editTarget`, exactly as `LayoutEditorDialog.tsx:155-162`
  // computes one beside `EMPTY_CREATE`. `name` is still seeded from the
  // chain's real name even though the field is hidden in edit mode, so
  // `createOverlayDraftSchema` — unchanged — keeps validating it. The form
  // holds the WHOLE `elements` array (FR-018), not just the one the editor
  // shows.
  const defaultValues = useMemo<CreateOverlayDraftInput>(() => {
    if (editTarget === undefined) return DEFAULT_INPUT;
    // The cast is needed because `OverlayShape` (`overlays.api.ts`) declares
    // `kind: 'Box' | 'Ellipse'` as ONE interface, while `CreateOverlayDraftInput`
    // (the schema's inferred output) carries `boxSchema`'s and `ellipseSchema`'s
    // member types separately — a discriminated union of two distinct object
    // types, not one shape with a union-valued `kind` — so neither narrows onto
    // the other structurally. `color` here is also pre-`canonicalColor`
    // (upper-cased, alpha-appended); every element from the API already
    // satisfies that shape at runtime, but TS cannot see through the schema's
    // `.transform()` to know it. (This is unrelated to `OverlayLabel`'s own
    // optional `kind`/`color` — that is a different type, kept only for
    // `OverlayEditor.tsx`'s byte-identical guards, FR-016.)
    return { name: editTarget.name, elements: editTarget.elements as CreateOverlayDraftInput['elements'] };
  }, [editTarget]);

  const {
    control,
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<CreateOverlayDraftInput>({
    resolver: zodResolver(createOverlayDraftSchema),
    defaultValues,
  });

  // Spec 441. `fields` carries a client-side row `id` per element so a selection
  // follows its element across a move or a removal. That id is never part of the
  // submitted value (`input.elements` below is the form value, not `fields`).
  const { fields, append, move, remove } = useFieldArray({ control, name: 'elements' });
  const watchedElements = useWatch({ control, name: 'elements' });
  // The live values, falling back to the row for the render a row was just added.
  const elements = fields.map((field, index) => (watchedElements?.[index] ?? field) as OverlayElement);
  const [chosenId, setChosenId] = useState<string | null>(null);
  // The new row's id exists only once the append has rendered: select the front-most row then.
  const [addedAtCount, setAddedAtCount] = useState<number | null>(null);
  if (addedAtCount !== null && fields.length > addedAtCount) {
    setAddedAtCount(null);
    setChosenId(fields[fields.length - 1]!.id);
  }
  // FR-019 as before: the first Text element on open, never index 0 by assumption.
  // A reset regenerates the row ids, so a stale choice falls back to that default.
  const chosenIndex = fields.findIndex((field) => field.id === chosenId);
  const selectedIndex = chosenIndex >= 0 ? chosenIndex : Math.max(textIndexOf(elements), 0);
  const selectedId = fields[selectedIndex]?.id ?? '';
  // Row ids are regenerated by every reset (including the seeding one on mount), so only an
  // explicit choice may key the editor; keying the default selection by id would remount it.
  const editorKey = chosenIndex >= 0 ? selectedId : 'default';
  useElementsChangeNotifier(watchedElements, onElementsChange);
  const selectedElement = elements[selectedIndex];
  const selectedIsText = selectedElement?.kind === 'Text';
  const overlaps = textOverlaps(elements);

  // Re-seed when the target (or create/edit mode) changes between mounts.
  useEffect(() => {
    reset(defaultValues);
  }, [defaultValues, reset]);

  const { resolvedPreview, isResolving, resolveFailed } = useResolvedPreview(selectedElement);

  const cameras = useCameraChoices();

  const onSubmit = handleSubmit(async (input) => {
    if (editTarget !== undefined) {
      // FR-013: kept as defence-in-depth alongside `saveBlocked` below (spec
      // 160 FR-002). Before spec 160 this was the only guard and the button
      // itself was natively `disabled`, so this really was unreachable
      // through the UI; now the button is `aria-disabled` (clickable) and
      // the form's submit guard is the one that actually keeps this
      // unreached — this still narrows `currentChain` for the `.version`
      // read below, and stays as a second line of defence rather than
      // trusting the caller. Not the silent no-op `LayoutEditorDialog.tsx:183`
      // uses, which this deliberately does not copy (that button gives no
      // explanation at all).
      if (currentChain === undefined) return;
      // FR-018: the whole set, all N elements — not just the one the form
      // shows. `ReplaceElements`/FR-006 make an edit a wholesale replacement,
      // so sending only the one element the editor shows would silently
      // delete every other element on the draft's first console edit.
      const result = await editDraftOverlayRevision({
        overlayIdentifier: editTarget.overlayIdentifier,
        revisionNumber: editTarget.revisionNumber,
        version: currentChain.version,
        elements: input.elements,
      });
      if (!('error' in result)) {
        reset(defaultValues);
        onDone();
      }
      return;
    }
    const result = await createOverlayDraft(input);
    if (!('error' in result)) {
      reset(DEFAULT_INPUT);
      onDone();
    }
  }, selectFirstInvalidElement);

  // Spec 441 FR-009: a refusal names an element by index; the operator should
  // land on it, not on whichever label happened to be selected.
  function selectFirstInvalidElement(invalid: FieldErrors<CreateOverlayDraftInput>) {
    const failing = Array.isArray(invalid.elements) ? invalid.elements.findIndex((entry) => entry !== undefined) : -1;
    const row = failing >= 0 ? fields[failing] : undefined;
    if (row !== undefined) setChosenId(row.id);
  }

  function addElement(kind: OverlayElement['kind']) {
    if (readOnly || fields.length >= MAX_ELEMENTS) return;
    append(newElement(kind), { shouldFocus: false });
    setAddedAtCount(fields.length);
  }

  function moveElement(ordinal: number, delta: 1 | -1) {
    if (readOnly) return;
    move(ordinal, ordinal + delta);
  }

  function removeElement(ordinal: number) {
    if (readOnly || fields.length <= 1) return;
    if (ordinal === selectedIndex) {
      const remaining = fields.filter((_, index) => index !== ordinal);
      const position = selectionAfterRemove(listPositionOf(ordinal, fields.length), remaining.length);
      setChosenId(remaining[ordinalAt(position, remaining.length)]!.id);
    }
    remove(ordinal);
  }

  const { backendError, offerReload } = describeSaveRefusal(error);

  // Spec 156 (issue #2372). The chain read is the only thing blocking
  // Save (FR-013 above), so an operator clicks Retry *in order to* Save —
  // focus lands there, not restored to wherever it was, when the operator's
  // own re-read succeeds.
  // `ComponentRef<'button'>`, not `HTMLButtonElement` (spec 154's own
  // `e2e/overlays.spec.ts` fix, bc30486f) — this app's eslint config has no
  // per-tag DOM lib globals, and naming the type literally trips `no-undef`;
  // widening the config would be the gate-weakening ADR-0144 rules out.
  const saveRef = useRef<ComponentRef<'button'>>(null);
  const [colourRefused, setColourRefused] = useState(false);
  const onColourValidityChange = useCallback((valid: boolean) => setColourRefused(!valid), []);

  // Spec 160 (issue #2387) FR-002/FR-007. Computed once, consumed by both
  // the button's `aria-disabled` and the form's submit guard below — today
  // the two stated overlapping conditions separately (here and in
  // `onSubmit`'s own `currentChain === undefined` check above).
  //
  // `chainFetching` alongside `currentChain === undefined`: RTK Query keeps
  // `currentData` defined for the same query arg while a refetch is in
  // flight, so the version held is known-stale. The common trigger is not
  // Reload but the conflict's own `invalidatesTags` refetch —
  // `LayoutEditorDialog.tsx:400-417` has the evidence.
  //
  // `chainFailed` (FR-007): a refused re-read still LEAVES `currentData` at
  // the pre-re-read version — `queryThunk.rejected` writes only
  // `status`/`error` (`@reduxjs/toolkit` 2.12.0,
  // `dist/query/rtk-query.modern.mjs:1443-1455`) and `currentData` is that
  // raw substate `data` (`dist/query/react/rtk-query-react.modern.mjs:155`).
  // Without this term the gate reopens on a version already known stale, and
  // a click resubmits it for an identical second 409. For a Retry-originated
  // refusal, Retry stays the way out (`ChainRecoveryNotice`'s chain arm,
  // `chainArmActive` on `readFailed`) — but not for a Reload-originated one:
  // `chainArmActive` excludes `origin === 'reload'` precisely so a refused
  // Reload keeps its OWN arm mounted instead, and Reload itself is the way
  // out there (`ChainRecoveryNotice.tsx`'s `chainArmActive` definition).
  const saveBlocked =
    isLoading || colourRefused || (isEdit && (currentChain === undefined || chainFetching || chainFailed));

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

  return (
    <form onSubmit={handleFormSubmit} className="flex flex-col gap-4">
      {!isEdit && (
        <FormField label="Name" htmlFor="overlay-name" error={errors.name?.message}>
          <Input id="overlay-name" autoFocus {...register('name')} />
        </FormField>
      )}
      {selectedElement !== undefined && (
        // Phase-6 should-fix 4 (#2350): while `readOnly` (the PLACEHOLDER
        // seed, before the real chain read has landed), `onChange` is a
        // no-op rather than `field.onChange` — every mutation path
        // `OverlayEditor` has (typing, dragging, the keyboard nudges,
        // undo/redo) funnels through this one callback
        // (`useOverlayEditHistory`'s own `onChange(next)` calls), so this
        // closes all of them at once rather than one input channel at a
        // time. `pointer-events-none` is paired with it only as a visual
        // cue — the actual guarantee is the no-op, which also holds for a
        // Tab-focused keyboard edit that no CSS property would stop.
        <div aria-busy={readOnly || undefined} className={readOnly ? 'pointer-events-none opacity-60' : undefined}>
          <Controller
            key={editorKey}
            control={control}
            name={`elements.${selectedIndex}`}
            render={({ field }) => (
              <OverlayEditor
                key={editorKey}
                value={field.value as OverlayEditorValue}
                layerName={layerName(elements[selectedIndex]!, selectedIndex, elements)}
                inertElements={{
                  below: elements.slice(0, selectedIndex),
                  above: elements.slice(selectedIndex + 1),
                }}
                onChange={readOnly ? noop : field.onChange}
                onColourValidityChange={onColourValidityChange}
                canvasWidthPx={canvasWidthPx}
                canvasHeightPx={canvasHeightPx}
                getToken={getToken}
                initialCamera={backdropCameraIdentifier}
                resolvedPreview={resolvedPreview}
                isResolving={isResolving}
                resolveFailed={resolveFailed}
                cameras={cameras}
                renderFrameGrabber={({ cameraIdentifier, onCaptured, onFailed }) => (
                  <OverlayDraftFormFrameGrabber
                    cameraIdentifier={cameraIdentifier}
                    getToken={getToken}
                    onCaptured={onCaptured}
                    onFailed={onFailed}
                  />
                )}
              />
            )}
          />
        </div>
      )}
      <div aria-busy={readOnly || undefined} className={readOnly ? 'pointer-events-none opacity-60' : undefined}>
        <OverlayLayerList
          elements={elements}
          rowIds={fields.map((field) => field.id)}
          selectedId={selectedId}
          onSelect={setChosenId}
          onMove={moveElement}
          onRemove={removeElement}
          onAdd={addElement}
          canAdd={fields.length < MAX_ELEMENTS}
          canRemove={fields.length > 1}
          readOnly={readOnly}
        />
      </div>
      {/* Always mounted, so a screen reader is already watching it when a line appears. */}
      <div role="status" data-testid="overlay-editor-overlap-status">
        {overlaps.map(([first, second]) => (
          <p
            key={`${fields[first]!.id}:${fields[second]!.id}`}
            data-testid="overlay-editor-overlap-warning"
            className="text-sm text-fg-muted"
          >
            {`"${layerName(elements[first]!, first, elements)}" and "${layerName(elements[second]!, second, elements)}" overlap, so one may hide part of the other on the wall.`}
          </p>
        ))}
      </div>
      {selectedIsText &&
        (() => {
          const textErrorMessage = (errors.elements?.[selectedIndex] as { text?: { message?: string } } | undefined)
            ?.text?.message;
          if (textErrorMessage === undefined) return null;
          // #2365: a stable testid so a test can address this alert without an
          // unscoped role query, now that OverlayEditor's own OverlayGeometryFields
          // always mounts four `role="alert"` regions in the same tree.
          return (
            <p role="alert" data-testid="overlay-editor-dialog-label-error" className="text-sm text-accent-fault">
              {textErrorMessage}
            </p>
          );
        })()}
      {/*
        Not gated on `isEdit`: the chain query is `skipToken` outside edit
        mode, so `chainFailed`/`chainFetching` are already inert there, and
        create mode's own `backendError` (a name clash) still needs to
        render through this same composite (phase-6 review, the "one alert
        at a time" comment this replaces applied to both modes).
      */}
      <ChainRecoveryNotice
        noun="overlay"
        readFailed={chainFailed}
        reReading={chainFetching}
        onReRead={() => void refetchChain()}
        backendError={backendError}
        offerReload={offerReload}
        onReadRecovered={() => saveRef.current?.focus()}
      />
      <div className="flex justify-end gap-2">
        <Button type="button" variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        {/*
          `unavailable` (ADR-0151), not the native `disabled` attribute —
          it emits `aria-disabled` and the dimming classes without blurring
          the focused element, the same finding this repo already shipped
          twice: `ChainRecoveryNotice.tsx:31-33` (spec 156) and
          `OverlayEditor.tsx:649-657` (spec 154). `saveBlocked` above is
          what defines the gate; `handleFormSubmit` on the form is what
          actually enforces it now that the button stays clickable.
        */}
        <Button ref={saveRef} type="submit" unavailable={saveBlocked} className="aria-disabled:cursor-progress">
          {isLoading ? 'Saving…' : isEdit ? 'Save draft' : 'Save as draft'}
        </Button>
      </div>
    </form>
  );
}
