# Plan 368: The ServiceDefaults header parsers, backlog check and claim under thirty lines

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2951.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 4 of the 7 S138 hits in `src/ServiceDefaults`, by shape only; S107 stays at 4; every new helper <= 4 parameters. |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| ADR-0142 | Claim/read SQL byte-identical, same parameter order and types, same classification order (mismatch before completed/in-progress). Pinned in process (§4.4) and by 23 Docker tests. |
| ADR-0113 | Upsert precondition branch precedence unchanged (both → If-None-Match → missing → If-Match). |
| ADR-0154 | The three `catch` clauses are untouched and still enclose the assessment. |
| ADR-0050 | `logger.OutboxBacklogConcerning` call unchanged, same cases. |
| §III | ServiceDefaults only; no project reference added. |
| House rules | `var`; one parameter per line when a call wraps; no drive-by comments; comments move with their code; private fields without underscore. |

## 2. Shape

`src/ServiceDefaults` only (cross-cutting, every service). Not a bounded context, no Domain or
Application layer, no messaging, no migration, no AppHost, no csproj change. Production files (4):
`ConcurrencyHeaders.cs`, `Idempotency/IdempotencyHeaders.cs`, `OutboxBacklogHealthCheck.cs`,
`Idempotency/IdempotencyStore.cs`.

No entities, value objects or invariants are added. The invariants that bind are existing ones:
- **At most one claimant** per (key, endpoint, caller): enforced by `INSERT … ON CONFLICT … DO
  UPDATE … WHERE resource_identifier IS NULL AND reserved_at < NOW() - @p5`, plus
  `SET reserved_at = NOW()` (the second-reclaimer guard). This lives in the SQL text, which is why
  §4.4 pins it byte for byte.
