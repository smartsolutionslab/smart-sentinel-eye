import {
  useCreateOverlayDraftMutation,
  useEditDraftOverlayRevisionMutation,
  useGetOverlayQuery,
  type OverlayElement,
  type OverlayLabel,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import {
  createOverlayDraftSchema,
  DEFAULT_OVERLAY_COLOR,
  type CreateOverlayDraftInput,
} from '@smart-sentinel-eye/shared/api/overlays.schema';
import { useResolveOverlayTextQuery } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { skipToken } from '@reduxjs/toolkit/query/react';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { ChainRecoveryNotice } from '@smart-sentinel-eye/shared/ui/composites/ChainRecoveryNotice';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { OverlayEditor } from '@smart-sentinel-eye/shared/ui/composites/OverlayEditor';
import {
  CONFLICT_FALLBACK,
  isStaleConflict,
  problemCode,
  problemDetail,
} from '@smart-sentinel-eye/shared/api/problemDetail';
import { useDebouncedValue } from '@smart-sentinel-eye/shared/hooks';
import { zodResolver } from '@hookform/resolvers/zod';
import { useCallback, useEffect, useMemo, useRef, type ComponentRef, type FormEvent } from 'react';
import { useAuth } from 'react-oidc-context';
import { Controller, useForm, useWatch } from 'react-hook-form';

/**
 * Spec 152. Carries what the caller already knows about the draft being
 * edited, so the form needs no lookup to render its first frame — the
 * `elements` array lifted off the target `OverlayRevision`, not the whole
 * revision (a spread would carry `state`/`createdAt`/etc. into the form
 * value and then into the PATCH body).
 *
 * <p>
 * Spec 150 (#2345) FR-018: `elements` is the WHOLE set, not just the one this
 * form's editor shows. Spec 300 (#2349) FR-019: the shared `OverlayEditor`
 * still edits exactly one Text element (FR-016) — found by kind, never by a
 * fixed index — but this must hold and submit every element the draft
 * carries, or the rest are silently deleted on the first console edit of a
 * mixed-kind overlay.
 * </p>
 */
export interface OverlayEditTarget {
  overlayIdentifier: string;
  revisionNumber: number;
  name: string;
  elements: OverlayElement[];
}

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
}

