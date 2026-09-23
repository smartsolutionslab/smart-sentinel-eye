# Tasks: The token a frozen session would keep

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Spec
number**: 222 · **Branch**: `2302-the-token-a-frozen-session-would-keep` ·
**Issue**: #2302

**Phase-4a colour**: characterisation, **observed green**, with a **mandatory
counterfactual** (T006). See spec §*Phase-4a colour*.

**Agent**: one `frontend-engineer`, sequentially. There is nothing for a
`test-writer`/engineer split to divide here — the change *is* the test, and the
production code is read-only.

---

## Parallelism, stated once

**There is none, and saying so is the useful answer.** Every task below reads or
writes the same single file,
`apps/management-web/src/features/cameras/CameraViewerLifecycle.test.tsx`.
ADR-0109 marks `[P]` for disjoint file ownership; nothing here is disjoint, so
**no task carries `[P]`**.

`[T007]` is the one exception in kind — it writes no repository file at all, only
a GitHub issue — so it *could* run beside the others. It is still left
unparallelised: it depends on T006's output, and fanning out one `gh issue
create` against a five-minute slice buys nothing.

**There is no foundational work.** No `Shared.Kernel`, no `Shared.Contracts`, no
`AppHost`, no Aspire resource, no migration, no `.csproj`. The orchestrator
should dispatch this as **one sequential slice to one agent** and expect a diff
of one file.

---

## User Story 1 — A frozen token fails the build (P1)

*The whole shippable slice. Closes #2302 on its own.*

### `[T001]` `[US1]` Confirm the premise before writing anything

Three things this spec rests on, all cheap, all to be **observed rather than
assumed** — this repository has delivered work against stale premises before, and
spec 222 exists because a test's claim was trusted instead of checked.

1. **The three production lines are as quoted.** Read
   `apps/shared/src/ui/composites/useWhepSession.ts` and confirm:
   `:134` `const getTokenRef = useRef(getToken);`,
   `:135-137` the dependency-array-less `useEffect` assigning
   `getTokenRef.current = getToken;`, and
   `:338` `getToken: () => getTokenRef.current(),`.
   Recorded as matching on `origin/develop` at `da7e398f`.
2. **The test is as quoted.** Read
   `apps/management-web/src/features/cameras/CameraViewerLifecycle.test.tsx` and
   confirm the mock at `:10-18` forwards the constructor argument to `construct`,
   and that the first test (`:46-63`) asserts only `construct` count and `close`
   absence.
3. **The baseline is green.** Run the file:
   `cd apps/management-web && npx vitest run src/features/cameras/CameraViewerLifecycle.test.tsx`
   → expect 3 passed.

**If (1) or (2) has drifted: STOP.** Do not adapt the assertion to whatever is
there. A changed ref mechanism is a different spec. Report and park.

**Depends on**: nothing. **Blocks**: everything.

**Done when**: all three confirmed in writing, with the observed line numbers and
the vitest count quoted.

---

### `[T002]` `[US1]` Read the two precedents in the same file

No code yet. Read, in this order:

- `CameraViewerLifecycle.test.tsx:73-75` — the file's **own** idiom for reading
  the captured options object:
  `const options = construct.mock.calls[0]![0] as { onConnectionStateChange?: ... }`.
  Mirror this shape exactly for `getToken` (plan §3). Do not introduce a shared
  type alias, a helper, or a new mock (NFR-001).
- `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx:657-682` — the
  sibling test in the package that actually owns the hook. **Read it to
  understand why it is not being edited**: it drives a real `FakePeerConnection`
  and a `fetch` mock rather than a constructor spy, so freshness there would have
  to be read off an `Authorization` header after forcing a reconnect. That is a
  second slice with a design question in it, filed by T007 — not a line to add
  here.

**Depends on**: T001. **Blocks**: T003.

**Done when**: the implementer can state, without re-reading, which existing cast
idiom the new lines will use and why the `apps/shared` sibling is out of scope.

---

### `[T003]` `[US1]` Add the freshness assertion

Edit **one file**:
`apps/management-web/src/features/cameras/CameraViewerLifecycle.test.tsx`, first
test only.

Make exactly these changes (plan §3 carries the sketch):

1. The test callback becomes `async`.
2. The title gains a clause naming the freshness half — the old title claims only
   the negative, and leaving it would leave the file describing a test it no
   longer is. Suggested:
   `'Does not renegotiate the peer connection when the getToken closure changes between renders, and the session can still reach the fresh token'`.
3. **After** the existing `expect(construct).toHaveBeenCalledTimes(1)` that
   follows `render`, capture the subject **once**:
   `const options = construct.mock.calls[0]![0] as { getToken: () => Promise<string | null> };`
   then assert `await expect(options.getToken()).resolves.toBe('token-a');`
   (FR-002 — this makes FR-001 a transition rather than a coincidence).
4. **After** the two existing post-rerender assertions, which stay textually
   unchanged and in their existing order (FR-003), add
   `await expect(options.getToken()).resolves.toBe('token-b');`

**Capture `options` before the rerender and reuse it** — do not re-derive it from
`construct.mock.calls` afterwards, and **never** assert against the `getToken`
prop passed to `rerender`. Plan §2 §*Why the subject must be captured* gives the
reason: an assertion built from the rerendered prop checks its own input and
stays green under T006's mutation, which would make this whole slice a no-op
wearing a passing test.

One short comment on the new post-rerender line, saying *why* — the session was
not rebuilt **and** it can still reach the new token — is warranted; the two
halves pull against each other and that is not obvious. No comment on the
`token-a` line.

**Do not touch**: the other two tests, the mocks, the imports, any file under
`apps/shared/src/` or `src/` (FR-004).

**Depends on**: T002. **Blocks**: T004.

**Done when**: the edit is in the working tree and `git diff --stat` shows
exactly one file changed.

---

### `[T004]` `[US1]` Observe it green, and the suites with it

1. `cd apps/management-web && npx vitest run src/features/cameras/CameraViewerLifecycle.test.tsx`
   → 3 passed. **Quote the output.**
2. Full `apps/management-web` suite → green, **count quoted**.
3. Full `apps/shared` suite → green, **count quoted**. Baseline from #2302:
   `apps/shared` 169/169, `apps/management-web` `features/cameras` 67/67. The
   `apps/shared` count must not *drop*; a rise is fine only if something else on
   `develop` added tests, which is worth a sentence either way.

**If the new assertion is red here: STOP and report.** Green-before-and-after is
the obligation for a behaviour-preserving change (constitution §Testing). An
assertion that has to be *adjusted* to pass is evidence the premise in spec
§*What was checked* was wrong — that is a finding worth more than this slice, not
a number to edit.

**If a suite is red for an unrelated reason**: re-run once before attributing it
to this diff. A first run after machine churn reads exactly like a regression.

**Depends on**: T003. **Blocks**: T005, T006.

**Done when**: three verbatim outputs are recorded.

---

### `[T005]` `[US1]` Toolchain clean, and the diff is one file

1. `pnpm --filter ./apps/management-web lint` → clean, no new
   `eslint-disable` anywhere in the diff (NFR-003).
2. `pnpm --filter ./apps/management-web typecheck` → clean. The `as { getToken:
   () => Promise<string | null> }` cast is the file's own idiom and needs no
   `@ts-expect-error`.
3. `pnpm format:check` → clean (or `pnpm format` then re-check).
4. `git status --short` and `git diff --stat origin/develop` → **exactly one
   file outside `specs/`**, under `apps/management-web/src/features/cameras/`
   (the spec/plan/tasks docs from phases 1-3 also show in the diff-stat; that's
   expected).

Step 4 is not bookkeeping. This slice's entire claim is that it contains no
production line; a stray whitespace diff in `useWhepSession.ts` falsifies it.

**Depends on**: T004. **Blocks**: T006.

**Done when**: all four clean, with the diff-stat quoted.

---

### `[T006]` `[US1]` The counterfactual — **the gate**

Prove the assertion is a guard. Without this, a green run is indistinguishable
from a test that asserts nothing, which is the exact defect #2302 reports.

1. In `apps/shared/src/ui/composites/useWhepSession.ts`, replace the body of the
   ref-sync effect: `getTokenRef.current = getToken;` → `void getToken;`
   (the issue's counterfactual, with the unused-parameter lint noise removed).
2. `cd apps/management-web && npx vitest run src/features/cameras/CameraViewerLifecycle.test.tsx`
3. **The first test must FAIL**, with a message of the shape
   `expected 'token-a' to be 'token-b'`. **Capture the verbatim output.**
   - If it **passes**, the assertion checks its own input — go back to T003 and
     read plan §2 again. Do not proceed.
   - If a *different* test fails, or all three fail, say so; that is information
     about the suite, not a licence to continue.
4. **Revert with `git checkout -- apps/shared/src/ui/composites/useWhepSession.ts`**
   — never by retyping the line. Then `git status --short` must show that file
   clean.
5. Re-run the file → 3 passed again.

Phase 1 already ran this exact procedure and recorded, on `develop`:

```
✓ Does not renegotiate the peer connection when the getToken closure changes between renders 38ms
✓ Reconnects automatically with a fresh WhepClient after the peer connection fails 28ms
✓ Closes the WHEP session when the viewer unmounts 5ms
```
```
× PROBE A: options.getToken reaches the fresh token after a rerender 46ms
  → expected 'token-a' to be 'token-b' // Object.is equality
