# Plan 158 — Save stays live during a re-read

**Phase:** 2 (Plan) — ADR-0037 · **Spec:** [`spec.md`](./spec.md) · **Issue:** #2379
**Base:** `develop` at `37011568`.

---

## 1. Context and layers

**Frontend only.** No bounded context, no Domain, no Application, no
Infrastructure, no Api. Nothing in `src/` is touched; nothing in
`Shared.Contracts` is touched; no migration, no Wolverine message, no domain or
integration event. The cross-context boundary rules (NetArchTest) are not
engaged because no .NET project changes.

The whole change lives in one React component's render, in one app:

```
apps/management-web/src/features/overlays/OverlayEditorDialog.tsx   ← the fix (1 term)
apps/management-web/src/features/layouts/LayoutEditorDialog.tsx     ← 1 comment correction only
```

`apps/shared` is **not** touched. `apps/kiosk-web` is **not** touched.
`ChainRecoveryNotice` is **not** touched — it already receives the same
`chainFetching` value as `reReading` and already renders the live status that
explains the disabled Save (FR-004).

## 2. The change

`OverlayEditorDialog.tsx:303`, one term:

```
disabled={isLoading || (isEdit && currentChain === undefined)}
      →
disabled={isLoading || (isEdit && (currentChain === undefined || chainFetching))}
```

`chainFetching` is **already in scope** at `:92` — this adds no binding, no
hook, no state, no import. It is the layout dialog's shape minus the
layout-only `knownCameras` term (FR-002).

A short comment goes above it explaining *why* the second term exists (the
`currentData`-survives-a-same-arg-refetch semantics, and the 412-invalidation
trigger), cross-referencing the layout dialog rather than restating it. `why`
only — `CLAUDE.md` §Karpathy, no drive-by comments.

## 3. What this is not

- **Not** a change to `onSubmit`. The existing `if (currentChain === undefined) return;`
  guard at `:189` stays exactly as is. Adding a `chainFetching` guard there
  would be a second, unrequested mechanism for the same thing, and the comment
  at `:185-188` already describes that guard as defensive rather than
  reachable — which the fix makes *more* true, not less.
- **Not** an `aria-disabled` conversion. See spec §3 out-of-scope item 2 and
  T006.
- **Not** a shared-helper extraction. Two call sites is not a pattern; a
  `useChainGate()` hook would be speculative generality (ADR-0036) and would
  put a shared file in the path of two otherwise-disjoint features.

## 4. Test design

### 4.1 File placement — the disjoint-file rule (ADR-0109)

Two new tests, in two **existing** files, in two different feature directories:

| Test | File | Owner |
|---|---|---|
| US1 (RED) | `apps/management-web/src/features/overlays/OverlayEditorDialogChainRecovery.test.tsx` | overlays |
| US2 (GREEN) | `apps/management-web/src/features/layouts/LayoutEditorDialogChainRecovery.test.tsx` | layouts |

Disjoint files, so `[P]`. **No new test file is created**: both target files
already carry the exact harness these tests need, and a sixth
`OverlayEditorDialog*.test.tsx` for one `it` would be worse than an append.
This differs from spec 156's rule-2 (which created new files to avoid a
concurrent PR's append conflict) because no concurrent PR touches either file
at `37011568` — verify that before appending; if one does, fall back to new
files named `*ChainGate.test.tsx`.

### 4.2 Why these two files, specifically

Both already build the state machine the tests need and neither currently
asserts Save's availability in the in-flight window:

- `OverlayEditorDialogChainRecovery.test.tsx` — `ChainQueryState` with an
  explicit `isFetching` (`:31-35`), a `useSyncExternalStore`-backed
  `setChainQueryState` so a state step actually re-renders the mounted dialog
  (`:53-58`), and `beginReRead()` (`:160-162`) which sets
  `{ ...chainQueryState, isError: false, isFetching: true }` — **retaining
  `data`**, which is the precise RTK Query semantic under test. Its FR-008 test
  (`:360-389`) already stages `data: { version 7 }` + a 409
  `OVERLAY_REVISION_STALE` + a Reload click, and asserts **focus only**.
- `LayoutEditorDialogChainRecovery.test.tsx` — the identical harness
  (`:147-149`, `:334-363`) for the layout side.

So each new test is a new `it` reusing an established `describe`'s
`beforeEach`, not new scaffolding.

### 4.3 US1 — the RED test

Stage exactly the FR-008 precondition, then assert what FR-008 does not:

