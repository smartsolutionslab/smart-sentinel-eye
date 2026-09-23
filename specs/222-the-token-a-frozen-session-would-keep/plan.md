# Implementation Plan: The token a frozen session would keep

**Spec**: [spec.md](./spec.md) · **Spec number**: 222 · **Branch**:
`2302-the-token-a-frozen-session-would-keep` · **Issue**: #2302

**Phase-4a colour**: characterisation, **observed green**, with a **mandatory
counterfactual**. See spec §*Phase-4a colour*.

---

## 1. Bounded context and layers

**None — and that is the design, not an omission.**

This slice adds no domain model, no aggregate, no value object, no command, no
query, no handler, no message, no migration and no endpoint. It touches one
frontend test file. There is no bounded context to place it in, no
`Shared.Kernel` or `Shared.Contracts` addition, no Wolverine queue, no EF
configuration, no Marten store.

The conventions that *would* apply — ADR-0092's per-aggregate Domain folders,
ADR-0093's per-message-kind Application folders, ADR-0039's `Identifier`-suffixed
Guid v7 ids, ADR-0047's `Result<T, Error>`, ADR-0105's `Ensure.That` guards,
ADR-0141's `Option<T>` preference, §II's primitive ban — are listed here only so
their absence is a recorded decision rather than an oversight. **A phase-4 agent
that finds itself writing C# has misread this plan.**

The one architectural fact that *is* load-bearing:

| Concern | Where it lives | Why it matters here |
|---|---|---|
| The hook under guard | `apps/shared/src/ui/composites/useWhepSession.ts` | On the constitution §IV event-to-overlay path. **Read-only in this slice.** |
| The client it constructs | `apps/shared/src/streaming/WhepClient.ts` | Consumes `options.getToken` at `:154` (POST) and `:254` (release DELETE). **Read-only.** |
| The test being extended | `apps/management-web/src/features/cameras/CameraViewerLifecycle.test.tsx` | **The only file this slice writes.** |

**The guard lives in a different package from the code it guards, and that is
inherited, not chosen.** `useWhepSession` is `apps/shared`; the only test that
renders two different tokens is `apps/management-web`. The issue names that file
and this slice honours it. The consequence — `apps/shared` has no guard of its
own for its own hook — is recorded in spec §*Scope* and filed by T007. Closing it
properly needs a different instrument in
`apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx`, which drives a
real `FakePeerConnection` and a `fetch` mock rather than a constructor spy.

---

## 2. The mechanism being pinned

Three lines of production code, and the assertion has to reach all three.

```
useWhepSession.ts:134   const getTokenRef = useRef(getToken);          // seeds
useWhepSession.ts:135-137 useEffect(() => { getTokenRef.current = getToken; });  // refreshes
useWhepSession.ts:338   getToken: () => getTokenRef.current(),         // indirects
```

`:134` alone passes the existing suite. `:338` alone passes the existing suite.
**`:135-137` is the line nothing reads**, and it is the only one of the three
whose deletion still compiles, still type-checks, still lints, and still ships
green.

The instrument is already in the file. Its `WhepClient` mock
(`CameraViewerLifecycle.test.tsx:10-18`) forwards the constructor argument to a
`vi.fn()`, so `construct.mock.calls[0]![0]` **is** the options object — including
the `() => getTokenRef.current()` indirection at `:338`. Invoking that captured
function after a rerender reads the ref through exactly the path production uses.

### Why the subject must be captured before the rerender

`construct` is called once and never again, so `construct.mock.calls[0]![0]`
returns the same object whenever it is read. Capturing it into a `const` *before*
the rerender therefore changes nothing mechanically — but it makes the test say
what it means: **one fixed subject, two different answers.** Re-deriving it from
the mock after the rerender reads as though the rerender produced it, which is
the opposite of the contract (FR-003: nothing was reconstructed).

The failure mode this avoids is the one this repository has filed twice — an
assertion that cannot fail because it checks its own input. Asserting against the
*prop* passed to `rerender` would be exactly that. Asserting against the captured
options object cannot be: the subject is frozen at mount, so the only thing that
can make the value change is the ref sync.

---

## 3. Shape of the change

One test, from this:

```tsx
it('Does not renegotiate the peer connection when the getToken closure changes between renders', () => {
```

to this (sketch — the implementer writes the final text):

```tsx
it('Does not renegotiate the peer connection when the getToken closure changes between renders, and the session can still reach the fresh token', async () => {
  const { rerender } = render(/* … token-a … */);
  expect(construct).toHaveBeenCalledTimes(1);
  const options = construct.mock.calls[0]![0] as { getToken: () => Promise<string | null> };
  await expect(options.getToken()).resolves.toBe('token-a');

  rerender(/* … token-b … */);

  expect(construct).toHaveBeenCalledTimes(1);
  expect(close).not.toHaveBeenCalled();
  // FR-001: the session was NOT rebuilt — and it can still reach the new token.
  await expect(options.getToken()).resolves.toBe('token-b');
});
```

Four things about that shape are deliberate:

1. **`async` + `await expect(...).resolves`.** A promise the production code
   itself returns. No settle, no yield count, no `waitUntil` — NFR-002, ADR-0150.
2. **The `token-a` assertion comes first.** Without it, FR-001 could pass on a
   hook that always returns the *latest* prop by accident of some other
   mechanism; with it, the test states a transition.
3. **The two existing assertions stay, textually unchanged, in their existing
   order** (FR-003). They are the stability half. The freshness line goes *after*
   them, so a reader meets the contract in the order the comment states it.
