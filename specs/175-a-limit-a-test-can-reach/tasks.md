# Spec 175 — Tasks

**Phase**: 3 (Tasks) · **Date**: 2026-09-17 · **Issue**: #2212
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Phase-4a colour**: **RED**, both stories. Behaviour-changing, so ADR-0139's
first obligation applies and there is no ambiguity to resolve. **A test here
that arrives green is a phase-4 failure, not a shortcut.**
**New ADR**: none expected. Writing one is a blocked outcome (ADR-0144).

---

## Declarations

| | |
|---|---|
| **Engineer** | `backend-engineer` |
| **Reviewer** | `backend-reviewer` (phase 6). Not security-sensitive: no auth, scope, secret or trust boundary changes; the limiter sits behind `RequireAuthorization` already. |
| **Phase-4a colour** | **RED**, explicit. Three reds (R1, R2, R3), each with its verbatim failure quoted in the PR body. |
| **Counterfactuals** | Sequencing red for R3 (T012 → T013), plus three defect injections at phase 5 (T016–T018). |
| **New ADR** | **No.** Spec §8 states why for each half. |
| **Latency budget** | **N/A**, no leg of §IV. Spec §7. |
| **Files phase 4 may touch** | The seven in plan.md §5. **Anything else is a stop-and-report.** |

### Files phase 4 may touch — restated here so it is not one click away

```
src/EventIngestion/Application/Ingress/IngestWriteOptions.cs                     (new)
src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs                     (line 29 only)
src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs          (:117 + near :137)
src/AppHost/AppHost.cs                                                           (one line, inside if (isE2ETests))
tests/EventIngestion.Application.Tests/Ingress/IngestWriteLimiterTests.cs        (append only)
tests/EventIngestion.Infrastructure.Tests/IngestWriteConcurrencyRegistrationTests.cs  (new)
tests/Integration.Tests/EventIngestion/IngestBackpressureIntegrationTests.cs     (new)
```

**`src/EventIngestion/Api/EventsEndpoints.Writes.cs` is read-only.** It is the
code under test. Editing it to make a test pass is editing the subject to fit
the measurement.

**The default stays 64.** `IngestWriteLimiter.DefaultConcurrency` at `:23` is
not touched, no `appsettings.json` key is added, and
`The_default_limiter_bounds_writes_at_sixty_four` passes **unmodified**. If you
find yourself wanting to change any of these, stop — that is a different issue
and the issue text says so in bold.

---

## US1 (P1) — The write limit can be set, and a bad setting fails loudly

Independently shippable. If the phase-3 gate drops US2, US1 ships alone with no
rework.

### Phase 4a — the reds

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T001** | | US1 | **AS-3, red.** Append two `[Fact]`s to `tests/EventIngestion.Application.Tests/Ingress/IngestWriteLimiterTests.cs`: `A_concurrency_of_zero_is_refused` and `A_negative_concurrency_is_refused`. `Should.Throw<ArgumentException>`, assert `ParamName == "concurrency"`. **Do not edit the four existing tests.** Run and capture the **verbatim** failure — today `new IngestWriteLimiter(0)` constructs cleanly, so the zero case must report "did not throw". | — |
| **T002** | `[P]` | US1 | **AS-1 + AS-2, red.** New file `tests/EventIngestion.Infrastructure.Tests/IngestWriteConcurrencyRegistrationTests.cs`, modelled on `IngestVolumeRegistrationTests.cs` in the same project (`Host.CreateEmptyApplicationBuilder(null)` + `AddInMemoryCollection` + `builder.AddEventIngestionInfrastructure()` + `BuildServiceProvider()`; host never started). Reuse its `Configuration` dictionary of five keys. **AS-1**: add `["EventIngestion:IngestWrite:Concurrency"] = "2"`, resolve `IngestWriteLimiter`, hold two leases, assert the third is refused. **AS-2**: no `IngestWrite` key, take 64 leases, assert all granted and the 65th refused. Run and capture the **verbatim** failure — AS-1 must fail (the third lease is granted), AS-2 must already pass. | — |

`[P]` on T002: disjoint file, disjoint project from T001. T001 and T002 can be
written in either order or together.

**Both reds are quoted in the PR body** (ADR-0139). The AS-1 failure is the one
that matters — it is the exact defect #2212 describes.

