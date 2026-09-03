# Verification: 059 — log dumps that return logs

**Feature**: 059 | **Issues**: #2053, #2054 | **Date**: 2026-09-03

Phase 5 (ADR-0037) evidence, written down here because it had nowhere else to
live. There was no PR at the time it was taken, and three artifacts — `spec.md`,
`plan.md`, `tasks.md` — each asserted that this note existed. Specs 046–058 all
have one; this feature did not, which is the same class of defect the feature is
about: a record that says a thing was checked, and no check anyone can find.

Provenance is stated per item. Everything below was observed on **Windows,
Aspire 13.5.3**, against a real fixture boot with Docker. **Nothing here was
observed on Linux or in CI.**

## 1. The dump returns logs — the one step that proves it to a human

Spec test-procedure step 3. `StreamFabScopingIntegrationTests` was forced to
fail by changing one expected status, so its `RecentLogs("stream-distribution")`
message would be printed, and the forced change was then reverted.

**Before the fix (on `develop`)**, the whole of the dump was the placeholder:

```
(tail subscribed but the resource emitted nothing)
```

**After the fix**, the same forced failure carried real `stream-distribution`
log lines — the service's own output, at the point the assertion failed. That is
US-1, and it is the only step in this feature that demonstrates the value to a
person rather than to CI.

This is the observation that also *found* #2054: step 3 was run at phase 5 as a
check on the #2053 fix, and it failed, which is how the scope grew from "four
names are missing from the tailed list" to "no tail has ever delivered a line".

## 2. The restart — and what it did **not** show

An instrumented build of `TailResourceLogsAsync` printed a marker on each
resolve and on each end of a watch, and `event-ingestion` was restarted through
Aspire's restart command.

- **one `[resolve]`**, not two;
- **no `[stream-ended]`**.

Read plainly: the DCP instance id **did not change across the restart**, and the
watch did not end. Both phase 5 and the phase-6 reviewer saw the same single id
through the full cycle:

```
Resource event-ingestion/event-ingestion-gxkpyqjx changed state: Running -> Stopping
Resource event-ingestion/event-ingestion-gxkpyqjx changed state: Stopping -> Finished
Resource event-ingestion/event-ingestion-gxkpyqjx changed state: Finished -> Starting
```

**So test C does not discriminate a resolve-once implementation.** A resolve
hoisted above the re-subscribe loop passes all three delivery tests on this
build. `spec.md`, `plan.md`, `tasks.md`, `AspireFixture.cs` and
`LogTailDeliversIntegrationTests.cs` all said the opposite in one form or
another, and all five were corrected at phase 6 (finding 1). Re-resolving every
turn stays, because id stability is a property of this DCP build and not a
published contract — but it is **defensive code that no test exercises**, and a
green suite is not evidence that hoisting it would fail.

Test C keeps its place on stronger ground: it is the **regression test for
#2038**. Tests A and B both read tails whose process has run undisturbed since
`StartAsync`, so a subscription that dies at a restart passes both.

**Linux is unverified.** The DCP suffix scheme is not documented as
platform-specific and nobody has watched it there.

## 3. `snapshotWaitHits = 0` across 8 boots

The "resource has published no snapshot yet" branch — the one that delays and
retries rather than recording a failure — was counted across eight fixture
boots. It was **never taken**: `snapshotWaitHits=0`, every boot.

The tails are launched immediately after `StartAsync`, before the
`WaitForResourceAsync` calls, so the branch exists because a snapshot *may* not
be there yet. On this machine it always was. The branch stays — it is a wait,
not a fault, and reporting a still-filling queue as broken is the exact defect
this feature exists to remove — but it is recorded here as **unexercised**
rather than as covered.

## 4. Assumption A1 — NOT answered

**A1 (the marginal cost of a tail is a subscription plus a bounded queue, not
extra log production) is not measured.** Two attempts, neither of which measured
it:

- **Phase 5, before the id fix.** Timed `InitializeAsync` with 8 tails against 4
  while every tail was subscribed to an empty stream — eight *idle* loops
  against four, not the thing A1 is about. Its 8-tail spread alone was 30.7 s,
  22 % of its own minimum, and both 4-tail figures sat inside it. The figures
  are in `spec.md` under A1, labelled for what they are.