4. **The title gains a clause.** The old title claims only the negative; leaving
   it would leave the file describing a test that no longer matches it. Renaming
   a test is not editing an assertion — FR-003 is about the `expect` calls.

**The `as { getToken: ... }` cast** mirrors the file's own existing idiom at
`:73-75`, where the second test casts `construct.mock.calls[0]![0]` to read
`onConnectionStateChange`. Reuse it; do not introduce a shared type alias for
one more call site (NFR-001).

---

## 4. Messaging, boundaries, persistence

- **Domain → integration event**: none. No domain event, no `Shared.Contracts`
  message, no `V<N>` contract, no Wolverine handler, no outbox row.
- **Cross-context references**: none created. `NetArchTest`'s boundary rules are
  unaffected — no `.csproj` is touched, so `tests/Architecture.Tests` has nothing
  new to inspect.
- **Persistence**: none. No migration, no `DbContext`, no Marten store.
- **Idempotency / retry safety** (ADR-0142, ADR-0143): none added. The slice
  *references* ADR-0143 in the spec's motivation — it explains why the release
  `DELETE`'s single attempt must carry a live credential — but introduces no
  `Idempotency-Key`, no `RetryEveryMethod()`, and no change to any HTTP client.
- **Frontend boundaries**: `apps/management-web` already depends on
  `@smart-sentinel-eye/shared`; the test already imports `CameraViewer` from it.
  No new import edge.

---

## 5. Verification strategy

Ordered, and each step is a distinct claim:

| # | Claim | How |
|---|---|---|
| 1 | The premise still holds | Read `useWhepSession.ts:134-137` and `:338` on the branch tip; confirm the three lines are as quoted in §2. Recorded at `da7e398f`. |
| 2 | The assertion passes on shipped code | `npx vitest run src/features/cameras/CameraViewerLifecycle.test.tsx` in `apps/management-web` → 3/3. |
| 3 | The assertion is a guard | Mutate `:136` to `void getToken;`, re-run → the first test **fails**, quoting `expected 'token-a' to be 'token-b'`. Revert; re-run → green. |
| 4 | Nothing else moved | Full `apps/management-web` suite and full `apps/shared` suite green, counts quoted. |
| 5 | The toolchain is clean | `pnpm --filter ./apps/management-web lint typecheck` and `pnpm format:check`. |

**Step 3 is the gate.** Steps 2 and 4 alone would satisfy a literal reading of
"characterisation, observed green" while proving nothing — which is the defect
#2302 reports, reproduced one level up. Do not report step 2 as the evidence.

**Step 3 hazard — restore by `git checkout --`, never by retyping.** A hand-typed
revert that differs by whitespace is a production-file diff in a slice whose
whole claim is that it has none. Verify with `git status --short` showing the
file clean before committing, and `git diff --stat` on the final branch showing
exactly one file.

---

## 6. Constitution and ADR alignment

| Rule | Status |
|---|---|
| §Testing — behaviour-preserving ⇒ characterisation, green before and after (ADR-0139) | **Binds.** Spec §*Phase-4a colour*; the new assertion must pass unmodified on shipped code. |
| §Testing — *Waiting* is a condition, not a count (ADR-0150) | **Satisfied by construction.** The only wait is a promise production code returns. |
| §IV — latency budget | **N/A**, and FR-003 protects the legs the ref exists to protect. Spec §*Latency budget impact*. |
| §II — no primitives on a domain model (ADR-0140) | **Not engaged.** No domain model; `'token-a'` is a TypeScript string literal in a test. |
| ADR-0105 — `Ensure.That` guards | **Not engaged.** No C#. |
| ADR-0141 — `Option<T>` in Domain/Application | **Not engaged.** No C#. |
| ADR-0109 — `[P]` for disjoint files | **Engaged, and the answer is "none".** See `tasks.md` §*Parallelism*. |
| ADR-0144 — the lane may not write an ADR or make a design decision | **Engaged by T007**, which files an issue and stops. Probe B's fix is a decision, not an implementation. |
| ADR-0144 — the lane may not weaken a gate to reach green | **Engaged by FR-003/FR-004.** Deleting or relaxing an existing assertion, or editing `useWhepSession.ts` to make a test pass, is a blocked outcome. |
| Karpathy §smallest possible change (ADR-0036) | **~8 lines, one file.** |

**No ADR is needed for this work.** The decision it rests on — that a
behaviour-preserving change is characterised and that the characterisation is
proven by counterfactual — is already ADR-0139 plus this repository's recorded
practice. If phase 4 or 6 concludes probe B *is* a defect requiring a fix, that
is a new decision and therefore **blocked in this lane**: file it (T007) and stop.

---

## 7. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| The implementer "improves" `useWhepSession.ts` while in the file | Medium — the ref-sync effect's missing dependency array looks like a bug to anyone who has not read its comment | FR-004 and §1's explicit "a phase-4 agent writing C# has misread this plan"; T005 diff-stat check |
| The counterfactual revert leaves whitespace drift | Medium | §5 hazard note: `git checkout --`, then `git status --short` |
| The freshness assertion is written against the rerendered prop rather than the captured options | Medium — it is the natural thing to type and it always passes | §2 §*Why the subject must be captured*; T006 catches it (a prop-based assertion stays green under the mutation) |
| Probe B gets "fixed" in passing | Low | Spec §*What was checked*; T007 files, does not fix |
| The full suite is flaky for unrelated reasons | Low | Compare against the #2302 baseline (169/169 and 67/67) before attributing anything to this diff; re-run once — a first run after machine churn reads exactly like a regression |