1. `chainQueryState = { data: { overlayIdentifier, version: 7 }, isError: false, isFetching: false }`
2. `editError` = 409 `OVERLAY_REVISION_STALE`; render; find **Reload**.
3. `await user.click(reload)` → the mocked `refetchChain` runs `beginReRead()`
   → state becomes `{ data: v7, isError: false, isFetching: true }`.
4. **Assert Save is disabled.** *Fails today*: `currentChain` is defined, so the
   shipped predicate is `false`.
5. **Assert the harm, not only the attribute** — `await user.click(save)` and
   assert `editDraftMock` was **not called again** (call count unchanged from
   the one that produced the 409). An attribute assertion alone would pass a
   cosmetic fix that left the form submittable by Enter.
6. **The complement, in the same `it`** — step the state to
   `{ data: v8, isError: false, isFetching: false }`, assert Save is **enabled**,
   click it, and assert the mutation was called with `version: 8`. Without this
   leg a predicate hard-wired to `disabled` would pass, which is how
   `LayoutEditorDialogChainRetention.test.tsx:218-222` describes closing the
   same hole.

**Expected red, quoted in the PR (ADR-0139).** Step 4 is the failing assertion.
If the test arrives green, the harness is wrong (most likely `beginReRead` was
overridden, or `refetchChainMock` was not given the default implementation from
the `beforeEach` at `:172-176`) — fix the harness, do not weaken the assertion.

### 4.4 US2 — the GREEN characterisation test

The same six steps against `LayoutEditorDialogChainRecovery.test.tsx`
(`LAYOUT_REVISION_STALE`, `layoutIdentifier`, `editDraftMock`). Expected
**green before any production edit** — capture that output.

Two obligations that follow from it being characterisation:

- It must pass **unmodified** after T003. It should be unaffected (T003 touches
  the overlay dialog), so a failure here means T003 leaked.
- It must be **proved by counterfactual** (T004), because a characterisation
  test that passes for the wrong reason is exactly what this story exists to
  rule out — and this repository has been wrong about that twice (#2371's
  `createState.reset()`, and the `data`/`currentData` counterfactual in
  `LayoutEditorDialogChainRetention.test.tsx:33-38`).

### 4.5 The conflict scenario (spec §5, "the 412's own invalidation refetch")

**Covered by construction, not by a third test.** Both triggers — the Reload
click and the invalidation refetch — reduce to the identical component state
(`currentData` defined, `isFetching` true, same argument). The mocked harness
cannot distinguish them, and an unmocked test that drove the real invalidation
would be re-testing RTK Query. The mechanism is documented in spec §1 and in
`LayoutEditorDialog.tsx:403-413`, where it was verified against a real 412.
Phase 5's manual procedure (spec §8) observes the real thing.

### 4.6 Create mode (FR-003)

No new test. The `isEdit &&` guard is unchanged and the existing suites already
cover create mode (`OverlayEditorDialog.test.tsx`). The regression net is the
existing tests staying green.

## 5. Risk

| Risk | Mitigation |
|---|---|
| The US1 test passes on first run | It is a phase-4 failure, not a shortcut (ADR-0139). Diagnose the harness — §4.3. |
| The new term deadlocks Save (never re-enables) | §4.3 step 6 is the complement leg; it fails if the gate sticks. |
| Focus is destroyed when Save disables while focused | Real, out of scope, filed by T006 — spec §3 item 2. Do not fix it here. |
| Appending to a file a parked PR also touches | Check open PRs before appending; fall back to new `*ChainGate.test.tsx` files (§4.1). |

## 6. Constitution and ADR alignment

- **ADR-0113** — the client half of two-layer optimistic concurrency; this
  restores the invariant that the submitted `If-Match` is the last version
  actually read.
- **ADR-0075** — the fix is a correct reading of RTK Query's `currentData` /
  `isFetching` contract; no new state management.
- **ADR-0139 / §Testing** — two obligations, one per story, declared in spec §7.
- **ADR-0109** — T001 and T002 are `[P]`; they own disjoint files.
- **§IV** — N/A, argued in spec §0. No leg, no measurement.
- **§II (primitive obsession)** — not engaged; TypeScript frontend.
- **ADR-0036** — smallest possible change: one term, one comment, two tests.

## 7. Gate

Plan aligns with the constitution and the ADRs above. **No new ADR is needed**
— every decision here is an application of ADR-0113 and ADR-0075 as already
written, and nothing architectural is being chosen.
