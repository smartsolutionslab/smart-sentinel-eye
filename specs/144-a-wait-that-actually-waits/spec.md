# Spec 144 — A wait that actually waits, and the constant that was quietly doing its job

**Issue:** #2201 — *A readiness wait that reads as satisfied exactly when the thing it
waits for is absent*
**Branch:** `fix/2201-readiness-wait-that-lies`
**ADRs:** 0037 (phases and gates), 0144 (the autonomous lane; the two colours of phase
4a), 0036 (smallest change; no speculative generality), 0103 (integration tests run
against the Aspire fixture, no Testcontainers), 0052 (xUnit + Shouldly + hand-written
fakes), 0053 (sentence-style test naming), 0054 (hand-written doubles, no AutoFixture),
0049 (`CancellationToken` mandatory last parameter), 0109 (`[P]` disjoint-file
parallelism), 0028 (GitFlow), 0086 (no `Co-Authored-By`), 0117 (§VII binds implemented
legs)

**Spec number 144.** Derived from refs, not the working tree, per spec 137's method:
`git log --all --name-only --pretty=format: -- specs/ | grep -oE 'specs/[0-9]{3}'` tops
out at **143**; `git ls-tree --name-only origin/develop specs/` confirms
`143-a-registry-to-be-unknown-of` as the tip; no remote branch is named `^[0-9]{3}-`.
143 + 1 = **144**.

---

## Phase 1: every claim in the issue was checked. The defect holds exactly as described.
## The self-correction holds. The duplication count does not.

### The defect — confirmed, line numbers drifted

The issue cites `NFR_VariableResolutionLatencyTests.cs:144-176`. The file has grown; the
substance is unchanged.

| Claim | Issue's line | Actual line | Verdict |
|---|---|---|---|
| `WaitUntilResolvableAsync` | `:144-176` | `:159-177` | **Holds** |
| `ResolvedTextAsync` maps non-200 → `string.Empty` | `:168-171` | `:179-191` (the map at `:183-186`) | **Holds** |
| `WarmupRounds = 3` | `:61` | `:75` | **Holds** |
| The measured loop | `:92-101` | `:111-115` | **Holds** |
| `MeasureOneChangeAsync`'s own poll | `:132-138` | `:147-153` | **Holds** |

The mechanism is exactly as filed. `ResolvedTextAsync` answers `string.Empty` for a non-200
(`:183-186`). The wait's condition is `!resolved.Contains($"{{{{{variableName}}}}}")`
(`:169`). `string.Empty.Contains(anything-non-empty)` is `false`, so the negation is
`true` on the **first iteration**, while the endpoint is still answering **404
`OVERLAY_NOT_IN_REVERSE_INDEX`**. The wait returns without waiting.

**A second defect the issue names only in passing, and it is created by the fix.** The
loop has **no `Task.Delay`**. Today that is invisible because the loop never runs twice.
The moment the wait is corrected it becomes a real loop, and an uncorrected one would hot-
spin a core against the API for up to 30 s. The corrected shape must bring a poll interval
with it — which the pattern the issue points at already has.

### The §IV self-correction — checked independently, and it holds

The issue withdraws its own original framing. That withdrawal is **correct**, and this
spec confirms it by reading the code rather than by trusting the note.

1. **`WarmupRounds = 3` runs before the measured loop.** `:106-109` runs three
   `MeasureOneChangeAsync` calls whose return values are discarded; `:111-115` then
   collects five into `measured`. Confirmed.
2. **`MeasureOneChangeAsync` cannot be satisfied by an empty string.** Its condition is
   `(await ResolvedTextAsync(...)).Contains(expected)` where `expected` is `"1000"`,
   `"1001"`, … — always non-empty. `string.Empty.Contains("1000")` is `false`, so a 404
   keeps the poll running, up to its own 10 s ceiling. Confirmed.

So warmup round 0 does the waiting the readiness check did not: it writes a value, then
polls until the snapshot answers 200 **and** carries that value — which cannot happen
until the reverse index has picked the overlay up. Its figure is discarded. The five
measured samples begin against a warm index.

