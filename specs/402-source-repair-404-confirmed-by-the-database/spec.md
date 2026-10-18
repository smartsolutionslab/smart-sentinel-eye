# Spec 402: A 404 on the source-repair PATCH is "path gone" only when the database agrees

**Feature Branch**: `fix/3013-source-repair-persistent-404` (cut from `origin/develop` at `6962f4ae`)

**Created**: 2026-10-18

**Status**: Specified, planned and tasked. No `[NEEDS CLARIFICATION]` remains. Two judgement calls
the decision did not make are marked **[ASSUMPTION]** in §5.

**Input**: Issue [#3013](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3013), item 1
only, *Source repair: a persistent 404, repeating gateway Warnings and a non-object page body are
still unhandled*. The residual spec 388 accepted in writing (§6, row "Every 404 on a PATCH is
treated as a retire race (FR-003)").

**Lane scope (#3013 comment, ADR-0144), recorded here and not re-opened:** ONLY item 1, using the
mitigation the issue proposes. A 404 on the source-repair PATCH is "path gone" only when
`ReadCommittedSourceAsync(path)` returns None; otherwise it takes the normal Warn/re-Warn path keyed
on the outcome (type, 404). Items 2 (repeating list-read and sweep Warnings) and 3 (non-object page
bodies) stay out.

**ADRs**: ADR-0050 (logging, `[LoggerMessage]`), ADR-0141 (`Option<T>` for the absence),
ADR-0052 / ADR-0103 (hand-written fakes, no Testcontainers), ADR-0139 / ADR-0144 (red first,
autonomous lane). Builds on spec 378 (the repair) and spec 388 (the failure log). **No ADR is
needed**: this narrows one log classification inside an existing type; it makes no throttling or
architectural decision (item 2 would).

---

## 1. Premise check (develop at `6962f4ae`)

| Claim in #3013 | Verified on develop | Holds? |
|---|---|---|
| Every 404 on the PATCH is Debug "path gone" and clears the path's state | `SourceRepairFailureLog.Failed` (`src/StreamDistribution/Infrastructure/Reconciler/SourceRepairFailureLog.cs:24-29`): `if (failure is HttpRequestException { StatusCode: HttpStatusCode.NotFound }) { logger.ReconcilerRepairPathGone(path); failures.Remove(path); return; }`. It does not add the path to `failedThisPass`, so no Warning ever follows. `ReconcilerRepairPathGone` is `LogLevel.Debug` (`Log.cs:40-41`) | **Yes** |
| It applies to the first PATCH and the re-patch | Both PATCHes are inside the one `try` in `MediaMtxSourceRepair.TryRepairPathAsync` (`MediaMtxSourceRepair.cs:74-100`); the `catch` at :96 hands every exception to `failureLog.Failed` at :98 | **Yes** |
| `snapshot.ReadCommittedSourceAsync(path)` exists and returns None for a retired or absent stream | `IStreamSourceSnapshot.ReadCommittedSourceAsync(MediaMtxPath, CancellationToken) : Task<Option<StreamSourceUrl>>` (`IStreamSourceSnapshot.cs`); `StreamSourceSnapshot.cs:24-39` filters `State != Retired && Path == path` and returns `None` on zero rows | **Yes**, name and shape exactly as the issue states |
| A persistent 404 has nothing above Debug | Nothing else logs on that branch; the path stays in `persisted`, stays diverged, and repeats the same Debug line every tick (10 s default) | **Yes** |
| Caveat: if retirement deletes the MediaMTX path before the DB commit, the race warns once | `RetireStreamCommandHandler` **saves first** (`streams.SaveAsync`) and removes the path after (FR-008a of its own spec, commented in the handler). A 404 caused by our own retire is therefore always preceded by a committed `Retired` row, and the confirmation read sees None. The caveat's window does not exist on the retire path; see §6 for the window that does | **Narrower than stated** |

**Verdict: the premise holds on develop.** Behaviour-changing → phase 4a **RED**.

## 2. User stories

### US1 (P1): an operator sees a Warning when MediaMTX persistently answers 404 for a live stream

A MediaMTX upgrade that renames the patch route, a path-name encoding mismatch, or a proxy that
answers 404 leaves every diverged tile on a stale source. Today that is Debug-only, forever. After
this change it Warns with the exception on the first failure and re-Warns on the spec 388 rules
(outcome change, or every 10 minutes), exactly like any other refusal.

### US1 guard: a genuine retire race stays quiet

A 404 for a stream the database no longer holds live is still one Debug "path gone" line, never a
Warning, and clears the path's state, as the owner's decision on #2994 (spec 388, decision 1:
"A 404 **from a retire race** is logged at Debug as 'path gone'") requires.

## 3. Acceptance scenarios

```gherkin
Scenario: a persistent 404 for a live stream warns like any refusal (happy path of the fix)
  Given a diverged path whose stream is live in the database
  And   every PATCH for it answers 404
  When  three passes run back to back
  Then  exactly one Warning is logged with the failure template and the 404 exception
  And   the passes after it log the Debug "still failing" line with "HttpRequestException 404"
  And   no "path gone" line is logged
  And   the path is not counted as repaired

Scenario: a 404 on the re-patch for a live stream warns too
  Given a diverged path whose committed source moves after the first patch
  And   the re-patch answers 404 while the stream is still live
  Then  one Warning is logged with the 404 exception and no "path gone" line

Scenario: a retire race is still "path gone" (guard)
  Given a diverged path whose stream is retired before the PATCH answers 404
  When  the pass runs
  Then  one Debug line naming the path and "path gone" is logged and no Warning
  And   the database was read once for that path to confirm it
  And   the other paths in the pass are still repaired

Scenario: a 404 that changes into a 400 on a live path is a change of outcome (conflict)
  Given a live path that warned for a 400
  When  the next pass answers 404, and the one after answers 400
  Then  each pass logs a Warning, because each is a change of outcome (spec 388 FR-002)

Scenario: the confirmation read itself fails (bad input from the dependency)
  Given a PATCH that answers 404
  And   the confirmation read throws a non-cancellation exception
  Then  the read's exception is logged as that path's failure through the normal Warn/re-Warn path
  And   no "path gone" line is logged
  And   the other paths in the pass are still repaired

Scenario: the caller cancels during the confirmation read
  Given a PATCH that answers 404
  And   the caller's token is cancelled during the confirmation read
  Then  the cancellation propagates and nothing is logged at Warning

Scenario: only a 404 costs a database read
  Given a PATCH that fails with anything other than 404
  Then  no confirmation read is made for that path

Scenario: a live-stream 404 whose stream is retired by the next pass stops warning
  Given a live path that warned for a 404
  When  its stream is retired before the next pass
  Then  the next pass logs nothing for it and its state is pruned (spec 388 FR-005)
```

Auth: N/A. The repair is an internal background worker with no HTTP surface; the control-API
credentials are unchanged.

## 4. Requirements

- **FR-001** (supersedes spec 388 FR-003) When a PATCH in a repair fails with
  `HttpRequestException` whose `StatusCode == 404`, the pass reads
  `snapshot.ReadCommittedSourceAsync(path)` once for that failure.
  - **None** → logged at Debug as "path gone" with the existing `ReconcilerRepairPathGone` template,
    the path's state cleared, not counted as repaired, not added to the pass's failed set (spec 388
    FR-003's behaviour, now conditional).
  - **Some** → the 404 is an ordinary failure: spec 388 FR-002 applies unchanged with the outcome
    (`HttpRequestException`, 404, its `HttpRequestError`), using the existing
    `ReconcilerFailedToRepairPathSource` / `ReconcilerStillFailingToRepairPathSource` templates.
  This applies to both the first PATCH and the re-patch.
- **FR-002** A failure that is not a 404 never triggers the confirmation read.
- **FR-003** **[ASSUMPTION A1]** If the confirmation read throws and the caller has not cancelled,
  the read's exception is passed to the failure log as that path's failure (Warn/re-Warn by its own
  outcome) and the pass continues. This is how a failing post-patch re-read is already handled
  (the `catch` at `MediaMtxSourceRepair.cs:96`), so no new rule is introduced.
- **FR-004** If the caller cancels during the confirmation read, the cancellation propagates, as it
  does for the PATCH and the re-read today (spec 388 FR-007).
- **FR-005** No log template is added, removed or reworded. `ReconcilerRepairPathGone`'s text
  ("its stream was retired during the pass") becomes accurate rather than assumed.
- **FR-006** `IStreamSourceSnapshot`, `IRtspGateway`, `StreamSourceSnapshot`, DI registration and
  `Shared.Contracts` are unchanged.

## 5. Assumptions (disclosed)

- **[ASSUMPTION A1]** FR-003 above: a failed confirmation read is logged as the read's failure, not
  as the 404. Alternative (log the 404 instead) would hide a database fault behind a gateway status.
- **[ASSUMPTION A2]** The confirmation read happens for **each** 404, including the re-patch, and
  is not cached across passes. At most one extra read per path per pass, only on the 404 branch; a
  persistent 404 over 250 diverged paths is 250 indexed single-row reads per tick (10 s).

## 6. Accepted residuals (in writing)

| Residual | Bound | Why accepted |
|---|---|---|
| A path removed from MediaMTX by something other than our retire (MediaMTX restart losing dynamic config, an operator deleting it) between the list read and the PATCH while its stream is live | One Warning; the next pass does not list the path, so it is skipped and its state pruned at `PassCompleted` (spec 388 FR-005) | It is a real divergence worth one Warning; the reconciler's add path restores it |
| The issue's caveat (retire deletes the path before the DB commit) | Not reachable through `RetireStreamCommandHandler`, which commits first | Recorded so the next reader does not re-derive it |
| Items 2 and 3 of #3013 | Unchanged | Out of lane scope; stay on #3013 |

## 7. Independent end-to-end test procedure

Unit level (no Docker): run the `StreamDistribution.Infrastructure.Tests` filter in tasks.md; the
R-tests drive `MediaMtxSourceRepair` with the stateful `SourceRepairGateway` / `SourceRepairSnapshot`
fakes. Integration level (Aspire fixture, CI-authoritative): R8 drives the real sweep with the real
`StreamSourceSnapshot` against Postgres and a gateway answering 404 for a seeded live stream, and
observes exactly one Warning.

## 8. Latency budget

**N/A.** The source repair is a background reconciler on a 10 s interval, not on the
event → overlay path (constitution §IV). The added read is on the 404 branch of that worker only.

## 9. Acceptance

1. Every scenario in §3 has a test; each new-behaviour test is observed red first (ADR-0139).
2. Exactly the existing assertions listed in plan §6 change, each named in the PR body with the
   reason; any other existing assertion that has to change is a **BLOCK**.
