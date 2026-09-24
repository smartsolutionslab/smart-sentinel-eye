# Plan 238 — The wrap no other catch sees

**Spec**: [spec.md](./spec.md) · **Issue**: #2435 · **Engineer**: `backend-engineer` (phase 4a: `test-writer`)

## 1. Scope and placement

- **Production code: none.** The audit (spec §2) found no site beyond `OutboxBacklogHealthCheck`
  that classifies the wrapped shape wrongly, and that site's fix (spec 172) is in place. The one
  residual shape is accepted (spec §3).
- **Tests: one file** — `tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs`, two new
  `[Fact]`s beside the existing one.
- No bounded context, no Domain/Application/Api layer, no `Shared.Contracts`, no AppHost, no
  migration, no messaging. Entities/value objects/invariants: N/A. Boundary rules: unaffected
  (ServiceDefaults.Tests already references ServiceDefaults and Npgsql's EF provider).
- Latency: N/A (constitution §IV) — not on the event→overlay path.

## 2. Harness

Reuse the existing test's shape exactly (`ProbeDbContext`, `NullLogger`, `"wolverine_probe"`,
`CheckHealthAsync(new HealthCheckContext())`). Add:

- one private nested `sealed class` deriving `Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor`
  that throws a supplied exception from `ConnectionOpeningAsync` (and `ConnectionOpening`, so the
  sync path cannot bypass it);
- options built as the existing test does, plus `.AddInterceptors(new …(exception))`. The connection
  string can be `UnreachableConnectionString`; the interceptor throws before Npgsql dials.
- the exception: `new Npgsql.PostgresException(messageText, "FATAL", "FATAL", sqlState)` — confirm the
  constructor signature against the pinned Npgsql before writing; if it is not public, stop and
  report (do not reflect into it).

Collections as collection expressions; no `ArgumentNullException.ThrowIfNull`; sentence-style names
(ADR-0053); Shouldly.

## 3. The two tests

| Test | Input SQLSTATE | Asserts |
|---|---|---|
| T1 `A_transient_refusal_that_carries_a_sqlstate_escapes_the_check_for_its_registration_to_resolve` | `57P03` (`PostgresErrorCodes.CannotConnectNow`) | `Should.ThrowAsync<InvalidOperationException>`; `InnerException` is `PostgresException` with `SqlState == "57P03"` |
| T2 `A_non_transient_refusal_is_not_wrapped_and_is_reported_as_an_unreadable_backlog` | `42P01` (`PostgresErrorCodes.UndefinedTable`) | `Status == Degraded`; `Data["error"] == nameof(PostgresException)` |

Use `PostgresErrorCodes` constants, not literals. Assertion messages (FR-002):

- **T1**: spec 238 §3 accepted this shape escaping to the registration's `failureStatus: Degraded`
  (ADR-0154 row 4). Red means either the check now handles it — a reclassification under ADR-0154
  that needs a human decision and an update to spec 238 §3 — or the provider stopped wrapping it.
- **T2**: red means the provider now wraps non-transient SQLSTATE errors, so
  `UniqueConstraintExceptionHandler.IsUniqueViolation` and `PersistenceLoopHostedService.IsMissingPartition`
  no longer see them (spec 238 §2.1 S2, S3).

Why T1 asserts the throw rather than the resolved `Degraded`: the resolution is the framework
applying a registration that lives inside `AddWolverineForContext`, which is not unit-callable
(spec 172 T007); a test that registered the check itself would assert its own input. The throw is
the part this codebase owns.

Class doc comment: extend with one paragraph naming spec 238 and the two shapes; do not rewrite the
existing paragraphs.

## 4. Counterfactuals (FR-003) — each observed red, quoted, then reverted

1. **T2 distinguishes wrapped from unwrapped**: temporarily change T2's input SQLSTATE to `57P03`.
   Expected: T2 red with an `InvalidOperationException` escaping. Revert.
2. **T1 notices a widening**: temporarily change `OutboxBacklogHealthCheck.cs:129`'s filter to
   `ex.InnerException is DbException inner` (drop `&& IsUnreachable(inner)`). Expected: T1 red
   (no exception thrown); the existing unreachable test stays green. Revert, and confirm
   `git diff src/` is empty afterwards.

## 5. Colour

**Characterisation, observed green** (ADR-0139). No production behaviour changes, so nothing can be
red-first. A new test arriving red is **not** a phase-4 bug to fix: it refutes an audit premise (A1
or A2) and the slice stops with the verbatim output (spec FR-004).

## 6. Orchestrator work (no code)

- File **O2** (composed health-check registration guard) as its own issue, body from spec §4 O2,
  citing ADR-0154, spec 172 T007 and #2435's comment item 2. Check the board for an existing issue
  first (none found by title/body search for `failureStatus` / `HealthCheckServiceOptions` /
  `Unhealthy` on 2026-09-24). Leave it **without** `agent:ready`: it carries the harness and
  exemption-policy choices of spec §4 O2 (3)–(4).
- File **O1** (suspected `KEYCLOAK_UNAVAILABLE` misattribution in
  `RotateWebhookClientCommandHandler.cs:139`) as its own issue, board-checked first; it wants a
  red Layer-2 race test before any fix.
- Board gate: #2435 is on Project #13 — verify by `content.url` with `--limit 2000`.
- PR body closes #2435 with a closing keyword and links both new issues.

## 7. Constitution / ADR check

- ADR-0154: unchanged; this spec is its Consequences' "not audited" follow-up, discharged. ADRs are
  records of a moment — do not edit ADR-0154; the spec is the forward pointer.
- ADR-0036: smallest change — zero production lines.
- ADR-0144: the lane makes no reclassification decision (spec §3) and no harness/policy decision
  (spec §4 O2).
- No new ADR.
