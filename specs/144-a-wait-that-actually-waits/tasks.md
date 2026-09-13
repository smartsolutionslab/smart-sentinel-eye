# Tasks — Spec 144, a wait that actually waits

**Spec:** `specs/144-a-wait-that-actually-waits/spec.md` · **Plan:** `plan.md`
**Issue:** #2201 · **Engineer:** `backend-engineer` · **Phase 4a colour:** RED for US-1,
CHARACTERISATION for US-4 — see `plan.md` § *Phase 4a*

`[ID] [P?] [Story]`. `[P]` marks disjoint files that may run concurrently (ADR-0109).
**T003 is the foundational task**: nothing downstream of it is meaningful until the wait is
correct, and T007 must not begin before it.

---

## Phase A — baseline and the red (US-1, US-3)

### T001 [P] [US-3] — Capture the `develop` baseline figure

Create `specs/144-a-wait-that-actually-waits/figures.md` and write into it the **run id,
SHA, median, worst and all five samples** from the newest green `develop` run's
`integration.trx`.

```sh
gh run list --workflow ci.yml --branch develop --status success --limit 10 \
  --json databaseId,headSha,createdAt
gh run download <id> -n integration-test-results -D <scratch>
grep -r "NFR spec 014 T031" <scratch>
```

Artifact retention is 14 days (spec 136's lesson: run id **and** figure go in the tree, not
a link). If no green run still has the artifact, run the test locally against the Aspire
stack and **label the figure `local`** — a labelled local figure is evidence; an unlabelled
one is not. Disjoint from every other task; may run first or alongside T002.

*Depends on: nothing.*

### T002 [US-1] — The red test, observed failing, against today's body

**`test-writer` only. The engineer may not edit this file to pass.**

New file `tests/Integration.Tests/Fixtures/OverlaySnapshotReadinessTests.cs`,
`[Trait("Category", "FixtureLogic")]`, modelled on `FixtureRetryPolicyTests` — a
hand-written scripted `HttpMessageHandler` (ADR-0054, no mocking framework), a real
`HttpClient` over it, **no Docker, no Aspire fixture, no `[Collection]`**.

Change `NFR_VariableResolutionLatencyTests.WaitUntilResolvableAsync` and `.ResolvedTextAsync`
from `private static` to `internal static`. **Visibility only — do not touch either body.**

Four facts, sentence-style (ADR-0053), Shouldly (ADR-0052):

| Fact | AC | On `develop` |
|---|---|---|
| `A_readiness_wait_does_not_return_while_the_snapshot_is_not_a_200` — 404, 404, then 200 resolved; assert the handler saw **3** requests | AC-1 | **RED** — sees 1 |
| `A_readiness_wait_does_not_return_on_a_200_that_still_carries_the_literal` — 200 stale, then 200 resolved | AC-2 | green (asserts the fix is a narrowing) |
| `A_readiness_wait_that_never_sees_a_200_says_so` — 404 forever, short ceiling; `TimeoutException` naming overlay + variable and saying *not a 200* | AC-3 | **RED** — returns instead of throwing |
| `A_readiness_wait_that_never_resolves_quotes_the_last_text` — 200-stale forever, short ceiling; `TimeoutException` quoting the text | AC-3 | green |
| `A_readiness_wait_polls_rather_than_spins` — 404 forever, 1 s ceiling; request count bounded (e.g. `< 20`) | AC-4 | **RED** — one request, but assert the *upper* bound so the meaning is the delay, not the early return. Bound the count, never the wall clock |

Run `dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release --filter "Category=FixtureLogic"` and **return the verbatim failure output** — it is the
brief for T003 and is quoted in the PR body (ADR-0139).

*Depends on: nothing. Blocks: T003.*

---

## Phase B — the fix (US-1, US-2)

### T003 [US-1] — Make the wait actually wait

In `tests/Integration.Tests/SystemVariables/NFR_VariableResolutionLatencyTests.cs`, adopt
`TwoPlaceholdersInOneLabelTests.WaitUntilResolvableAsync:214-237`'s shape — **copy it, do
not reinvent it**:

- `ResolvedTextAsync` returns `string?`; a non-200 answers `null`, and the doc comment says
  the two states are different and the wait must tell them apart
- readiness = `resolved is not null && !resolved.Contains(literal, StringComparison.Ordinal)`
- `await Task.Delay(PollIntervalMs)` between polls; `private const int PollIntervalMs = 200`
- the ceiling becomes a parameter defaulting to `30_000` (five parameters is fine — tests
  suppress `S107`, `Directory.Build.props:108`; do **not** invent a parameter object)
- the `TimeoutException` distinguishes *"not a 200"* from *"a 200 carrying '<text>'"* and
  names both overlay and variable

**The one coupling — `MeasureOneChangeAsync:149` shares `ResolvedTextAsync`.** Its
`.Contains(expected)` must become null-tolerant with **identical semantics**: a non-200
meant "not a match" before (empty string) and must go on meaning exactly that. Do not throw
on `null`; do not treat `null` as a match.

**The trap — do not add a delay to `MeasureOneChangeAsync`'s poll.** `:134-135` polls
tightly on purpose; a delay would quantise every §IV sample and the test would stay green.

Run the T002 filter; all five facts green. Run the NFR integration test; still green.

*Depends on: T002. Blocks: T004, T005, T007.*

### T004 [US-2] — Record what the warmups are for

`WarmupRounds = 3` stays. Replace the bare constant with a doc comment giving the reason,
which the engineer must confirm by observation rather than copy: **the readiness wait
exercises only `GET /system-variables/snapshot`; the measured path is a different one** —
version read, `PUT .../value`, domain event, outbox, resolve — so warmup round 0 is still
its first execution, carrying first-call JIT, Wolverine handler resolution and EF plan
compilation. The wait warms the read path; the warmups warm the write-and-propagate path.

Add the warmup samples to the existing `Console.WriteLine` artefact (a *second* line, not a
change to the existing one, which spec 014/T039 comparisons parse). That is the evidence:
if round 0 is markedly slower than rounds 1–2, the warmups are demonstrably load-bearing
and the comment above is observed rather than asserted. **No assertion changes.**

If the observation contradicts the reasoning — round 0 indistinguishable from rounds 1–2 —
say so in the PR and keep the warmups anyway; the cost is three rounds and the downside is
a corrupted figure.

*Depends on: T003.*

---

## Phase C — the figure (US-3)

### T005 [US-3] — Re-read the figure and compare

Run the integration job (PR CI, or locally against the Aspire stack) and grep
`[NFR spec 014 T031]` out of `integration.trx`. Write the post-fix row into `figures.md`
beside T001's baseline: run id, SHA, median, worst, five samples.

**Run it twice if anything about the machine changed** — the first run after machine churn
looks exactly like a regression.

*Depends on: T001, T003, T004.*

### T006 [US-3] — State the verdict, and do not touch §IV

One line in `figures.md` and in the PR body:

- **Did not move** (expected) → *"the median is within noise of the baseline; the three
  warmup rounds were covering what the readiness wait was not."* That sentence is the whole
  point of US-3.
