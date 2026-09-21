# Verification — Spec 208 (#2284)

**T013/T014, re-run against worktree `D:/Github/sse-2284`, branch `2284-stream-authorize-rate-limit`, HEAD `c79e2fbf` — after phase-6 review's three blockers and eleven should-fix items all landed.** This file replaces the version written at `360a0fce` (pre-fix), which phase-6 code review correctly flagged as stale and contradicting the branch's actual state (BLOCKER 3): it reported `WhepHandshakeLatencyTests` FAILED, the rate-limit class 2/7, and both root-cause defects "neither fixed here." All three claims are corrected below by genuinely re-running everything, not by editing the old numbers.

**What changed since `360a0fce`, in commit order:**

| Commit | What |
|---|---|
| `5ab3d23b` | BLOCKER 1 — gateway no longer proxies `/streams/authorize` |
| `d89269fd` | BLOCKER 2 — spec.md FR-010/US3 and tasks.md T012 corrected (M15 is documentation, not a guarded gate) |
| `b7465993` | S3 — third stale "nothing rate-limits it" comment corrected |
| `f8825f29` | S5 — bounded `IMemoryCache` replaces the never-evicted `ConcurrentDictionary`; S13 — `Ensure.That` guards on `PermitLimit`/`Window` at startup |
| `c79e2fbf` | S4, S6, S7, S8, S9, S10 — `WhepAuthorizeRateLimitTests.cs` hardening (comment-stripped source scan + negative shape check, sentinel-anchored log counting, asserted preconditions, `PermitLimit` 30→50 with full accounting, documented `WaitUntilAdmittedAgainAsync` reasoning) |

Machine state during this pass: ~6.1 GB free of 23.8 GB (same order of memory pressure as the original pass — Rider/Docker/browser resident). Two independent Aspire-fixture boots were run rather than one, specifically to re-confirm the class is not flaky under this pressure — see §T013.

---

## T013 — full gate

| Check | Result |
|---|---|
| `dotnet build SmartSentinelEye.slnx -c Release` | **Clean. 0 Warning(s) treated as errors, 0 Error(s)** on the incremental build. A full `-c Release` rebuild of the whole solution (after `git status` confirmed no stray `obj`/`bin` drift) surfaced **217 warnings, all `S104`/`S107`/`S138`/`S1541`** — ADR-0084's advisory code-metric analyzers, `warning`-severity and explicitly carved out of Release's `TreatWarningsAsErrors` (CLAUDE.md's own table). None of them are new to this branch's touched files beyond the three already-known `StreamEndpoints.cs` and `AppHost.cs` items the prior pass also recorded. 0 errors. |
| `Architecture.Tests` | **444/444 passed**, 6 s. Same count as the pre-fix pass — M15's phase-6 comment correction (BLOCKER 2) added prose only, no new census-derivation test, so the count is unchanged, as expected. |
| `WhepValidatorRefreshRestraintTests` + `WhepValidatorUnreachableRealmTests` | **11/11 passed**, 6 s — confirms S3's comment-only fix (the third stale "nothing rate-limits it" instance, in this file's own doc comment) moved no behaviour. |
| `WhepHandshakeLatencyTests` | **PASSED.** `Whep_auth_hook_p95_stays_under_three_seconds_over_twenty_opens`: **p50 = 25 ms, p95 = 80 ms, max = 132 ms** (budget 3000 ms). This is the test that failed deterministically at `360a0fce` under the test-mode `PermitLimit=20` (zero headroom against its own 21-call pattern). S8's `PermitLimit=50` — sized against all three consumers of the shared partition, not just this test — gives it real margin; it passed clean on the first attempt in this pass. |
| `WhepAuthIntegrationTests` | **6/6 passed** (not 7 — see note below). |
| Full `WhepAuthorizeRateLimitTests` class (7 facts), run twice as instructed | **7/7 passed both times**, independently. See §2 below. |
| Coverage gates (Domain ≥ 90 / Application ≥ 80 / Shared ≥ 90) | **Not run — by design, same reasoning as the pre-fix pass.** `git diff --stat 1788d773...HEAD` (the whole branch, all nine commits) touches only `src/ApiGateway/Program.cs`, `src/AppHost/AppHost.cs`, `src/StreamDistribution/Api/{Program.cs,StreamEndpoints.cs,appsettings.json,Log.cs}`, `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs` (comments only), `tests/Architecture.Tests/StatusProducerDeclarationTests.cs`, `tests/Integration.Tests/**`, `tests/StreamDistribution.Infrastructure.Tests/**`, and this spec's own docs. None of Domain, Application or Shared.* is in that list. Movement in those gates would be a signal this diff shouldn't produce; none is expected. |

