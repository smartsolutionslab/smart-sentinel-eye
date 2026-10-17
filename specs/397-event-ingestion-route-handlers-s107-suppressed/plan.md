# Plan 397: the EventIngestion route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2971.

## 1. Context and layers

Bounded context **EventIngestion**, **Api layer only** (`src/EventIngestion/Api`). No Domain,
Application, Infrastructure, Shared.Kernel, Shared.Contracts or AppHost file. No entity, value
object, invariant, domain event or integration event changes; no messaging; no boundary rule is
approached (no new project reference, no new type). Constitution §II is not engaged (no domain
model state).

## 2. The change — one attribute form, ten placements

### 2.1 The form (verbatim; identical on all ten handlers)

```csharp
    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
```

- Shape mirrors the repository's one existing `SuppressMessage`
  (`src/AuditObservability/Domain/AuditEvent/AuditEvent.cs:121`): one argument per line, the
  justification wrapped with a leading `+`.
- Roslyn matches a suppression on the ID before the colon (`S107`); the category and the title after
  the colon are documentation. They are the SonarSource category and rule title for S107.
- `System.Diagnostics.CodeAnalysis.SuppressMessageAttribute` — **not**
  `UnconditionalSuppressMessageAttribute` (spec D-2: the latter is emitted and would change the
  route-table baseline).
- Each file gains `using System.Diagnostics.CodeAnalysis;` in its sorted position (not an implicit
  using; `Log.cs` is the only Api file that has it today). No `global using`.

### 2.2 Placement

Directly above the `private static async Task<IResult>` line, **after** any `/// <summary>` block
(so the XML doc still attaches to the method — `Undeclare` has one), before nothing else. Nothing
else in the method, its signature, its body or its `Map*` chain changes.

| File | Handlers |
|---|---|
| `src/EventIngestion/Api/EventsEndpoints.Writes.cs` | `IngestManual`, `IngestWebhook` |
| `src/EventIngestion/Api/EventsEndpoints.Reads.cs` | `ListEvents`, `ListDeadLetters` |
| `src/EventIngestion/Api/EventSourcesEndpoints.cs` | `Declare`, `Change`, `Undeclare` |
| `src/EventIngestion/Api/EventTypesEndpoints.cs` | `Register` (not `RegisterAsync`), `Retire` |
| `src/EventIngestion/Api/WebhookIntegrationsEndpoints.cs` | `Revoke` |

**Not** on: `GetEvent`, any `List` handler, `WebhookIntegrationsEndpoints.Register` (not hits — a
suppression that suppresses nothing is noise and misstates what is exempt), any helper,
`Log.cs`, any `*Services` record.

## 3. What could break, and why it does not

| Reader | How it reads the handler | Effect of the attribute |
|---|---|---|
| `EventIngestionRouteTableCharacterisationTests` | Endpoint metadata, which includes the handler method's custom attributes (rendered as `other <type>`) | None: `SuppressMessageAttribute` is `[Conditional("CODE_ANALYSIS")]`, `CODE_ANALYSIS` defined nowhere, so not emitted. If it were, this test goes red — it is a discriminating detector here, not a tautology. |
| `RouteChainReader.MethodBodies` (used by Precondition/EndpointScope/IdempotentKeyReuse/RouteValueRefusal/StatusProducer/ConcurrencyConflict declaration tests) | Regex `(?:private\|public\|…)[^;{}()\n]*?\bName\s*\(` on literal-masked source, then the balanced body | None: the attribute sits on preceding lines; the regex forbids `\n` and `(` between modifier and name, and the justification is a masked literal. |
| The guards' `…offers_no_way_to_excuse…` / `suppress` vocabulary theories | Scan **the guard's own source** (`GuardSource`), not endpoint files | None. |
| `HandlerDeconstructionTests` | `Handle`/`HandleAsync` signatures | Not a route handler; unaffected. |
| Line-number pins | Searched `tests/` for `EventIngestion/Api/*.cs:<line>` and file+line tuples: none | Shifted lines are harmless. |
| OpenAPI | Generated from the same endpoint metadata | Unchanged (no emitted attribute); no EventIngestion OpenAPI snapshot exists (spec A-3). |
| S104 (300 LOC/file) | Sonar LOC | `EventSourcesEndpoints.cs` ~260 → ~276. Measured by SC-2. |