- **Moved** → report it as a **finding**: file an issue, quote both figures, and change
  nothing. **Do not edit constitution §IV**, its budget, its figures or its Measured
  column. §IV carries no cell for this test's number in any case — its
  `Event → overlay state` row is about the production metric (#1707). ADR-0144: the lane
  implements decisions, it does not make them.

*Depends on: T005.*

---

## Phase D — one copy (US-4) — **after** the fix, never before

### T007 [US-4] — Fold the readiness helpers into the fixture

New `tests/Integration.Tests/Fixtures/OverlaySnapshotReadiness.cs`, `internal static class`
in `SmartSentinelEye.Integration.Tests.Fixtures`, mirroring `VariableRequests` /
`OverlayRequests` in shape and doc-comment style:

- `SnapshotAsync(HttpClient, Guid, CancellationToken)`
- `ResolvedTextAsync(HttpClient, Guid, CancellationToken) → Task<string?>` — the doc comment
  states `null` ⇔ not a 200, because that distinction *is* #2201
- `WaitUntilResolvableAsync(HttpClient, Guid, string, int, CancellationToken)` — T003's body
- `ResolvedTextIn(string body)` — the JSON projection, today in `TwoPlaceholders…:257` and
  inlined twice more

`UniqueVariableName()` → `VariableRequests.UniqueName()`, not onto the readiness class.

Repoint **three** call sites — the issue says two:

1. `NFR_VariableResolutionLatencyTests` (delete `:159`, `:179`, `:216`; the `internal`
   widening from T002 disappears with them, and `OverlaySnapshotReadinessTests` retargets at
   the fixture)
2. `TwoPlaceholdersInOneLabelTests` (delete `:214`, `:243`, `:254`, `:257`, `:264`)
3. `ResolvedTextReachesItsFabTests` (delete `:580`, and the inlined snapshot read at
   `:585-592`) — **this one gains a 200 ms poll delay it did not have.** Its readiness
   semantics are unchanged (it already guards on `IsSuccessStatusCode`), so its assertions
   are unaffected — but say it in the PR rather than letting a reviewer find it.

**Characterisation, observed green.** All three integration tests pass **unmodified**. An
assertion that has to be edited is evidence behaviour moved — block, do not adjust. Capture
the three green before the fold and again after, and quote both.

*Depends on: T003, T005 (the figure is read against the fixed-but-unfolded code, so a moved
figure can only be the fix and never the fold).*

### T008 [US-4] — Bring the prose back in line with the code

`tests/Integration.Tests/Automation/AcceptToDecideLatencyTests.cs:414-421` says NFR's wait
"maps a non-200 to an empty string, so it returns on its first iteration against a 404, and
it has no delay so it would spin if it ever looped (#2201)". Every clause is false after
T003 and T007. Rewrite to record that the two now share one implementation, and **keep the
reason** — a readiness check that returns early does not merely wait less, it moves set-up
work into the first measured sample — because that reason is still true and is stated
nowhere else.

**Comment-only in a `Category=Measurement` file.** Do not touch `WaitForValueAsync`'s code:
it waits on a variable's value over `GET /system-variables/{name}`, a different endpoint and
a different readiness question. Explicitly out of scope.

*Depends on: T007.*

### T009 [P] [US-5] — Evaluate `PublishOverlayReferencingAsync`, and be willing to decline

Two named copies with **different arities** (`NFR…:193` takes one placeholder name;
`TwoPlaceholders…:176` takes two) plus a third inlined in
`ResolvedTextReachesItsFabTests:523-541` whose overlay also needs a munich layout, and all
three use a different overlay-name prefix.

Fold **only if** it collapses to one signature of the form
`OverlayRequests.PublishWithLabelAsync(HttpClient overlays, string labelText, string namePrefix, CancellationToken)`
— each caller composing its own label text. If it needs a knob per caller, **leave it and
say why in the PR** (ADR-0036, no speculative generality; spec 137 row 8 is the precedent
for declining a fold). Declining is a valid outcome and does not block the PR.

*Depends on: T007. Disjoint from T008.*

---

## Parallelism

- **T001 ∥ T002** — `figures.md` vs the new test file plus a visibility edit. Disjoint.
- **T008 ∥ T009** — `AcceptToDecideLatencyTests.cs` vs `OverlayRequests.cs` + the three
  call sites. Disjoint, both after T007.
- Everything else is a chain. T003 is the foundational task; T002 gates it, and T007 must
  not precede it.

## Gate for phase 3

- [ ] Tasks atomic and ordered
- [ ] **Feature issue #2201 on Project #13** — added by hand;
      `gh project item-add 13 --owner smartsolutionslab --url <issue-url>`. No per-task
      issues (CLAUDE.md: the practice stopped after spec 028). Verify with `--limit 2000`.
