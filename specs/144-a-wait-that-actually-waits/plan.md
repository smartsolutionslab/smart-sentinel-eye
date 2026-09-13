# Plan — Spec 144, a wait that actually waits

**Spec:** `specs/144-a-wait-that-actually-waits/spec.md`
**Issue:** #2201 · **Branch:** `fix/2201-readiness-wait-that-lies` · **Engineer:**
`backend-engineer`

---

## Bounded context and layers

**None, and that is the entire architectural statement.** This change lives wholly inside
`tests/Integration.Tests/`. No `src/` file is touched, no bounded context gains or loses
anything, no `Shared.Contracts` message changes, no migration, no Aspire resource, no
NetArchTest boundary is engaged. The reviewer's boundary check for this PR is the diff's
file list: anything under `src/` is a defect.

No entities, no value objects, no invariants, no domain events, no integration events.
The only "domain" here is the integration suite's own readiness vocabulary, and it has
exactly one invariant worth naming:

> **A readiness signal must distinguish *absent* from *satisfied*.** `string.Empty` cannot,
> because every `Contains` against a non-empty needle answers `false` — the same answer a
> fully resolved label gives. `string?` can: `null` is the absence, and the caller is
> forced by the compiler to decide what it means.

That is the same shape as this repo's `Option<T>` preference (ADR-0141) and as spec 129's
title, *absent is not empty*. Nothing new is being invented.

---

## Phase 4a — the colour, reasoned out, because this one does not fit the usual mould

**Declared colour: RED (behaviour-changing) for US-1, with a CHARACTERISATION obligation
layered on US-4. Both, on different artefacts, in that order.**

ADR-0144 assumes one colour per issue. This issue genuinely has two parts, and forcing it
into one would discard evidence. The reasoning, spelled out so the engineer can be held to
it and a reviewer can disagree with it:

### Why the obvious answer — "it's only test code, so there's nothing to write a red test
### against" — is wrong

The instinct is that the *system under test* (SystemVariables' API, the reverse index, the
snapshot handler) is unchanged, so the change is behaviour-preserving. That is true of the
product and irrelevant to the gate. **The code being changed is
`WaitUntilResolvableAsync`, and its behaviour changes materially**: from *returns on
iteration 1 having read a 404* to *blocks until a 200 whose text no longer carries the
literal*. That is a behaviour change by any reading, and ADR-0144 says ambiguity resolves
to red anyway — the path that fails loudly.

The second instinct — "you can't unit-test a private helper on a test class" — is also
wrong, and this repo already disproves it. `tests/Integration.Tests/Fixtures/` holds four
classes that test the integration suite's own machinery:
`FixtureRetryPolicyTests`, `AspireFixtureMigrationGateTests`, `AspireFixtureStartupGateTests`,
`AspireFixtureReportSelectionTests`. `FixtureRetryPolicyTests` is the direct template: a
hand-written `CountingHandler : HttpMessageHandler`, a real `HttpClient` built over it, an
assertion on the **attempt count**, no Docker, no stack, `[Trait("Category",
"FixtureLogic")]`, run by `ci.yml:67-72` in the fast Release job. The red test for AC-1 is
that file's shape with a different handler script.

### Why a *counterfactual* is not enough here, though the session's instinct points at one

The standing technique — *prove a guard by counterfactual* — proves a guard **catches**
what it claims to. It is the right tool when the code is already correct and the question
is whether the safety net has holes. Here the code is **wrong**, and the artefact worth
producing is not "the fixed wait would have caught this" but **the failing output of the
wait as it stands today**. A counterfactual run after the fix would be a reconstruction;
the red run is the evidence itself, and ADR-0139 requires it quoted in the PR body where a
later reader can check it. Take the red. The counterfactual is the fallback if the red
turns out unobtainable, and it will not.

### The one concession, and why it is not a refactor smuggled in first

To exercise today's body, the red test must reach it. Two members change from
`private static` to `internal static`:

- `NFR_VariableResolutionLatencyTests.WaitUntilResolvableAsync`
- `NFR_VariableResolutionLatencyTests.ResolvedTextAsync`

A visibility widening within one assembly, zero behavioural content, and **T007 deletes
both** when the bodies move to the fixture. This is deliberately *not* the alternative of
extracting to the fixture first and writing the red there: the issue forbids extracting
before the fix, for the stated reason that it moves the code under test out of the file
that has the defect. Widening visibility leaves the defect exactly where it is.

### Why US-4 is the other colour, and why it cannot be folded into US-1's red