**Note on the "seven" in spec.md SC-003:** SC-003 and this file's own pre-fix version both said "the seven existing `WhepAuthIntegrationTests` cases." Reading `tests/Integration.Tests/StreamDistribution/WhepAuthIntegrationTests.cs` at `c79e2fbf` counts **six** `[Fact]` methods, each posting once to `/streams/authorize`. Recorded here rather than silently matched to the wrong number — the same discipline this delivery applied to FR-010/US3 (BLOCKER 2) and the three stale comments (S3, `9a7597d5`). SC-003's own text is out of this pass's corrected scope (it wasn't named in the review brief), so it is flagged here, not edited; the count used in S8's `AppHost.cs`/`WhepAuthorizeRateLimitTests.cs` accounting is the observed **6**, not the stated 7.

### §2 — `WhepAuthorizeRateLimitTests`, run twice, independently

**Run 1** — alongside `WhepHandshakeLatencyTests` and `WhepAuthIntegrationTests` in one Aspire-fixture boot (`--filter "FullyQualifiedName~WhepHandshakeLatencyTests|FullyQualifiedName~WhepAuthIntegrationTests|FullyQualifiedName~WhepAuthorizeRateLimitTests"`):

```
Total tests: 14
     Passed: 14
```

All 7 `WhepAuthorizeRateLimitTests` facts passed: `A_partition_entering_the_throttled_state_is_logged_once` (18 s), `Health_and_readiness_are_never_throttled` (20 s), `A_throttled_authorize_never_reaches_the_handler` (20 s), `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record` (19 s), `Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket` (12 ms), `Authorize_declares_the_429_it_can_answer` (187 ms), `Authorize_refuses_a_source_over_its_window_with_429` (19 s). The running log showed the S5 fix operating directly: `Partition ip:::1 entered the throttled state for policy whep-authorize (limit 50).` A transient `postgres_check` health-check `OperationCanceledException` appeared in the background noise (the same memory-pressure-adjacent instability the pre-fix pass recorded) but did not touch any test outcome — `Test Run Successful`, exit code 0.

