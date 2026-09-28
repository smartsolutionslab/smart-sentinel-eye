# Implementation Plan: The latch the fake clock kept

**Spec**: [spec.md](./spec.md) · **Issue**: #2641 · **Branch**: `fix/2641-resolve-preview-waitfor-timeout`

## Summary

Two `OverlayEditorDialogResolvePreview` tests time out on CI because an earlier test in the same
file latches the **shared app store's** RTK `autoBatchEnhancer` (`notificationQueued` stays
`true` after its notify callback is scheduled on a fake clock and then discarded). From then on,
query `pending`/`rejected` update state but never notify React. Fix: give every test its own
store. Test file only; no production change.

## Declarations (ADR-0144)

| | |
|---|---|
| **Engineer** | `frontend-engineer` |
| **New ADR?** | **No.** A test-harness defect; ADR-0075's one-store-per-app stands (the app store is untouched). |
| **Phase-4a colour** | **Behaviour-preserving (characterisation), test-harness only.** No production behaviour moves; no assertion changes. The evidence is the counterfactual pair (spec SC-001 red on today's harness under an injected slow window, SC-002 green on the fixed one), captured by `test-writer` **before** the engineer touches the file. |

## Context

- **Bounded context / layer**: none — `apps/management-web` test code.
- **Files touched**
  - `apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx` — the fix.
  - `apps/management-web/src/test/setup.ts` — comment-only correction of the causal paragraph.
- **Files not touched**: `src/app/store.ts`, `OverlayEditorDialog.tsx`, anything in `apps/shared`.
- **Messaging / entities / boundaries**: N/A.
- **Latency**: N/A.

## Design

### Per-test store (FR-001)

Mirror `OverlayEditorDialogChainRetention.test.tsx:96-101`:

```ts
function createStore() {
  return configureStore({
    reducer: {
      [overlaysApi.reducerPath]: overlaysApi.reducer,
      [systemVariablesApi.reducerPath]: systemVariablesApi.reducer,
    },
    middleware: (getDefault) => getDefault().concat(overlaysApi.middleware, systemVariablesApi.middleware),
  });
}
```

- `overlaysApi` because `useGetOverlayQuery(skipToken)` and the mocked module's other hooks still
  read its slice; `systemVariablesApi` for the resolve query. Cameras/streams hooks are mocked
  and need no slice. **Validated** in the phase-1 scratch run (6/6 green, with and without the
  stall). If the engineer finds another un-mocked hook needs a slice, add it — do not import
  `app/store.ts`.
- **Keep the dynamic-import ordering.** `vi.stubEnv('VITE_API_GATEWAY_URL', …)` must precede the
  import of any `*.api` module (gateway resolves the origin at module load). `configureStore`
  and `overlaysApi` are imported the same dynamic way as `systemVariablesApi` today.
- `renderDialog()` takes the store (or reads a `let store` assigned in `beforeEach`); the
  existing `store.dispatch(systemVariablesApi.util.resetApiState())` goes — a new store has an
  empty cache by construction.
- RTK Query's middleware state (`updateSyncTimer`, subscriptions) is per store instance, so a
  fresh store also cannot inherit a pending subscription timer.

### Comments (FR-004)

- File header / `beforeEach`: replace the "store is the app's real singleton" rationale with one
  sentence on why each test gets its own store (the enhancer's notification latch survives
  `resetApiState`).
- #2520 guard doc comment: drop "the reason is the deadline rather than the assertion"; the guard's
  remaining purpose (fail here if `asyncUtilTimeout` drops below ~1.5 s) stays true.
- `setup.ts`: keep `asyncUtilTimeout: 10_000` and the fake-timer warning; correct the
  #2520/#2419 causal claim to point at this spec. Comment-only — prove it by hashing
  comment-stripped content (memory: *characterise a comment-only change by hashing the code*).

### Rejected alternatives

| Alternative | Why not |
|---|---|
| Raise `asyncUtilTimeout` again | The element never arrives; a longer wait only makes the failure slower. |
| `configureStore({ enhancers: g => g({ autoBatch: { type: 'tick' } }) })` in `app/store.ts` | Changes production batching to fix a test; `queueMicrotask` is un-faked today only by Vitest's default. |
| `vi.useFakeTimers({ toFake: [...] })` excluding `requestAnimationFrame` | Relies on jsdom's real rAF firing the notify; silently re-breaks if someone widens `toFake`; leaves the store shared. |
| Drain timers (`runOnlyPendingTimersAsync`) before `useRealTimers()` | Fires the pending 250 ms debounce, which starts a second query whose `pending` re-queues the notify on the fake clock — the hazard moved, not removed. |
| Reorder the tests | Order-dependence is the defect; reordering hides it. |

## Verification protocol (phase 4a → 4b → 5)

1. **4a, red half** — `test-writer` creates an **uncommitted** scratch copy
   (`ZzLatchProbe.test.tsx`, same directory so the workspace config applies) of today's file,
   adds `const realSetTimeout = globalThis.setTimeout;` at module scope and
   `await new Promise((r) => realSetTimeout(r, 600));` as the last statement of *"Never flags…"*'
   `try` block, runs it, and returns the verbatim output. Expected: the two tests fail at ~10 s
   with the `placeholder-preview-error` signature. Also runs the **unmodified** real file once and
   returns that output (green, the characterisation baseline).
2. **4b** — engineer applies the design; may not touch any `expect(` line.
3. **4b, green half** — re-create the stall probe from the **fixed** file; 10 runs, all 6/6.
   Delete the probe. Then 20 consecutive runs of the real file, and the full workspace suite.
4. **PR body** quotes 1 and 3 verbatim, plus `git diff --stat` and a grep showing no `expect(`
   line in the diff.

A permanent in-repo guard is deliberately **not** added: a guard would need a real sleep in a
committed test, which ADR-0150's rule exists to keep out; and the fix removes the channel
structurally rather than narrowing a timing window.

## Risks

- **Assumption, marked**: the natural trigger is `subscriptionsUpdated`'s real 500 ms timer.
  The fix does not depend on it being that action (see spec *Assumptions*).
- **Sibling files** with singleton-store + fake-timer shape are out of scope; the orchestrator
  should file a follow-up (spec *Edge Cases*).
