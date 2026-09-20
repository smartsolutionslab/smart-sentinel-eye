# Plan 194 — A guard that can still fail

**Phase:** 2 (Plan) — ADR-0037
**Spec:** [`spec.md`](./spec.md)
**Issue:** [#2293](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2293)

---

## Bounded context and layers

| | |
|---|---|
| **Bounded context** | Event Ingestion |
| **Layer touched** | **Tests only** — `tests/EventIngestion.Infrastructure.Tests/` |
| **Layers not touched** | Domain, Application, Infrastructure, Api — none |
| **Cross-context refs** | none added; none possible from a test project of one context |
| **`Shared.Contracts`** | not involved |

The single file in the diff:

```
tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs
```

The subject under test, **read but not modified**:

```
src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs
```

NetArchTest boundary rules are unaffected — no project reference changes, no new
type, no namespace moves.

---

## How the loop actually behaves (grounding)

Written from the code, and confirmed by the four runs in `spec.md`. The plan
below only makes sense against this.

`RunCycleAsync` (`:125`) keeps **arrivals and retries in separate passes**,
deliberately (class docstring `:37-44`):

- `retrying` is drained from `carried` at the top of every cycle.
- With nothing carried, the cycle **blocks** on `channel.ReadBatchAsync`.
- With something carried, it spends the backoff in `Task.Delay` — *not* blocked
  in a read — then takes whatever has arrived with `TakeAvailable` and stores
  both populations in the same cycle.

`StoreArrivalsAsync` (`:176`) tries the batch, which is all-or-nothing. **On
failure it falls back to `RetryAsync(arrived)`** (`:182`) — one delivery at a
time, each getting its own ending. That fallback is what US-1's test actually
guards, and it is correct today.

Traced against the test at `:81` (batch = `[poison, healthy]`, both served in
one read):

1. Batch save throws — `ScriptedEventRepository.SaveAsync` fails the whole
   pending set when any of it is poisoned (`:367`, and its docstring explains
   why modelling one pending event would be wrong).
2. Fallback `RetryAsync([poison, healthy])`:
   - `poison` → throws → `NoteFailure` records `failingSince[poison]` → `Failed`
     → `carried`.
   - `healthy` → **attempted anyway**, pending now holds only `healthy`, save
     succeeds → `Stored` → acknowledged.
3. `healthy.Stored == 1` at roughly T+0, inside cycle 1.

So under the **correct** loop the healthy event never waited for anything, and
the 500 ms window plays no part. Under the mutated loop, step 2 stops at
`poison` and `healthy` is carried untried; it can only be stored after `poison`
exhausts its window and is dead-lettered — at 500 ms today (green, inside the
10 s deadline), at 30 s after this change (red, outside it).

**`Exhausted`** (`:463`) is the whole mechanism: `clock.UtcNow - since >= window`,
where `clock` in the harness is `AdvancingClock` — a real `Stopwatch`. The
window is therefore a **wall-clock** deadline in every test that takes it, which
is the shared root of both findings.

---

## Entities, value objects, invariants

**None introduced.** This slice adds no type. For completeness, the types the
tests already exercise and their invariants, none of which change:

| Type | Invariant relied on |
|---|---|
| `EventIdentifier` | identity key for `failingSince` and `Exhausted` |
| `OccurredAt` | the skew rule that makes `Skewed(...)` permanently refused |
| `Payload` | carries the `"poison"` / `"healthy"` marker the fake repository matches on |
| `DeadLetter` | `RejectionReason` embeds the window — `"not storable after {window}"` |

One consequence worth stating because it is the only place the widened window is
observable outside timing: the dead-letter reason text for an abandoned delivery
embeds `MaximumRetryWindow`. `Records_and_releases_a_delivery_that_never_stores`
(`:61`) asserts `.Error.Value.ShouldContain("not storable after")` — the prefix
only, not the duration. **That test keeps the 500 ms default and is not edited**,
so the assertion is unaffected either way. Noted so a reviewer does not have to
go and check.

---

## Messaging — domain event → integration event

**None.** No domain event is raised, no integration event is published, no
Wolverine handler, queue, or outbox row is involved. `PersistenceLoopHostedService`
is a `BackgroundService` draining an in-process channel; the tests substitute
hand-written fakes for the repository, the dead-letter repository and the
completion callback. ADR-0088's per-module queue isolation is not in play.

---

## Design

### Change 1 (US-1) — `One_delivery_that_never_stores_does_not_hold_up_the_others`

Add to the harness initialiser at `:85-88`:

```csharp
Harness harness = new(Delivery("poison", poison), Delivery("healthy", healthy))
{
    PoisonPayload = "poison",
    // Far longer than this test waits, so the healthy event can only be stored
    // by the loop moving past the failure - not by the failure being abandoned
    // out of the way.
    Window = TimeSpan.FromSeconds(30),
};
```

The comment is the sibling's comment (`:116-119`), verbatim in substance. That
is intentional: the finding is that a correction written down next door was
never carried across, and the two tests should now read the same way.

Assertions unchanged. `RunUntilAsync`'s 10 s deadline unchanged — it remains the
single failure bound (ADR-0150).

### Change 2 (US-2) — `Retries_a_failed_delivery_and_acknowledges_nothing_until_it_lands`

```csharp
Harness harness = new(Delivery("a", completion))
{
    FailuresBeforeSuccess = 2,
    // The window is a wall clock (AdvancingClock), and this is the only test
    // that must finish its retries inside one. Nothing here asserts
    // abandonment, so the window is given enough room that load cannot cause
    // it - the 10 s deadline in RunUntilAsync stays the only failure bound.
    Window = TimeSpan.FromSeconds(30),
};
```

Same value as change 1 and as the sibling: three tests, one number, one meaning
("longer than this test can run"). All three assertions unchanged.

### Change 3 (US-3) — the harness default's docstring

`:247-252` currently explains why the default is *short*. Extend it to say who
it is *for*, since that is the part whose absence caused this spec:

```csharp
/// <summary>
/// Short enough to keep the abandon case a fast test. The bound is a
/// duration in production too - five minutes - so shortening it here
/// exercises the same code rather than a test-only branch.
///
/// <para>
/// Only the abandonment cases should take this default. A test that must
/// not be unblocked by a delivery being abandoned sets <see cref="Window"/>
/// instead - taking the default silently lets abandonment satisfy the
/// assertion, which is how the head-of-line guard came to pass under the
/// very defect it names (spec 194, issue #2293).
/// </para>
/// </summary>
```

Comment only; ADR-0036's "comments say *why*, only when the why is non-obvious"
is satisfied — this why is demonstrably non-obvious, it was missed once.

### After the change: who still takes the 500 ms default

| Test | Takes default? | Legitimate? |
|---|---|---|
| `Acknowledges_each_delivery_in_a_stored_batch` (`:25`) | yes | nothing fails; window never consulted |
| `Retries_a_failed_delivery_...` (`:43`) | **no — change 2** | — |
| `Records_and_releases_a_delivery_that_never_stores` (`:61`) | yes | **this is what the default is for** — the fast abandon case |
| `One_delivery_that_never_stores_...` (`:81`) | **no — change 1** | — |
| `An_event_arriving_behind_a_failing_one_...` (`:104`) | no (already 30 s) | — |
| `A_batch_is_stored_in_one_save_not_one_per_event` (`:149`) | yes | nothing fails |
| `An_envelope_no_rule_will_accept_...` (`:171`) | yes | refused on the fast path; never retried, window only reaches the dead-letter text |
| `An_acknowledgement_that_throws_...` (`:191`) | yes | acknowledgement throws, storage does not; never carried |

Every remaining consumer is one that either never reaches `Exhausted` or wants
it fast. The default's docstring becomes precisely true.

---

## Phase plan and colours

| Phase | Agent | Output |
|---|---|---|
| 4a | **test-writer** | red evidence (both counterfactuals), then the three edits, then green; verbatim output returned |
| 4b | **not applicable** | no production file is modified — see below |
| 5 | verify | `verification.md`: the 2×2, reproduced, plus the clean-revert proof |
| 6 | **backend-reviewer** | EventIngestion infrastructure C#; not infra-reviewer — no Aspire, Docker, CI or Helm surface |

**Phase 4b is not applicable rather than skipped.** ADR-0144 splits phase 4 so
the engineer cannot edit the tests it was handed. Here the delivered diff is
*only* tests, so the engineer has nothing to receive and nothing to implement.
The PR body states this in ADR-0037's skip form:
`Phase 4b: not applicable — no production file is modified.`

**The counterfactual mutation belongs to phase 4a**, applied and reverted by the
test-writer inside its own pass. It is not an implementation step; it is how the
red is obtained. Its revert is a separate, asserted task (T007) precisely
because shipping it would be the worst possible outcome of this spec.

### Colour, restated for the reviewer

- Changes 1 and 2: **red**, by counterfactual. Reasoning in `spec.md` §Phase-4a
  colour. Four verbatim outputs are quoted in the PR, not two.
- The six untouched tests: **characterisation**, green before and after,
  unmodified. Both obligations, different populations.

---

## Risks and how each is closed

| Risk | Closure |
|---|---|
| The mutation ships | T007: `git diff HEAD --stat -- src/` must be empty **and** the restored baseline re-run |
| A stale build makes a bad revert look clean | T007 `touch`es the reverted file before re-running — a restored file can keep an older timestamp and MSBuild then skips the rebuild |
| 30 s reads as "a threshold raised to get green" | `spec.md` argues it is not, and T006 records that `Attempts == 3` — the load-bearing assertion — is untouched |
| The red is confused for a production bug | T003's evidence pairs the mutated red with the *unmutated* green; the PR states plainly that the loop is correct |
| A future test inherits the trap again | Change 3. Accepted as a documentation mitigation only; the alternative (inverting the default) is rejected in `spec.md` §Out of scope |
| `An_event_arriving_behind_a_failing_one` is thought redundant with `:81` | Recorded in `spec.md`: under the mutation the sibling **passes**. They catch different defects |

---

## Constitution and ADR alignment

| Rule | Status |
|---|---|
| §Testing — new behaviour observed failing, failure quoted (ADR-0139) | satisfied by the counterfactual red; reasoning argued in the spec |
| §Testing — behaviour-preserving changes stay green | satisfied for the six untouched tests |
| §Testing — **Waiting** is a condition, not a count (ADR-0150) | strengthened: each edited test is left with exactly one failure bound |
| §II value objects / `PrimitiveBoundaryTests` | not engaged — no domain model touched |
| §IV latency budget | N/A, nothing under `src/` changes |
| No cross-context project references | unchanged |
| ADR-0036 smallest change, no speculative generality | three edits, two of them one line; `TimeProvider` rejected on this ground |
| ADR-0052 / ADR-0054 test stack, hand-written fakes | unchanged; no new package, no new fake |
| ADR-0053 test naming | names unchanged |
| ADR-0065 coverage gates | unaffected — no production line added or removed |
| ADR-0103 no Testcontainers | not engaged; this class is pure in-memory |
| ADR-0144 — the lane may not weaken a gate | this change *strengthens* one and weakens none; argued explicitly |
| ADR-0144 — the lane may not write an ADR | the governance question in `spec.md` is **flagged for Heiko**, not answered |

## Gate — phase 2

Plan aligns with the constitution and the ADRs above. Hand back for review
before phase 3.
