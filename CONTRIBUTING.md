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

## Branch protection — ADR-029

Both `main` and `develop` enforce:

- ✅ PR required (no direct pushes)
- ✅ Linear history (squash or rebase merges only; **no** merge commits)
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
| Integration tests | Aspire AppHost + Testcontainers (Postgres + RabbitMQ + Keycloak) |
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

## Code style — ADR-034

- **C#:** `dotnet format` enforces `.editorconfig` + Roslyn analyzers
  (`EnableNETAnalyzers=true`, `AnalysisLevel=latest`). Warnings are
  errors in `Release` builds.
- **TypeScript / React:** ESLint + Prettier with the configs shipped
  in `apps/web/`.
- **YAML / Markdown:** Prettier defaults.

Husky pre-commit hooks run formatters on **staged files only** — fast
local loop. CI verifies with `--verify-no-changes` so a bypassed hook
still fails the PR.

## Working with Spec-Kit

Use slash commands inside Claude Code. The git extension auto-creates
feature branches and (optionally) auto-commits.

```
/speckit-constitution   amend principles (rare; requires ADR)
/speckit-specify        write a feature spec; creates NNN-feature branch off develop
/speckit-clarify        de-risk ambiguities before planning (optional)
/speckit-plan           technical approach
/speckit-tasks          break plan into ordered atomic tasks
/speckit-implement      execute tasks
/speckit-checklist      (optional) generate completeness checklist
/speckit-analyze        (optional) cross-artifact consistency report
/speckit-taskstoissues  push tasks into the GitHub Project board
```

**Don't** write code outside a Spec-Kit feature branch unless the work
is docs / ADR / infra (`chore`, `docs`, `ci`, `build` types).

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
