# Spec 350: The overlay designer context under its limits

**Feature Branch**: `refactor/2892-overlay-designer-metrics` (cut from `origin/develop` at `8ded100e`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked explicitly (§6).

**Input**: Issue [#2892](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2892),
*Clear the ADR-0084 metric warnings in Overlay Designer's Domain, Application and Infrastructure (12)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Spec number.** 350. Checked on 2026-10-14: `origin/develop` (`8ded100e`) highest 348; every
remote branch via `git ls-tree` highest 348; worktree `sse-2891` holds unpushed 349 (#2891, system
variables); `sse-1545` nothing above 338; open PRs #2811 and #2837 carry no spec. Highest visible
349, so 350. Re-check before the PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S138 <= 30 lines/method, S107 <= 4
parameters; advisory `warning`), ADR-0144 (autonomous lane; phase 4a colours; the lane may not
weaken a gate), ADR-0036 (smallest change), ADR-0050 (`[LoggerMessage]` fields come from the
template), ADR-0113 (Layer-1 `If-Match` check before any mutation), ADR-0121 / spec 037 (branching
a stranded chain), ADR-0141 (`Option<T>` for absences in Application), ADR-0051 (per-context DI
module), ADR-0142 (idempotency store registration), ADR-0165 (overlay element kinds; the
`OverlayElement` construction lock), constitution §II (no primitives on a Domain model).

---

## 1. What was measured (premise verified, not assumed)

The issue's command does not run as written (`src/OverlayDesigner` holds no project file). The
equivalent, which compiles Domain and Application transitively:

```sh
dotnet build src/OverlayDesigner/Infrastructure/SmartSentinelEye.OverlayDesigner.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep OverlayDesigner
```

Result on `8ded100e`: 0 errors, **12 hits, exactly the 12 the issue lists, at the same lines. None
is stale.** Six sit in generated `Migrations/` (out of scope, §8). The same build also reports
eleven `ServiceDefaults` hits; they are not this context's (out of scope).

Baseline test counts on `8ded100e` (`dotnet test -c Release`): OverlayDesigner.Domain.Tests 297,
OverlayDesigner.Application.Tests 49, all green. There is **no** OverlayDesigner.Infrastructure.Tests
project; Infrastructure is reached from `Architecture.Tests` (which references it) and from
`Integration.Tests` (Docker, CI only).

## 2. Decision per hit

| # | File:line | Member | Rule (measured) | Decision |
|---|---|---|---|---|
| 1 | `Application/Commands/Handlers/BranchDraftRevisionCommandHandler.cs:14` | `HandleAsync` | S138 (33) | **Clear** by extraction |
| 2 | `Application/Commands/Handlers/EditDraftRevisionCommandHandler.cs:14` | `HandleAsync` | S138 (32) | **Clear** by extraction |
| 3 | `Domain/Overlay/OverlayElement.cs:72` | private 5-parameter constructor | S107 (5) | **Clear** by a private nested parameter object (J1) |
| 4 | `Domain/Overlay/OverlayElement.cs:118` | `OverlayElement.TextElement` (public factory) | S107 (5) | **Leave**: called by the Api layer (`Api/OverlayElementParsing.cs`) and at 40 call sites in 17 test files; a parameter object changes every caller and fails "tests unmodified" (J2) |
| 5 | `Infrastructure/OverlayDesignerInfrastructureModule.cs:36` | `AddOverlayDesignerInfrastructure` | S138 (35) | **Clear** by extraction (public signature unchanged) |
| 6 | `Infrastructure/Persistence/Configurations/OverlayConfiguration.cs:39` | `Configure` | S138 (122) | **Clear** by extraction |
| 7 | `.../Migrations/20260527135708_InitialOverlayDesigner.cs:12` | `Up` | S138 | **Out of scope**: generated migration |
| 8 | `.../Migrations/20260907011959_OverlayNameUniquePerLiveChain.cs:11` | `Up` | S138 | **Out of scope**: generated migration |
| 9 | `.../Migrations/20261004121044_AddOverlayRevisionLabels.cs:21` | `Up` | S138 | **Out of scope**: generated migration |
| 10 | same `:83` | `Down` | S138 | **Out of scope**: generated migration |
| 11 | `.../Migrations/20261005105722_AddOverlayElementKindAndColour.cs:20` | `Up` | S138 | **Out of scope**: generated migration |
| 12 | same `:166` | `Down` | S138 | **Out of scope**: generated migration |

**Cleared: 5** (4 S138, 1 S107). **Left: 1** (public Domain factory). **Out of scope: 6**
(migrations). Expected after-count of the §1 grep: **7** (hit 4 plus the six migrations); with
`| grep -v Migrations` appended, **1**. Hit 4's line number moves because its file is edited;
compare by member, not by line.

No `[LoggerMessage]` hit exists in this context (`Application/Log.cs` reports nothing).

## 3. User story

### US1 (P1): Overlay Designer's non-Api layers meet ADR-0084 wherever that is mechanical

As a maintainer, I want the 5 clearable hits gone, each by an extraction or a parameter object that
names one concept, **without** any change an HTTP caller, the kiosk, another context, a log reader
or the database could observe.

**Why P1 / independently shippable.** It is the whole issue: one context, no contract, no
migration, no runtime resource, no Api file.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: Every existing Overlay Designer test passes unmodified (happy path)
  Given OverlayDesigner.Domain.Tests (297) and OverlayDesigner.Application.Tests (49)
  When the refactor commits land
  Then all pass and `git diff <last 4a commit>..HEAD -- tests/` is empty

Scenario: Branching refuses in today's order (conflict)
  Given a chain at version 1 with an open Draft and no Published revision
  When it is branched with expected version 7
  Then the answer is OVERLAY_REVISION_STALE, not OVERLAY_NO_PUBLISHED_REVISION
  And the name lookup is never called and nothing is saved
  Given a fully archived chain whose name another overlay now holds
  When it is branched with the right version
  Then the answer is OVERLAY_NAME_TAKEN with today's exact message, after exactly one name lookup, and nothing is saved
  Given a healthy Published chain
  When it is branched
  Then no name lookup happens, the repository is saved once, and the log reads exactly today's text

Scenario: Editing a draft refuses in today's order (bad request / conflict)
  Given an empty element set for an overlay that does not exist
  Then the answer is OVERLAY_ELEMENTS_EMPTY and the repository is never called
  Given a stale version and a revision number that does not exist
  Then the answer is OVERLAY_REVISION_STALE
  Given a Published revision at the right version
  Then the answer is OVERLAY_REVISION_NOT_DRAFT with today's exact message and nothing is saved

Scenario: Overlay elements are built exactly as today (Domain)
  Then each factory keeps the instances it was handed, Text is present iff Kind is Text,
  and refusals keep their type, parameter name, message and order
  And a revision's elements are deep copies of their source (equal values, distinct instances, dense ordinals)

Scenario: The DI composition is unchanged
  Then AddOverlayDesignerInfrastructure registers the same Overlay Designer services, in the same order, with the same lifetimes

Scenario: The EF model is unchanged
  Then OverlayDesignerDbContext's logical model, read through the public metadata API, equals the baseline captured on 8ded100e
  And OverlayElement is still materialised through its (kind, color) constructor
```

**Auth scenario.** No authorisation boundary moves: no Api file, no scope, no `RequireScope`, no
fab resolution, no idempotency-key handling is touched. The idempotency store keeps its registration
(scoped, over `OverlayDesignerDbContext`), pinned by the DI scenario.

#### Independent test procedure (end to end)

1. The `dotnet test` runs of plan §5, green before and after.
2. The grep of §1 prints exactly the 7 rows of §2's "Leave" and "Out of scope" after the change.
3. CI's Integration.Tests `OverlayDesigner/*` green, notably `OverlayLifecycleIntegrationTests`,
   `OverlayRevisionLifecycleIntegrationTests`, `OverlayElementCheckConstraintIntegrationTests`,
   `OverlayNameUniquenessIntegrationTests` and `OverlayETagIntegrationTests`: they round-trip
   elements through real Postgres (EF materialisation through the constructor J1 touches). Docker
   only, so observed in CI, not locally.

## 4. Functional requirements

- **FR-001** The 5 "Clear" hits of §2 report nothing after the change. If an extraction leaves a
  hit, the PR says so with the measured value; it is never suppressed.
- **FR-002** No public or `protected` signature changes: the two command handlers and their
  constructors, `OverlayElement`'s public surface (`TextElement`, `Box`, `Ellipse`, properties,
  `MaxElements`), `AddOverlayDesignerInfrastructure`, `OverlayConfiguration.Configure`,
  `Shared.Contracts` and `Log.cs`.
- **FR-003** For every input: the order of refusals and of repository calls (`GetByIdentifierAsync`,
  `GetByNameAsync`, `SaveAsync`), error codes and messages, the log calls (method, arguments,
  order) and the returned value are identical.
- **FR-004** `OverlayElement` instances carry exactly today's property values and instance
  identities; EF still materialises it through the `(ElementKind kind, OverlayColor color)`
  constructor; `RequireConsistent` still runs once per factory call.
- **FR-005** DI registrations keep their order and lifetimes; the EF configuration produces the
  same model.
- **FR-006** No new primitive on a Domain model (constitution §II): the one new Domain type is a
  private nested type of value objects, unreachable from any public property.
- **FR-007** No suppression, `#pragma`, threshold, `.editorconfig` or `SonarLint.xml` change; no
  guard edited; no file under `Api/` or `Migrations/` touched; no existing test file edited.

## 5. Success criteria

- **SC-001** Before/after hit lists quoted in the PR body; after = the 7 rows of §2.
- **SC-002** Characterisation observed green on unrefactored code, green and unmodified after; both
  outputs quoted, each with its observed counterfactual (plan §4).
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the
  Domain, Application and Infrastructure projects; coverage gates hold (Domain >= 90 %,
  Application >= 80 %).

## 6. Judgement calls (marked)

- **J1. A private parameter object in Domain.** The five parameters of `OverlayElement`'s private
  constructor are one concept: the element's complete state. They become a
  `private readonly record struct` nested in `OverlayElement`, built from value objects only
  (§II). The positional form keeps today's guarantee that the one constructor which sets every
  property cannot be called with a part missing (a compile error, not a silent null). Measured on
  `8ded100e`: SonarAnalyzer does **not** report S107 on a positional record struct's primary
  constructor (a 5-member probe added to `OverlayElement.cs` produced only hits 3 and 4, then was
  reverted). The EF materialisation constructor keeps its signature. Rejected alternative: delete
  the 5-parameter constructor and set the owned parts through object initializers on the
  `(kind, color)` constructor; it spreads property assignment over four sites and loses the
  compile-time completeness.
- **J2. Leaving the public Domain factory.** `TextElement(string text, int fontSizePx, ...)` could
  take a parameter object, but the Api layer and every test caller would change. "Tests unmodified"
  and "no Api file" decide it. Precedent: spec 348 J2 (`RegisteredClient.Register`).
- **J3. The model characterisation lives in `Architecture.Tests`.** Siblings put it in
  `<Context>.Infrastructure.Tests`; this context has none, and creating a test project is out of
  proportion. `Architecture.Tests` already references `OverlayDesigner.Infrastructure` and composes
  its DbContext (`PostgresComposition`).

**No bug fix is folded in.** Oddities found and **pinned as-is, not changed**:

1. `EditDraftRevisionCommandHandler` validates the element set **before** the overlay lookup, so an
   empty or over-long set for an unknown overlay answers 400, not 404. Plausibly intended (cheap
   checks first); pinned.
2. Both handlers deconstruct with the `(var a, var b, ...) = command;` spelling, which
   `HandlerDeconstructionTests`' pattern (`var (...) = x;`) does not match, so the guard does not
   scan these two handlers today. Not changed here (rewriting the line would change guard coverage
   in a shape-only change); follow-up candidate for the guard.
3. The doc comment of `OverlayElement`'s 5-parameter constructor lists its callers as "the three
   factories, EF's materialisation constructor, or `AtOrdinal`" and omits `DeepClone`. The comment
   is rewritten anyway (J1); naming `DeepClone` there is permitted, as it is comment-only on the
   touched member.

## 7. Latency budget impact (constitution §IV)

**N/A, with reason.** None of the touched members is on a §IV leg. Overlay Designer is the
editor-time authoring context: branching and editing a draft are operator CRUD; `OverlayElement`
is constructed when an operator's request is parsed, when a revision is built or branched, and when
EF materialises an overlay for a query or at publish. The *Event -> overlay state* leg runs through
Automation and LayoutComposition, which carry overlay **identifiers** and highlight state, not
`OverlayElement` instances; the kiosk's *Composite + render* leg draws from its own TypeScript
model. No per-event code path constructs, clones or materialises an `OverlayElement`. J1 adds a
stack-allocated struct per element construction on these editor-time paths only.

## 8. Out of scope

Every `Api/` file (including `OverlayElementParsing` and `OverlayEndpoints.Commands.cs`, already
split by #2896), the six migrations, tests' own metrics, the `ServiceDefaults` hits shown by the same
build, hit 4, and the oddities of §6.

## 9. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052, ADR-0054), sentence-style names (ADR-0053),
`Ensure.That` guards (ADR-0105), `Option<T>` for absences in Application (ADR-0141), `var` wherever
it compiles, collection expressions, fluent DI chains, one parameter per line when wrapping, no
leading underscores (CLAUDE.md house rules). No Testcontainers (ADR-0103).
