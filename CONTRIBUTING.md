# Contributing to Smart Sentinel Eye

Thanks for working on Smart Sentinel Eye. This guide is the practical
companion to the **[constitution](.specify/memory/constitution.md)** —
read that first for principles and stack; this file is how we apply them
day-to-day.

> If anything here conflicts with the constitution, the constitution
> wins, and the contradiction is itself a bug — please open a PR to fix.

## TL;DR

1. Read `.specify/memory/constitution.md` and `docs/adr/0000-initial-decisions.md`.
2. New work? Run `/speckit-specify` — it creates a feature branch off `develop` and a spec.
3. Open a PR into `develop` using the template; squash- or rebase-merge after review + green CI.
4. Releases cut `release/x.y.z` from `develop`, stabilize, then merge to **both** `main` and `develop` and tag on `main`.

---

## Branching (GitFlow) — ADR-028

```
main              v1.0.0      v1.0.1     v1.1.0
  \             /            /         /
   release/1.0.0 ─┐         /         /
                  └── hotfix/cve-x ───/
   release/1.1.0  ────────────────────
develop  ───────────────────────────────────
  \    \    \
   001-camera-register
        002-camera-stream
              003-overlay-text
```

- **`main`** — production-released code only. Always tagged. Direct
  pushes forbidden.
- **`develop`** — integration line. Default branch on GitHub. New work
  merges here.
- **`NNN-feature-name`** — Spec-Kit feature branches, cut from
  `develop`, merge back into `develop`. Spec-Kit creates these on
  `/speckit-specify` with sequential numbering.
- **`release/x.y.z`** — cut from `develop` when freezing for release.
  Only fixes and release-prep commits land here. Merges to **both**
  `main` and `develop`.
- **`hotfix/<short>`** — cut from `main` for production fixes. Merges
  to **both** `main` and `develop`.

Non-spec work (ADR-only edits, infra config, dependency bumps) uses a
short-lived branch off `develop` with the Conventional Commit type as
the prefix (e.g. `docs/adr-029-protection`, `chore/bump-aspire`).

## Branch protection — ADR-029 + ADR-0087

Both `main` and `develop` enforce:

- ✅ PR required (no direct pushes)
- ✅ **Rebase-only merges** (no squash, no merge commits). Every
  feature-branch commit lands individually on the target branch.
- ✅ Required passing checks: build, unit, integration, NetArchTest,
  format/lint, secret scan, container smoke
- ✅ ≥ 1 approving review for PRs touching `src/` or `apps/web/`
  (docs-only PRs relaxed but still require checks)
- ✅ Force-push blocked
- ✅ Branch deletion blocked
- ⬜ Signed commits — deferred until GPG/SSH signing is set up

## Commit messages — ADR-030