## 4. Gate measurement and covering suites (Phase 4a: characterisation, observed green)

### 4.1 Measurement (before and after; orchestrator or engineer)

```sh
dotnet build src/EventIngestion/Api/SmartSentinelEye.EventIngestion.Api.csproj -c Release --no-incremental \
  "-flp:logfile=<scratch>/metrics.log;verbosity=normal;NoSummary"
grep -oE '[^ ]+\([0-9]+,[0-9]+\): warning [A-Za-z]+[0-9]+' <scratch>/metrics.log | tr '\\' '/' \
  | sed -E 's#^.*/src/#src/#' | sort -u
```

Before: spec §1's 10 + 13 = 23 lines. After: exactly spec §1's 13. Diff the two sorted lists; the
only difference may be the ten removed lines. Log to the session scratchpad, never the worktree.
Stop any running AppHost first (it holds binaries — MSB3027).

### 4.2 Counterfactual (once, scratch edit, reverted)

Annotate `ListEvents` first. Build: H3 gone. Remove the attribute, rebuild: H3 returns at
`EventsEndpoints.Reads.cs(<line>,50)`; restore it. Quote both lines in the PR. This proves
spec A-2 before the other nine are added.

### 4.3 In-process suites (test-writer captures green on unchanged code; re-run unmodified after)

`tests/Architecture.Tests` (no Aspire stack needed; in-process endpoint hosts):

```sh
dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj -c Release --filter \
"FullyQualifiedName~EventIngestionRouteTableCharacterisationTests|FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~EventIngestEndpointCharacterisationTests|FullyQualifiedName~EventSourceAndTypeEndpointCharacterisationTests|FullyQualifiedName~WebhookEndpointCharacterisationTests|FullyQualifiedName~WebhookRotationEndpointCharacterisationTests|FullyQualifiedName~HandlerDeconstructionTests|FullyQualifiedName~SourceScanCharacterisationTests"
```

Quote the summary line (non-zero passed count) before and after. If the machine has room, also run
the whole `Architecture.Tests` project once after (the shard filters are not touched: no new test
class). No Aspire-fixture run is required: nothing executable changed, and the shared machine has
one stack (memory: one machine, one Aspire stack).

`git diff origin/develop -- tests/` must be empty at every commit. An assertion that would need
editing is a **block**, reported, not adjusted (ADR-0144).

## 5. Commits

One commit per file, each building on its own (ADR-0087), Conventional Commits (ADR-0030), no
`Co-Authored-By` (ADR-0086). E.g. `chore(event-ingestion): exempt the events read handlers from S107
(ADR-0084)`. The spec commit precedes them.

## 6. Cross-spec consistency (orchestrator)

Spec 398 (#2972, LayoutComposition) applies the same amendment in parallel. The form in §2.1 should
be byte-identical there — the justification names no context, so it can be. If 398's architect chose
different words, reconcile before either PR merges; the grep `grep -rn '"S107:' src` is how the
45-handler total in ADR-0084's amendment becomes checkable.

## 7. Constitution / ADR check

- ADR-0084 (+ 2026-10-16 amendment): applied exactly — handlers only, the recommended mechanism, no
  `.editorconfig` section, helpers stay subject.
- ADR-0144: the suppression is the owner-authorised one; no test, threshold, analyzer or other
  suppression changes; Phase 4a characterisation colour.
- ADR-0036: no shared constant, no helper, nothing beyond the ten attributes and five `using`s.
- Constitution §IV: N/A (spec §5).