### Phase 4b — the wiring

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T003** | | US1 | Create `src/EventIngestion/Application/Ingress/IngestWriteOptions.cs`. `public sealed class`, `public const string SectionName = "EventIngestion:IngestWrite";`, `public int Concurrency { get; set; } = IngestWriteLimiter.DefaultConcurrency;`. Reference the constant, never the literal `64`. Doc comment says *why the value is configurable*, not what the property is. Mirror `IngestRetryOptions.cs` in the same folder. | T002 |
| **T004** | | US1 | Guard `src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs:29`: expression body becomes a block, `Ensure.That(concurrency).AtLeast(1);` then the assignment. Add `using SmartSentinelEye.Shared.Kernel;`. **No `.csproj` edit** — `Shared.Kernel` is already referenced (`...Application.csproj:5`). If an edit turns out to be needed, stop and report: the plan's file list would be wrong. | T001 |
| **T005** | | US1 | `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs`: add `builder.Services.AddOptions<IngestWriteOptions>().Bind(builder.Configuration.GetSection(IngestWriteOptions.SectionName));` beside the `IngestRetryOptions` binding at `:134-138`. Replace `AddSingleton<IngestWriteLimiter>()` at `:117` with a factory reading `IOptions<IngestWriteOptions>.Value.Concurrency`. **Keep the existing comment at `:114-116`** and extend it rather than replacing it — it records why the limiter exists at all. | T003 |
| **T006** | | US1 | Run `dotnet test tests/EventIngestion.Application.Tests/ tests/EventIngestion.Infrastructure.Tests/`. Capture **verbatim**. Expect: T001's two cases green, T002's AS-1 and AS-2 green, and **the four pre-existing `IngestWriteLimiterTests` green and unmodified** (`git diff` on that file shows additions only). | T004, T005 |
| **T007** | | US1 | Run `dotnet test tests/Architecture.Tests/`. Expect green and unchanged. A NetArchTest or `PrimitiveBoundaryTests` failure here is a **finding to report**, not a rule to adjust. | T005 |

---

## US2 (P2) — The 429 is proved off the real endpoint

**Gated.** See §Gate. If the reviewer reads issue comment 2 as an absolute bar,
T008–T015 are dropped and filed; nothing in US1 changes.

### The red sequence — mandatory, and not foldable into one step

This is the anti-pattern the issue names by hand. The order is the evidence.

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T008** | | US2 | Create `tests/Integration.Tests/EventIngestion/IngestBackpressureIntegrationTests.cs` — AS-5. `[Collection(AspireCollection.Name)]`, primary constructor `(AspireFixture aspire, ITestOutputHelper output)`. Client via `aspire.CreateAuthenticatedClientAsync("event-ingestion", …)`; body shape and run-unique `kind` from `DirectWriteHonestyIntegrationTests`. Eight `PostAsJsonAsync("/events/manual", …)` through **one** `Task.WhenAll`. Assert: at least one 429, at least one 201, and **every** 429's problem `title` is `EVENT_INGEST_BACKPRESSURE`. Append `aspire.RecentLogs("event-ingestion")` to failure messages. **No loop, no retry, no wall-clock bound** (spec §5.5). | T007 |
| **T009** | | US2 | **Red 1 — against the fully unwired state.** `git stash` the US1 production changes (or run T008 on a worktree at `origin/develop` + this test file). Run the test. **Expect red**: eight 201s, no 429. Capture **verbatim**. | T008 |
| **T010** | | US2 | **Red 2 — against US1 landed, AppHost line still absent.** Restore US1's production changes. Run the test again. **Expect still red**, for the same reason: the AppHost passes no value, so concurrency is still 64. Capture **verbatim**. **This observation is the point** — it proves the test measures the *delivered* value, not the existence of a config class. A green here means the test is wired to nothing: **stop and report**. | T009 |
| **T011** | | US2 | Add the line to `src/AppHost/AppHost.cs`, inside the **existing** `if (isE2ETests)` block at `:501-514`, on the `eventIngestion` local from `:405`: `eventIngestion.WithEnvironment("EventIngestion__IngestWrite__Concurrency", "1");`. Comment says why 1 and why only here. Nothing else in the file changes — **no new `if`, no new parameter in the fixture's array**. | T010 |
| **T012** | | US2 | **Green.** Re-run the integration test. Capture **verbatim**. All three captures (T009, T010, T012) go in the PR body. | T011 |
| **T013** | | US2 | Run the **whole** Aspire integration bucket, not just the new test. The E2E concurrency is now 1 stack-wide; spec §5.4 argues no existing test contends for it, and this is where that argument is checked rather than trusted. Pay attention to `EventTypeRegistryConcurrencyIntegrationTests`, `ManualIngestFabScopingIntegrationTests`, `MissingPayloadIsRefusedIntegrationTests` and `AnonymousIngestIsRefusedTests` (AS-6, AS-7). **A redden here is a finding: report it, do not add a retry.** | T012 |

---

## Phase 5 — verification and the defect-injection counterfactuals

