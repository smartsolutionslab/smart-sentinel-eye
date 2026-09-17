# Spec 175 — Tasks

**Phase**: 3 (Tasks) · **Date**: 2026-09-17 · **Issue**: #2212
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Follow-up**: **#2441** — the integration test, split out at this gate. Not here.
**New ADR**: none expected. Writing one is a blocked outcome (ADR-0144).

---

## Declarations

| | |
|---|---|
| **Engineer** | `backend-engineer` |
| **Reviewer** | `backend-reviewer` (phase 6). Not security-sensitive: no auth, scope, secret or trust-boundary change; the limiter already sits behind `RequireAuthorization`. |
| **Phase-4a colour** | **RED overall**, declared per piece — see below. |
| **Counterfactuals** | Two defect injections at phase 5 (T010, T011). |
| **New ADR** | **No.** Spec §8 states why for each half. |
| **Latency budget** | **N/A**, no leg of §IV. Spec §7. |
| **Files phase 4 may touch** | The five below. **Anything else is a stop-and-report.** |

### Phase-4a colour, per piece (ADR-0144)

**Do not collapse these into one colour.** Spec §6.2 explains why that
particular mistake is expensive here.

| Piece | Colour | Obligation |
|---|---|---|
| **AS-1** — a configured value bounds the registered limiter (**R2**) | **RED** | New behaviour. Must be observed failing first. **The load-bearing red.** |
| **AS-3** — zero/negative refused at construction (**R1**) | **RED** | New behaviour. `new IngestWriteLimiter(0)` constructs cleanly today. |
| **AS-2** — the default is still 64, through the registration | **Characterisation, GREEN** | Behaviour-preserving. Captured passing **before** the change; must pass **unmodified** after. |
| **AS-4** — the four existing `IngestWriteLimiterTests` | **Characterisation, GREEN** | Unmodified. An assertion that has to be edited is evidence the behaviour moved: **block, don't adjust.** |

### Files phase 4 may touch

```
src/EventIngestion/Application/Ingress/IngestWriteOptions.cs                          (new)
src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs                          (line 29 only)
src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs               (:117 + near :137)
tests/EventIngestion.Application.Tests/Ingress/IngestWriteLimiterTests.cs             (append only)
tests/EventIngestion.Infrastructure.Tests/IngestWriteConcurrencyRegistrationTests.cs  (new)
```

**`src/EventIngestion/Api/EventsEndpoints.Writes.cs` is read-only.** It is the
code under test. Editing it to make a test pass is editing the subject to fit
the measurement.

**`src/AppHost/AppHost.cs` is out of scope.** The `if (isE2ETests)` override
line belongs to #2441. If you find yourself editing an AppHost file, you are
doing #2441's work.

**The default stays 64.** `IngestWriteLimiter.DefaultConcurrency` at `:23` is
not touched, no `appsettings.json` key is added, and
`The_default_limiter_bounds_writes_at_sixty_four` passes **unmodified**. The
issue says so in bold and T011 turns it into an enforced constraint rather than
an instruction.

---

## Phase 4a — the reds (test-writer)

**Tests only. Run them. Return the verbatim output.** The engineer receives that
output as its brief and **may not edit the tests to pass** (ADR-0144).

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T001** | | US1 | **AS-3, red.** Append two `[Fact]`s to `tests/EventIngestion.Application.Tests/Ingress/IngestWriteLimiterTests.cs`: `A_concurrency_of_zero_is_refused` and `A_negative_concurrency_is_refused`. `Should.Throw<ArgumentException>`, assert `ParamName == "concurrency"` so the test pins the diagnosis and not merely the throw. **Do not edit the four existing tests.** Run; capture **verbatim**. The zero case must report that it did not throw. | — |
| **T002** | `[P]` | US1 | **AS-1 red + AS-2 green.** New file `tests/EventIngestion.Infrastructure.Tests/IngestWriteConcurrencyRegistrationTests.cs`, modelled on `IngestVolumeRegistrationTests.cs` in the same project: `Host.CreateEmptyApplicationBuilder(null)` + `AddInMemoryCollection` + `builder.AddEventIngestionInfrastructure()` + `BuildServiceProvider()`, host never started, nothing dialled. Reuse its `Configuration` dictionary of five keys. **AS-1**: add `["EventIngestion:IngestWrite:Concurrency"] = "2"`, resolve `IngestWriteLimiter`, hold two leases, assert the third is refused. **AS-2**: no `IngestWrite` key, take 64 leases, assert all granted and the 65th refused. Run; capture **verbatim**. **AS-1 must fail; AS-2 must already pass** — that split is the evidence, so report it explicitly rather than as "2 failed". | — |

