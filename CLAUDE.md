# Smart Sentinel Eye — Claude Code project guide

This file is the orienting context for Claude Code in this repo. Read it
once per session, then defer to the documents it points at.

## What this project is

Professional camera management / CCTV system for industrial production
fabs. 24/7 operation. 250-camera target. WebRTC, .NET Aspire, k3s.

The full picture lives in:

- **`.specify/memory/constitution.md`** — non-negotiable principles,
  locked tech stack, NFRs, governance.
- **`docs/adr/`** — every architectural decision, with reasoning.
  Start with `0000-initial-decisions.md`.
- **`specs/`** — per-feature specs produced by Spec-Kit. Each spec links
  to its `plan.md` and `tasks.md`.

## Branching — GitFlow (ADR-0028)

**`develop` is the default branch.** Every feature, doc, chore, and CI
branch is cut from `develop` and merges back to `develop`. **Never open
a PR against `main`.** `main` only ever receives merges from
`release/x.y.z` and `hotfix/<short>` branches.

When you run `gh pr create`, pass `--base develop` explicitly until the
repo's local default tracking is fixed. The harness's git-status header
may say "Main branch" but trust this file over the header.

`develop`'s protection rules require **linear history** with
**rebase-merge only** (no squash, no merge commits) — ADR-0087.

## Workflow — guided phased process (ADR-0037)

Seven phases, each with an artifact and an **explicit gate**. **Do not
autonomously advance past a gate.** Stop at every gate and ask the
user to confirm before continuing.

| # | Phase | Command(s) | Artifact | Gate |
|---|---|---|---|---|
| 1 | Specify | `/speckit-specify` (+ `/speckit-clarify`) | `specs/NNN-x/spec.md` | Spec reviewed; no `[NEEDS CLARIFICATION]` left. |
| 2 | Plan | `/speckit-plan` | `specs/NNN-x/plan.md` | Plan aligns with constitution + ADRs. |
| 3 | Tasks | `/speckit-tasks` + `/speckit-taskstoissues` | `tasks.md` + GitHub issues | Tasks atomic; on Project #13. |
| 4 | Implement | `/speckit-implement` | Code + tests; format & analyzers clean | Tests green; commits follow ADR-0030. |
| 5 | Verify | `/verify` or explicit run/test | Verification note on the PR | Behaviour observed end-to-end. Latency cited if on SLO path. |
| 6 | QA | `/code-review`; `/security-review` if security-sensitive | Findings addressed | All findings resolved or accepted in writing. |
| 7 | PR | `gh pr create` with template | PR to `develop`, CI green | Reviewer approval + green CI. |

**Skipping a phase:** allowed only for trivial changes (typo, dep
bump, comment-only). Write `Phase X: skipped — <one line>` in the PR
body. Documentation-only PRs typically skip 5 and 6.

**Resumability:** every phase's artifact is the resumption point. If
interrupted, read the latest artifact and resume from its phase.

No code is written outside this loop. Every PR references at least one
task ID. Every spec references at least one ADR. Other meta-commands
exist for amendments — `/speckit-constitution` (rare; requires ADR).

## Coding behavior — Karpathy guidelines (ADR-0036)

The `andrej-karpathy-skills:karpathy-guidelines` skill is **baseline
coding behaviour** in this repo. It is invoked automatically during
phases 4–6. Internalize the operational rules:

- **Smallest possible change.** A bug fix changes the bug, nothing
  else. A refactor changes shape, not behaviour. Don't mix them.
- **Define "done" up front.** State the verifiable success criterion
  (a passing test, a measurement, an observable behaviour) before
  writing code. No "done when it compiles".
- **Surface assumptions; don't bury them.** When the task is
  ambiguous, ask one or two clarifying questions before guessing.
  Mark unavoidable guesses explicitly in the PR.
- **No speculative generality.** No frameworks, abstractions, or
  config knobs for needs that don't exist yet — except the
  explicitly-scoped forward-compat interfaces in constitution §IX.
