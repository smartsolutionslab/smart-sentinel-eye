# Verification: A plant-floor event reaches the things it is supposed to drive

**T015** — [quickstart.md](./quickstart.md) walked. "Done" is the observations,
so they are here rather than a tick.

Observed on 2026-08-19 against the real Aspire stack.

**A green run is not the evidence here**, and that is the whole point of this
feature. 228 green integration tests, twenty coverage gates and a green CI
coexisted with the break this test exists to catch. **The evidence is §3.**

## 1. The journey works — both effects

`EventReachesItsEffectsTests`. Define a variable, activate a rule, publish a
matching event over MQTT as a machine does, then read the value back through the
API and take the highlight at the hub a kiosk connects to:

```
defined oee01a01a6f75a97 = 0
activated rule reach01a01a6f984b7          (created Draft, published, read back Active)
published a matching event over the broker
oee01a01a6f75a97 = 80 after 13094 ms

activated highlight rule light01a01a72... for overlay 01a01a72-...
published a matching event over the broker
overlay 01a01a72-... highlighted after 14366 ms
```

Nothing between the publish and the read is asserted. Every one of these was
true throughout the failure and would have passed against it: the event was
stored and readable, `FabEventIngestedV1` was published, the rule was evaluated,
the effects were computed, `SystemVariableValueRequestedV1` was published — that
last one *is* what broke. Only the two effects sit downstream of every join.

**Two arrangements are load-bearing.** The rule is published and asserted
`Active`, because `POST /rules` mints a Draft and a Draft never fires — a test
that stopped at create would observe nothing and keep observing nothing after
the journey broke. And the variable is defined first, because
`SetVariableValueCommandHandler` refuses a variable it cannot find, which would
fail the test three contexts from the cause.

## 2. An event changes only the variable whose rule matches it

```
published one event that matches one of the two active rules
hit…    = 80 after 300 ms      (the matching rule)
idle…   after an event its rule does not match: 7
```

Without a case like this, asserting "the value is 80" would pass on a completely
dead system if the value were already 80. This is what makes §1 mean something.

**Its first shape could not fail for the right reason, and the review caught
it.** It published an event nothing matched, slept ten seconds, and asserted
nothing had changed — which is also what a dead broker, an unpersisted event, or
simply a slow stack reports. §5 of this very note measures the first event of a
run at 12–14 s, so whenever xUnit happened to order this test first it read the
variable *before an effect could possibly have arrived* and passed. The one case
whose job is to prove the others can fail was the one that could not.

The fix is a second rule rather than a longer sleep. One event, two active
rules, one matching and one not: both effects are fanned out from the same event
in the same pass, so once the matched variable has moved, the ignored one has
had its chance and did not take it. The wait is now bounded by an observation
instead of a guess, and the case proves the system was alive while it declined
to act.

## 3. The journey broken on purpose — the step that cannot be skipped

`OutboxEventBus`'s ambient publish routed into the DbContext outbox, which is
precisely the failure spec 021 shipped:

```
Failed: 3, Passed: 1

An_event_..._changes_the_variable_a_rule_names        Failed
An_event_..._highlights_the_overlay_a_rule_names      Failed
The_same_event_twice_applies_its_effect_once          Failed
An_event_no_rule_matches_changes_nothing              Passed
```

with, from the two effects:

```
never reached 80; last seen 0

"the highlight never reached the hub. The rule was active and the event was
 published, so the break is in one of the joins between EventIngestion,
 Automation and LayoutComposition — which is exactly the failure this test
 exists to catch."
```

**This is the deliverable.** Against a break that 228 tests and a green CI
passed over, every positive case fails, and each message points at the joins
rather than at a missing message — because in the real break the messages were
fine and nothing consumed them.

The negative case correctly **passed** while the journey was broken: on a dead
journey, nothing changing is the right answer. The cases pointing in opposite
directions is what makes any of them evidence.

**A first attempt at this break proved nothing, and is recorded because the near
miss is the point.** Commenting the branch out with `if (false && …)` failed the
build on an analyzer rule (S1125), so the test run used the previous, unbroken
binaries — and reported 4/4 green. Taken at face value that would have been a
conclusion exactly backwards from the truth, reached by the same mechanism as
the bug this feature exists to catch: something green that never ran. The
compiling break routes the ambient publish into the outbox instead, which is
also a closer copy of what actually shipped.

Line restored afterwards; `git status` shows no production file modified.
## 4. A redelivered event applies its effect once

```
published the identical event twice
dup… = 80 after 267 ms      (and still 80 five seconds later)
```

Redelivery stopped being rare with spec 020, so this is an ordinary case now.

**Its name and its docstring both claimed more than it checks, and the review
caught that too.** They said SystemVariables dedups by causing event. It does,
but this test is no evidence for it: `IngestEventCommandHandler` checks
`ExistsAsync(fab, identifier)` and returns `EventAlreadyIngested` *before*
raising `FabEventIngestedV1`, so the duplicate is stopped at the first join and
never reaches Automation or SystemVariables at all. The test would have passed
unchanged with the downstream dedup deleted.

