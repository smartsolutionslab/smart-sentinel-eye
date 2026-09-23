# Feature Specification: The token a frozen session would keep

**Feature Branch**: `2302-the-token-a-frozen-session-would-keep`

**Spec**: 222

**Issue**: [#2302](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2302) — related to [#2198](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2198) (closed) and [#2157](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2157) (open)

**Created**: 2026-09-23

**Status**: Draft

**Input**: "The `getTokenRef` freshness contract is untested — a session frozen on a dead token ships green."

---

## Why this exists

`useWhepSession` holds the caller's `getToken` behind a ref
(`apps/shared/src/ui/composites/useWhepSession.ts:134-137`) and hands `WhepClient`
an indirection through it (`:338`, `getToken: () => getTokenRef.current()`).
The ref exists for a latency reason stated in its own comment: callers pass
`getToken` as a fresh inline closure on every render — ADR-0080's
`react-oidc-context` shape, `() => Promise.resolve(auth.user?.access_token)` —
and without the ref, the session effect's dependency array would tear down and
renegotiate the `RTCPeerConnection` on every parent render.

So the ref carries **two** obligations, and they pull against each other:

1. **Stability** — a new `getToken` identity must *not* rebuild the session.
2. **Freshness** — the client must nevertheless be able to reach the *latest*
   `getToken`, because the token behind it expires and is refreshed.

**Only the first is tested.** `CameraViewerLifecycle.test.tsx:46-63` renders with
`token-a`, rerenders with `token-b`, and asserts `construct` was called once and
`close` was not. Both are assertions of *absence*. Nothing anywhere asserts that
the constructed client can now reach `token-b`.

### The proof, re-run on this branch

`apps/shared/src/ui/composites/useWhepSession.ts:135-137` was mutated in a
throwaway working tree — `getTokenRef.current = getToken;` replaced by
`void getToken;`, which is the issue's counterfactual with the lint noise
removed — and `CameraViewerLifecycle.test.tsx` was re-run:

```
✓ Does not renegotiate the peer connection when the getToken closure changes between renders 38ms
✓ Reconnects automatically with a fresh WhepClient after the peer connection fails 28ms
✓ Closes the WHEP session when the viewer unmounts 5ms
```

**Three of three green with the freshness contract deleted.** A probe carrying
the assertion this spec adds was red in the same run:

```
× PROBE A: options.getToken reaches the fresh token after a rerender 46ms
  → expected 'token-a' to be 'token-b' // Object.is equality
```

The mutation was reverted and the probe deleted; `develop` is untouched. The
figures above are the evidence the phase-4 gate needs and are not to be taken on
trust — **T006 re-runs the counterfactual and quotes its own output.**

### Why it matters beyond the ref

The token the client reaches is the credential on two wire calls: the WHEP
`POST` offer (`WhepClient.ts:154`) and the session-release `DELETE`
(`WhepClient.ts:254-255`, the path #2198 built). `releaseSession`'s own comment
states the stake plainly:

> the dominant failure is a 401 from an expired token — `getToken()` is
> re-resolved above, at release time, so a retry would only re-present the same
> dead credential (ADR-0143).

That argument is only sound if "re-resolved at release time" reaches a *live*
closure. Frozen on the mount-time closure it resolves a credential that is by
then months of session-lifetime stale, the `DELETE` 401s, and the MediaMTX
session leaks until ICE reclaims it at ≈30 s. **ADR-0143 forbids retrying that
`DELETE`**, so there is no second chance — which is exactly why the freshness
contract deserves a test rather than a comment.

**Nothing is wrong today.** The current code is correct; this spec proves it.

---

## What was checked before scoping — and one thing it found

The issue's body prescribes the fix, so the phase-1 work was to establish whether
this is purely a coverage gap or whether the honest assertion exposes a defect.
Three probes were run against unmutated `develop`:

| Probe | Question | Result |
|---|---|---|
| **A** | After a rerender with a fresh closure, does `construct.mock.calls[0][0].getToken()` resolve to `token-b`? | **Yes.** Green. |
| **B** | When `whepUrl` **and** the token change in the *same* commit, which token does the outgoing session's teardown resolve? | **`token-a`** — the previous one. |
| **C** | At **unmount**, after a token change in an earlier commit, which token does teardown resolve? | **`token-b`** — the fresh one. Green. |

**A and C say the contract holds.** This is a coverage gap, not a defect: the
behaviour is right and nothing proves it.

**B is a real, narrow residual, and it is deliberately not part of this slice.**
React runs *all* effect cleanups before *any* setup, so on a commit that both
tears a session down and refreshes the token, the session effect's cleanup —
`client.close()` → `releaseSession()` → `getTokenRef.current()` — runs *before*
the ref-sync effect has taken the new closure. The outgoing `DELETE` therefore
carries the credential that authorized the session it is releasing. That is
arguably correct, and it is bounded by MediaMTX's ≈30 s ICE reclaim either way.

It is out of scope for three reasons, each sufficient on its own:

- The issue does not claim it and the counterfactual does not exercise it.
- Pinning it as characterisation would **encode a possible bug as the safety
  net** — the shape CLAUDE.md's phase-4a rule names explicitly.
- Deciding it is an architecture question about effect ordering in a §IV-path
  hook, which belongs with #2157's restructuring, not with a one-line assertion.

**Action**: the `[T007]` task below files it as its own issue with the probe's
output, cross-referenced to #2157. It is a report, not a fix — filed as **#2544**.

---

## Scope

**In scope.** One assertion added to one existing test in
`apps/management-web/src/features/cameras/CameraViewerLifecycle.test.tsx`, plus a
counterfactual run proving the assertion is a guard.

**Out of scope, explicitly.**

- **No `src/` or `apps/*/src` production change of any kind.** No file under
  `apps/shared/src/` is edited. If a task touches `useWhepSession.ts`,
  `WhepClient.ts` or `CameraViewer.tsx`, the design has been misread.
- **No change to the three existing assertions.** `construct` called once,
  `close` not called, and the other two tests' bodies stay **byte-identical**.
  They are the stability half of the contract; the freshness assertion is added
  *beside* them, never in place of them.
- **The same-commit teardown residual (probe B) is reported, not fixed, and not
  pinned.** See above.
- **No new test file, no new mock, no new helper.** The instrument already
  exists: the file's `construct` spy captures the options object. A second file
  would duplicate the `WhepClient` / `streams.api` mock pair for one assertion.
- **`apps/shared`'s own sibling gap is noted, not closed.**
  `CameraViewerCameraSwap.test.tsx:657-682` covers the same rerender shape from
  the package that owns the hook, and is likewise all-negative. Closing it there
  needs a different instrument — that file drives a real `FakePeerConnection` and
  a `fetch` mock, so freshness would have to be read off an `Authorization`
  header after forcing a reconnect. That is a second slice with a real design
  question in it, not a line. Recorded in `[T007]` alongside probe B.
- **No latency work.** See §*Latency budget impact*.

---

## User Scenarios & Testing

### User Story 1 — A frozen token fails the build (Priority: P1)

An engineer simplifies `useWhepSession` — removes the ref because "the effect has
no deps, that looks wrong", or converts the indirection at `:338` to pass
`getToken` straight through, or moves the sync into the session effect where it
would only run on a real session change. Today all three ship green through a
suite that renders two different tokens and looks like it covers exactly this.
After this story, each of them fails the build with a message naming the two
tokens.

**Why P1**: it is the whole issue and the whole shippable slice — one assertion,
green on `develop`, red the moment the freshness contract is broken.

**Independent Test**: run
`npx vitest run src/features/cameras/CameraViewerLifecycle.test.tsx` in
`apps/management-web` on a clean `develop` — green. Replace
`getTokenRef.current = getToken;` (`useWhepSession.ts:136`) with `void getToken;`
— red, quoting `expected 'token-a' to be 'token-b'`. Revert. No Docker, no
Aspire, no network; the file runs in under five seconds.

**Acceptance Scenarios**:

1. **Happy path.** **Given** `CameraViewer` mounted with
   `getToken={() => Promise.resolve('token-a')}` and the `WhepClient` constructor
   spied, **When** the tree is rerendered with
   `getToken={() => Promise.resolve('token-b')}` and the constructed client's
   captured `options.getToken()` is awaited, **Then** it resolves to `'token-b'`
   — and `construct` has still been called exactly once and `close` not at all.

2. **Conflict — stability is not traded for freshness.** **Given** the same
   rerender, **When** the new assertion is added, **Then** the two pre-existing
   assertions in the same test (`construct` once, `close` never) still pass
   unmodified. A freshness assertion bought by letting the session renegotiate is
   a **regression on the §IV path**, not a pass.

3. **Bad request — the guard is proven, not assumed.** **Given** the assertion in
   place, **When** `useWhepSession.ts:136` is mutated to drop the ref
   assignment, **Then** this test fails, and the verbatim failure is quoted in
   the PR body. A characterisation test never observed red against its own
   counterfactual proves the code compiles, not that the code is right.

4. **Auth — the credential is the point, and it is a fake.** **Given** the test
   uses string literals `'token-a'` / `'token-b'` against a mocked `WhepClient`,
   **When** it runs, **Then** no real Keycloak token, no OIDC flow and no network
   call is involved (ADR-0080's `react-oidc-context` is not exercised). The test
   asserts the hook's *plumbing* of a credential, never a credential's validity.
   Nothing about the `sse.*` scope catalogue or fab authorization is in play.

5. **Edge — the assertion must not check its own input.** **Given**
   `options.getToken` is captured from `construct.mock.calls[0]![0]` **before**
   the rerender, **When** it is invoked **after** the rerender, **Then** the
   subject (the captured indirection) is fixed while the expected value changes.
   Re-reading `construct.mock.calls[0]![0]` after the rerender would return the
   same object and pass either way — but capturing the *prop* instead of the
   *captured options* would assert the test's own literal and could never fail.

---

### Edge Cases

- **The ref-sync effect has no dependency array.** It re-runs on *every* commit,
  including commits where `getToken` is unchanged. That is intentional and cheap
  (one assignment) and is what makes the freshness half work at all. The test
  exercises it via a genuine identity change; it does not assert the
  no-dependency-array shape, which is an implementation detail.
- **Effect ordering on mount.** The ref-sync effect is declared *above* the
  session effect (`:135` vs `:191`), so on any commit that constructs a client
  the ref is already current. This does not need the effect to have run at
  all: `useRef(getToken)` at `:134` seeds the ref with the mount-time closure
  via its initializer, independent of the effect — phase 1's own counterfactual
  proved this, since deleting the effect's body left exactly one assertion red
  (`expected 'token-a' to be 'token-b'`), not two. The `'token-a'` assertion is
  a baseline that makes FR-002's post-rerender assertion a transition rather
  than a coincidence; it does not guard declaration order.
- **Cleanup ordering** — probe B, reported not pinned. See §*What was checked*.
- **A synchronous throw from `getToken`.** `releaseSession` calls
  `this.opts.getToken()` outside any `try`, so a caller whose `getToken` throws
  rather than rejecting would propagate out of a React cleanup. Pre-existing,
  unrelated to this ref, and not touched here.
- **Awaiting inside the test.** The assertion is `await expect(...).resolves`, so
  the test becomes `async`. It waits on a **promise**, not on a count of event
  loop yields — ADR-0150 §2 is satisfied by construction; no `setTimeout(0)`
  loop, no settle, no `waitUntil` is needed or permitted here.

---

## Requirements

### Functional Requirements

- **FR-001**: The test at
  `apps/management-web/src/features/cameras/CameraViewerLifecycle.test.tsx:46`
  MUST, after the rerender, assert that the **already-constructed** client's
  captured `options.getToken()` resolves to `'token-b'`.
- **FR-002**: The same test MUST also assert, **before** the rerender, that the
  captured `options.getToken()` resolves to `'token-a'` — so that FR-001 is
  proven to be a *change* rather than a coincidence of the first value.
- **FR-003**: The two pre-existing assertions in that test
  (`expect(construct).toHaveBeenCalledTimes(1)` after the rerender, and
  `expect(close).not.toHaveBeenCalled()`) MUST remain, unmodified.
- **FR-004**: No file outside that one test file may be modified. In particular
  no file under `apps/shared/src/` and no file under `src/`.
- **FR-005**: The counterfactual mutation MUST be applied, the test MUST be
  observed red, the mutation MUST be reverted, and the verbatim red output MUST
  appear in the PR body.
- **FR-006**: The full `apps/management-web` suite and the full `apps/shared`
  suite MUST be green after the change, with counts reported.
- **FR-007**: The residual from probe B, and the `apps/shared` sibling gap, MUST
  be filed as a GitHub issue carrying the probe output, cross-referenced to
  #2157 and to this spec. The lane may file an issue; it may not write the ADR
  or make the design decision that issue asks for.

### Non-Functional Requirements

- **NFR-001**: No new dependency, no new test file, no new helper, no new mock.
- **NFR-002**: The added assertion MUST NOT introduce a fixed-count settle, a
  bare `setTimeout` drive, or any wait that is not a promise the production code
  itself returns (constitution §Testing *Waiting*, ADR-0150).
- **NFR-003**: `pnpm lint`, `pnpm format:check` and `pnpm typecheck` for
  `apps/management-web` stay clean. No `eslint-disable` is introduced.

---

## Success Criteria

- **SC-001**: With `getTokenRef.current = getToken;` deleted from
  `useWhepSession.ts`, `CameraViewerLifecycle.test.tsx` fails. Today it passes
  3/3. *(Measured by T006.)*
- **SC-002**: With the file as shipped, `CameraViewerLifecycle.test.tsx` passes
  3/3 and both app suites are green. Baseline to beat: `apps/shared` 169/169,
  `apps/management-web` `features/cameras` 67/67, as recorded in #2302.
- **SC-003**: The diff touches exactly one file and adds no more than ~8 lines.
- **SC-004**: One follow-up issue exists carrying probe B's output and the
  `apps/shared` sibling gap.

---

## Latency budget impact

**N/A — no production code changes.** No leg of constitution §IV is affected;
the event-to-overlay path executes byte-identical code before and after.

Recorded anyway because the file under guard is *on* that path, and because the
protection runs the other way: `useWhepSession`'s ref is the reason a parent
re-render does not renegotiate the `RTCPeerConnection`, and a renegotiation per
render would destroy **Camera → SFU (≤ 80 ms)** and **SFU → kiosk decode
(≤ 120 ms)** on every keystroke in the operator console. FR-003 exists precisely
so the freshness assertion cannot be bought at that price — scenario 2 above
makes trading stability for freshness a failure, not a pass.

---

## Phase-4a colour

**Characterisation, observed GREEN — with a mandatory counterfactual.**

This is behaviour-preserving: nothing under `apps/shared/src/` or `src/` changes,
so constitution §Testing's second obligation binds. The new assertion is written
against code that already behaves correctly, and it must pass **unmodified** on
the shipped file. An assertion that has to be adjusted to pass is evidence the
premise in §*What was checked* was wrong — **block and report, do not adjust**.

The counterfactual is not optional garnish, and it is what makes a green
observation mean anything. This repository has shipped five assertions in one
week that could not fail, and has filed the general lesson twice: *prove a guard
by counterfactual*, and *an assertion must not check its own input*. A
characterisation test observed only green is indistinguishable from a test that
asserts nothing — which is the exact defect #2302 reports. T006 is therefore a
gate, not a nicety.

**Ambiguity resolves to red** per CLAUDE.md. There is no ambiguity here: the
diff contains no production line.

---

## Assumptions

- `construct.mock.calls[0]![0]` is the options object `useWhepSession` passes to
  `new WhepClient({...})` — verified by reading `useWhepSession.ts:336-340` and
  the file's own mock at `CameraViewerLifecycle.test.tsx:10-18`, and exercised by
  probe A.
- RTL's `rerender` is `act`-wrapped, so passive effects — including the ref sync
  — have flushed before the assertion runs. Verified empirically by probe A, not
  assumed from the React docs.
- The `'token-a'` / `'token-b'` literals already in the file are kept; no new
  fixture vocabulary is introduced.
- `apps/management-web`'s vitest can run a single file without a workspace
  install beyond what `develop` already has. Verified: the probe ran in 4.19 s.

---

## References

- **ADR-0037** — the seven phases and their gates; this document is phase 1.
- **ADR-0144** — the autonomous lane; #2302 carries `agent:ready`.
- **ADR-0139** (as amended by **ADR-0140**) — rules that fail the build, not the
  review; the source of the two testing obligations and of the quoted-failure
  requirement.
- **ADR-0080** — browser auth; the origin of the inline `getToken` closure whose
  per-render identity change is the reason the ref exists at all.
- **ADR-0143** — `POST`/`PATCH` are not retried. The reason the session-release
  `DELETE` gets exactly one attempt, and therefore the reason the credential it
  carries must be live.
- **ADR-0150** — waiting is a condition, not a count; NFR-002.
- **ADR-0109** — `[P]` marks disjoint file ownership. See `tasks.md`
  §*Parallelism*.
- **Constitution §IV** — the latency budget; §*Latency budget impact* above.
- **Constitution §Testing** — the two obligations; §*Phase-4a colour* above.
