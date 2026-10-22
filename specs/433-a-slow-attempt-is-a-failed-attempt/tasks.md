# Tasks: A slow probe attempt is a failed attempt

**Feature**: 433 | **Issue**: #3087 | **Spec**: `./spec.md` | **Plan**: `./plan.md`
**Branch**: `test/3087-probe-attempt-timeout`
**Worktree**: `D:/Github/sse-3087` (cut from `origin/develop` at `cbdc137c`)

## The three declarations (ADR-0144)

1. **Engineer: `infra-engineer`.** `AspireFixture.cs` and its Docker-free tests,
   as specs 062, 084, 098, 424 and 430. No bounded-context work.
2. **ADR: none.** Owner ruled "No ADR"; applies ADR-0103/0068/0143.
3. **Phase 4a colour: RED (behaviour-changing).** A timed-out attempt stops
   escaping the probe loop. Red shown Docker-free under
   `--filter "Category=FixtureLogic"`. T001 lands the seam first so the reds are
   assertion failures, not compile errors.

All tasks touch the same two files — **none is `[P]`**; strictly sequential.

## Phase 1 — Seam (blocks everything)

- [ ] **T001 [US1]** `refactor(3087)`: extract `PollUntilSuccessAsync(probe, path,
  attempts, delay, cancellationToken)` in `AspireFixture.cs` with **today's**
  catch (`HttpRequestException` only); route `WaitForKeycloakRealmAsync` and
  `WaitForMediaMtxAsync` through it, keeping each
  `throw await ProbeExhaustedAsync("… after 60 attempts …")` verbatim.
  Behaviour-preserving: `Category=FixtureLogic` green before and after,
  unmodified (spec 430's source-scan test included).

## Phase 2 — Red (test-writer; quote output verbatim)

- [ ] **T002 [US1]** `test(3087) (red)`: tests 1-6 of plan §Tests in new
  `AspireFixtureProbeLoopTests.cs`; add the class to
  `tests/Integration.Tests/ci-shards/shard-1.filter`. Expected on T001:
  1-3 **red** (`TaskCanceledException` / `TimeoutRejectedException` thrown from
  attempt 1 instead of `false`/`true`); 4-6 **green** (characterisation).
  Depends on T001.

## Phase 3 — Green (infra-engineer; may not edit T002's tests)

- [ ] **T003 [US1]** `fix(3087)`: add
  `catch (TimeoutRejectedException)` and
  `catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)`
  to the helper, with a *why* comment (spec §2). Tests 1-6 green. Depends on T002.

## Phase 4 — Counterfactuals (record each observed red in the PR)

- [ ] **T004** Drop the `when` filter (catch every `TaskCanceledException`) →
  **observed: all six FixtureLogic probe-loop tests stay green** (equivalent
  mutant; `Task.Delay` re-throws under the cancelled token — plan §Tests). Not
  pinned by a test; held by review.
- [ ] **T005** Remove the `TimeoutRejectedException` catch → test 2 red.
- [ ] **T006** Remove the `TaskCanceledException` catch → tests 1 and 3 red.
- [ ] **T007** Revert `WaitForKeycloakRealmAsync` to an inline loop with the old
  catch → no FixtureLogic test fails. **Expected, and recorded as the seam's
  limit**: the helper is proven, the call site is held by review and by spec
  430's source scan of the exhaustion sentence, not by a test. Say so in the PR.

## Phase 5 — Verify

- [ ] **T008** `Category=FixtureLogic` green in Release;
  `dotnet format --verify-no-changes`; CI's Docker integration job green
  (discharges "passing runs unaffected"). A live provoked hang of Keycloak or
  MediaMTX is not required and not possible locally (Docker down) — state it in
  the verification note.
