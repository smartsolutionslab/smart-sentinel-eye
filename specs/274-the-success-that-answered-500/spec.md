# Spec 274 — The success that answered 500

**Issue**: #2490 · **Branch**: `fix-2490-idempotent-durability-write-failure` · **Phase**: 1 (Specify)
**Date**: 2026-09-27 · **Base**: `1a51bd18` (`origin/develop`)
**Context**: ServiceDefaults — `src/ServiceDefaults/Idempotency/IdempotentRequest.cs` only. No bounded-context
code, no contract, no endpoint call site, no `IIdempotencyStore` change.
**Engineer**: `backend-engineer` (4b) after `test-writer` (4a) · **Reviewer**: `backend-reviewer`
**Phase 4a colour**: **both, split exactly** (§7) — new facts red; the existing
`IdempotentRequestTests` facts are characterisation.
**ADRs**: ADR-0142 (idempotency keys — the mechanism being fixed), ADR-0050 (OTel-first logging — the
signal chosen in §6), ADR-0036 (smallest change), ADR-0139 (new behaviour observed red first),
ADR-0037 (phases). Predecessor: spec 201 / #2290 (failure-path release; this issue was its explicit
out-of-scope follow-up).
**Constitution**: §IV — **N/A**. Idempotency bookkeeping on REST creates/rotations is not a leg of the
event→overlay path. §Testing — see §7.
**New ADR needed**: **No.** The signal choice reuses an idiom already in the same file (§6).

---

## 1. The defect

`IdempotentRequest.RunAndRecordAsync`, after `work()` has returned an outcome, records it:
`CompleteAsync(scope, identifier)` when something was created, `ReleaseAsync(scope)` when nothing was.
Neither call is guarded. If the store throws there — a dropped connection, a lock timeout — the
exception propagates out of `ExecuteAsync` and the caller receives **500** for an operation that
**succeeded**: the resource exists (or the refusal was genuinely decided), and the caller is told
otherwise. A client that trusts the 500 may then retry and hit exactly the duplicate the key exists to
prevent.

## 2. Product-owner decision (made; not re-opened here)

On a throw from `CompleteAsync`/`ReleaseAsync` after `work()` succeeded: **catch it, record it as a real
operator-visible signal, and return `outcome.Response`.** No inline retry of the bookkeeping write
(separate design question). No silent swallow.

## 3. Scope

**In**: the success-path bookkeeping in `RunAndRecordAsync`; new unit facts in
`tests/ServiceDefaults.Tests/Idempotency/IdempotentRequestTests.cs`.

**Out** — do not do:
- Retry the bookkeeping write, or fall back from a failed `CompleteAsync` to `ReleaseAsync` (§6 says
  why the fallback would be worse, not merely out of scope).
