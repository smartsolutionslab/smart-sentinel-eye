import { useCallback, useRef, useState, type BaseSyntheticEvent, type ComponentRef, type FormEvent } from 'react';

interface SaveGateInput {
  isLoading: boolean;
  isEdit: boolean;
  currentChain: unknown;
  chainFetching: boolean;
  chainFailed: boolean;
  onSubmit: (event?: BaseSyntheticEvent) => Promise<void>;
}

export function useSaveGate({ isLoading, isEdit, currentChain, chainFetching, chainFailed, onSubmit }: SaveGateInput) {
  // Spec 156 (issue #2372). The chain read is the only thing blocking
  // Save (FR-013, see `OverlayDraftForm`'s `onSubmit`), so an operator clicks Retry *in order to* Save —
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
  // `OverlayDraftForm`'s `onSubmit`'s own `currentChain === undefined` check).
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

  return { saveRef, saveBlocked, onColourValidityChange, handleFormSubmit };
}