```

**That is context, not evidence for this PR.** It was produced against a probe
file that no longer exists. T006 re-runs it against the assertion actually being
shipped and quotes **its own** output in the PR body (FR-005).

**Depends on**: T005. **Blocks**: T007 and the PR.

**Done when**: the red output, the revert, and the re-greened run are all
recorded verbatim, and `useWhepSession.ts` is clean in `git status`.

---

## Follow-up (not part of the slice's green gate)

### `[T007]` File the two residuals — report, do not fix

Open **one** GitHub issue, labelled `tech-debt`, titled along the lines of
*"useWhepSession's teardown reads the previous token when a session change and a
token refresh land in one commit"*. It must carry:

1. **Probe B's finding and its output.** React runs all effect cleanups before
   any setup, so on a commit that both changes `whepUrl` and refreshes
   `getToken`, the outgoing session's `client.close()` →
   `releaseSession()` → `getTokenRef.current()` resolves the **previous** token.
   Measured on `develop`: `PROBE B tokenSeenAtClose = ["token-a"]`. Probe C
   confirmed the common case — teardown at **unmount**, after a token change in
   an earlier commit — resolves the **fresh** token (`["token-b"]`), so this is
   narrow.
2. **The stakes, honestly bounded.** ADR-0143 gives the release `DELETE` exactly
   one attempt, so a 401 there leaks the MediaMTX session until ICE reclaims it
   at ≈30 s. Bounded, not unbounded — and it may well be *correct* that a session
   is released with the credential that authorized it. **This issue asks for a
   decision, it does not assert a defect.**
3. **The `apps/shared` sibling gap.**
   `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx:657-682` covers
   the same rerender shape from the package that owns the hook and is likewise
   all-negative, so `apps/shared` still has no guard of its own for
   `useWhepSession`'s freshness contract. Closing it needs a different instrument
   (an `Authorization` header read after a forced reconnect), which is why spec
   222 did not.
4. Cross-references: **#2302**, **#2157** (the restructuring this belongs with),
   **#2198**, `specs/222-the-token-a-frozen-session-would-keep/spec.md`
   §*What was checked before scoping*.

**Do not apply `agent:ready`.** ADR-0144 forbids this lane from making the
architectural decision this issue asks for — effect ordering in a §IV-path hook.
It needs a human, and probably an ADR.

**Do not fix either residual in this PR.** Pinning probe B's behaviour as
characterisation would encode a possible bug as the safety net.

**Depends on**: T006 (so the issue can carry observed output, not a prediction).

**Done when**: the issue exists and its number is quoted in the PR body.

---

## Dependency graph

```
T001 ──► T002 ──► T003 ──► T004 ──► T005 ──► T006 ──► T007
 │                                             │
 premise                                     GATE ──► PR
```

Strictly linear. One agent, one file, no `[P]`.

---

## Definition of done for the whole slice

- [ ] `CameraViewerLifecycle.test.tsx` asserts `'token-a'` before and `'token-b'`
      after the rerender, against the options object captured at mount (FR-001,
      FR-002).
- [ ] The two pre-existing assertions in that test are textually unchanged
      (FR-003).
- [ ] `git diff --stat origin/develop` shows **exactly one** changed file
      (FR-004).
- [ ] Both app suites green, counts quoted, `apps/shared` not below 169 (FR-006).
- [ ] The counterfactual was run, observed red, quoted verbatim in the PR body,
      and reverted (FR-005) — **the gate**.
- [ ] Lint, typecheck and format clean; no new `eslint-disable` (NFR-003).
- [ ] The follow-up issue exists, is *not* `agent:ready`, and is referenced
      (FR-007).
- [ ] The PR body cites spec 222, closes #2302, and cites ADR-0139 (the two
      testing obligations) and ADR-0143 (why the release credential must be
      live). Latency: **N/A, no production code changed** — with the one-line
      reason from spec §*Latency budget impact*.