- **No drive-by error handling.** Validate at trust boundaries only.
  Swallowed exceptions are review blockers.
- **No drive-by comments.** Code says what; comments say *why*, only
  when the why is non-obvious. References to issues/tasks belong in
  the PR body, not the code.
- **Read before write.** Read the surrounding code and tests; mirror
  existing patterns rather than introducing new ones unjustified.

## What lives where

```
src/
  AppHost/              Aspire composition root (dev + prod)
  ServiceDefaults/      Aspire defaults: telemetry, resilience, health
  Shared.Kernel/        Value-object base types, Result<T,E>, no domain
  Shared.Contracts/     RabbitMQ messages + HTTP DTOs (versioned)
  <Context>/
    Domain/             Pure domain. No I/O, no framework refs.
    Application/        Use cases, command/query handlers.
    Infrastructure/     Persistence, RabbitMQ, external adapters.
    Api/                HTTP + RabbitMQ entry points.

apps/web/               React + TypeScript + Vite. Aspire JS resource.
tests/                  xUnit. Includes NetArchTest boundary rules.
deploy/helm/            Generated by `aspire publish --target k8s`.
```

## House rules

- **DDD with value objects.** Primitives (`Guid`, `string`, `double`)
  do not cross domain boundaries. If you find yourself passing one,
  introduce a value object (e.g. `CameraId`, `Percentage`).
- **No cross-context project references.** Communication between
  bounded contexts only via `Shared.Contracts`. NetArchTest enforces
  this; PRs that break the rule cannot merge.
- **CQRS / event sourcing only where it earns its keep.** Overlays
  and Automation are first candidates (via Marten). Other contexts
  default to plain CRUD against Postgres.
- **Latency budget is sacred** (constitution §IV). Any change on the
  event-to-overlay path cites which leg it affects.
- **Aspire is the composition root.** New runtime resources go in
  `AppHost`. Don't wire connection strings by hand.
- **Tests:** TDD for domain; integration against the real Aspire stack
  (or Testcontainers in CI); NetArchTest for boundaries.

## Latency budget (do not erode)

`event arrival → overlay rendered, frame-synced ≤ 800 ms`, broken
down as:

| Leg | Budget |
|---|---|
| Camera → SFU | ≤ 80 ms |
| SFU → kiosk decode | ≤ 120 ms |
| Presentation buffer (PTP) | ≤ 200 ms |
| Event → overlay state | ≤ 200 ms |
| Composite + render | ≤ 50 ms |
| Headroom | ≤ 150 ms |

## Stack at a glance

