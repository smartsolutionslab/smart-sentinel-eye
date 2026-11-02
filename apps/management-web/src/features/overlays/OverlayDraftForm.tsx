import {
  useCreateOverlayDraftMutation,
  useEditDraftOverlayRevisionMutation,
  type OverlayElement,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import { MAX_ELEMENTS } from '@smart-sentinel-eye/shared/api/overlays.schema';
import { useGetStreamQuery } from '@smart-sentinel-eye/shared/api/streams.api';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { ChainRecoveryNotice } from '@smart-sentinel-eye/shared/ui/composites/ChainRecoveryNotice';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { OverlayEditor, type OverlayEditorValue } from '@smart-sentinel-eye/shared/ui/composites/OverlayEditor';
import { OverlayLayerList } from '@smart-sentinel-eye/shared/ui/composites/OverlayLayerList';
import { layerName } from '@smart-sentinel-eye/shared/ui/composites/overlayLayers';
import { FrameGrabber } from '@smart-sentinel-eye/shared/ui/composites/FrameGrabber';
import type { OverlayEditTarget } from './overlayDraftForm/overlayEditTarget.js';
import { DEFAULT_INPUT, useOverlayDraftFormState } from './overlayDraftForm/useOverlayDraftFormState.js';
import { useElementSelection } from './overlayDraftForm/useElementSelection.js';
import { useCameraChoices } from './overlayDraftForm/useCameraChoices.js';
import { useResolvedPreview } from './overlayDraftForm/useResolvedPreview.js';
import { useDraftChain } from './overlayDraftForm/useDraftChain.js';
import { describeSaveRefusal } from './overlayDraftForm/describeSaveRefusal.js';
import { OverlapStatus } from './overlayDraftForm/OverlapStatus.js';
import { TextErrorAlert } from './overlayDraftForm/TextErrorAlert.js';
import { useSaveGate } from './overlayDraftForm/useSaveGate.js';
import { useUnmountMutationReset } from './overlayDraftForm/useUnmountMutationReset.js';
import { useLatestAccessToken } from './overlayDraftForm/useLatestAccessToken.js';
import { Controller } from 'react-hook-form';

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

  const { control, register, handleSubmit, errors, reset, defaultValues, fields, append, move, remove, elements } =
    useOverlayDraftFormState(editTarget, onElementsChange);
  const {
    selectedIndex,
    selectedId,
    editorKey,
    setChosenId,
    selectFirstInvalidElement,
    addElement,
    moveElement,
    removeElement,
  } = useElementSelection({ fields, elements, append, move, remove, readOnly });
  const selectedElement = elements[selectedIndex];
  const selectedIsText = selectedElement?.kind === 'Text';

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

  const { backendError, offerReload } = describeSaveRefusal(error);

  const { saveRef, saveBlocked, onColourValidityChange, handleFormSubmit } = useSaveGate({
    isLoading,
    isEdit,
    currentChain,
    chainFetching,
    chainFailed,
    onSubmit,
  });

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
      <OverlapStatus elements={elements} rows={fields} />
      {selectedIsText && <TextErrorAlert errors={errors} selectedIndex={selectedIndex} />}
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