**Conclusion: the published figure is neither compromised nor noisier, and this spec
asserts nothing to the contrary.** Spec 106 reached the same conclusion independently
(`specs/106-the-automation-leg-is-measured/spec.md:389-405`) before filing #2201, which is
corroboration rather than a second opinion from the same reading.

**One correction of scope, on which the issue is slightly loose.** §IV does not carry this
test's number in any cell. The `Event → overlay state` row reads *implemented: yes,
measured: **recorded, not yet readable*** — a statement about the production metric that
nothing outside the emitting process can read (#1707), not about this test's console
figure. So "§IV should be told" can only mean *file a finding*, never *edit the table*.
This spec forbids the latter outright (see **Out of scope**).

### The duplication — the issue's count is wrong in both directions, and the shape matters

The issue says `PublishOverlayReferencingAsync`, `UniqueVariableName` and
`ResolvedTextAsync` "now exist in **three** copies". Counted line by line across
`tests/Integration.Tests/`:

| Shape | NFR_Variable… | TwoPlaceholders… | ResolvedTextReachesItsFab… | Named copies |
|---|---|---|---|---|
| `WaitUntilResolvableAsync` | `:159` **broken** | `:214` **correct** | `:580` **correct on the 200, hot-spins** | **3** |
| `ResolvedTextAsync` | `:179` returns `string` | `:243` returns `string?` | *inlined* at `:585-592` | **2** + 1 inline |
| `PublishOverlayReferencingAsync` | `:193` (one name) | `:176` (two names) | *inlined* at `:523-541` | **2** + 1 inline, **different arities** |
| `UniqueVariableName` | `:216` | `:264` | *inlined literal* at `:515` | **2** + 1 inline |

Three findings the issue does not state, each of which changes the work:

- **There is a third `WaitUntilResolvableAsync`**, in `ResolvedTextReachesItsFabTests`, and
  it is **not** broken — it guards on `IsSuccessStatusCode` before testing the literal. It
  does, however, hot-spin (no delay) and its timeout message cannot distinguish *"never a
  200"* from *"a 200 that stayed stale"*. So the fold-in has three call sites, not two, and
  one of them gains a behaviour the others already have.
- **The other three helpers are duplicated twice, not three times** — the third instance is
  inlined, not a named method — and `PublishOverlayReferencingAsync`'s two copies have
  **different signatures** (one placeholder vs two). They are not interchangeable today.
- **A fourth file already documents this defect in prose.**
  `tests/Integration.Tests/Automation/AcceptToDecideLatencyTests.cs:414-421` says NFR's
  version "maps a non-200 to an empty string, so it returns on its first iteration against
  a 404 … (#2201)". **That sentence becomes false the moment this fix lands**, and nothing
  points at it from here. Leaving it is how a correct-looking comment outlives the code it
  describes — this repository's own recurring defect. It is in scope.

---

## The one thing that is actually broken

**A safety mechanism contributing no safety, whose job is being done by an unrelated
constant that does not know it is doing it.**

The readiness wait cannot fail. The three warmups are load-bearing for a reason nothing
records. Set `WarmupRounds = 0` — a plausible trim for someone shortening a slow test —
and the first measured sample silently becomes a reverse-index catch-up measurement. It
would not go red. It would print a number, against a budget, in a test whose entire
purpose is to produce a defensible figure, and the number would be wrong in the direction
that matters.

---

## User stories

### US-1 (P1) — The readiness wait holds until the overlay is genuinely resolvable

*The independently-shippable slice.* Everything else in this spec is either evidence for
this story or cleanup enabled by it.

`WaitUntilResolvableAsync` returns only when the snapshot has answered **200** *and* the
placeholder literal is gone; it delays between polls; and its timeout message says which
of the two it was still missing. The shape is copied from
`TwoPlaceholdersInOneLabelTests.WaitUntilResolvableAsync:214-237`, not reinvented.

### US-2 (P2) — The warmups' job is written down, with evidence

The decision recorded in the code, with its reason, so the next person trimming the test
meets it. Phase 1's answer, which the engineer must confirm or overturn by observation:
**keep them.** The readiness wait exercises only `GET /system-variables/snapshot`. The
measured path is a different one — `GET` version, `PUT .../value`, the domain event, the
outbox, the resolve — and warmup round 0 is still its first execution, carrying first-call
JIT, Wolverine handler resolution and EF plan compilation. The readiness wait warms the
read path; the warmups warm the write-and-propagate path. They are not redundant, and
after this fix that is a statement with a reason rather than an accident.

### US-3 (P2) — The figure is re-read and compared, and the comparison is reported

A `develop` figure and a post-fix figure, both from CI trx, written into this spec folder
as spec 136 established (run id **and** figure in the tree, because artifact retention is
14 days). If the median has not moved materially — the expected outcome — that one line is
the evidence the warmups really were covering. If it has moved, that is a **finding to
report**, never a number to change here.

### US-4 (P3) — One copy of the overlay-snapshot readiness helpers

Folded into `tests/Integration.Tests/Fixtures/`, **after** US-1, exactly as the issue
requires: doing it first would move the code under test out of the file that carries the
defect. Three call sites, one implementation, and `AcceptToDecideLatencyTests`' prose
brought back in line with the code.

### US-5 (P4) — *Not delivered.* Fold `PublishOverlayReferencingAsync`.

Two named copies with **different arities** plus an inlined third whose overlay also needs
a fab layout. Unifying them means inventing a parameterised builder for three call sites
that genuinely differ — ADR-0036's "no speculative generality", and spec 137's row 8
precedent for leaving a diverged shape alone. T009 evaluates whether it collapses to a
single `labelText` + name-prefix signature; if it needs one knob per caller, it stays
where it is and the PR says so. **Not a gate on this spec.**

---

## Acceptance scenarios

### AC-1 — happy path: the wait blocks until the snapshot is a 200 *(US-1, the red test)*

```gherkin
Given a readiness wait driven by a stub that answers 404 for its first two requests
  And answers 200 carrying the label with the placeholder resolved on its third
 When the wait is invoked
 Then it does not return before the third request has been issued
  And it returns once that 200 has been read
```

**This is red against `origin/develop`**: today's wait returns after request one, having
read a 404, so the observed request count is 1 and not 3.

### AC-2 — conflict: a 200 that is still stale does not satisfy the wait *(US-1)*

```gherkin
Given a stub that answers 200 but whose resolvedText still carries "{{name}}"
  And then answers 200 with the placeholder resolved
 When the wait is invoked
 Then it does not return on the stale 200
  And it returns on the resolved one
```

This half is green today by accident — the existing condition does test the literal — and
is asserted so that the fix is observably a **narrowing**, not a replacement that dropped
the literal check.

### AC-3 — bad request: neither condition ever arrives *(US-1)*

```gherkin
Given a stub that answers 404 forever
 When the wait is invoked with a short ceiling
 Then it throws a TimeoutException
  And the message names the overlay and the variable
  And the message says the last snapshot was "not a 200"
```

```gherkin
Given a stub that answers 200 carrying "{{name}}" forever
 When the wait is invoked with a short ceiling
 Then it throws a TimeoutException
  And the message quotes the last text seen rather than saying "not a 200"
```

The two diagnoses must differ. A timeout that cannot say which half failed is how an
unbooted index and a snapshot loop that stopped early came to look identical from here.

### AC-4 — the poll is not a spin *(US-1)*

```gherkin
Given a stub that answers 404 forever
 When the wait runs against a ceiling of 1 second
 Then it issues far fewer requests than an undelayed loop would
```

Asserted as an upper bound on the request count, not as a timing measurement — a wall-clock
assertion on shared CI is the kind that flakes and then gets deleted.

### AC-5 — auth: unchanged, and that is the assertion *(US-1)*

```gherkin
Given the snapshot endpoint requires an authenticated admin client
 When the NFR test runs end to end against the Aspire fixture
 Then it still obtains its client through aspire.CreateAdminClientAsync
  And no scope, role or client registration changes
```

This change touches no production code and no auth surface. The scenario exists so that a
reviewer can confirm that, not because anything moves.

### AC-6 — characterisation: the three integration tests pass **unmodified** *(US-4)*

```gherkin
Given NFR_VariableResolutionLatencyTests, TwoPlaceholdersInOneLabelTests
  And ResolvedTextReachesItsFabTests, captured green before the fold-in
 When the readiness helpers are folded into one fixture helper
 Then all three pass with no assertion edited
```

An assertion that has to be adjusted is evidence behaviour moved. Block; do not adjust.

---

## Independent end-to-end test procedure

A reviewer can confirm the whole of this without reading the diff.

1. **See the defect, before the fix.** On `origin/develop`, add a temporary counting
   handler to the NFR wait or simply reason from `:183-186` + `:169`. The mechanical
   version: check out `develop`, apply only T002's new test file, run
   `dotnet test tests/Integration.Tests/… --filter "Category=FixtureLogic"`, and observe
   AC-1 fail with an observed request count of **1**. No Docker, no stack.
2. **See it fixed.** On the branch, the same filter runs green.
3. **See the figure.** Run the integration job (or read the PR's `integration.trx`) and
   grep for `[NFR spec 014 T031]`. Compare the median against
   `specs/144-a-wait-that-actually-waits/figures.md`.
4. **See the fold.** `grep -rn "WaitUntilResolvableAsync" tests/` returns one definition
   and three call sites.
5. **See the prose match the code.** `AcceptToDecideLatencyTests`' remark no longer claims
   NFR's wait returns on its first iteration.

---

## Locked tech choices (no new decisions; no ADR needed)

| Concern | Choice | Authority |
|---|---|---|
| Integration harness | `AspireFixture` + `AspireCollection`, no Testcontainers | ADR-0103 |
| Docker-free fixture-logic test | `[Trait("Category", "FixtureLogic")]`, run by `ci.yml:67-72` | #2064, existing `FixtureRetryPolicyTests` |
| Test doubles | Hand-written `HttpMessageHandler`, no mocking framework | ADR-0052, 0054 |
| Assertions | xUnit + Shouldly | ADR-0052 |
| Test naming | Sentence-style with underscores | ADR-0053 |
| Async | `CancellationToken` last parameter on new helpers | ADR-0049 |
| Shared test helpers | `tests/Integration.Tests/Fixtures/`, `internal static` class | existing `VariableRequests`, `OverlayRequests`, `LayoutRequests` |

**No ADR is required.** Nothing here makes an architectural decision: it repairs a test
helper, adopts a pattern that already exists in the same directory, and folds three copies
of it into the fixture folder that already holds that kind of helper. The autonomous lane
may not write an ADR (ADR-0144), and does not need to.

---

## Latency-budget impact

**Leg: `Event → overlay state` (≤ 200 ms, constitution §IV) — measurement instrumentation
only; no production code on any leg is touched.**

This change alters nothing the wall executes. It repairs the readiness gate of the test
that *produces* the figure for that leg, and US-3 re-reads the figure to confirm it did not
move. The expected result is no change, because the warmups were already absorbing what the
readiness wait failed to. A moved figure is a finding, reported and filed, not a table edit.

---

## Out of scope — explicitly

- **Any edit to constitution §IV**, its budget, its figures, or its Measured column. If
  US-3's comparison moves, file a finding (ADR-0144: the lane implements decisions, it does
  not make them). §IV carries no cell for this test's number in any case.
- **Any production-code change.** None is required and none is permitted here.
- **`MeasureOneChangeAsync`'s poll interval.** It polls tightly *on purpose* (`:134-135` —
  a fixed delay would quantise every sample). The delay added by US-1 belongs to the
  readiness wait alone. Adding one to the measured poll would corrupt the very figure this
  spec exists to protect.
- **`AcceptToDecideLatencyTests.WaitForValueAsync`.** It waits on a variable's value over
  `GET /system-variables/{name}` — a different endpoint, a different readiness question. Its
  *comment* is in scope; its code is not.
- **`WarmupRounds`' value.** US-2 records the reason for 3; it does not tune it.
- **Deleting `ResolvedTextReachesItsFabTests`' or `TwoPlaceholdersInOneLabelTests`'
  assertions or restructuring their arrangements.** The fold-in swaps a helper body; the
  tests are the characterisation and must pass unmodified.
