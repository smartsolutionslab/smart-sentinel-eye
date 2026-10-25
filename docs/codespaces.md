# Working in a GitHub Codespace

The repository ships a dev container (`.devcontainer/`) so a contributor with
nothing installed locally can build, test and lint in a browser or VS Code.
It installs .NET 10, Node 22, pnpm (via corepack), Docker-in-Docker, `gh`,
PowerShell and the claude-code feature, then runs `dotnet restore` and
`pnpm install --frozen-lockfile`, installs Playwright chromium and the Aspire
CLI, and trusts the ASP.NET dev certificate (the last two are best-effort and
only warn on failure).

## Open it

1. On GitHub, **Code > Codespaces > Create codespace on develop** (or on your
   branch).
2. Choose a machine of **at least 4 cores / 16 GB RAM / 32 GB storage**. That
   is the `hostRequirements` value in `devcontainer.json`, and it is sized for
   building and running unit tests, not for the full stack.
3. Wait for the post-create step. It starts by asserting the toolchain
   (`.devcontainer/check-toolchain.mjs`): `node` must satisfy `engines.node`,
   `pnpm` must equal `packageManager`, and `dotnet` must satisfy `global.json`.
   A mismatch stops post-create and names the expected and actual version.

Full Aspire stack (`dotnet run --project src/AppHost`): **machine size
unverified.** Nobody has booted the stack in a Codespace yet, so there is no
basis for a recommended size; try a larger machine than the minimum above and
record what you observe here.

## What works

Observed in a local dev container on Docker Desktop (not a Codespace), as
recorded in the phase-4 verification report for this change:

- `docker info` succeeds inside the container (Docker-in-Docker).
- `pnpm lint` and `pnpm typecheck` ran in the container.
- `dotnet build -c Release` and the unit and architecture test projects ran in
  the container. The report does not list which test projects were run.

The container is Linux, so it behaves like CI, not like a Windows dev machine.

## Not yet verified

- **`pnpm test`.** Linux CI runs `pnpm test` on every frontend change and it
  passed (`ci.yml:246-247`; run 40181953188 at 677b8054). In the Docker
  Desktop dev container it ran every test green but exited 1 on vitest
  worker-start timeouts ("Failed to start forks worker ... Timeout waiting for
  worker to respond"). That exit 1 was observed only there; the cause is
  unknown, and it is still unverified in a Codespace. The spec's acceptance
  "fails identically on Linux CI" is answered: it does not.
- **The Husky pre-commit hook.** `pnpm install` runs `husky`, but no no-op
  commit was observed running the hook on this branch.
- **The Aspire AppHost and the integration fixture** (ADR-0103). Docker is
  there, but nothing has booted the stack or the fixture in a container.
- **A real Codespace boot.** Nobody has created a Codespace from this
  definition; everything under "What works" was observed in a local dev
  container.
- **The Aspire stack** (`src/AppHost`) and its dashboard inside a container or
  Codespace, including the machine size it needs (see above).

## What does not work

- **WebRTC media from real cameras.** Codespaces forwards TCP only; ICE over
  UDP from cameras on a fab network cannot reach the container.
- **Inter-display sync (PTP)** and anything needing a host GPU.
- **Prebuilds** are a repository setting an owner may enable; none is
  configured here, so the first create runs the full post-create.

## Ports and secrets

Forwarded ports (Aspire dashboard 15276 / 17069, web apps 5173-5177) are
labelled in `devcontainer.json`. The dev container schema has no
port-visibility key, so nothing is declared there: Codespaces forwards ports
**privately** by default. Do not change a port to public. The Aspire dashboard
is protected only by a dev login token printed to the console; the web apps
and Keycloak run with dev-only secrets. Do not make any of them public.

The image holds no secrets. The stack uses the same dev realm as a local run.
Anything contributor-supplied belongs in your own Codespaces secrets, never in
the repository.

## Why Docker-in-Docker

A Codespace exposes no host Docker socket, so docker-outside-of-docker would
have nothing to mount. Images therefore live in the container's own storage,
which is part of why the machine needs 32 GB.

## Rebuild

Command palette: **Codespaces: Rebuild Container**. After editing
`.devcontainer/`, rebuild to pick the change up.

## Validate the definition without a Codespace

Syntax first, then the build. The build needs a Docker engine; the syntax check
does not:

```sh
node .devcontainer/check-devcontainer-json.mjs && npx @devcontainers/cli@0.89.0 build --workspace-folder .
```

The two halves catch different things:

- `check-devcontainer-json.mjs` rejects JSON syntax errors (a missing comma, an
  unclosed brace, an unterminated comment, a non-object root) and names the
  file, line and column. Comments and trailing commas are allowed, as JSONC
  permits. It runs in milliseconds and needs no Docker.
- `npx @devcontainers/cli build` exits non-zero when a Feature cannot be
  resolved or the image fails to build. It does **not** catch JSON syntax
  errors: it parses `devcontainer.json` leniently, so a missing comma and an
  unclosed brace both still exit 0.
