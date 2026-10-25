# Plan 456: A Codespace that builds, tests, and says what it cannot do

**Spec:** [spec.md](spec.md) · **Issue:** #3030

## Context and layers

No bounded context, no Domain/Application/Infrastructure/Api code, no Shared.Kernel/Contracts, no AppHost change, no
messaging. Files touched:

| File | Change |
|---|---|
| `.devcontainer/post-create.sh` | FR-3 toolchain assertion, after `corepack prepare`, before `dotnet restore` |
| `.devcontainer/devcontainer.json` | FR-4 only if the schema supports port visibility; otherwise untouched |
| `docs/codespaces.md` (new) | FR-1, FR-2 |
| `README.md` | one link line to `docs/codespaces.md` |

## Design decisions

1. **Baseline is `develop`'s devcontainer** (#3071), not #2811 — spec Premise 1, Q1.
2. **Docker-in-Docker** (already chosen): a Codespace exposes no host Docker socket, so docker-outside-of-docker has
   nothing to mount; Aspire (ADR-0103 fixture, AppHost) needs an engine inside. Cost: images live in the container's
   storage, which is why storage is 32 GB — recorded in the docs page.
3. **Toolchain assertion is a shell check, not a new script language or dependency**: read the pins with tools already
   present (`node -p` over `package.json`/`global.json`; semver range via `node` and pnpm's bundled checks, or
   `npx --no-install semver` only if it is already in the lockfile — engineer verifies, otherwise compare manually).
   It runs under `set -euo pipefail`, so a mismatch fails post-create loudly, before restore spends minutes.
4. **Validation command pins an exact `@devcontainers/cli` version** at least two weeks old; no new dev dependency
   in `package.json`.
5. **Port visibility**: check the published devcontainer.json schema / Codespaces docs for a `visibility` key under
   `portsAttributes`. Do not add an unknown key — an ignored key is a claim the environment does not hold.
6. **No secrets**: confirm nothing in `.devcontainer/` carries a credential (grep); Keycloak's `dev-only-*` values
   stay in the realm file as today.

## Boundary rules

None affected — no `src/` change, so NetArchTest boundaries and `PrimitiveBoundaryTests` are untouched. The
Husky hook (`scripts/pre-commit.mjs`) is exercised, not changed.

## Verification shape (phase 5)

Local Docker on this machine (one Aspire stack per machine; check disk on C: first). Quote: validation command output,
toolchain assertion output, `dotnet build -c Release` summary, unit-test summary, `pnpm lint/typecheck/test` exit
codes, both counterfactuals. Codespace boot and in-Codespace Aspire stack: recorded **unobserved**.