- Change `IIdempotencyStore` or `IdempotencyStore<TDbContext>`.
- Touch any of the 11 endpoint call sites constructing `IdempotentExecution`.
- Change `ReleaseQuietlyAsync` (the #2290 failure-path case).
- Introduce `ILogger` into `IdempotentRequest` (§6).

## 4. User story

### US1 (P1) — A succeeded operation is answered as a success even when its key cannot be recorded

As an API caller sending an `Idempotency-Key`, I want an operation that succeeded to be answered with its
success response even if the server fails to record the key afterwards, so that I am never told "500" for
a resource that exists; and as the on-call operator I want that recording failure visible in the trace
of the request that suffered it.

**Independent test**: the new facts (§7) fail today with the store's exception escaping `ExecuteAsync`,
and pass after; every existing `IdempotentRequestTests` fact passes unmodified.

## 5. Acceptance scenarios

```gherkin
Feature: success-path idempotency bookkeeping cannot turn success into 500

  Scenario: happy — bookkeeping succeeds (unchanged)
    Given a caller sends a first request with an Idempotency-Key
    And the work creates a resource
    When the store records the key
    Then the caller receives the work's response
    And the key is completed with the resource's identifier

  Scenario: conflict — the key cannot be completed after a create succeeded
    Given the work created a resource
    And the store's CompleteAsync throws
    When ExecuteAsync returns
    Then the caller receives the work's own response, not an exception or 500
    And the completion failure is recorded as an "exception" event on the current Activity

  Scenario: bad request — the key cannot be released after a refusal
    Given the work refused the request and created nothing (NothingCreated)
    And the store's ReleaseAsync throws
    When ExecuteAsync returns
    Then the caller receives the refusal response the work produced, not an exception or 500
    And the release failure is recorded as an "exception" event on the current Activity

  Scenario: auth — no key, no bookkeeping (unchanged)
    Given a request without an Idempotency-Key
    Then the store is never called and the work's response is returned as today
```

(Auth proper — the caller-scoped key — is untouched: `IdempotencyScope` and `BeginAsync` do not change.)

## 6. Functional requirements

- **FR-001 Response preserved.** When `work()` returned an outcome, `ExecuteAsync` MUST return
  `outcome.Response` whether or not the subsequent `CompleteAsync`/`ReleaseAsync` throws.
- **FR-002 Not silent.** A throw from that bookkeeping MUST be recorded via
  `Activity.Current?.AddException(exception)` — the same call `ReleaseQuietlyAsync` makes.
- **FR-003 No retry, no fallback.** The bookkeeping call MUST be attempted exactly once. In particular a
  failed `CompleteAsync` MUST NOT be followed by `ReleaseAsync`: releasing would let an immediate retry
  with the same key re-run the work and create a **second** resource. Leaving the row reserved makes such
  a retry wait and then receive `409 IDEMPOTENT_REQUEST_IN_PROGRESS` — the safer failure.
- **FR-004 Failure path untouched.** A `work()` that throws keeps today's behaviour exactly
  (`ReleaseQuietlyAsync`, rethrow) — the existing #2290 facts are the characterisation.
- **FR-005 Explained in code.** The catch carries a *why* comment in the style of `ReleaseQuietlyAsync`'s:
  why it is not rethrown, why this is not a swallowed exception, and what the orphaned reservation
  becomes (reclaimed after `IdempotencyReclamation.StaleAfter`).

### Resolved ambiguity — the signal: `Activity.AddException`, not `ILogger` (recorded per ADR-0036)

**Decision: reuse the file's own idiom.** `ReleaseQuietlyAsync` already handles the same shape — a
bookkeeping failure that must not be lost but must not replace the more important outcome — with
`Activity.Current?.AddException(...)`, and its comment argues that this satisfies the "no swallowed
exceptions" rule. That argument was accepted in #2290's review. ADR-0050 makes OpenTelemetry the
observability spine, so an exception event on the request's span is an OTel-native, operator-queryable
signal correlated to the exact request.

**Alternative rejected: `[LoggerMessage]` source-gen through an injected `ILogger`.**
`IdempotentRequest` is a static class; neither it, `IdempotentExecution`, `IdempotencyStore<TDbContext>`
nor any of the 11 call sites carries an `ILogger`. Adding one means a new `IdempotentExecution` member and
edits to all 11 endpoint files and their `[FromServices]` records — a blast radius disproportionate to a
one-method fix, with no precedent in this file (ADR-0036: smallest change, no speculative generality).

**Known limit, accepted:** `Activity.Current` is null when the request is not sampled, in which case the
event is not recorded. This is identical to the precedent's limit; ServiceDefaults' tracing creates the
ASP.NET Core request activity for every request in the configured environments. If on-call later needs a
log line or a metric for this, that is a follow-up that would add it to *both* catch sites together.

**This catch is not drive-by error handling:** it sits at a durability boundary (the store's own write)
and exists to protect a response that is already decided — the exempted case in CLAUDE.md's rule.

## 7. Phase 4a colour — both, split exactly

**RED — new facts** in `IdempotentRequestTests` (the fake `RecordingStore` gains `CompleteThrows`,
mirroring its existing `ReleaseThrows`):
1. `A_completion_that_throws_after_the_work_succeeded_still_answers_with_the_work_s_response` —
   `CompleteThrows` set; `ShouldBeOk(result, "done")`.
2. `A_release_that_throws_after_a_refusal_still_answers_with_the_refusal` — `CreatesNothing` +
   `ReleaseThrows`; `ShouldBeOk(result, "refused")`.
3. `A_completion_that_throws_records_its_failure_on_the_current_activity` — same listener pattern as
   `A_release_that_throws_records_its_failure_on_the_current_activity`; asserts an `exception` event whose
   `exception.message` is the completion failure's.
4. `A_completion_that_throws_is_not_followed_by_a_release` — `CompleteThrows` set; `store.Released`
   is `0` (FR-003).

Facts 1–3 are red today (the exception escapes `ExecuteAsync`). Fact 4 is red today only incidentally
(the call throws before its assertion) — the test-writer must quote which assertion/exception failed
for each; a fact that arrives green is a 4a failure. **Quote the verbatim failures in the PR.**

**CHARACTERISATION, GREEN** — every existing fact in `IdempotentRequestTests` (notably the #2290 facts
`A_release_that_throws_does_not_hide_the_failure_that_caused_it` and
`A_release_that_throws_records_its_failure_on_the_current_activity`) captured passing before 4b and
passing **unmodified** after.

## 8. Independent end-to-end test procedure

1. `dotnet test tests/ServiceDefaults.Tests --filter "FullyQualifiedName~IdempotentRequestTests"` —
   all facts green. No Aspire stack needed: the store is the only collaborator and is faked.
2. Counterfactual: remove the new `try/catch` → facts 1–3 red, existing facts green; restore; `git diff`
   empty.
3. Counterfactual for FR-003: add a `ReleaseAsync` call in the catch → fact 4 red; restore.

## 9. Residual risk (recorded, not fixed)

After a failed `CompleteAsync` the key stays reserved. A retry with the same key within
`IdempotencyReclamation.StaleAfter` (10 min) receives `409 IDEMPOTENT_REQUEST_IN_PROGRESS`, not a replay; a
retry *after* it reclaims the key and re-runs the work, which may create a duplicate (or earn the
endpoint's own genuine-duplicate 409). The caller that received the success response has no reason to
retry, so this only bites a caller that lost the response in transit *and* hit a store failure on the
same request. Closing it needs the deferred bookkeeping-retry design (§2), not this fix.
