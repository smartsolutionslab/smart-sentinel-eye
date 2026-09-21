# Tasks 208 — A ceiling the hook never had

**Spec:** `specs/208-a-ceiling-the-hook-never-had/spec.md` · **Plan:** `specs/208-a-ceiling-the-hook-never-had/plan.md`
**Issue:** #2284 · **Branch:** `2284-stream-authorize-rate-limit`

**Format:** `[ID] [P?] [Story]` — `[P]` marks tasks that own **disjoint files** and may run in parallel (ADR-0109).

**Phase-4a colour: RED, every story.** T003–T006 are written and **observed failing first**; their verbatim output is the brief for T007+ and is quoted in the PR body (ADR-0139, ADR-0144). The engineer may not edit those tests to make them pass.

---

## Ordering at a glance

```
T001 ─┐                          (config + AppHost ceiling: foundational,
T002 ─┤                           every test below reads the small ceiling)
      ▼
T003 [P] ─ T004 [P] ─ T005 [P] ─ T006 [P]     RED — write, run, quote
      ▼
T007 ─ T008 ─ T009                            US1 implementation
      ▼
T010 [P]   T011 [P]   T012 [P]                US1 prose / US2 / US3
      ▼
T013 ─ T014                                   full suite + verification note
```

**T001 and T002 block everything.** They are the only foundational tasks: the configuration section and the integration-lane ceiling override. Without T002 the red tests in T003–T005 would need 2001 requests each and would poison their neighbours' window (plan §*Test-mode ceiling*), so they cannot be written first in a form that survives. The orchestrator can fan out T003–T006 the moment T002 lands, and T010–T012 the moment T009 lands.

---

## Foundational — blocks everything

- [ ] **T001 [US1]** Add the configuration section to `src/StreamDistribution/Api/appsettings.json`:
  `"WhepAuthorizeRateLimiting": { "PermitLimit": 2000, "Window": "00:01:00" }`.
  Mirror `src/ApiGateway/appsettings.json:20-24`'s shape exactly. Nothing else in the file changes.
  *Derivation of 2000 is spec §Sizing the ceiling — do not adjust the number without redoing it.*

- [ ] **T002 [US1]** In `src/AppHost/AppHost.cs`, add one `if (isE2ETests)` block after the `streamDistribution` resource (`:386-403`) setting
  `WhepAuthorizeRateLimiting__PermitLimit=20` and `WhepAuthorizeRateLimiting__Window=00:00:10` on it.
  Comment must say *why* the gate is `isE2ETests` and not `isRunMode` — the flag is set by `AspireFixture` and not by `ci.yml`'s plain `dotnet run` e2e boot, so the integration lane gets a testable ceiling while the e2e wall runs the production one (`AppHost.cs:211-216` documents the asymmetry).
  Then **run `AppHostE2ESwitchTests`** (6 cases) and confirm still green — they assert resources and volumes, not environment, but verify rather than assume.

---

## Phase 4a — RED. Write, run, observe failing, quote verbatim.

All four are new files or new cases in existing files that **no other task touches**, so they are `[P]`.

- [ ] **T003 [P] [US1]** `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs` (new file, `[Collection(AspireCollection.Name)]`) —
  **`Authorize_refuses_a_source_over_its_window_with_429`**. Post `PermitLimit + 1` requests to `/streams/authorize` (no token — the cheapest admitted shape) and assert the last is `429`.
  Mirror `GatewayRateLimitIntegrationTests.cs`' loop-until-target shape (`:28-37`) rather than inventing one.
  **Expected red:** every request answers `401`; the assertion fails on `Unauthorized` vs `TooManyRequests`.
  Finish by **waiting for the condition** that a request is admitted again (window elapsed) before the test returns, so the next test in the collection is not poisoned — never a fixed sleep and never a count (constitution §Testing).

- [ ] **T004 [P] [US1]** Same file — **`Authorize_from_a_second_source_is_unaffected_by_an_exhausted_window`**.
  The one scenario that distinguishes this design from the rejected global bucket. It **cannot be red today** (nothing is ever throttled, so it passes trivially) — write it, and in the PR body label it a *standing guard*, not red evidence. Do not present it as satisfying the phase-4a gate.
  *If varying the source address from the test host proves impossible, assert the partition-key shape instead and say so explicitly — do not silently drop the scenario.*

