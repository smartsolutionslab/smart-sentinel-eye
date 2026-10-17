# Spec 397: the EventIngestion route handlers carry ADR-0084's S107 suppression

**Feature Branch**: `chore/2971-eventingestion-s107-suppressions` (cut from `origin/develop` at `7d2d6af3`)

**Created**: 2026-10-18

**Status**: Specified. No `[NEEDS CLARIFICATION]` remains.

**Input**: Issue [#2971](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2971) — the
**comments**, not the body. The body asks for spec 341's `[AsParameters]` service bundles and forbids
any suppression; the owner's comment of 2026-10-17 supersedes both: ADR-0084's route-handler
amendment is accepted (#2970, merged `c7a6e586`), and its prescribed `[SuppressMessage]` is the one
new suppression the lane is authorised to add. Prior work: spec 380 (cleared the three helper hits,
PR #2997), spec 365 (the route-table characterisation this spec relies on).

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation,
observed green**. Engineer: **backend-engineer**. Not security-sensitive: no executable line
changes, and the attribute is not emitted to IL (§3, D-2); `/code-review` only.

**Spec number.** 397, assigned by the orchestrator (398 is #2972's, in parallel). Checked
2026-10-18 after `git fetch`: highest on `origin/develop` is 394; 395 is on
`origin/fix/2876-label-hold-below-two-tiles`; nothing carries 397. Re-check before the PR opens.

**ADRs.** **ADR-0084** and its 2026-10-16 amendment ("S107 does not apply to a minimal-API route
handler's request parameters"; "Recommended mechanism, and its cost") — the authority for every line
this spec adds. ADR-0144 (lane; characterisation colour; a suppression outside an accepted ADR would
be a weakened gate), ADR-0070 (Minimal APIs), ADR-0036 (smallest change), ADR-0087 (each commit
builds on its own).

**No new ADR.** The decision is ADR-0084's amendment, already accepted. This spec applies it.

## 1. Premise check — the re-measured hit list

Measured 2026-10-18 on `7d2d6af3` by building **only** the EventIngestion Api project (shared
machine; the project build compiles every project the Api depends on, which is every project whose
warnings could land under `src/EventIngestion/Api`):

```sh
dotnet build src/EventIngestion/Api/SmartSentinelEye.EventIngestion.Api.csproj -c Release --no-incremental \
  "-flp:logfile=metrics.log;verbosity=normal;NoSummary"
grep -oE '[^ ]+\([0-9]+,[0-9]+\): warning S107' metrics.log | tr '\\' '/' | sed -E 's#^.*/src/#src/#' \
  | sort -u | grep 'src/EventIngestion/Api'
```

0 errors. **10 S107 hits under `src/EventIngestion/Api`, all ten mapped route handlers; 0 helpers.**
The issue's original 13 less spec 380's three helpers (`DeclareAsync`, `RegisterAsync`,
`AuthenticateWebhookAsync` — absent, stay cleared). `Log.cs` is absent too: ADR-0084's first
amendment exempts it in `.editorconfig` (`[src/**/Log.cs]`).

| # | Hit | Method | Mapped at | Counted |
|---|---|---|---|---|
| H1 | `EventsEndpoints.Writes.cs(21,52)` | `IngestManual` | `POST /events/manual` | 6 |
| H2 | `EventsEndpoints.Writes.cs(172,53)` | `IngestWebhook` | `POST /events/webhook/{integrationName}` | 5 |
| H3 | `EventsEndpoints.Reads.cs(16,50)` | `ListEvents` | `GET /events/` | 12 |
| H4 | `EventsEndpoints.Reads.cs(114,55)` | `ListDeadLetters` | `GET /events/dead-letters` | 6 |
| H5 | `EventSourcesEndpoints.cs(121,47)` | `Declare` | `POST /event-sources/` | 6 |
| H6 | `EventSourcesEndpoints.cs(224,46)` | `Change` | `PUT /event-sources/{source}/mode` | 6 |
| H7 | `EventSourcesEndpoints.cs(284,49)` | `Undeclare` | `DELETE /event-sources/{source}` | 5 |
| H8 | `EventTypesEndpoints.cs(94,48)` | `Register` | `POST /event-types/` | 6 |
| H9 | `EventTypesEndpoints.cs(195,46)` | `Retire` | `DELETE /event-types/{kind}` | 5 |
| H10 | `WebhookIntegrationsEndpoints.cs(155,46)` | `Revoke` | `DELETE /webhook-integrations/{name}` | 5 |

Every row is a `private static async Task<IResult>` passed as a method group to a `Map*` call and
appears by name in `EventIngestionRouteTableCharacterisationTests`' baseline. "Counted" is Sonar's
message; it excludes `[FromServices]` parameters (ADR-0084 amendment, evidence bullet 1).

**Every other warning the same build reports** (the "unchanged" set, §4 SC-2) — 13 lines, none under
`src/EventIngestion/Api`:

```text
src/EventIngestion/Application/Commands/Handlers/IngestEventBatchCommandHandler.cs(37,51): S107
src/EventIngestion/Application/Commands/Handlers/IngestEventCommandHandler.cs(25,46): S107
src/EventIngestion/Domain/DeadLetter/DeadLetter.cs(70,37): S107
src/EventIngestion/Domain/Event/Event.cs(39,31): S107
src/EventIngestion/Domain/SourceMode/SourceMode.cs(30,37): S107
src/EventIngestion/Infrastructure/Persistence/Migrations/20260528163630_InitialEventIngestion.cs(11,33): S138
src/EventIngestion/Infrastructure/Persistence/Migrations/20260528171138_WebhookIntegrationsAndDeadLetters.cs(12,33): S138
src/EventIngestion/Infrastructure/Persistence/Migrations/20260817213441_WebhookIntegrationFab.cs(31,33): S138
src/ServiceDefaults/Authentication/ClientCredentialsTokenProvider.cs(88,42): S107
src/ServiceDefaults/Authorization/FabResolution.cs(49,75): S107
src/ServiceDefaults/Idempotency/IdempotencyScope.cs(46,39): S107
src/ServiceDefaults/WolverineDefaults.cs(40,43): S138
src/ServiceDefaults/WolverineDefaults.cs(40,77): S107
```

No other warning ID (CS, IDE, CA, other S) appears in the log. Domain, Application and
ServiceDefaults hits are **out of the amendment's scope** and stay advisory; nothing here touches
them.

## 2. User story

### US1 (P1) — S107 measures only what a person could act on, in EventIngestion's Api layer

As a maintainer reading the S107 count, I want the ten route handlers whose parameters *are* the
HTTP contract to carry the exemption ADR-0084 decided, written where it moves with the method, so
that the Api layer reports zero S107 and a future helper with five parameters is a visible new hit.

**Independent test.** Release `--no-incremental` build of the Api project: S107 under
`src/EventIngestion/Api` is empty; every line of §1's "unchanged" set is present and nothing else is
added. The route-table characterisation and the six source-scanning declaration guards pass with
`git diff origin/develop -- tests/` empty.

#### Acceptance scenarios

```gherkin
Scenario: the ten handlers no longer report S107
  Given the ten handlers of §1 each carry the S107 SuppressMessage of plan §2
  When the Api project is built in Release with --no-incremental
  Then no "warning S107" line names a file under src/EventIngestion/Api
  And the remaining warnings are exactly §1's thirteen-line set

Scenario: the suppression is what cleared the hit (counterfactual)
  Given one handler's SuppressMessage is removed in a scratch edit
  When the Api project is rebuilt
  Then exactly that handler's S107 line returns, at the same column
  And the scratch edit is reverted

Scenario: the route table does not move
  Given EventIngestionRouteTableCharacterisationTests renders method-level attributes as endpoint metadata
  When it runs against the annotated handlers
  Then it passes unmodified, because SuppressMessageAttribute is [Conditional("CODE_ANALYSIS")] and not emitted

Scenario: a helper is not exempted (scope conflict)
  Given ADR-0084's amendment covers mapped route-handler methods only
  When the diff is reviewed
  Then no SuppressMessage sits on a method that no Map* call names
  And no .editorconfig, SonarLint.xml, Directory.Build.props or csproj line changes

Scenario: wire behaviour is untouched (bad request / auth)
  Given the endpoint characterisation tests for ingest, sources, types and webhooks
  When they run after the change
  Then every 400, 401, 403, 409, 422 and 428 they assert is unchanged and no assertion was edited
```

Auth and bad-request behaviour are covered by existing tests rather than new ones: the change has
no executable line, so there is no new behaviour to start red (constitution §Testing, ADR-0144).

## 3. Decisions

- **D-1 — scope is the ten handlers, nothing else.** All ten hits are handlers (§1); there is no
  helper hit left to treat "the pre-amendment way", so there is nothing to list as out of scope in
  the Api layer.
- **D-2 — `SuppressMessage`, never `UnconditionalSuppressMessage`.** The route-table baseline
  renders method-level attributes as endpoint metadata (`ListEvents` shows
  `other System.Runtime.CompilerServices.NullableContextAttribute`). `SuppressMessageAttribute` is
  `[Conditional("CODE_ANALYSIS")]`; `CODE_ANALYSIS` is defined nowhere in the repository (searched
  props, targets, csproj, cs), so the attribute stays in source and never reaches metadata, the
  route table or OpenAPI. `UnconditionalSuppressMessageAttribute` *is* emitted and would add an
  `other …` line to the baseline — a block.
- **D-3 — one literal justification, identical on all ten.** ADR-0084 prescribes "a
  `[SuppressMessage]` for S107 with a justification"; it does not fix the words. Plan §2 fixes them
  so a reviewer can grep the exemption (`grep -rn '"S107:' src`) and count it. No shared constant
  (a cross-context `const` in ServiceDefaults is a new abstraction for one string — ADR-0036).
  The text should match the one the sibling specs for #2972-#2975 use; see plan §6.
- **D-4 — no new tests.** The covering tests exist and are discriminating for this change: the
  route-table baseline pins all ten parameter lists *and* method-level metadata; the declaration
  guards resolve each handler body by its declaration line (`RouteChainReader.MethodBodies`), which
  an attribute on the preceding lines does not affect. Phase 4a is therefore the existing suite,
  observed green before and unmodified after, plus the build-log measurement.
- **D-5 — S104 stays clear.** Plan §2's form adds 5 lines per handler plus one `using`;
  `EventSourcesEndpoints.cs` (three handlers) grows from ~260 to ~276 code lines, under S104's 300.
  SC-2 catches it if the estimate is wrong.

## 4. Success criteria

- **SC-1**: S107 under `src/EventIngestion/Api` goes from 10 to 0 (§1's command).
- **SC-2**: every other warning of that build is exactly §1's thirteen-line set — no new S104, S138,
  S1541, S134, IDE0005 or any other line.
- **SC-3**: `git diff origin/develop -- tests/` is empty, and plan §4's suites pass, observed green
  before the change and after it.
- **SC-4**: counterfactual observed (scenario 2), verbatim build line quoted in the PR.
- **SC-5**: the diff touches only the five endpoint files; each touched file gains exactly one
  `using System.Diagnostics.CodeAnalysis;` and one attribute per §1 handler it holds.

## 5. Latency budget

N/A. No executable line changes; the attribute is compile-time only (D-2). No leg of constitution
§IV is touched.

## 6. Closing #2971

After this lands, every hit #2971 named is resolved: 3 refactored (spec 380), 10 exempt by
ADR-0084's second amendment (this spec), `Log.cs` exempt by its first amendment. The PR uses
`Closes #2971`; check the issue state after the merge (a mention rarely auto-closes).

## 7. Assumptions (marked)

- **A-1** The project-scoped build reports every warning a solution build would for files under
  `src/EventIngestion/Api` (a warning belongs to the project that compiles the file). Not
  re-checked against a full solution build, to keep load off the shared machine.
- **A-2** Roslyn honours a source-level `SuppressMessage` for a Sonar diagnostic without
  `CODE_ANALYSIS` defined. Scenario 2 proves it on the first handler before the other nine are
  annotated.
- **A-3** No OpenAPI document snapshot exists for EventIngestion; the route-table baseline is the
  metadata OpenAPI is generated from (its own summary says so), so it stands in for an OpenAPI diff.
