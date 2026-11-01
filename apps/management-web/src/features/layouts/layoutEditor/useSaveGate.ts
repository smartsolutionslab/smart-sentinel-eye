import { useRef, type ComponentRef, type FormEvent } from 'react';
import type { useLayoutChain } from './useLayoutChain.js';
import type { useLayoutEditorForm } from './useLayoutEditorForm.js';

interface SaveGateInput {
  isLoading: boolean;
  knownCameras: ReadonlyMap<string, unknown>;
  isEdit: boolean;
  currentChain: ReturnType<typeof useLayoutChain>['currentChain'];
  chainFetching: boolean;
  chainFailed: boolean;
  onSubmit: ReturnType<typeof useLayoutEditorForm>['onSubmit'];
}

export function useSaveGate({
  isLoading,
  knownCameras,
  isEdit,
  currentChain,
  chainFetching,
  chainFailed,
  onSubmit,
}: SaveGateInput) {
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
    isLoading || knownCameras.size === 0 || (isEdit && (currentChain === undefined || chainFetching || chainFailed));

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
  return { saveRef, saveBlocked, handleFormSubmit };
}