`[P]` on T002: disjoint file, disjoint project from T001.

**AS-2 takes 64 leases rather than asserting `IngestWriteOptions.Concurrency == 64`.**
Slower, and that is the point: the property default cannot distinguish a factory
registration that reads it from one that ignores it (spec §6.3 CF-A).

---

## Phase 4b — the wiring (backend-engineer)

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T003** | | US1 | Create `src/EventIngestion/Application/Ingress/IngestWriteOptions.cs`. `public sealed class`, `public const string SectionName = "EventIngestion:IngestWrite";`, `public int Concurrency { get; set; } = IngestWriteLimiter.DefaultConcurrency;`. **Reference the constant, never the literal `64`.** Doc comment says *why the value is configurable*, not what the property is. Mirror `IngestRetryOptions.cs` in the same folder. | T002 |
| **T004** | | US1 | Guard `src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs:29`: the expression body becomes a block — `Ensure.That(concurrency).AtLeast(1);` then the assignment. Add `using SmartSentinelEye.Shared.Kernel;`. **No `.csproj` edit** — `Shared.Kernel` is already referenced (`...Application.csproj:5`) and `Ensure.That` is used across that project's handlers. If an edit turns out to be needed, **stop and report**: the plan's file list would be wrong. | T001 |
| **T005** | | US1 | `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs`: add `builder.Services.AddOptions<IngestWriteOptions>().Bind(builder.Configuration.GetSection(IngestWriteOptions.SectionName));` beside the `IngestRetryOptions` binding at `:134-138`. Replace `AddSingleton<IngestWriteLimiter>()` at `:117` with a factory reading `IOptions<IngestWriteOptions>.Value.Concurrency`. **Keep the existing comment at `:114-116` and extend it** rather than replacing it — it records why the limiter exists at all. | T003 |
| **T006** | | US1 | Run `dotnet test tests/EventIngestion.Application.Tests/ tests/EventIngestion.Infrastructure.Tests/`. Capture **verbatim**. Expect: T001's two cases green, T002's AS-1 **and** AS-2 green, and the four pre-existing `IngestWriteLimiterTests` green. | T004, T005 |
| **T007** | | US1 | `git diff tests/EventIngestion.Application.Tests/Ingress/IngestWriteLimiterTests.cs` shows **additions only**. A modified or deleted line in the existing four is a weakened gate (ADR-0144) and a **stop**, not a fix. | T006 |
| **T008** | | US1 | Run `dotnet test tests/Architecture.Tests/`. Expect green and unchanged. A NetArchTest or `PrimitiveBoundaryTests` failure here is a **finding to report**, not a rule to adjust. | T005 |
| **T009** | | US1 | Build Release (`dotnet build -c Release`) so the analyzer set that only fires there is exercised — `dotnet_style_prefer_collection_expression` at `warning` and the SonarAnalyzer metrics (ADR-0084). **Stop the Aspire stack first**; a running AppHost holds the service binaries and MSB3027 reads exactly like a broken build. | T006 |

---