- **The fence token is the returned `reserved_at`** (#2491): `ReservedAs` carries exactly the value
  read back.
- **A mismatched row is refused before completion is considered** (spec 302 I2/I3), NULL stored
  fingerprint fails closed, comparisons are ordinal.

## 3. Refactor shape per method

| Method | Extraction | Stays in the method |
|---|---|---|
| `TryReadUpsertPrecondition` | `private static bool TryReadCreateAssertion(HttpRequest request, [NotNullWhen(false)] out IResult? problem)`: the `If-None-Match` `[0]` trim + wildcard check. | Guard, out init, both-headers check, the dispatch, `!hasIfMatch`, `TryReadExpectedVersion` delegation. |
| `IdempotencyHeaders.TryRead` | `private static bool TryParse(string raw, out Option<IdempotencyKey> key, [NotNullWhen(false)] out IResult? problem)`: the `try { IdempotencyKey.From } catch (ArgumentException)` block **with its "Spelled out rather than routed through BoundaryParse" comment**. No `!` operator introduced (ADR-0141). | Guard, out init, absent/zero-count, count > 1, null-or-whitespace. |
| `CheckHealthAsync` | `private HealthCheckResult Assess(long pending, int attempts)`: data dictionary, pending-0 Healthy, description, the two-signal comparison, log + Degraded, **with both comments**. | `try { var (pending, attempts) = await ReadBacklogAsync(…); return Assess(pending, attempts); }` and all three `catch` clauses byte-identical (J4). |
| `BeginAsync` (G3) | `private const string ClaimSql` and `ReadBindingSql` (J2), the #2290 comment above `ClaimSql`; `private async Task<DateTime[]> ClaimAsync(IdempotencyScope scope, CancellationToken)` with the spec 302 typed-parameter comment and the #2491 `SqlQueryRaw` comment; `private async Task<StoredBinding[]> ReadBindingAsync(IdempotencyScope scope, CancellationToken)`; `private static IdempotencyReservation Classify(IdempotencyScope scope, StoredBinding row)` with the I2/I3 comment. | Guard, `claimed.Length == 1` → `ReservedAs`, the "someone else holds the key" comment, read, the "Empty means" comment + `Length == 0` → `InProgress`, `return Classify(scope, existing[0])`. |

Rules for the engineer: keep `Length == 1`, keep the argument order
`(key, endpoint, caller, fabParameter, fingerprint, StaleAfter)`, keep `Match<object>` with
`DBNull.Value`, keep `StringComparison.Ordinal`, keep `ToArrayAsync` (no LINQ operator composes
the `RETURNING`). Do not touch `CompleteAsync` or `ReleaseAsync` or `StoredBinding`'s shape.

## 4. Test design (`tests/ServiceDefaults.Tests`, new files only)

| ID | File | Notes |
|---|---|---|
| C1 | `UpsertPreconditionProblemCharacterisationTests.cs` | spec §4.1. Existing `UpsertPreconditionTests.cs` and `ConcurrencyHeadersTests.cs` are not edited. |
| C2 | `Idempotency/IdempotencyHeadersTests.cs` | spec §4.2. `DefaultHttpContext().Request`; assert `ProblemHttpResult` status, `ProblemDetails.Title`, `.Detail`. |
| C3 | `OutboxBacklogAssessmentCharacterisationTests.cs` | spec §4.3. Private `ProbeDbContext`, `SuppressOpenInterceptor`, a `ScalarsInterceptor` returning a one-row `DataTable` (column `Value`, typed `long` then `int`) per call and recording `CommandText` (CRLF→LF). `CapturingLogger<OutboxBacklogHealthCheck<ProbeDbContext>>`. Schema `wolverine_probe`. |
| C4 | `Idempotency/IdempotencyStoreCharacterisationTests.cs` | spec §4.4. `UseNpgsql("Host=unused;…")` + `SuppressOpenInterceptor` + `RecordingReaderInterceptor(Queue<Func<DataTable>>)` capturing text and `(ParameterName, Value, NpgsqlDbType)` per command (cast to `NpgsqlParameter`). Claim table: column `Value` typed `DateTime`; read table: `ResourceIdentifier` (`Guid`, nullable), `Fab`, `RequestFingerprint` (`string`, nullable). Fixed timestamp with explicit `DateTimeKind`. Scopes built through `IdempotencyScope.For`. |

Expected SQL literals are written out in the test as `const string` raw literals, copied once
from the observed command text on `2b895f82`. They are never built from the production constant.
The interceptors are private nested types, copied rather than shared. Test projects do not
reference one another, and `Architecture.Tests`' copies stay untouched.

## 5. Proof strategy

1. **Observed green first**: C1-C4 pass on `2b895f82` (production untouched) in Release. Quote
   the output.
2. **Counterfactuals** (scratch edits, reverted, never committed; quote the red):
   - C4: delete `SET reserved_at = NOW(),` from the claim SQL → the byte pin goes red; change the
     `fab` parameter to `NpgsqlDbType.Text` → the parameter pin goes red; change `== 1` to `> 0`
     → O6 goes red; swap the mismatch and completed checks → O3f goes red.
   - C3: change `<` to `<=` on `ConcerningAttempts` → the 4/4,999 case or the attempts-5 case goes red.
   - C2: drop the `values.Count > 1` branch → the two-headers case goes red.
   - C1: return `true` for a non-wildcard `If-None-Match` → red.
3. **Refactor**, then re-run byte-unmodified (`git diff <green-sha> -- tests/` empty) after each commit.
4. **Build**: `dotnet build src/ServiceDefaults -c Release --no-incremental` → 3 S138 + 4 S107.
5. **Second observer**: CI integration shards (spec §4.5) green on the PR.
6. Phase 6: `/code-review` + `/security-review` (G1's `TryRead`, G3's `BeginAsync`).

## 6. Commit groups

| Group | Commits |
|---|---|
| G0 | `docs(spec): spec 368 …` (this directory) |
| G1 (header parsers) | `test(service-defaults): pin the upsert precondition and idempotency header refusals` (C1+C2) → `refactor(service-defaults): read the If-None-Match assertion in its own method` → `refactor(service-defaults): parse the idempotency key in its own method` |
| G2 (health) | `test(service-defaults): pin the outbox backlog's four read outcomes` (C3) → `refactor(service-defaults): assess the outbox backlog in its own method` |
| G3 (claim, last, deferrable per J1) | `test(service-defaults): pin the idempotency claim's SQL, parameters and outcomes` (C4) → `refactor(service-defaults): split the idempotency claim from the binding read` |

Each commit builds and passes `tests/ServiceDefaults.Tests` on its own (rebase-merge lands them
individually, ADR-0087).

## 7. Risks

| Risk | Mitigation |
|---|---|
| EF cannot materialise `DateTime` / `StoredBinding` from a `DataTableReader` | J1: G3 is deferred, G1/G2 ship. Try first, before writing all of C4. |
| CRLF vs LF in the raw literals (`.gitattributes`) makes the byte pin flaky across OSes | Normalise CRLF→LF on both sides only; nothing else. CI is Linux, dev is Windows: run once on each (CI covers Linux). |
| A refactor that moves the assessment out of the `try` | Called out in J4 and the plan table; a reviewer check. |
| A comment dropped while code moves | Reviewer diff check: every removed comment line reappears verbatim (`git diff --word-diff`). |
| Unit tests cannot observe concurrent reclaim | Not claimed. The Docker integration tests stay the observer (§4.5). |
| An expected literal copied from the production constant | Forbidden (plan §4); reviewer check. |