The fold-in changes **no** observable behaviour of any of the three integration tests. Its
proof is therefore the opposite kind: the three files' assertions pass **unmodified**
after. An assertion that has to be edited is evidence behaviour moved — block, do not
adjust (constitution §Testing). Running it as red would be meaningless; there is nothing to
fail.

One nuance the engineer must hold: the fold-in **does** change one call site's behaviour.
`ResolvedTextReachesItsFabTests.WaitUntilResolvableAsync:580` currently hot-spins; the
folded helper delays 200 ms between polls. Its *readiness semantics* are identical — it
already guards on `IsSuccessStatusCode` — so its assertions are unaffected and
characterisation is the right frame. But it is a real change and the PR must say so rather
than letting a reviewer discover it.

### The falsifiable statement of "done"

> `dotnet test tests/Integration.Tests/… --filter "Category=FixtureLogic"` contains a test
> that fails on `origin/develop` with an observed request count of **1** against a
> 404-then-200 stub, and passes on this branch with **3**; and the three integration tests
> named in AC-6 pass with no assertion edited.

Not "done when it compiles". Not "done when the NFR test is green" — it is green today.

---

## The sequence, and the one coupling that will bite

Order is load-bearing. The issue's steps 1→2→3→4 are preserved.

```
T001 baseline  ──┐
T002 red test  ──┼─→ T003 fix in place ─→ T004 warmups ─→ T005 figure+compare ─→ T006 report
                 │                                                                    │
                 └────────────────────────────────────────────────────────────────────┘
                                                                     T007 fold ─→ T008 prose ─→ T009 evaluate
```

### The coupling: `ResolvedTextAsync` is shared with the measured poll

`MeasureOneChangeAsync:149` reads

```csharp
if ((await ResolvedTextAsync(variables, overlay)).Contains(expected, StringComparison.Ordinal))
```

Change `ResolvedTextAsync` to return `string?` and that line dereferences a possible null.
It must become a null-tolerant equivalent whose **semantics are unchanged**: a non-200
already meant "not a match" (empty string), and it must go on meaning exactly that —
`null` is likewise not a match. The measured poll's behaviour is **preserved**; only its
spelling moves. Getting this wrong in either direction is the failure mode: throwing on a
404 turns a transient into a red run, and treating `null` as a match would corrupt the
figure silently, which is the very defect this spec is repairing one method away.

### The trap: do not add a delay to the measured poll

`MeasureOneChangeAsync:134-135` polls tightly **on purpose** — "a fixed delay would
quantise every sample to the delay and measure the test, not the system." The 200 ms
interval US-1 introduces belongs to the readiness wait and nowhere else. A well-meaning
sweep that adds `Task.Delay` to both would quantise the §IV figure to 200 ms and the test
would still be green.

### Parameter count: the helper may take five parameters

The corrected wait needs a ceiling parameter so AC-3 and AC-4 can drive it short.
`(HttpClient, Guid, string, int, CancellationToken)` is five, past ADR-0084's limit of
four. **Tests are exempt** — `Directory.Build.props:108` suppresses `S107` (with `S104`,
`S138`, `S1541`, `S134`) for test projects, and `AcceptToDecideLatencyTests.WaitForValueAsync`
already takes six. Do **not** invent a parameter object to dodge a rule that does not apply.

---

## Target shapes

### T003 — the corrected wait, in place (still in `NFR_VariableResolutionLatencyTests`)

Copied from `TwoPlaceholdersInOneLabelTests:214-237`, not reinvented. The obligations, each
mapping to an acceptance scenario:

| Obligation | Why | AC |
|---|---|---|
| `ResolvedTextAsync` returns `string?`; `null` ⇔ not a 200 | absent ≠ empty | AC-1 |
| Readiness = `resolved is not null && !resolved.Contains(literal)` | both halves, neither alone | AC-1, AC-2 |
| `await Task.Delay(PollIntervalMs)` between polls, `PollIntervalMs = 200` | the loop is now real; an undelayed one spins | AC-4 |
| Ceiling parameterised, defaulting to `30_000` | AC-3/AC-4 must drive it short without a 30 s test | AC-3 |
| Timeout message distinguishes *"not a 200"* from *"'<last text>'"*, and names overlay + variable | an unbooted index and a stalled snapshot loop otherwise look identical | AC-3 |

### T007 — the folded helper