A red from absent wiring proves the test notices the feature. It does not prove
the test asserts what it claims.

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T014** | | US2 | **CF-A — the title.** Change `EventsEndpoints.Writes.cs:348` to `title: "EVENT_INGEST_OVERLOAD"`. Re-run. **Predicted: R3 red on the title assertion, 429 status still present.** Capture verbatim, `git checkout -- src/`, confirm `git diff --stat src/` is empty. This is the injection that distinguishes "asserts the contract" from "asserts a status code". | T013 |
| **T015** | | US2 | **CF-B — the gate.** Drop the `slots.Wait(0)` gate in `IngestWriteLimiter.TryAcquire` so it always grants — `:36-37` today, but **find it by content**: T004's guard shifts every line below `:29`. **Predicted: R3 red (no 429), R2 red, R1 green.** Capture, revert, confirm clean. | T014 |
| **T016** | | US1 | **CF-C — the registration.** Revert `EventIngestionInfrastructureModule` to `AddSingleton<IngestWriteLimiter>()`. **Predicted: R2 red, R3 red, R1 green, AS-2 green.** The **asymmetry is the finding**: AS-2 cannot tell the two registrations apart, which is why AS-2 alone would not have caught this. Capture, revert, confirm clean. | T015 |
| **T017** | | | Compare every result against the prediction written in spec §6.3. **A mismatch is reported, never edited into agreement.** | T016 |
| **T018** | | | Run spec §9's independent end-to-end procedure against a dev-mode stack (`dotnet run --project src/AppHost`, no `E2ETests`). Six steps. **Write the observed figures into the verification note as observed** — a measurement reported only to the orchestrator is invisible to every later grep and reviewer. | T017 |
| **T019** | | | `git status --short` clean of anything outside the seven files in plan.md §5. | T018 |

**A running Aspire stack holds the service binaries** — stop it before building,
or MSB3027 will read as a broken build. And **one machine, one Aspire stack**:
do not boot a second while the session's is running.

---

## Parallelism (ADR-0109)

Almost none within this slice, and that is honest rather than a failure to look.

- **`[P]` T002** — disjoint file *and* disjoint project from T001.
- Everything else is a chain: the options class feeds the registration, the
  registration feeds the reds, and US2's whole value is the **order** T009 →
  T010 → T011 → T012. Parallelising that would destroy the evidence.

**Nothing here is foundational.** No `Shared.Kernel`, `Shared.Contracts` or
Aspire-resource change blocks other work, so the orchestrator can fan out other
issues freely alongside this one. The only shared file is `src/AppHost/AppHost.cs`
(one line, T011) — worth a contention check against any concurrent branch
touching the AppHost.

---

## Out of scope — file, do not implement

| # | Item | Why not here |
|---|---|---|
| **F1** | **Is 64 right for production on a 250-camera target?** The issue raises it and then excludes it. | A sizing decision needing a measurement, not a testability fix. "A behaviour change smuggled into a testability fix would be two issues." |
| **F2** | **Startup-time options validation** (`ValidateOnStart`) repo-wide. There is not one call site in `src/`, so a configured `0` throws on first use rather than at boot. | A repo-wide convention; inventing it for one options class is the speculative generality ADR-0036 forbids. |
| **F3** | **The limiter's acquisition timeout is untested.** Spec 104's review found `slots.Wait(50)` passes all four existing unit tests. | Spec 104's uncovered clause; closing it changes the gate, which is a behaviour change. |
| **F4** | **The webhook path's 429 has no integration test** (`EventsEndpoints.Writes.cs:171`). | Same two lines, different auth shape. Recorded so the absence reads as a decision (plan §6.4). |
| **F5** | **#2211 — the MQTT path's `Wait`.** | Already filed; a design question with no ADR, and the lane may not write one. |

---

## Gate (phase 3)

**Two things the reviewer must settle before phase 4 starts.**

1. **Does US2 land here, or get filed?** Issue #2212's body permits the
   integration test *with a red*; its comment 2 calls it "explicitly out of
   scope". Spec §0 records both readings in full. This spec includes US2 behind
   the mandatory red sequence (T009/T010/T012), which satisfies the body's
   condition literally. **If the reviewer prefers comment 2's stricter
   reading**, drop T008–T016 and file them: US1 needs no rework and only the
   `AppHost.cs` line goes with them.
2. **Confirm the file list** in plan.md §5 and the read-only status of
   `EventsEndpoints.Writes.cs`.

**Board:** #2212 is already on Project #13 (status Todo, `agent:ready`). **No
per-task issues** — the repo stopped creating those after spec 028, and this
`tasks.md` is the artifact the work is tracked against.