const DEFAULT_INPUT: CreateOverlayDraftInput = {
  name: '',
  elements: [
    {
      kind: 'Text',
      color: DEFAULT_OVERLAY_COLOR,
      text: 'Overlay text',
      normalizedX: 0.1,
      normalizedY: 0.1,
      normalizedWidth: 0.3,
      normalizedHeight: 0.08,
      fontSizePx: 32,
    },
  ],
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

export function OverlayDraftForm({ editTarget, onDone, onCancel, canvasWidthPx, canvasHeightPx }: OverlayDraftFormProps) {
  const isEdit = editTarget !== undefined;
  const [createOverlayDraft, createState] = useCreateOverlayDraftMutation();
  const [editDraftOverlayRevision, editState] = useEditDraftOverlayRevisionMutation();

  // The If-Match version has to be the chain's *current* one (ADR-0113), read
  // back rather than inferred: in US1 the page's held version merely might be
  // stale by the time Save is clicked, and in US2 the branch that got the
  // operator here was itself a write, so the page's version is already one
  // behind. `version + 1` is the obvious wrong implementation and it works on
  // a single-operator machine (LayoutEditorDialog.tsx:59-65).
  //
  // `currentData`, never `data` (phase-6 review, re-pinned by
  // `OverlayEditPageNavigation.test.tsx` on the route): `data` is RTK
  // Query's last successful result for ANY argument this hook has ever been
  // called with, so it survives a `skipToken` step and an argument change.
  // `currentData` resets on both, which is what "re-read", not "reused",
  // requires.
  //
  // `refetchOnMountOrArgChange`: without it a reopen within the 60s cache
  // window answers from cache with no request, which makes FR-011's "re-read
  // from the server" only sometimes true. Does not stand in for the
  // `currentData` fix above — `data` would still shadow a fresh fetch either
  // way.
  const {
    currentData: currentChain,
    isError: chainFailed,
    isFetching: chainFetching,
    refetch: refetchChain,
  } = useGetOverlayQuery(editTarget?.overlayIdentifier ?? skipToken, { refetchOnMountOrArgChange: true });
  const { isLoading, error } = isEdit ? editState : createState;

  // Spec 147 T010. Stable identity, holding the newest token behind a ref —
  // copied from `CameraDetailPage.tsx:20-38`, including its reasoning.
  // `OverlayEditor` puts this into a WHEP session's connect effect the same
  // way `CameraViewer` does, so a fresh function every render would tear that
  // session down and reconnect it on every render rather than only when the
  // camera changes — the same failure that silently killed the decode
  // sampler once already (issue 1889).
  //
  // `auth?.` rather than `auth.`: unlike `CameraDetailPage`, this form is
  // exercised in tests with no `<AuthProvider>` in the tree, where
  // `react-oidc-context`'s real `useAuth()` warns and returns `undefined`
  // rather than throwing.
  const auth = useAuth();
  const accessTokenRef = useRef(auth?.user?.access_token);
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = auth?.user?.access_token;
  const getToken = useCallback(() => Promise.resolve(accessTokenRef.current ?? null), []);

  // Spec 305 (#2350). Resets BOTH mutation states on unmount — not on an
  // `open` prop, which this form does not have. The dialog that used to host
  // this body kept it permanently mounted and reset explicitly on close; this
  // form is instead always given a fresh mount per target (the dialog's own
  // `Dialog` unmounts its children on close, and `OverlayEditPage`/
  // `OverlayCreatePage` unmount this form entirely on navigating away), so a
  // cleanup-on-unmount reset is the equivalent guarantee: a refused save's
  // banner must never survive into a later mount of this form for a
  // different target.
  //
  // Resets BOTH, not the one `isEdit` names (phase-6 review, carried over
  // from the dialog this form replaces): a mode-selected reset would always
  // clear create's state, leaving a refused edit's error free to leak into
  // whatever mounts next.
  //
  // eslint-disable-next-line react-hooks/exhaustive-deps -- deliberately
  // mount-once: the cleanup must run exactly once, on unmount, not on every
  // render the mutation hooks happen to return a new object from.
  useEffect(() => {
    return () => {
      createState.reset();
      editState.reset();
    };
  }, []);

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

  // Spec 300 (#2349) FR-019: fixed for the life of one open/edit — the set
  // of kinds a draft carries does not change interactively, only an
  // element's own fields do, so this is derived from the seed, not watched.
  const textIndex = useMemo(() => textIndexOf(defaultValues.elements), [defaultValues]);

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

  // Re-seed when the target (or create/edit mode) changes between mounts.
  useEffect(() => {
    reset(defaultValues);
  }, [defaultValues, reset]);

  // Spec 148 US1 + US3. The query lives here, not in `OverlayEditor` — three
  // suites render that component bare, with no Redux `<Provider>`, and
  // mounting the hook there fails all of them with "could not find
  // react-redux context value" (plan.md "Frontend wiring"). The settled text
  // drives the query; `value.text` (via `Controller` below) keeps driving the
  // input, never the reverse — `useDebouncedValue`'s own doc comment says the
  // field would drop characters otherwise. The Text element only (spec 150;
  // spec 300 FR-019) — this form edits exactly one label, FR-016's
  // single-label editor seam. `''` when the set carries no Text element
  // (shapes only) — there is nothing to resolve.
  const hasTextElement = textIndex >= 0;
  const defaultLabelText = hasTextElement ? (defaultValues.elements[textIndex] as OverlayLabel).text : '';
  // Watches the whole array, not `elements.${textIndex}.text` — `textIndex`
  // can be `-1` (no Text element), and a `useWatch` call must not be made
  // conditional on a value that can change across renders of the same
  // mounted form (rules of hooks).
  const watchedElements = useWatch({ control, name: 'elements' });
  const watchedLabelText = hasTextElement
    ? (watchedElements?.[textIndex] as OverlayLabel | undefined)?.text
    : undefined;
  const labelText = watchedLabelText ?? defaultLabelText;
  const settledLabelText = useDebouncedValue(labelText);
  const shouldResolve = settledLabelText.includes('{{');
  const {
    currentData,
    isFetching,
    isError: resolveFailed,
  } = useResolveOverlayTextQuery({ text: settledLabelText }, { skip: !shouldResolve });
  // `data` retains the last successful result across `skip` and arg changes
  // (RTK Query, not a bug here to work around) — clearing the field, or a
  // fresh mount of this form for a different overlay, would keep showing the
  // previous resolve. `currentData` resets on both. `settled` additionally
  // withholds the preview while debounce is still catching up to what was
  // typed, so the panel never diffs live braces against a response for an
  // earlier version of the text (phase 6 blockers 2+3).
  const settled = settledLabelText === labelText;
  const resolvedPreview = settled ? currentData : undefined;
  const isResolving = isFetching || !settled;

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
  });

  // Keyed on the code rather than the status (ADR-0119): "reload to see their
  // version" is useless advice for a name clash, and "try again" is useless
  // advice for a stale one or a state that moved out from under the operator.
  const staleConflict = isStaleConflict(error);
  const notDraft = problemCode(error) === 'OVERLAY_REVISION_NOT_DRAFT';
  // Create-mode only — this form's create 409 is always OVERLAY_NAME_TAKEN,
  // never the stale-version or not-a-draft conflicts edit mode can hit.
  const nameTaken = problemCode(error) === 'OVERLAY_NAME_TAKEN';
  const backendError = problemDetail(
    error,
    staleConflict
      ? CONFLICT_FALLBACK
      : notDraft
        ? 'This revision is no longer a draft. Reload to see its current state.'
        : nameTaken
          ? 'That overlay name is already taken. Choose a different one.'
          : 'Could not save the overlay. Try again.',
  );
  // Reload, never retry: retrying replays the same stale intent over
  // whoever wrote in between (staleConflict), or resubmits against a
  // revision that has already left draft (notDraft) — neither can succeed.
  const offerReload = staleConflict || notDraft;

  // Spec 156 (issue #2372). The chain read is the only thing blocking
  // Save (FR-013 above), so an operator clicks Retry *in order to* Save —
  // focus lands there, not restored to wherever it was, when the operator's
  // own re-read succeeds.
  // `ComponentRef<'button'>`, not `HTMLButtonElement` (spec 154's own
  // `e2e/overlays.spec.ts` fix, bc30486f) — this app's eslint config has no
  // per-tag DOM lib globals, and naming the type literally trips `no-undef`;
  // widening the config would be the gate-weakening ADR-0144 rules out.
  const saveRef = useRef<ComponentRef<'button'>>(null);

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
  const saveBlocked = isLoading || (isEdit && (currentChain === undefined || chainFetching || chainFailed));

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
      {hasTextElement ? (
        <Controller
          control={control}
          name={`elements.${textIndex}`}
          render={({ field }) => (
            <OverlayEditor
              value={field.value as OverlayLabel}
              onChange={field.onChange}
              canvasWidthPx={canvasWidthPx}
              canvasHeightPx={canvasHeightPx}
              getToken={getToken}
              resolvedPreview={resolvedPreview}
              isResolving={isResolving}
              resolveFailed={resolveFailed}
            />
          )}
        />
      ) : (
        // FR-019: a mixed-kind draft with no Text element at all (shapes
        // only) has nothing for this single-label editor to show. The
        // shapes themselves are not coerced into a label, and Save still
        // sends them unchanged (onSubmit carries the whole `elements`
        // array either way).
        <p data-testid="overlay-editor-dialog-shapes-only-notice" className="text-sm text-fg-muted">
          This overlay has only shapes. Edit them through the API until the editor supports shapes (#2343).
        </p>
      )}
      {hasTextElement &&
        (() => {
          const textErrorMessage = (errors.elements?.[textIndex] as { text?: { message?: string } } | undefined)?.text
            ?.message;
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