**Run 2** — a second, independent Aspire-fixture boot, `WhepAuthorizeRateLimitTests` alone alongside `GatewayRoutingIntegrationTests` (chosen to also re-confirm BLOCKER 1's new gateway test in the same pass):

```
Total tests: 19
     Passed: 19
```

The same 7 facts, same names, all passed again — `A_partition_entering_the_throttled_state_is_logged_once` (20 s), `Health_and_readiness_are_never_throttled` (20 s), `A_throttled_authorize_never_reaches_the_handler` (20 s), `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record` (19 s), `Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket` (10 ms), `Authorize_declares_the_429_it_can_answer` (242 ms), `Authorize_refuses_a_source_over_its_window_with_429` (19 s). The 12 `GatewayRoutingIntegrationTests` cases also passed, including the new `Gateway_does_not_forward_the_anonymous_whep_authorize_hook` (33 ms) — BLOCKER 1's fix confirmed against the real running gateway, not just read from source. Some `minio_check` background health-check `TaskCanceledException` noise appeared again during this run (same class of transient container-health flakiness as run 1 and the original pre-fix pass); again it touched no test outcome.

**This directly falsifies the pre-fix verification's "2/7 both times, root cause identified" finding — as expected, since that finding correctly diagnosed the shared-circuit-breaker cascade (`3a17b146`) and the sentinel/window-isolation gaps (`ed609b38`) that were fixed before this pass began, and this round's S4/S6/S7/S8/S9/S10 hardening on top of those fixes.** Two independent stack boots, 7/7 both times, is the same reproducibility bar the pre-fix pass used to establish the opposite result — the mechanism changed, not the measurement discipline.

Also confirmed clean in this pass: `AppHostE2ESwitchTests` (6/6, 447 ms — no Aspire boot needed, confirms S8's `AppHost.cs` `PermitLimit=50` edit disturbed none of its resource/volume assertions).

---

## T014 — Phase 5 observations

Unchanged in structure from the pre-fix pass — plan.md §*Phase 5* asks for five observations, each with a figure written down. Observation 1 is re-confirmed at current HEAD; observations 2–4 remain genuinely out of reach on this machine today, exactly as the pre-fix pass recorded, and are **not** re-attempted or fabricated here per this task's explicit brief. Observation 5's figure is new, obtained cleanly (see T013 above) now that the defect blocking it is fixed.

### 1. The ceiling fires on the running stack, and exactly one log record is emitted

**Re-confirmed, on the real Aspire-orchestrated stack, twice independently** (§2 above). `A_partition_entering_the_throttled_state_is_logged_once` passed both times (18 s, then 20 s), and the live log line was observed directly: `Partition ip:::1 entered the throttled state for policy whep-authorize (limit 50).` — one record, not one per refused request. FR-009/US2 behaviour confirmed against the running service.

### 2. A real wall at the production ceiling — nothing throttled

**Still not completed. Still an open item, not fabricated.** Same reasoning as the pre-fix pass: this needs the full run-mode stack (`dotnet run` on AppHost, production `PermitLimit=2000`) plus a real wall opening tiles, and is materially heavier than the Aspire-fixture integration boots this pass ran twice — both of which already showed real, if non-fatal, container-health noise (`postgres_check`, `minio_check`) on this machine's ~6.1 GB-free state. The inference the pre-fix pass drew still holds and is repeated, not re-derived: the production ceiling (2000) has no interaction with the known request-count patterns this pass exercised (the largest, `WhepAuthIntegrationTests` + `WhepHandshakeLatencyTests` + this class's own exhaustion, is a few dozen requests, ≪ 2000), so nothing in this pass's findings changes the risk profile of this open item. Flagged for a follow-up run once the machine is free or on a dedicated box.

### 3. SC-005 — authorize POSTs per single WHEP open

**Still not completed — open item, unchanged.** Requires the same run-mode stack as #2 with MediaMTX actually negotiating a WHEP session. Not attempted for the same resource reasoning. The spec's assumption (1 POST per open, 2.5× margin covers up to 2) remains unverified by this pass and stays an open risk item for the PR body.

### 4. What MediaMTX does with a `429`

**Still not completed — open item, unchanged.** Same dependency as #2/#3. #2160 already records the identical gap for `5xx`; spec.md's assumption 3 remains unobserved after this pass.

### 5. Latency — `WhepHandshakeLatencyTests`' figure

**Obtained cleanly this pass.** `Whep_auth_hook_p95_stays_under_three_seconds_over_twenty_opens`: **p50 = 25 ms, p95 = 80 ms, max = 132 ms**, budget 3000 ms — comfortably inside. This is a genuine observation from this pass's own run (not the historical `#2149` figure the pre-fix pass had to fall back to citing, since the test failed before reaching its measurement line at `360a0fce`). NFR-001/SC-004 is now held and observed, not merely asserted.

---

## Summary for the PR body

- **Build/Architecture.Tests: clean.** 0 build errors; 217 pre-existing advisory warnings (ADR-0084, not CI-enforced); 444/444 Architecture.Tests.
- **All three phase-6 blockers and all applicable should-fix items are fixed and verified, not merely fixed:**
  - **BLOCKER 1** (gateway proxying the anonymous hook) — fixed in `5ab3d23b`, confirmed by a new integration test (`Gateway_does_not_forward_the_anonymous_whep_authorize_hook`, passing against the real running gateway in run 2).
  - **BLOCKER 2** (FR-010/US3's false red-then-green claim) — corrected in `d89269fd`; the census's own class stays 444/444 with M15 present, confirming the row was always "documentation, not a gate," now stated accurately.
  - **BLOCKER 3** (this file being stale) — this rewrite.
  - **S3, S4, S5, S6, S7, S8, S9, S10, S13** — all applied (`b7465993`, `f8825f29`, `c79e2fbf`) and verified green: `WhepValidatorRefreshRestraintTests`/`WhepValidatorUnreachableRealmTests` 11/11; `WhepHandshakeLatencyTests` green with a real p95 figure; `WhepAuthorizeRateLimitTests` 7/7 twice, independently.
  - **S11 (config-guard unit test) and S4's relocation to `Architecture.Tests`** — deliberately deferred per the review brief; not attempted here. Noted as follow-ups.
- **Phase 5 observation 1 (429 + one log record) is directly re-confirmed against the real stack, twice.** Observations 2–4 remain genuinely open (resource-constrained on this machine, exactly as before — not re-attempted or guessed at) and observation 5 (latency) is now a real, clean figure instead of a historical citation.
- **One new, small finding recorded rather than silently fixed:** spec.md's SC-003 says "seven" `WhepAuthIntegrationTests` cases; the file has six. Out of this pass's corrected scope (not named in the review brief), so flagged here rather than edited.