New: `tests/Integration.Tests/Fixtures/OverlaySnapshotReadiness.cs`,
`internal static class`, mirroring `VariableRequests` / `OverlayRequests` / `LayoutRequests`
in namespace (`SmartSentinelEye.Integration.Tests.Fixtures`), visibility and doc-comment
style.

Members:

- `internal static Task<HttpResponseMessage> SnapshotAsync(HttpClient, Guid, CancellationToken)`
- `internal static Task<string?> ResolvedTextAsync(HttpClient, Guid, CancellationToken)` —
  `null` ⇔ not a 200, and the doc comment says so, because that is the whole defect
- `internal static Task WaitUntilResolvableAsync(HttpClient, Guid, string, int, CancellationToken)`
- `internal static string ResolvedTextIn(string body)` — the JSON projection, currently
  duplicated between `TwoPlaceholders…:257` and the two inline readers

`UniqueVariableName()` goes to **`VariableRequests.UniqueName()`** — a variable concern,
in the fixture that already owns variable concerns — not onto the readiness helper.

Call sites repointed: `NFR_VariableResolutionLatencyTests`,
`TwoPlaceholdersInOneLabelTests`, `ResolvedTextReachesItsFabTests`. Three, not two.

### T008 — the prose that outlived its code

`tests/Integration.Tests/Automation/AcceptToDecideLatencyTests.cs:414-421` currently states
NFR's wait "maps a non-200 to an empty string, so it returns on its first iteration against
a 404, and it has no delay so it would spin if it ever looped (#2201)". After T003 and T007
every clause is false. Rewrite it to record that the two now share one implementation, and
keep the *reason* — a readiness check that returns early moves set-up work into the first
measured sample — because that reason is still the point and is stated nowhere else.

---

## Messaging, boundaries, persistence

**Not applicable, in every case, and stated so the absence is deliberate rather than
unnoticed.** No domain event, no integration event, no `Shared.Contracts` change. No
cross-context project reference is added or removed; NetArchTest's rules are unengaged. No
DbContext, no migration, no EF mapping, no Marten. No Aspire resource, no connection string,
no Keycloak client, no scope. No HTTP endpoint gains or loses an `Idempotency-Key`
(ADR-0142) and no client's retry posture changes (ADR-0143) — `FixtureRetryPolicyTests`
governs the suite's clients and is not touched.

---

## Evidence artefacts this spec commits to producing

Spec 136 established that a CI figure must be written **into the tree** — run id *and*
figure — because artifact retention is 14 days and a link is not evidence.

`specs/144-a-wait-that-actually-waits/figures.md` carries:

| Row | Source |
|---|---|
| `develop` baseline: run id, SHA, median, worst, five samples | newest green `develop` `integration-test-results` / `integration.trx`, grep `[NFR spec 014 T031]` |
| Post-fix: run id, SHA, median, worst, five samples | this PR's own integration run |
| Verdict | moved / did not move, and the one line saying what that proves |

If the baseline artifact has expired, T001 re-captures from the newest green `develop` run
that still has one; if none does, it runs the test locally against the Aspire stack and
says so in `figures.md` — a local figure labelled local is evidence; an unlabelled one is
not.

---

## Risks

| Risk | Mitigation |
|---|---|
| The red test is written against the fixed code and never observed red | T002 lands **before** T003 and its verbatim failure is quoted in the PR (ADR-0139). The test-writer returns the output; the engineer may not edit the test |
| `ResolvedTextAsync`'s new `string?` breaks the measured poll's semantics | Called out above as the one coupling; the poll's `null` and old `string.Empty` must both mean "not a match" |
| A delay is added to `MeasureOneChangeAsync`, quantising the §IV figure | Named as a trap in this plan and as **Out of scope** in the spec; a reviewer checks `:147-153` is untouched but for the null-tolerance |
| The fold-in silently changes `ResolvedTextReachesItsFabTests` | It does — it gains a poll delay. AC-6 requires it pass unmodified; the PR states the change rather than letting it be discovered |
| The figure moves and someone "fixes" §IV | Spec's **Out of scope** forbids it outright; ADR-0144 forbids the lane amending the constitution. A move is a filed finding |
| The baseline trx has expired (14 days) | T001 falls back to an older green run, then to a labelled local run |
| Someone later trims `WarmupRounds` again | T004 puts the reason in the code, which is the only place the next trimmer will look |
| `Category=FixtureLogic` job is skipped and the red/green is never actually run in CI | `ci.yml:67-72` runs it unconditionally in the Release job; the four-bucket manual read before merge covers a skipped bucket |
