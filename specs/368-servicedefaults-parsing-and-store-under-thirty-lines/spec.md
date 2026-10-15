# Spec 368: The ServiceDefaults header parsers, backlog check and claim under thirty lines

**Feature Branch**: `refactor/2951-servicedefaults-parsing-and-store` (cut from `origin/develop` at `2b895f82`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Four judgement calls
are marked (§6).

**Input**: Issue [#2951](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2951),
split out of #2932 by spec 367 §8 A (branch `refactor/2932-identity-automation-servicedefaults-s138`).

**Lane.** Autonomous (ADR-0144). Behaviour-preserving, so Phase 4a is **characterisation, observed
green** (constitution §Testing). Engineer: **backend-engineer**. **Security-sensitive**:
`IdempotencyStore<T>.BeginAsync` is the at-most-once claim (ADR-0142, #2290 reclaim, #2491 fencing,
spec 302 request binding) and `IdempotencyHeaders.TryRead` is the trust boundary in front of it.
Phase 6 runs `/security-review` as well as `/code-review`.

**Spec number.** 368. Checked 2026-10-15 after `git fetch`: `origin/develop` highest 366; highest
across every remote head (`git ls-tree` over `refs/remotes/origin/*`) 366; worktree
`D:\Github\sse-2932` (unpushed, #2932) holds 367. Re-check before the PR is opened.

**ADRs.** ADR-0084 (S138 <= 30 lines, advisory), ADR-0144 (autonomous lane, phase 4a colours, no
gate weakened), ADR-0142 (idempotency: scope, at-most-once claim, replay), ADR-0113 (`If-Match` /
upsert precondition), ADR-0154 (readiness is a liveness and routing signal), ADR-0050
(`[LoggerMessage]`), ADR-0141 (`Option<T>`), ADR-0105 (`Ensure.That`), ADR-0036 (smallest change),
ADR-0052/ADR-0103 (xUnit + Shouldly + Moq; no Testcontainers). No new ADR: shape, not a decision.

**Prior art reused.** The in-process EF interceptor technique: `SuppressOpenInterceptor` +
`DbCommandInterceptor.ReaderExecutingAsync` returning `SuppressWithResult(dataTable.CreateDataReader())`
(`tests/Architecture.Tests/Persistence/AuditChunkArchiverCharacterisationTests.cs`), and the
`ThrowingOnOpenInterceptor` in `tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs`.
`CapturingLogger<T>` in `tests/ServiceDefaults.Tests/Fakes`.

---

## 1. What was measured (premise verified, not assumed)

```sh
dotnet build src/ServiceDefaults -c Release --no-incremental
```

Result on `2b895f82`: **all four of the issue's S138 hits are present, same methods, same counts.**

| # | File:line | Method | Lines | This spec |
|---|---|---|---|---|
| 1 | `ConcurrencyHeaders.cs:74` | `TryReadUpsertPrecondition` | 31 | keep (G1) |
| 2 | `Idempotency/IdempotencyHeaders.cs:49` | `TryRead` | 31 | keep (G1, security) |
| 3 | `OutboxBacklogHealthCheck.cs:77` | `CheckHealthAsync` | 41 | keep (G2) |
| 4 | `Idempotency/IdempotencyStore.cs:34` | `BeginAsync` | 61 | keep (G3, last, security) |

Out of scope and **must not change**: S138 `AuthenticationDefaults.AddBearerAuthentication` (46),
`AddRevocationCheck` (32) (spec 367 §8 B), `WolverineDefaults.AddWolverineForContext` (56) (§8 C);
S107 `ClientCredentialsTokenProvider` ctor (5), `FabResolution` (5), `IdempotencyScope.For` (5),
`AddWolverineForContext` (9). After this spec: **3 S138 + 4 S107** in `src/ServiceDefaults`.

## 2. User story

### US1 (P1): The four ServiceDefaults methods fit ADR-0084's 30-line limit with no observable change

Every one of the nine services calls these methods; a maintainer reading them should meet one
decision per method, and nothing a caller, a database or an orchestrator can observe may move.

**Independent test.** Run §4's characterisation on `2b895f82` (green), apply each refactor commit,
re-run byte-unmodified (green); the §1 build shows 3 S138 left, none of them in §1's rows.

**Acceptance scenarios**

```gherkin
Scenario: Happy - each method splits and its characterisation stays green unmodified
  Given §4's tests observed green on 2b895f82
  When a refactor commit of plan §3 lands
  Then the same test files, byte-unmodified, pass in Release
  And the §1 build no longer reports that method under S138

Scenario: Conflict - a characterisation needs an edit after a refactor
  Given a refactor commit has landed
  When any §4 assertion fails
  Then the behaviour moved: the refactor is reverted or fixed, and the assertion is not touched

Scenario: Bad request - header refusals keep their exact problem
  Given a request with two Idempotency-Key headers, an empty one, or one IdempotencyKey.From refuses
  When IdempotencyHeaders.TryRead runs
  Then it returns false with a 400 problem whose title is IDEMPOTENCY_KEY_MALFORMED (the
    constant's current value) and whose detail is byte-identical to today's
  And If-Match plus If-None-Match, a non-wildcard If-None-Match, or neither header keep their
    exact 400/400/428 problems from TryReadUpsertPrecondition

Scenario: Auth/scope - the claim cannot hand one caller another's answer
  Given a stored row under the same key, endpoint and caller
  When its fab differs, its fingerprint differs, or its stored fingerprint is NULL
  Then BeginAsync returns Mismatched, also when that row is completed
  And the claim and read SQL texts and their parameter lists are byte-identical to today's
```

## 3. Functional requirements

- **FR-001** `TryReadUpsertPrecondition`, `TryRead`, `CheckHealthAsync`, `BeginAsync` each <= 30
  lines (S138) after the change; every new helper <= 30 lines and <= 4 parameters (no new S107).
- **FR-002** Public signatures of `ConcurrencyHeaders`, `IdempotencyHeaders`,
  `OutboxBacklogHealthCheck<T>`, `IdempotencyStore<T>` unchanged; new members are `private`.
- **FR-003** Header parsers: same return value, same `out` values, same `IResult` (status, title,
  detail) for every input, and the same branch precedence.
- **FR-004** Health check: same `HealthStatus`, description, data dictionary (keys, value types,
  values), same commands in the same order (the second query still skipped when pending is 0), the
  same warning log record emitted in exactly the same cases; the three `catch` clauses are not
  touched and still wrap the assessment (it stays inside the `try`).
- **FR-005** Claim: the claim SQL and the binding-read SQL are **byte-identical** (after CRLF→LF
  normalisation only), their parameters keep the same order, names, values and Npgsql types
  (`fab` stays a typed `Varchar`, `DBNull` for `None`), the read is still issued only when the claim
  returns other than exactly one row, and each of the outcomes in §4.4 is unchanged. Every comment
  in the method moves with the code it explains, verbatim.
- **FR-006** No change to `CompleteAsync`, `ReleaseAsync`, `ReadBacklogAsync`, `Identifier`,
  `Unreachable`, `IsUnreachable`, `TryReadExpectedVersion`, `Malformed`, `Missing`, `MissingUpsert`.

## 4. Characterisation (phase 4a, observed green on `2b895f82` before any production edit)

All in `tests/ServiceDefaults.Tests` (runs in CI's `coverage-check.ps1` unit pass; no shard filter
applies there). No existing test file is edited. Expected SQL and problem details are **hand-written
literals in the test**, never read from the production constants (an assertion must not check its
own input).

### 4.1 `TryReadUpsertPrecondition` (gap: exact bodies; existing 7 + 12 tests stay)

Both headers → 400 `IF_MATCH_MALFORMED` "Send either If-Match or If-None-Match, not both."; neither
→ 428 with `MissingUpsert`'s full detail; `If-None-Match: x` → 400 with its detail;
`If-None-Match: " * "` (trimmed) → true/None; two `If-None-Match` values (`*`,`x`) → true (only
`[0]` read) and (`x`,`*`) → 400; an `If-Match` failure is passed through (weak tag → its detail);
`If-Match: "3"` → `Some(3)`, problem null. Status, title, detail per case.

### 4.2 `IdempotencyHeaders.TryRead` (gap: no direct unit test)

Absent → true/None/null; present with zero values (see J3); two headers → 400 with "Send at most
one Idempotency-Key header; this request carried 2."; empty and whitespace → "… was present but
empty."; a single `null` value → the empty refusal; a 129-character key (`MaxLength` is 128; 128 accepted) and a key with `!` → 400
whose detail is `IdempotencyKey.From`'s `ArgumentException.Message` **including** its
`(Parameter 'value')` suffix; a valid key with surrounding spaces → `Some` of the trimmed value.

### 4.3 `OutboxBacklogHealthCheck.CheckHealthAsync` (gap: the four read outcomes)

Driven in process with the suppress-open + canned-reader interceptors. Cases: pending 0 (one
command only; Healthy "No announcements are waiting."; data `{pending: 0L, maxAttempts: 0,
schema}`); pending 4,999 / attempts 4 (Healthy, description text, no log); pending 1 / attempts 5
(Degraded); pending 5,000 / attempts 0 (Degraded). Each pins status, description, data keys in order
with value types (`long`, `int`, `string`), `Exception` null, both SQL texts, the command count, and
the log record (Warning, exact rendered message) or its absence.

### 4.4 `IdempotencyStore<T>.BeginAsync` (gap: in process; today only 23 Docker tests)

Driven in process with a suppress-open interceptor and a `DbCommandInterceptor` that records every
command (text, parameters: name, value, `NpgsqlDbType`, order) and returns a canned reader per call.
Outcomes:

| # | Claim returns | Read returns | Expected |
|---|---|---|---|
| O1 | one row `t` | (not issued) | `ReservedAs(new IdempotencyClaim(scope, t))`, `ReservedAt == t` incl. `Kind`; 1 command |
| O2 | no row | no row | `InProgress`; 2 commands |
| O3a-e | no row | fab differs / stored fab NULL with `Some` / stored fab set with `None` / fingerprint differs / stored fingerprint NULL | `Mismatched` |
| O3f | no row | mismatched row that is **completed** | `Mismatched` (precedence over replay) |
| O3g | no row | fab differs only by case | `Mismatched` (ordinal) |
| O4 | no row | matching, `resource_identifier` NULL | `InProgress` |
| O5 | no row | matching, completed with `g` | `CompletedWith(g)` |
| O6 | two rows | matching, unfinished | `InProgress` (pins `== 1`, not `> 0`) |

Plus: both SQL texts byte for byte, for a `Some` fab and a `None` fab (`DBNull`, still `Varchar`);
the `StaleAfter` parameter value (10 minutes); `BeginAsync(null!)` throws as today.

### 4.5 Existing coverage (CI integration, Docker; the second observer)

`IdempotencyFencingIntegrationTests` (6), `IdempotencyRequestBindingStoreIntegrationTests` (9),
`StaleIdempotencyReservationIntegrationTests` (8), `OutboxBacklogIsVisibleTests`, and the endpoint
characterisations of specs 363/365/366 must be green on the PR. They observe what §4.4 cannot: real
Postgres row locking under two concurrent claimers.

## 5. Success criteria

- **SC-001** §1 build: 3 S138 (exactly the three out-of-scope methods), 4 S107 unchanged, no new
  warning.
- **SC-002** §4.1-§4.4 observed green on `2b895f82` (output quoted in the PR), green and
  byte-unmodified after each refactor commit.
- **SC-003** Counterfactuals (plan §5) observed red, then reverted, quoted in the PR.
- **SC-004** `tests/ServiceDefaults.Tests` and `tests/Architecture.Tests` green; CI integration
  shards green.
- **SC-005** `git diff --stat origin/develop` touches only the 4 production files of §1, the new
  test files, and this spec directory.

## 6. Judgement calls (marked)

- **J1: `BeginAsync` is its own last commit group (G3).** If §4.4 cannot be made green in process
  (for example EF cannot materialise the canned reader), `BeginAsync` is **deferred**: its test and
  refactor are dropped, #2951 gets a comment with the verbatim failure, and G1 + G2 ship. Partial
  delivery is recorded in the PR body, not hidden.
- **J2: the SQL texts become `private const` fields.** That is what brings `ClaimAsync` under 30
  lines without splitting a statement. A raw string literal's content does not depend on its
  indentation level, and §4.4's byte pin proves it. Their comments move with them, verbatim.
- **J3: "present with zero values" may be unconstructible.** `HeaderDictionary` drops a key set to
  `StringValues.Empty`. If the test cannot build that request through `DefaultHttpContext`, record
  that and keep the `values.Count == 0` clause byte-identical in the refactor. Do not delete it as
  dead code: that would change behaviour, which this lane may not do.
- **J4: the assessment stays inside the `try`.** Moving it out would be a semantic change (an
  exception from the logger would escape differently), so it stays inside, even though such an
  exception is unlikely.

## 7. Latency budget impact (constitution §IV)

**N/A.** Idempotency covers the administrative creates and rotations. The health check serves the
readiness probe. Neither is on a leg of the event → overlay path. The refactor adds no round-trip
and no allocation beyond a method call.

## 8. Locked tech choices

.NET 10, EF Core raw SQL against Npgsql (unchanged), `Result`/`IResult` problems (ADR-0089),
`Ensure.That` (ADR-0105), `Option<T>` (ADR-0141), `[LoggerMessage]` (ADR-0050), xUnit + Shouldly +
Moq (ADR-0052), no Testcontainers (ADR-0103).