- [ ] **T005 [P] [US1]** Same file — **`A_throttled_authorize_never_reaches_the_handler`** (FR-002).
  Assert by the **absence of the handler's own log record** for the refused attempts (read through the fixture's log tail), not by a mock — the point is that middleware short-circuits before any crypto or repository work.
  **Expected red:** today every request reaches the handler and logs accordingly.

- [ ] **T006 [P] [US1]** Same file — **`Authorize_declares_the_429_it_can_answer`** (FR-007) and **`Health_and_readiness_are_never_throttled`** (FR-005, US1 scenario 7): exhaust the window, then call `MapDefaultEndpoints`' health/readiness and the other three `/streams` routes and assert they answer normally.
  **Expected red** for the declaration case: the chain declares no `429`.

> **Gate:** all four run, their failures are captured verbatim, and that text is the brief handed to T007. A green arrival on T003, T005 or T006 is a phase-4 failure — investigate, do not proceed.

---

## Phase 4b — US1 implementation (sequential: one file each, in dependency order)

- [ ] **T007 [US1]** `src/StreamDistribution/Api/Program.cs` — register the named `"whep-authorize"` fixed-window policy and add `app.UseRateLimiter()`.
  - Read limits from `WhepAuthorizeRateLimiting:PermitLimit` / `:Window` with the gateway's `GetValue<T?>(...) ?? default` idiom (`ApiGateway/Program.cs:30-31`).
  - Partition: `RateLimitPartition.GetFixedWindowLimiter($"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}", …)`; `PermitLimit`, `Window`, `QueueLimit = 0`; `options.RejectionStatusCode = StatusCodes.Status429TooManyRequests`.
  - `UseRateLimiter()` goes **immediately after `app.UseExceptionHandler()`**, before `UseAuthentication()`, so a throttled request does no authentication work either.
  - Comments say *why the key is the remote address and not a global bucket* (a global bucket lets an anonymous caller deny MediaMTX) and *why not `X-Fab`* (caller-invented on an anonymous route). One or two lines each; the reasoning lives in the spec, not in the file.

- [ ] **T008 [US1]** `src/StreamDistribution/Api/StreamEndpoints.cs` — on the `/authorize` mapping only (`:63-80`), add `.RequireRateLimiting("whep-authorize")` and `.ProducesProblem(StatusCodes.Status429TooManyRequests)`.
  **Attached to the mapping, never to the `MapGroup`.** Extend the existing `.WithSummary` with one clause naming the ceiling, in the voice already there. Match `EventsEndpoints.cs:50-53`'s pattern for declaring a limiter's `429`.

- [ ] **T009 [US1]** Run T003–T006. They must now be green **unmodified**. Any assertion that needs editing means behaviour moved beyond the added status — stop and report, do not adjust (constitution §Testing).

---

## Parallel finishing work — disjoint files, fan out after T009

- [ ] **T010 [P] [US1]** `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs` — **comments only** (FR-008). Correct `:142-143` and `:210-212`, which currently justify their mitigations by *"nothing rate-limits it"*. Re-state each against the ceiling that now exists, citing spec 208, and keep the mitigation's own reasoning intact — both are still correct, for a narrower reason.
  Then run `WhepValidatorRefreshRestraintTests` + `WhepValidatorUnreachableRealmTests` and confirm green: a comment-only change must move nothing.
  *No behaviour may change in this file. If a test moves, the edit was not comment-only.*

