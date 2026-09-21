# Tasks — Spec 201, a reservation that lets go

**Issue:** #2290 · **Spec:** `spec.md` · **Plan:** `plan.md`
**Phase-4a colour:** **RED** — a test that arrives green is a phase-4 failure (ADR-0139).

## Parallelism (ADR-0109)

**US1 and US2 are fully parallel.** They touch disjoint files
(`IdempotencyStore.cs` vs `IdempotentRequest.cs`), disjoint test projects
(`Integration.Tests` vs `ServiceDefaults.Tests`), and neither reads the other's
change. Two engineers can run them at once.

**US3 depends on US1** for one thing only: `IdempotencyReclamation.StaleAfter`,
created by **T002**. Once T002 has landed, US3 is independent of the rest of
US1 and runs alongside it.

**The one foundational task is T002** — a new file in `ServiceDefaults` that
both US1 and US3 read. It blocks T003 and T009. Everything else fans out.

**The seven registration edits (T012) are `[P]` among themselves** — seven
different `*InfrastructureModule.cs` files in seven bounded contexts, one line
each, no shared file. If the orchestrator splits them, they are seven
independent edits.

**File contention with other issues: none.** `spec.md` §*File contention*
checked every open PR; #2489 is the only one and touches nothing here. Any
issue not touching `src/ServiceDefaults/Idempotency/` or the seven
`*InfrastructureModule.cs` files is free to run concurrently.

---

## US1 (P1) — A reservation nobody is holding is reclaimed by the next caller

### Phase 4a — RED (`test-writer`)

**T001 [US1]** — New file
`tests/Integration.Tests/CameraCatalog/StaleIdempotencyReservationIntegrationTests.cs`,
namespace `SmartSentinelEye.Integration.Tests.CameraCatalog`,
`[Collection(AspireCollection.Name)]`, `AspireFixture` by primary constructor.
Copy the request shape (`CreateAdminClientAsync("camera-catalog")`,
`RegisterAsync`, `NewName()`) from `IdempotentCameraRegistrationIntegrationTests.cs`
in the same folder.

Write the shared helper first — it is the load-bearing part of the whole
suite. **It must damage a row the system wrote, never insert one by hand**
(`plan.md` §*Testability*): register once with key `K`, then through
`aspire.CreateCameraCatalogDbContextAsync()` run

```sql
UPDATE idempotency_key
   SET resource_identifier = NULL, completed_at = NULL, reserved_at = NOW() - {1}
 WHERE key = {0};
```

Parameterise the interval so the same helper serves the stale and the live
case. **Do not hand-write a `caller` value and do not `INSERT` a fresh row** —
`caller` is the operator identifier the endpoint derives from the bearer token,
so a guessed string lands in a different scope and the test passes while
proving nothing.

Every key and camera name is run-unique (`$"key-{Guid.CreateVersion7():N}"`);
the integration database is shared across runs.
*Depends on:* nothing.

**T002 [US1]** — New file
`src/ServiceDefaults/Idempotency/IdempotencyReclamation.cs`:
`public static class IdempotencyReclamation` with
`public static TimeSpan StaleAfter { get; } = TimeSpan.FromMinutes(10);`.

The doc comment carries the arithmetic from `spec.md` §US1 — 120x the 5 s
in-progress wait, 60x the 10 s attempt timeout the comment at
`IdempotentRequest.cs:63` names, and why too-short is a correctness failure
while too-long is an annoyance. A reader must be able to re-derive the number,
not merely find it.

**This is a production file written during 4a**, and deliberately: the test at
T003 backdates by 30 minutes against this bound, so the constant is part of the
test's premise rather than part of the fix. It changes no behaviour on its own.
*Depends on:* nothing. **Blocks T003, T009.**

**T003 [US1]** — The red case:
`A_reservation_older_than_the_bound_is_reclaimed_by_the_next_caller`. Register
name `N1` under key `K`; damage the row to `NOW() - INTERVAL '30 minutes'`;
`POST /cameras` again with the same `K` and a **different** name `N2`.

