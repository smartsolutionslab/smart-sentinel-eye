import {
  useCreateOverlayDraftMutation,
  useEditDraftOverlayRevisionMutation,
  useGetOverlayQuery,
  type OverlayLabel,
} from '@smart-sentinel-eye/shared/api/overlays.api';
import { createOverlayDraftSchema, type CreateOverlayDraftInput } from '@smart-sentinel-eye/shared/api/overlays.schema';
import { useResolveOverlayTextQuery } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { skipToken } from '@reduxjs/toolkit/query/react';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
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
 * Spec 152. Carries what the page already knows about the draft being
 * edited, so the dialog needs no lookup to render its first frame — the six
 * `OverlayLabel` fields lifted off the target `OverlayRevision`, not the
 * whole revision (a spread would carry `state`/`createdAt`/etc. into the form
 * value and then into the PATCH body).
 */
export interface OverlayEditTarget {
  overlayIdentifier: string;
  revisionNumber: number;
  name: string;
  label: OverlayLabel;
}

export interface OverlayEditorDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** When set the dialog edits an existing draft (spec 152); otherwise it creates. */
  editTarget?: OverlayEditTarget;
}

const DEFAULT_INPUT: CreateOverlayDraftInput = {
  name: '',
  label: {
    text: 'Overlay text',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    fontSizePx: 32,
  },
};

export function OverlayEditorDialog({ open, onOpenChange, editTarget }: OverlayEditorDialogProps) {
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
  // `currentData`, never `data` (phase-6 review, OverlayEditorDialogChainRetention.test.tsx):
  // `data` is RTK Query's last successful result for ANY argument this hook
  // has ever been called with, so it survives a `skipToken` step and an
  // argument change. This dialog stays permanently mounted in `OverlaysPage`
  // and is driven `editTarget: A -> undefined -> B` on the same component —
  // not an unmount and a fresh mount — so `data` would hand overlay B's
  // dialog overlay A's chain, including A's version, before B's own GET has
  // ever answered. `currentData` resets on both, which is what "re-read",
  // not "reused", requires. `apps/shared/src/ui/composites/OverlayEditorDialog`'s
  // own resolve-preview query documents the identical trap seventy lines below.
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
  // `auth?.` rather than `auth.`: unlike `CameraDetailPage`, this dialog is
  // exercised in tests with no `<AuthProvider>` in the tree, where
  // `react-oidc-context`'s real `useAuth()` warns and returns `undefined`
  // rather than throwing.
  const auth = useAuth();
  const accessTokenRef = useRef(auth?.user?.access_token);
  // eslint-disable-next-line react-hooks/refs -- see above
  accessTokenRef.current = auth?.user?.access_token;
  const getToken = useCallback(() => Promise.resolve(accessTokenRef.current ?? null), []);

  // Drop any prior backend error when the dialog closes so a stale banner
  // doesn't greet the operator on the next open.
  //
  // Resets BOTH mutation states, not the one `isEdit` names (phase-6 review):
  // `OverlaysPage.tsx` drives `open={editTarget !== undefined}`, so `open`
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

  // The create seed (`DEFAULT_INPUT`) is untouched; edit seeds a second value
  // computed from `editTarget`, exactly as `LayoutEditorDialog.tsx:155-162`
  // computes one beside `EMPTY_CREATE`. `name` is still seeded from the
  // chain's real name even though the field is hidden in edit mode, so
  // `createOverlayDraftSchema` — unchanged — keeps validating it.
  const defaultValues = useMemo<CreateOverlayDraftInput>(() => {
    if (editTarget === undefined) return DEFAULT_INPUT;
    return { name: editTarget.name, label: editTarget.label };
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

  // Re-seed when the target (or create/edit mode) changes between opens.
  useEffect(() => {
    reset(defaultValues);
  }, [defaultValues, reset]);

  // Spec 148 US1 + US3. The query lives here, not in `OverlayEditor` — three
  // suites render that component bare, with no Redux `<Provider>`, and
  // mounting the hook there fails all of them with "could not find
  // react-redux context value" (plan.md "Frontend wiring"). The settled text
  // drives the query; `value.text` (via `Controller` below) keeps driving the
  // input, never the reverse — `useDebouncedValue`'s own doc comment says the
  // field would drop characters otherwise.
  const labelText = useWatch({ control, name: 'label.text' }) ?? defaultValues.label.text;
  const settledLabelText = useDebouncedValue(labelText);
  const shouldResolve = settledLabelText.includes('{{');
  const {
    currentData,
    isFetching,
    isError: resolveFailed,
  } = useResolveOverlayTextQuery({ text: settledLabelText }, { skip: !shouldResolve });
  // `data` retains the last successful result across `skip` and arg changes
  // (RTK Query, not a bug here to work around) — clearing the field, or
  // closing and reopening this dialog for a different overlay, would keep
  // showing the previous resolve. `currentData` resets on both. `settled`
  // additionally withholds the preview while debounce is still catching up
  // to what was typed, so the panel never diffs live braces against a
  // response for an earlier version of the text (phase 6 blockers 2+3).
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
      const result = await editDraftOverlayRevision({
        overlayIdentifier: editTarget.overlayIdentifier,
        revisionNumber: editTarget.revisionNumber,
        version: currentChain.version,
        label: input.label,
      });
      if (!('error' in result)) {
        reset(defaultValues);
        onOpenChange(false);
      }
      return;
    }
    const result = await createOverlayDraft(input);
    if (!('error' in result)) {
      reset(DEFAULT_INPUT);
      onOpenChange(false);
    }
  });

  // Keyed on the code rather than the status (ADR-0119): "reload to see their
  // version" is useless advice for a name clash, and "try again" is useless
  // advice for a stale one or a state that moved out from under the operator.
  const staleConflict = isStaleConflict(error);
  const notDraft = problemCode(error) === 'OVERLAY_REVISION_NOT_DRAFT';
  // Create-mode only — this dialog's create 409 is always OVERLAY_NAME_TAKEN,
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
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          reset(defaultValues);
        }
        onOpenChange(next);
      }}
      title={isEdit ? 'Edit overlay draft' : 'New overlay'}
      description={
        isEdit
          ? `Editing draft v${editTarget.revisionNumber} of ${editTarget.name}. The change is saved onto this draft.`
          : 'Pick a name, type the label, and drag it to position. The overlay starts as a draft.'
      }
    >
      <form onSubmit={handleFormSubmit} className="flex flex-col gap-4">
        {!isEdit && (
          <FormField label="Name" htmlFor="overlay-name" error={errors.name?.message}>
            <Input id="overlay-name" autoFocus {...register('name')} />
          </FormField>
        )}
        <Controller
          control={control}
          name="label"
          render={({ field }) => (
            <OverlayEditor
              value={field.value}
              onChange={field.onChange}
              getToken={getToken}
              resolvedPreview={resolvedPreview}
              isResolving={isResolving}
              resolveFailed={resolveFailed}
            />
          )}
        />
        {errors.label?.text?.message !== undefined && (
          <p role="alert" className="text-sm text-accent-fault">
            {errors.label.text.message}
          </p>
        )}
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