- [ ] **T011 [P] [US2]** `src/StreamDistribution/Api/Program.cs` is already owned by T007, so this lands **after** it — the `OnRejected` callback plus a `[LoggerMessage]` source-generated method (ADR-0050) for the record.
  **Transition-only per partition**, via the `Interlocked`-guarded pattern at `WhepAuthValidator.cs:142-151` — one `Warning` on entering the throttled state, nothing on the repeats (FR-009; ADR-0118's single sink must stay readable at exactly the moment it would otherwise drown).
  Structured fields: the partition key and the configured limit. **Never the bearer token or any part of the body.**
  No metric: ASP.NET Core already emits `aspnetcore.rate_limiting.*` and a second instrument would be speculative generality.
  Paired red test (write first): `A_partition_entering_the_throttled_state_is_logged_once` in T003's file, plus the flood case asserting the repeats are silent.
  *Marked `[P]` against T010/T012 — it shares no file with either — but it is sequenced after T007.*

- [ ] **T012 [P] [US3]** `tests/Architecture.Tests/StatusProducerDeclarationTests.cs` — add `M15` to the `Census` array (`:158-183`) for this limiter: `"429"`, `Visibility.Chain` (**not** M14's `Visibility.None` — this one *is* declared on the chain, per T008).
  The row's comment records the precondition from spec §*Partition key*: the key's integrity rests on nothing in `src` configuring `ForwardedHeaders`, and a later `UseForwardedHeaders` without `KnownProxies` silently unbounds it.
  **Do not touch** `RouteHandlerMappingCount = 60` or `EndpointFileCount = 13` — no route and no endpoint file is added. If either needs to move, something outside this design was changed.
  Observe the census assertion red before the row and green after.

---

## Close-out

- [ ] **T013** Full gate: `dotnet format` clean, Release build clean (analyzers are errors in Release — collection expressions at `warning`, ADR-0084's metrics carved out), full unit + integration suite, `BoundaryTests` / `PrimitiveBoundaryTests` / `HandlerDeconstructionTests` green, coverage gates held (ADR-0065: Domain ≥ 90, Application ≥ 80, Shared ≥ 90 — **none of those three layers is touched**, so a movement there is a signal, not noise), and `WhepHandshakeLatencyTests` green with its figure noted for NFR-001.

- [ ] **T014** Phase 5 — `verification.md` in this directory, recording each of the five observations in plan §*Phase 5*, **including the figures**:
  1. the `429` and the single log record on the running stack;
  2. a real wall's tiles all live at the **production** ceiling, nothing throttled;
  3. **SC-005** — the observed count of authorize POSTs per single WHEP open (assumed 1; if > 2, revisit T001's default before merge);
  4. what MediaMTX does with a `429` (#2160's unobserved territory for `5xx`);
  5. the handshake-latency figure.
  Write every number down. A measurement reported only to the orchestrator is invisible to every later grep and reviewer.

---

## Dependencies

| Task | Depends on | Notes |
|---|---|---|
| T001, T002 | — | foundational; block all |
| T003–T006 | T002 | `[P]` with each other (new file, separate cases) |
| T007 | T003–T006 observed red | |
| T008 | T007 | |
| T009 | T008 | |
| T010, T012 | T009 | `[P]` — disjoint files |
| T011 | T007, T009 | `[P]` against T010/T012 |
| T013 | T010–T012 | |
| T014 | T013 | phase 5 |

## Phase-6 note for the orchestrator

**`security-reviewer` is mandatory on this PR regardless of diff size**, and should be asked specifically to attack: the partition key, the `2000/min` ceiling, the unthrottled surface, and whether the limiter can be made to deny MediaMTX. Same reasoning as spec 207 / #2285 — a small diff that is the only control on an anonymous, high-value endpoint.

## Two things to report, not fix

1. **`IngestWriteLimiter` has no census row.** `src/EventIngestion/Api/EventsEndpoints.Writes.cs:344-350` answers `429` from a handler body and the census's only `429` entry (M14) names the gateway limiter. A real pre-existing gap in the register, in another context's files — file it, do not fold it in.
2. **#2092 carries a live enumeration oracle in the lines it already owns.** `AuthorizeWhepCommandHandler.cs:85-92` returns `200` for an unregistered path and `403 WHEP_STREAM_UNAVAILABLE` for a registered-but-offline one, so any valid `sse.streams.read` token can probe the camera id-space. Worth a comment on #2092 (which is `agent:blocked` pending an ADR); not this spec's to fix.