## Phase 5 — verification and the defect injections

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T010** | | US1 | **CF-A — the registration.** Revert the factory in `EventIngestionInfrastructureModule` to `AddSingleton<IngestWriteLimiter>()`. **Predicted: R2 red, R1 green, AS-2 green.** The **asymmetry is the finding** — AS-2 cannot tell the two registrations apart, so AS-2 alone would never have caught this. Capture verbatim, `git checkout -- src/`, confirm `git diff --stat src/` is empty. | T009 |
| **T011** | | US1 | **CF-B — the default.** Change `IngestWriteOptions`'s initialiser from `IngestWriteLimiter.DefaultConcurrency` to a literal `32`. **Predicted: AS-2 red, R1 green, R2 green.** This is what makes "keep the default at 64" an enforced constraint rather than an instruction. Capture, revert, confirm clean. | T010 |
| **T012** | | | Compare every result against the prediction in spec §6.3. **A mismatch is reported, never edited into agreement.** | T011 |
| **T013** | | | Run spec §9's six-step procedure against a dev-mode stack (`dotnet run --project src/AppHost`). Step 5 — the guard producing a named `ArgumentException` in the `event-ingestion` log rather than a silent 429 — is the one that distinguishes this change from the one the issue warned about. **Write the observed results into the verification note as observed**; a measurement reported only to the orchestrator is invisible to every later grep and reviewer. | T012 |
| **T014** | | | `git status --short` clean of anything outside the five files above. | T013 |

**One machine, one Aspire stack.** A stack is already running in this session
(pid 3312) — do not boot a second; two concurrent boots give `FailedToStart`
that reads exactly like a code defect.

---

## Parallelism (ADR-0109)

Almost none within this slice, and that is honest rather than a failure to look.

- **`[P]` T002** — disjoint file *and* disjoint project from T001.
- Everything else is a chain: the options class feeds the registration, and the
  registration is what the reds are red against.

**Nothing here is foundational.** No `Shared.Kernel`, `Shared.Contracts` or
Aspire-resource change blocks other work, and **no file outside `EventIngestion`
is touched at all** — so the orchestrator can fan out other issues freely
alongside this one with no contention check needed.

---

## Out of scope — already filed, or to file

| # | Item | Where |
|---|---|---|
| **#2441** | **The integration test off the real endpoint**, with the AppHost `isE2ETests` override. Carries the full design: the two-step red, CF-A's problem-title injection, and the non-determinism framing. | **Filed**, `agent:ready`, on Project #13. Blocked by this spec. |
| **F1** | **Is 64 right for production on a 250-camera target?** The issue raises it and then excludes it. | To file. A sizing decision wanting a measurement — *"a behaviour change smuggled into a testability fix would be two issues."* |
| **F2** | **Startup-time options validation** (`ValidateOnStart`) repo-wide. Not one call site exists in `src/`, so a configured `0` throws on first use rather than at boot. | To file. A repo-wide convention; inventing it for one options class is the speculative generality ADR-0036 forbids. |
| **F3** | **The limiter's acquisition timeout is untested.** Spec 104's review found `slots.Wait(50)` passes all four existing unit tests. | To file. Spec 104's uncovered clause; closing it changes the gate, which is a behaviour change. |
| **F4** | **#2211 — the MQTT path's `Wait`.** | Already filed. A design question with no ADR, and the lane may not write one. |

---

## Gate (phase 3)

**Scope settled.** #2212's own scope note (2026-09-08) is authoritative: two
things — the config knob and the guard — shipping together, with the integration
test explicitly out of scope. That test is now **#2441**, `agent:ready`, on
Project #13, blocked by this work.

**Board:** #2212 is on Project #13 (Todo, `agent:ready`). **No per-task issues** —
the repo stopped creating those after spec 028, and this `tasks.md` is the
artifact the work is tracked against.

**Phase 4 dispatch:** 4a is `test-writer` (T001–T002, tests only, verbatim output
returned); 4b is `backend-engineer` (T003–T009), briefed with that output.