**Human commits** use [Conventional Commits](https://www.conventionalcommits.org/)
with scope = bounded context.

```
feat(camera-catalog): register camera by RTSP URL
fix(stream-distribution): handle SFU failover within 5 s
refactor(overlays): extract CEL evaluator to Shared.Kernel
docs(adr): add ADR-028 git workflow
test(automation): cover cron trigger conflict resolution
chore(repo): bump dotnet workload to 10.0.301
ci(workflows): cache aspire publish artefacts
```

Allowed **types:** `feat`, `fix`, `docs`, `refactor`, `test`, `chore`,
`perf`, `build`, `ci`.

Allowed **scopes:** the 9 bounded contexts —
`camera-catalog`, `stream-distribution`, `layout-composition`,
`system-variables`, `event-ingestion`, `overlays`, `automation`,
`identity`, `audit-observability` — plus `repo`, `infra`, `web`,
`tests`, `deploy`, `adr`, `workflows`.

**Body** explains *why*, not *what*. Required for non-trivial commits.

**No `Co-Authored-By` footer** (ADR-0086). Every commit is attributed
solely to the human author, regardless of LLM assistance. PR
descriptions remain the place to document significant LLM involvement.

**Breaking changes** use `!` after type/scope and a `BREAKING CHANGE:`
footer:

```
feat(api)!: rename camera.id to camera.publicId

BREAKING CHANGE: existing API consumers must use camera.publicId
instead of camera.id. Migration path: /docs/migrations/0042.md.
```

**Spec-Kit auto-commits** keep their `[Spec Kit] <stage>` prefix —
they're a separate namespace and don't need to conform.

`commitlint` runs via Husky on `commit-msg` and rejects non-conforming
human commits.

## Pull requests — ADR-031

PR title mirrors the squashed commit message (Conventional Commits
format). The repo's `.github/pull_request_template.md` provides the
body skeleton. **Mandatory sections:**

- **Linked** — spec(s) and ADR(s) the PR implements or amends.
- **Summary** — 2–4 bullets, what changed and why.
- **Latency budget impact** — which legs of the
  [latency budget](.specify/memory/constitution.md#iv-the-latency-budget-is-sacred)
  this PR touches, measured values vs budget. Use **`N/A`** if the PR
  is not on the streaming or overlay path.
- **Test plan** — checkbox list of what was tested and how.
- **Breaking change?** — `[ ] yes  [x] no`.

PR review:

- Reviewers may request additional context. Missing mandatory sections
  is a review blocker.
- `ultrareview` (cloud multi-agent review) is run by the author for
  PRs touching security boundaries: Identity & Authorization, Event
  Ingestion validation, StreamKeeper PTP/network code.

## CI gates — ADR-033

GitHub Actions runs the following on **every** PR into `develop` and
`main`. All are required for merge.

| Job | Tool |
|---|---|
| .NET build (Release) | `dotnet build` |
| .NET unit tests | xUnit |
| Boundary rules | `NetArchTest` (in test project) |
| .NET format | `dotnet format --verify-no-changes` |
| Web build | `vite build` |
| Web type-check | `tsc --noEmit` |
| Web lint | ESLint |
| Web unit tests | `vitest run` |
| Secrets scan | `gitleaks` |
| Integration tests | Aspire AppHost via the `AspireFixture` (real Postgres, RabbitMQ, Keycloak, MinIO) |
| Container smoke | `aspire publish --target k8s` |

**Exemption:** PRs touching only `docs/`, `specs/`, or top-level `*.md`
skip integration and container smoke via `paths-ignore`. Format/lint
and secret scan still run.

Expect ~10–20 minutes per code PR. Self-hosted runners on the
roadmap once concurrency demands it.

## Versioning and releases — ADR-032

SemVer for the whole product. Tags only on `main`.

| Step | Action |
|---|---|
| 1. Feature work | Spec-Kit branch → PR → squash-merge to `develop`. Conventional Commit subject drives the change category. |
| 2. Cut release | `git checkout -b release/x.y.z develop` |
| 3. Stabilize | Only `fix`, `docs`, `chore` commits on the release branch. No new features. |
| 4. Tag and merge | PR `release/x.y.z` → `main`. After merge, tag `vx.y.z` on `main`. PR `release/x.y.z` → `develop` to pull stabilization fixes back. |
| 5. Changelog | Generated from Conventional Commits between this tag and the previous tag. Tool TBD (`release-please` or `changesets`). |

Pre-releases use `-alpha.N`, `-beta.N`, `-rc.N` suffixes. Hotfixes
bump patch and follow the same dual-merge pattern from `hotfix/<short>`.

## Code style — ADR-034 + ADR-0064 + ADR-0084

- **C#:** `dotnet format` enforces `.editorconfig` + Roslyn analyzers
  (`EnableNETAnalyzers=true`, `AnalysisLevel=latest`,
  `AnalysisMode=Recommended`) plus **SonarAnalyzer.CSharp**. Warnings
  are errors in `Release` builds.
- **TypeScript / React:** ESLint + Prettier with the configs shipped
  in `apps/web/`.
- **YAML / Markdown:** Prettier defaults.
- **Line length:** 160 (ADR-0064).
- **Code metric limits** enforced by SonarAnalyzer (ADR-0084):
  - Max LOC per file: **300** (S104)
  - Max LOC per method: **30** backend / **50** frontend (S138)
  - Max parameters: **4** (S107)
  - Cyclomatic complexity: **≤ 10** (S1541)
  - Nesting depth: **≤ 3** (S134)
  - Test projects exempt — narrative test methods are valuable.

Husky pre-commit hooks run formatters on **staged files only** — fast
local loop. CI verifies with `--verify-no-changes` so a bypassed hook
still fails the PR.

## Guided phased development — ADR-0037

Every feature or non-trivial change moves through **seven phases**.
Each phase has a concrete artifact and an **explicit gate** —
Claude Code stops between phases and asks for your confirmation
before continuing.

| # | Phase | Command(s) | Artifact | Gate |
|---|---|---|---|---|
| 1 | **Specify** | `/speckit-specify` (+ `/speckit-clarify`) | `specs/NNN-x/spec.md` | Spec reviewed; no `[NEEDS CLARIFICATION]` left. |
| 2 | **Plan** | `/speckit-plan` | `specs/NNN-x/plan.md` | Plan aligns with constitution + ADRs. |
| 3 | **Tasks** | `/speckit-tasks` + `/speckit-taskstoissues` | `tasks.md` + GitHub issues on Project #13 | Tasks atomic and independently testable. |
| 4 | **Implement** | `/speckit-implement` (Karpathy-guided) | Code + tests; format clean; analyzers clean | Tests green; commits follow ADR-0030. |
| 5 | **Verify** | `/verify` (or explicit run/test) | Verification note on the PR | Behaviour observed end-to-end. UI changes: run the app, click through golden + edge paths. Backend: Aspire AppHost integration green. Latency-sensitive: measured value per leg vs budget. |
| 6 | **QA / Review** | `/code-review`; `/security-review` if security-sensitive | Findings list, each resolved or noted | All findings resolved or carry written rationale in the PR. |
| 7 | **PR** | `gh pr create` with template; respond to CI | PR open against `develop`, template fully filled, CI green | Reviewer approval + green CI + merge. |

**Karpathy guidelines (ADR-0036)** are invoked automatically during
phases 4–6.

**Skipping a phase** is allowed only for trivial changes (typo fix,
dependency bump, comment-only). Write
`Phase X: skipped — <one-line reason>` in the PR body. Documentation-
and ADR-only PRs typically skip phases 5 and 6.

**Resumability.** Each phase's artifact is the resumption point. If
interrupted, the next session reads the latest artifact and continues
from there — no rework.

### Slash commands you'll use

```
/speckit-constitution   amend principles (rare; requires ADR)
/speckit-specify        phase 1; creates NNN-feature branch off develop
/speckit-clarify        de-risk ambiguities before planning (optional)
/speckit-plan           phase 2
/speckit-tasks          phase 3
/speckit-taskstoissues  push tasks into Project #13
/speckit-implement      phase 4
/speckit-checklist      (optional) completeness checklist
/speckit-analyze        (optional) cross-artifact consistency
/verify                 phase 5 — run the app / tests, confirm behaviour
/code-review            phase 6 — review the diff for correctness bugs
/security-review        phase 6 (security boundaries) — review for vulns
```

**Don't** write code outside this loop unless the work is docs / ADR /
infra (`chore`, `docs`, `ci`, `build` types) — those use a short
branch off `develop` and a docs-only PR.

## Coding behavior — Karpathy guidelines (ADR-0036)

The `andrej-karpathy-skills:karpathy-guidelines` skill is the
**baseline coding behaviour** for this repository. Whether the author
is human or an LLM agent, the same operational rules apply:

- **Smallest possible change.** Fix the bug, nothing else. Refactor
  changes shape, not behaviour. Don't mix.
- **Define "done" up front.** State the verifiable success criterion
  before writing code.
- **Surface assumptions; don't bury them.** Ask one or two clarifying
  questions before guessing. Mark unavoidable guesses in the PR body.
- **No speculative generality.** No abstractions for needs that don't
  exist yet — except the forward-compat interfaces in constitution §IX.
- **No drive-by error handling.** Validate at trust boundaries only.
  Swallowed exceptions are review blockers.
- **No drive-by comments.** Code says what; comments explain *why*,
  only when non-obvious. Task references belong in the PR body.
- **Read before write.** Mirror existing patterns; don't invent new
  ones without justification.

## Domain conventions — DDD

Driven by ADRs 0016, 0038–0045, 0066, 0069.

- **Bounded contexts (9)** are isolated. No cross-context project
  references; communicate only via `Shared.Contracts` integration
  events. Boundaries enforced by `NetArchTest` in
  `tests/Architecture.Tests/`.
- **Value objects are maximalist** (ADR-0038). Wrap every primitive
  at a domain boundary — IDs, semantic primitives (`Url`, `Percentage`,
  `Duration`), free-text fields (`CameraName`, `Description`).
  Framework-typed values (`DateTimeOffset`) may stay unwrapped.
- **Value objects are hand-written** sealed `record`s with private
  constructors, `.From(...)` factories, and `Ensure.That(...)`
  validation chains (ADR-0046, ADR-0059). They implement
  `IValueObject<T>` (ADR-0066).
- **Aggregate IDs are `Guid v7`** wrapped in `readonly record struct`
  with `IValueObject<Guid>` (ADR-0039, ADR-0090). Create with
  `Guid.CreateVersion7()`.
- **ID types use the `Identifier` suffix**, never `Id`
  (ADR-0090): `CameraIdentifier`, `LayoutIdentifier`,
  `OverlayIdentifier`, `OperatorIdentifier`, `KioskIdentifier`,
  `CellIdentifier`.
- **Properties and variables holding identifiers are named after the
  noun**, not the suffix (ADR-0094):
  ```csharp
  public OwnerIdentifier Owner { get; private set; }    // not OwnerIdentifier, not OwnerId
  var camera = CameraIdentifier.New();                  // not cameraIdentifier, not cameraId
  ```
- **Aggregate roots inherit `AggregateRoot<TId>`** which carries
  `Version` (ADR-0043) and the domain-event list. Behaviour lives on
  the aggregate — rich model, not anemic (ADR-0045).
- **Domain events** stay inside the context; **integration events**
  in `Shared.Contracts` with explicit `V<N>` suffix (ADR-0040,
  ADR-0073). Translation happens in a handler inside the publishing
  context.
- **Repository interfaces in `Domain/Repositories/`**, implementations
  in `Infrastructure/Persistence/` (ADR-0041). Per-aggregate
  interfaces only — no generic `IRepository<T>`.
- **Custom `Deconstruct(...)`** on value objects and request DTOs so
  endpoints can `var (name, url) = request;` (ADR-0069).
- **Plural variable names** for repository injections
  (`ICameraRepository cameras` — not `repository`, not `cameraRepo`).
- **No shortcuts or aliases** in any identifier (ADR-0091): spell out
  `Identifier`, `Repository`, `Manager`, `Configuration`, `Context`,
  `Database`, `Message`, `Authentication` / `Authorization`,
  `Request`, `Response`, `Service`, `Command`, `Query`, `Handler`,
  `Event`. Industry-standard exceptions (`Url`, `Json`, `Api`, `Sdk`,
  `Dto`, `Cqrs`, `Rbac`, `Ptz`, `Rtsp`, `Onvif`, etc.) remain.
- **Domain folder structure: per aggregate** (ADR-0092). Each
  aggregate root gets its own folder containing the aggregate, its
  identifier, all value objects, the repository interface, and an
  `Events/` subfolder for its domain events:
  ```
  CameraCatalog.Domain/
    Camera/
      Camera.cs, CameraIdentifier.cs, CameraName.cs, RtspUrl.cs,
      ICameraRepository.cs,
      Events/CameraRegisteredDomainEvent.cs
    CameraGroup/
      …
  ```
- **Domain events named `<Aggregate><Verb>DomainEvent`** to
  distinguish from integration events (`<Aggregate><Verb>V1`).

## Application & infrastructure conventions

Driven by ADRs 0042, 0047–0051, 0057, 0070, 0071, 0072.

- **Application folder structure: per message kind** (ADR-0093). Top-
  level folders `Commands/`, `Queries/`, `EventHandlers/`, `DTOs/`.
  Each command/query has its own file plus a paired
  `<Verb><Aggregate>Errors.cs`; handlers live in a `Handlers/`
  subfolder:
  ```
  CameraCatalog.Application/
    Commands/
      RegisterCameraCommand.cs, RegisterCameraErrors.cs,
      Handlers/RegisterCameraCommandHandler.cs
    Queries/
      GetCameraByIdentifierQuery.cs, …
      Handlers/GetCameraByIdentifierQueryHandler.cs
    EventHandlers/
      CameraRegisteredDomainEventHandler.cs       (in-process)
      OperatorRevokedIntegrationEventConsumer.cs  (from RabbitMQ)
    DTOs/
      CameraDto.cs
  ```
- **Handlers** implement `ICommandHandler<TCommand, TResult>` or
  `IQueryHandler<TQuery, TResult>` from `Shared.CQRS`. **Wolverine
  dispatches them under the hood** (ADR-0042, ADR-0057). Application
  code does not import Wolverine types.
- **Return `Result<T, Error>`** for expected business failures, with
  a sealed-record error hierarchy per handler (ADR-0047). Per-handler
  errors inherit from `ApiError(string Code, string Message,
  HttpStatusCode Status)` (ADR-0089) so endpoints emit RFC 7807
  Problem Details without per-case mapping. Exceptions are reserved
  for bugs and infra; never swallowed.
- **Use `Option<T>`** from `Shared.Kernel` for absences. NRT is
  **disabled** at the solution level (ADR-0048).
- **Every public async method takes `CancellationToken ct` as the
  last parameter** (ADR-0049). No `ConfigureAwait`. No
  sync-over-async.
- **Inject `ILogger<T>`**; Serilog provides the implementation;
  structured fields only (`{CameraId}` placeholders, not string
  interpolation) (ADR-0050).
- **DI registration** lives in per-context `Add<Context>{Infrastructure,Api}`
  extension methods (ADR-0051). No assembly scanning for application
  code; Wolverine's handler discovery is the framework exception.
- **API style: Minimal APIs only** (ADR-0070). Endpoints registered
  via `Map<Context>Endpoints` static methods; handlers are static
  local functions inside the registration method.
- **CQRS read models** use Marten inline projections in Postgres
  (ADR-0071) for event-sourced contexts (Overlays, Automation).
  Reads bypass the event stream.
- **Multi-step sequences** use **Wolverine sagas** with explicit
  state machines and compensating actions (ADR-0072). Single-step
  rules do not use sagas.
- **Wolverine defaults** (ADR-0088): per-module queue isolation
  (`{moduleQueuePrefix}.{eventType.FullName}`) prevents competing-
  consumer races; eager transaction mode wraps every handler in an
  EF / Marten transaction; Postgres-backed outbox per context.
- **Database migrations** run via the dedicated
  `SmartSentinelEye.MigrationRunner` worker, ordered before any Api
  starts (ADR-0067). Api services never run migrations themselves.

## Testing conventions

Driven by ADRs 0052–0054, 0063, 0065, 0068, 0081.

| Concern | Choice |
|---|---|
| Framework | xUnit |
| Assertions | **Shouldly** (MIT-licensed, fluent) |
| Mocking | **Moq** for interfaces; **hand-written fakes** for repositories/stateful collaborators |
| Real infrastructure | Real containers via the Aspire fixture (`AspireFixture`, `DistributedApplicationTestingBuilder`); no Testcontainers — ADR-0103 |
| Naming | Sentence-style with underscores: `Register_a_camera_with_valid_input_returns_the_new_id()` |
| Test data | Hand-written fluent builders in `tests/.../Builders/`. No AutoFixture. |
| Layout (initial) | `tests/Architecture.Tests/` + `tests/Integration.Tests/` only; per-context per-layer test projects added per-feature |

**Coverage gates** (CI-enforced, ADR-0065):

- `<Context>.Domain` ≥ 90%
- `<Context>.Application` ≥ 80%
- `Shared.Kernel` and `Shared.Contracts` ≥ 90%
- `<Context>.Infrastructure` and `.Api` — no gate (integration tests
  cover them).

Bypass via PR label `coverage-exempt` (requires justification).

**Test pyramid shape is intentionally not fixed** (ADR-0081) — it
emerges from real defect-discovery data once features ship.

## Frontend conventions

Driven by ADRs 0074–0080.

- **Two apps** (ADR-0074): `apps/management-web/` (admin + operator)
  and `apps/kiosk-web/` (always-logged-in display). Shared code in
  `apps/shared/`.
- **Redux Toolkit + RTK Query** for state (ADR-0075). One store per
  app; real-time updates dispatch into the store.
- **Realtime transport is replaceable behind an abstraction**
  (ADR-0076): WebSocket implementation v1, SSE candidate v2. Server
  side: `IRealtimeChannel` / `IRealtimeTransport` in
  `SmartSentinelEye.Realtime.Abstractions`. Client side: parallel
  TypeScript interface in `apps/shared/realtime/`. Operator commands
  go over REST, not the realtime channel.
- **UI primitives** built on Radix UI + Tailwind tokens (ADR-0077,
  ADR-0078). All visual code in `apps/shared/ui/`. No external
  component library.
- **Forms** use React Hook Form + Zod (ADR-0079). One Zod schema per
  concept, reused across form validation and API client types.
- **Browser auth**: `react-oidc-context` for the management app
  (auth-code with PKCE), **custom flow for kiosk** using
  device-bound `client_credentials` (ADR-0080).

## ADRs — when to write one

Write a new `docs/adr/NNNN-<slug>.md` when you:

- Change anything in the constitution.
- Pick a new technology or library.
- Change a bounded context boundary, message schema, or persistence
  choice.
- Decide a non-trivial policy (security, retention, latency budget).
- Supersede or partly undo a previous ADR.

ADR template lives in `docs/adr/_template.md`. Keep them short — one
context, one decision, one consequences section.

## CODEOWNERS and issue templates — ADR-035

- `.github/CODEOWNERS` currently assigns all reviews to
  [@notonlywhite](https://github.com/notonlywhite). Per-context owners
  added as the team grows.
- `.github/ISSUE_TEMPLATE/` has bug, feature, and ADR-proposal
  templates. Open-ended discussions go in **GitHub Discussions**, not
  Issues.

## Security and secret handling

- No secrets in the repo. `.env*` are gitignored.
- `gitleaks` runs in CI; a hit blocks the PR.
- Reportable security issues: please use
  [GitHub Security Advisories](https://github.com/smartsolutionslab/smart-sentinel-eye/security/advisories/new),
  not public issues.

---

If you spot drift between this document and reality, file a PR with
type `docs`. This document is meant to match what we actually do — if
it doesn't, one of the two is wrong.