Assert `201`, and assert a camera named `N2` exists. The different name is what
makes success observable — a replay would return `N1`'s identifier, so `N2`
existing is what proves the work actually ran.
*Depends on:* T001, T002.

**T004 [P] [US1]** — Control:
`A_reservation_that_is_still_young_is_still_refused`. Same helper,
`INTERVAL '10 seconds'`. Assert `409` and title
`IDEMPOTENT_REQUEST_IN_PROGRESS`, and that no camera named `N2` exists. This
one is **green today and must stay green** — it is the assertion that a
too-eager reclaim would break.
*Depends on:* T001.

**T005 [P] [US1]** — **The ADR-0142 guard, and the most important assertion in
the suite:** `A_completed_key_is_replayed_however_old_it_is`. Register `N1`
under `K`; backdate `reserved_at` to `NOW() - INTERVAL '30 days'` **leaving
`resource_identifier` and `completed_at` intact**; `POST` again with `K` and
name `N2`.

Assert `201` carrying **`N1`'s identifier**, and that no camera named `N2`
exists. A reaper that ignored `resource_identifier` would pass T003 and T004
and fail only here. Aged far past the bound on purpose — a fresh completed row
proves nothing about it.
*Depends on:* T001.

**T006 [P] [US1]** — `A_stale_reservation_is_not_another_callers_to_reclaim`.
Damage a row belonging to one operator; `POST` the same key as a **different**
operator (the fixture's second subject, as
`EventTypeRegistryIdempotencyIntegrationTests` uses). Assert the second
operator gets its own `201` for its own name and never `N1`'s identifier.
`IdempotencyScope.Caller` is a security boundary; reclamation must not become a
way across it.
*Depends on:* T001.

**T007 [US1]** — **Run T003–T006 and capture the verbatim output.** T003 must
fail with the `409`; T004, T005 and T006 must pass. Quote the failure in the PR
body — it is the only form of the phase-4a evidence a later reader can check.
A green T003 means the test is wrong, not that the bug is absent.
*Depends on:* T003, T004, T005, T006.

### Phase 4b — GREEN (`backend-engineer`)