| Concern | Choice | ADR |
|---|---|---|
| Frontend | React + TypeScript + Vite, **two apps** (`management-web` + `kiosk-web`) | 0074 |
| Frontend state | Redux Toolkit + RTK Query | 0075 |
| Real-time push | **Replaceable transport** (WebSocket v1, SSE v2 candidate) | 0076 |
| UI primitives | Radix UI headless components + custom design system | 0077 |
| Styling | Tailwind CSS with design tokens via CSS custom properties | 0078 |
| Frontend forms | React Hook Form + Zod | 0079 |
| Browser auth | `react-oidc-context` + custom kiosk flow | 0080 |
| Backend | .NET 10 + ASP.NET Core + .NET Aspire | 0024 |
| API style | Minimal APIs only | 0070 |
| Mediator | Hand-rolled `ICommandHandler<T,R>` / `IQueryHandler<T,R>` + Wolverine as dispatcher | 0042, 0057 |
| Domain events | Separate domain (in-process) and integration (`Shared.Contracts`, `V<N>` suffix) | 0040, 0073 |
| Value objects | **Maximalist hand-written**, `IValueObject<T>` marker, `.From(...)` + `Ensure.That(...)` | 0038, 0046, 0066 |
| IDs | **Guid v7** in strongly-typed records with **`Identifier` suffix** (`CameraIdentifier`, `LayoutIdentifier`) | 0039, 0090 |
| Naming | **No shortcuts or aliases** (`Identifier` not `Id`, `Repository` not `Repo`, …); identifier-typed properties named after the noun (`Owner` not `OwnerIdentifier`) | 0091, 0094 |
| Domain layout | Per-aggregate folder containing aggregate + VOs + repository + `Events/` subfolder | 0092 |
| Application layout | Per-message-kind: `Commands/`, `Queries/`, `EventHandlers/`, `DTOs/`, each with `Handlers/` subfolder and paired `*Errors.cs` | 0093 |
| Errors | `Result<T, Error>` with `ApiError(Code, Message, HttpStatusCode)` base | 0047, 0089 |
| Nulls | **NRT disabled + `Option<T>` everywhere** | 0048 |
| Async | `CancellationToken` mandatory last param; no `ConfigureAwait` | 0049 |
| Persistence | PostgreSQL (+ Marten for ES contexts with inline projections) | 0009, 0071 |
| Concurrency | Optimistic with explicit `Version` field on aggregates | 0043 |
| Object store | MinIO (future) | 0009 |
| Messaging | RabbitMQ (via Wolverine) | 0010, 0042 |
| Sagas | Wolverine state machines + compensating actions | 0072 |
| Identity | Keycloak (OIDC) per fab | 0007, 0008 |
| Streaming | WebRTC SFU; passthrough + GPU transcode fallback | 0011, 0012 |
| Time | PTP (IEEE 1588) per fab | 0014, 0021 |
| Logging | Serilog behind `ILogger<T>`, OTLP exporter, structured fields | 0050 |
| DI | Per-context `Add<Context>{Infrastructure,Api}` extension methods | 0051 |
| Migrations | Dedicated `MigrationRunner` worker | 0067 |
| Test framework | xUnit + **Shouldly** (free) + **Moq** + hand-written fakes + Testcontainers | 0052 |
| Test naming | Sentence-style with underscores | 0053 |
| Test data | Hand-written fluent builders, no AutoFixture | 0054 |
| Coverage gates | Domain ≥ 90%, Application ≥ 80%, Shared ≥ 90% (CI-enforced) | 0065 |
| Code metrics | Max 300 LOC/file, 30 LOC/method, 4 params, complexity ≤ 10, depth ≤ 3 (SonarAnalyzer) | 0084 |
| Wolverine defaults | Per-module queue isolation + eager transactions + Postgres outbox | 0088 |
| Git: commits | Conventional Commits, **no `Co-Authored-By` footer** | 0030, 0086 |
| Git: merge | **Rebase-only** (no squash, no merge commits) | 0029, 0087 |
| Observability | OpenTelemetry → Aspire dashboard + Grafana stack (parallel comparison) | 0026 |
| Orchestration | Aspire AppHost (dev) → k3s + Helm (prod) | 0024, 0025 |

**Diverges from Yumney on:** NRT (we: disabled; Yumney: enabled), `Result<T, Error>` shape, Shouldly vs FluentAssertions, Moq vs NSubstitute, sentence-style vs `Method_Scenario_Expected` test naming, initial test layout (minimal vs full per-layer), **Marten** for event-sourced contexts (Yumney: EF Core), narrower Architecture.Tests scope, no story-ref in commits. See ADRs 0056–0063, 0082, 0083, 0085 for the reasoning per divergence.

**Aligns with Yumney on:** Hand-written VOs, Guid v7 typed IDs, `Identifier` suffix, no shortcuts, per-aggregate Domain folders, per-message-kind Application folders, identifier-noun property naming, custom `Deconstruct(...)`, plural variable names for repository injections, `IValueObject<T>` marker, `MigrationRunner` pattern, AspireFixture pattern (deferred), 90/80/90 coverage gates, hand-rolled `ICommandHandler<T,R>` interfaces with Wolverine dispatcher, `ApiError` with HTTP status, per-module Wolverine queue isolation + eager transactions, no `Co-Authored-By`, rebase-only merge, SonarAnalyzer code-metric limits.