- **After the id fix.** **Confounded, and its figures are deliberately recorded
  nowhere.** The 4-tail side was timed before the fix, when the tails enqueued
  nothing; the 8-tail side after, when they carry real traffic. That is "4
  versus 8" confounded with "0 delivering versus 8 delivering", and the drift
  between boots on one side is the same order as the difference between the
  sides. A number written into an artifact is a number someone later quotes as
  clearance, so none is.

An honest measurement needs roughly eight interleaved boots per side,
alternating, **both sides on the same build**, against a bring-up of two to
three minutes each. It was not done. Nothing in this feature depends on the
answer, and `plan.md`'s recommendation (add the four names; 8 tails) never
did.

## 5. Phase-6 findings, and what changed

The reviewer ran the full diff and reproduced §2 independently. Nine findings
were fixed; one was declined by the requester and is not listed here.

| # | Finding | Outcome |
|---|---|---|
| 1 | The id-changes-on-every-restart claim, in five places | Corrected in all five (§2) |
| 2 | No `verification.md` | This file |
| 3 | A1 recorded as measured on a confounded comparison | Recorded as not measured (§4) |
| 4 | `ShouldCarry`'s three placeholder assertions were unreachable | Order inverted; they run before the token check |
| 5 | Test B had no poll; `identity` had no readiness gate | Both added |
| 6 | `RecentLogs` hid a recorded tail failure once the queue was non-empty | Failure appended to the lines; the loop also now recovers |
| 7 | Stale-id recovery asserted but unbounded | Labelled reasoned, not observed; left unbounded, with the reason |
| 8 | The fourth capture placeholder is unreachable from its only caller | Kept and labelled defensive against a replicated name |
| 10 | The coverage guard's blind spots were unstated | Both named in its doc |

Two of those make the tests stronger rather than weaker (4 and 5). **No
assertion that could fire was removed, no threshold lowered, no trait dropped,
no suppression added.**

### Finding 6, in more detail — it was reachable, and it was reached

`TailResourceLogsAsync` caught a non-cancellation exception *outside* the
re-subscribe loop, recorded it, and exited permanently; `RecentLogs` reported
the record only when the queue was empty. Before the id fix the queue was always
empty, so the failure always surfaced. After it, a tail that ran for two minutes
and then faulted would return 400 stale lines and look healthy — this branch's
own thesis verbatim, a dump that reads like output when it is an omission.

Not hypothetical: the reviewer captured

```
Error streaming logs for event-ingestion-gxkpyqjx … "resource is being deleted"
```

during test C's restart. **Both fixes were taken**, not one: the catch moved
inside the loop so a fault is recorded *and retried* rather than ending the tail
for the rest of the run, and `RecentLogs` appends the record to the lines
instead of gating on their absence. The restart is the substantive fix — a
transient during the one event the loop exists to survive was killing the tail —
and the append is what keeps a recovered tail from hiding the gap it left.

## 6. Test and build evidence (phase 6 re-run)

```
Passed!  - Failed:     0, Passed:   113, Skipped:     0, Total:   113, Duration: 2 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

`dotnet build -c Release` clean, 0 warnings, for both touched projects
(`Integration.Tests`, `Architecture.Tests`) — CI uses `TreatWarningsAsErrors`.

The three delivery tests were re-run against a real fixture boot after finding
4's reorder and finding 5's poll, and all three still pass. Their verbatim
output is in the PR body.

## 7. What was **not** observed

Stated so that one green run is not read as more than it is.

- **`CaptureOneResourceLogAsync` through a real startup timeout.** No test can
  provoke one. Its correctness rests entirely on sharing `TryResolveResourceId`
  with the tail loop, which *is* observed — that is why there is one resolver
  and not two.
- **The replicated-name branch of `TryResolveResourceId`**, and therefore the
  fourth capture placeholder. Nothing in this fixture is replicated.
- **The stale-id recovery path.** Reasoned, not seen; nothing bounds the inner
  watch, so a dead instance whose logger stayed open and silent would park a
  tail. No such watch has been observed, and a bound was deliberately not added
  (a timeout forces a periodic re-subscribe, whose window is exactly where the
  line a test is waiting for would be dropped).
- **The snapshot-wait branch.** `snapshotWaitHits=0` across 8 boots (§3).
- **Any of this on Linux or in CI.** Test C carries
  `[Trait("Category", "Disruptive")]` and is excluded on the runner, where the
  restart command fails outright with "Failed to stop resource"; it is observed
  by hand on Windows, exactly as `RestartLosesNothingIntegrationTests` is.
- **A1.** §4.