**T008 [US1]** — `src/ServiceDefaults/Idempotency/IdempotencyStore.cs`,
`BeginAsync` only: change the claim's `ON CONFLICT (key, endpoint, caller) DO
NOTHING` to the `DO UPDATE SET reserved_at = NOW() WHERE
idempotency_key.resource_identifier IS NULL AND idempotency_key.reserved_at <
NOW() - {3}` form in `plan.md` §US1, passing
`IdempotencyReclamation.StaleAfter` as the fourth parameter (a `TimeSpan`;
Npgsql maps it to `interval` — do not format it into the string).

**Nothing below the statement changes.** `inserted == 1` keeps its meaning for
both insert and conflict-update; the fallback `SELECT`, the `InProgress` branch
and `CompletedWith` are untouched. Add `completed_at IS NULL` to neither guard
— `plan.md` says why.

A comment saying **why `SET reserved_at = NOW()` is there** (it is what makes a
second concurrent reclaimer lose, not bookkeeping) — that is a non-obvious why,
which is the only kind of comment this repo wants.
*Depends on:* T002, T007.

---

## US2 (P2) — A failed release does not replace the failure that caused it

Runs `[P]` with all of US1 — different source file, different test project.

### Phase 4a — RED (`test-writer`)

**T009 [P] [US2]** — In the existing
`tests/ServiceDefaults.Tests/Idempotency/IdempotentRequestTests.cs`, extend the
`RecordingStore` fake with a mode that makes `ReleaseAsync` throw. Add
`A_release_that_throws_does_not_hide_the_failure_that_caused_it`: work throws
`InvalidOperationException("work failed")`, `ReleaseAsync` throws a
distinguishable exception; assert the exception escaping `ExecuteAsync` is
**the work's**.

Red today: the release's exception escapes instead, because `throw;` at
`IdempotentRequest.cs:180` is never reached.

**Do not modify any existing test in this file.** `A_failed_attempt_releases_its_key_so_a_retry_can_claim_it`
and `A_cancelled_attempt_still_releases_its_key` are the characterisation half
and must pass unedited after T011. An assertion that has to be adjusted is
evidence the behaviour moved — block, do not adjust.
*Depends on:* nothing.

**T010 [P] [US2]** — The activity assertion, **with a listener or not at all**.
`Activity.Current` is null in a bare xUnit test, so an assertion against it
would pass vacuously. Start an `ActivitySource` with an `ActivityListener`
whose `Sample` returns `ActivitySamplingResult.AllData`, start an activity
around the call, and assert the release failure landed on it as an exception
event.

If that is judged disproportionate for the value, **drop this task and say so
in the PR** — an unlistened `Activity.Current` assertion is worse than no
assertion, because it looks like coverage.
*Depends on:* T009.

**T011 [US2]** — Run the `ServiceDefaults.Tests` idempotency suite; capture the
verbatim output. T009 red, every pre-existing case green.
*Depends on:* T009, T010.

### Phase 4b — GREEN (`backend-engineer`)

**T012 [US2]** — `src/ServiceDefaults/Idempotency/IdempotentRequest.cs`: extract
the release in the `catch` at `:172-181` into a private
`ReleaseQuietlyAsync(IIdempotencyStore, IdempotencyScope)` that wraps
`ReleaseAsync` in its own `try`/`catch`, records the failure with
`Activity.Current?.AddException(releaseFailure)`, and returns — leaving `throw;`
to rethrow the original.

The `catch` **must state its case in a comment**: a swallowed exception is a
review blocker in this repo, so the code has to say why this one is right —
the release runs on the connection that just failed the work, the second
failure is the less informative of a correlated pair, and the row it could not
delete is no longer permanent because `BeginAsync` reclaims it after
`IdempotencyReclamation.StaleAfter`.

Keep the existing `CancellationToken.None` and the comment explaining it.
**Do not touch `:185` or `:189`** — `spec.md` §Out of scope.
*Depends on:* T011.

---

## US3 (P3) — Reservations nobody ever retries do not accumulate

Shippable on its own; must not block US1. `[P]` with US2 throughout.

### Phase 4a — RED (`test-writer`)

**T013 [US3]** — In the file T001 created, add three sweep cases driving
`RunOnceAsync` directly on a resolved
`IdempotencyReservationSweepHostedService<CameraCatalogDbContext>` (never
waiting for a tick): a stale unfinished row **vanishes**; an aged-completed row
**survives with its identifier**; a fresh unfinished row **survives**. Reuse
T001's damage helper for all three.

The middle case is the ADR-0142 guard again and is not optional.
*Depends on:* T001, and on T014's type existing to compile against.
*Note for the orchestrator:* this is the one place 4a and 4b interleave — the
test cannot compile before the class exists. Write T014's file as a **shell
that does nothing** (an empty `RunOnceAsync`), confirm all three cases fail,
then fill it in at T015.

**T014 [US3]** — New file
`src/ServiceDefaults/Idempotency/IdempotencyReservationSweep.cs` — declarations
only at this step: `IdempotencyReservationSweepOptions` (`SectionName`,
`TickInterval` default 1 hour) and
`IdempotencyReservationSweepHostedService<TDbContext> : BackgroundService`
with a public `RunOnceAsync` that does nothing yet.
*Depends on:* T002.

**T015 [US3]** — Run T013's three cases; capture the verbatim output. The
stale-row case must fail (nothing was swept); the two survival cases must pass.
*Depends on:* T013, T014.

### Phase 4b — GREEN (`backend-engineer`)

**T016 [US3]** — Fill in `IdempotencyReservationSweep.cs`, mirroring
`AuditRetentionHostedService.cs:38-58` shape for shape:

- `ExecuteAsync` runs `RunOnceAsync` once at startup, then loops a
  `PeriodicTimer` over the injected `TimeProvider`, catching
  `OperationCanceledException` for shutdown.
- `RunOnceAsync` stays **public** so tests drive it without a timer, and
  resolves the scoped `TDbContext` inside an
  `IServiceScopeFactory.CreateScope()` — the singleton-resolving-scoped mistake
  `AuditRetentionHostedService.cs:70-73` documents.
- One statement:
  `DELETE FROM idempotency_key WHERE resource_identifier IS NULL AND reserved_at < NOW() - {0}`,
  with `IdempotencyReclamation.StaleAfter` as the parameter. **Both guards, or
  the sweep deletes completed keys and ADR-0142 is broken.**
- `Ensure.That(...)` guards per ADR-0105.
*Depends on:* T015.

**T017 [US3]** — `src/ServiceDefaults/Log.cs`: one `[LoggerMessage]` at
**Information**, emitted only when the swept count is non-zero, naming the
count. A zero-count sweep logs nothing — hourly x seven services x a Debug line
buys nothing. The comment says why Information: a swept reservation means an
attempt died somewhere, and that is worth a line an operator can grep.
*Depends on:* T016.

**T018 [US3]** — The registration extension in
`IdempotencyReservationSweep.cs`:
`AddIdempotencyReservationSweep<TDbContext>(this IServiceCollection)`, binding
the options section and calling
`AddHostedService<IdempotencyReservationSweepHostedService<TDbContext>>()`.
*Depends on:* T016.

**T019 [P] [US3]** — Seven one-line registrations, one per file, **each `[P]`
against the others**: add
`builder.Services.AddIdempotencyReservationSweep<XDbContext>();` immediately
below the existing `AddScoped<IIdempotencyStore, IdempotencyStore<XDbContext>>()`
line in

| File | Line today |
|---|---|
| `src/Automation/Infrastructure/AutomationInfrastructureModule.cs` | 45 |
| `src/CameraCatalog/Infrastructure/CameraCatalogInfrastructureModule.cs` | 72 |
| `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs` | 55 |
| `src/Identity/Infrastructure/IdentityInfrastructureModule.cs` | 94 |
| `src/LayoutComposition/Infrastructure/LayoutCompositionInfrastructureModule.cs` | 47 |
| `src/OverlayDesigner/Infrastructure/OverlayDesignerInfrastructureModule.cs` | 51 |
| `src/SystemVariables/Infrastructure/SystemVariablesInfrastructureModule.cs` | 55 |

**AuditObservability and StreamDistribution get nothing** — they register no
`IIdempotencyStore`, so they have no `idempotency_key` table and a sweep there
would be a worker over a table that does not exist.
*Depends on:* T018.

---

## Phase 5 — Verify (`verify`)

**T020** — Run `spec.md` §*Independent end-to-end test procedure* by hand
against the booted stack: the `psql` insert, the `409` before, the `201` after,
the identifier written, and **step 5** — the repeat that must replay rather
than create a second camera.

**Write the observed values into `verification.md`**, not only into the report.
A measurement reported only to the orchestrator is invisible to every later
grep.

Latency: **N/A**, and say so explicitly — no constitution §IV leg is on this
path (`spec.md` §*Latency-budget impact*).
*Depends on:* T008, T012, T019.

## Phase 6 — QA

**T021 [P]** — `backend-reviewer` over the full diff, against `plan.md`
§*Review focus for phase 6*: the `resource_identifier IS NULL` guard on **both**
new statements, `SET reserved_at = NOW()` present, the red test deriving
`caller` from the system rather than a literal, the pre-existing
`IdempotentRequestTests` cases unmodified, and nothing touching an endpoint
file, `IdempotencyScope`, or a migration.

**T022 [P]** — `security-reviewer`, narrowly: reclamation must not cross the
`caller` boundary (T006 is the test; confirm the SQL cannot), and no key,
secret or response body has been added to the row or to any log line.

## Phase 7 — PR

**T023** — `gh pr create --base develop`. Body carries: the verbatim T007 and
T011 failures, `Closes #2290`, the phase-5 figures, the **out-of-scope
follow-up** for `IdempotentRequest.cs:185, 189` (filed as its own issue, not
folded in), and the note that ADR-0142 needed no amendment with a pointer to
`spec.md` §*Why this is not a TTL*.