That is this feature's own subject turned on itself — a test whose title
promises a guarantee it never exercises is precisely how the spec 021 break
survived. Renamed and re-documented to claim only what it shows: **ingestion
idempotency, end to end**. SystemVariables' own dedup remains uncovered and is
listed below rather than left implied.

## 5. How long it took, and what that is worth

| | |
|---|---|
| first event after startup | **12 067**, **12 091**, 13 094, 14 366, 13 311 ms |
| every event thereafter | **267**, **275**, **278**, 348, 300, 320, 4 085 ms |

The split is the interesting part, and it is not noise: whichever test runs
first pays 12-14 s and every test after it pays about a quarter of a second, on
the same stack in the same run. That is a structural start-up cost — first
routing, first connection, first dispatch — not a slow journey.

**Against the ≤ 200 ms `event → overlay state` leg (constitution §IV): neither
number is a verdict.** The warm figure is close to it and measured on a host
running nine services and a broker at once; the cold figure is nowhere near it
and describes a condition a fab does not spend its time in. Spec 020 was
explicit that a figure taken on this fixture is not a figure about a plant, and
that applies unchanged. **What is established is that the effect arrives and
roughly how fast; what is not established is compliance with the budget.**

The 12-second cold path is worth someone's attention on its own — it is not
obviously anything this feature introduced, and it is out of scope here.

## 6. Where it runs (SC-004), and the fixture bug that was hiding here

**I had this wrong, and the correction is the most useful thing in this note.**

An earlier draft of this section read *"the tests are stable, the machine is
not"*, and attributed a boot failure in roughly one run in three to Docker on
this box. That was a guess dressed as a finding. It was the fixture.

`AspireFixture.WaitForServiceHealthAsync` retries sixty times and catches
`HttpRequestException` for "listener not bound yet". The probe client carries
the standard resilience handler, whose 30 s timeout surfaces as
`TimeoutRejectedException` — **not** an `HttpRequestException`. So a service
that is *slow* rather than *absent* escaped the catch on the first attempt,
faulted `InitializeAsync`, and failed all 232 tests with an error naming none of
them. A retry loop that could not retry.

What gave it away was a control, not a theory: three consecutive runs of this
class failed instantly, and an unrelated class passed on the same machine a
minute later. A machine that cannot boot Docker does not do that.

Two things are worth taking from it. The mis-attribution was **load-bearing** —
it was the stated reason SC-004 could not be met, so "it's the environment"
would have shipped a real defect as a fact of life. And the failure mode is this
feature's own subject in miniature: a suite reporting 227 failures that name nothing,
where the true cause is one join nobody watched.

Fixed in `tests/Integration.Tests/Fixtures/AspireFixture.cs` by also catching the
timeout, so the loop retries as it was always written to. Test-only; nothing in
`src/` changes, so FR-010 still holds.

### What the runs then showed

SC-004 asked for three consecutive green runs of the whole suite. With the
fixture fixed they were obtainable on the first attempt:

```
run a:  Failed: 0,  Passed: 232
run b:  Failed: 0,  Passed: 232
run c:  Failed: 0,  Passed: 232
```

**SC-004 is met**, and the manner of it is the confirmation: a gate declared
unobtainable became obtainable the moment the real cause was fixed, which is
what tells you the diagnosis was right rather than merely plausible.

232 is the 228 the suite had before this branch plus exactly these four, so they
ran rather than being filtered out — a test excluded by a category filter passes
a build without ever executing, and this feature exists because of something
green that never ran.

The test is therefore **not excluded** (FR-007, and T012 warns why: a third
`Category=Disruptive` exclusion, on the one path with no other coverage, would
put the system back where the break got through). The earlier draft of this note
was one step from arguing for exactly that exclusion, on evidence that turned out
to be a bug in the harness.

CI agrees independently: its `integration tests (Docker)` job reported
`Failed: 0, Passed: 232, Skipped: 1` on this branch before the fixture fix
existed — the Linux runner is not slow in the way that triggered it, which is
also why the defect survived until a Windows dev box ran the suite four times in
an afternoon.

An earlier CI run had `e2e (Playwright, full stack)` fail, unrelated: canceled
after 40 minutes inside **Install Playwright Chromium**, before a single test
ran, on a branch touching no frontend or e2e code. Re-run unchanged, it passed
in 7m36s.

## 7. Coverage

```
All 20 gates pass — unmoved from spec 021, as expected for a change that adds
no production code. A movement here would have meant something unintended was
touched.
```

The fixture fix in §6 is test infrastructure, so this still holds: nothing under
`src/` changes anywhere on this branch (FR-010).

## What this does not cover

**One event, not load.** Sustained throughput is spec 020's and spec 021's
ground.

**The hub, not a browser.** The highlight is asserted as the frame leaving the
hub a kiosk connects to. Whether a browser then applies the CSS class is the
e2e suite's question, not this one.

**SystemVariables' own dedup.** §4 covers ingestion idempotency; a duplicate
that gets *past* ingestion — which is what would exercise the dedup-by-causing-
event in SystemVariables — is not covered here.

**Two effects, not an exhaustive matrix.** These are the two actions rules can
take today. A third would need its own case — covering one proves nothing about
another, which is the whole reason the highlight got its own.

**Not a budget verdict.** See §5.
