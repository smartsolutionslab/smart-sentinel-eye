# Spec 456: A Codespace that builds, tests, and says what it cannot do

**Issue:** #3030 (feature-level, on Project #13) · **Branch:** `chore/3030-devcontainer`
**ADRs:** ADR-0103 (integration runs against the real Aspire stack, so the container needs an engine), ADR-0118
(dev observability sink is the Aspire dashboard), ADR-0030/0086/0087 (commit rules the hook and container must keep),
ADR-0144 (autonomous lane), ADR-0037 (phases).
**Lane:** autonomous · **Phase-4a colour:** neither red nor characterisation — no product behaviour changes; the check is
a container build plus an in-container toolchain smoke run (see Testing) · **Engineer:** infra-engineer.
**Security-sensitive:** low — port visibility and "no secrets in the image" only. `infra-reviewer` at phase 6.
**New ADR needed:** **no.** The dev-environment choices (Docker-in-Docker, machine size) are tooling, not architecture,
and are recorded in the docs page this spec adds. Nothing in the product's stack, ports or scripts changes (issue: out of scope).
**Written 2026-10-25 against `develop` at `677b8054`.**

---

## Premise, checked

1. **Stale: the repo does not start from nothing.** The issue (opened 2026-10-17) says the devcontainer "starts from
   nothing" and that PR #2811 is not to be reused. Since then **PR #3071 merged (2026-10-20)** "devcontainer for
   Codespaces, with the review fixes from #2811": `develop` already has `.devcontainer/devcontainer.json` and
   `.devcontainer/post-create.sh`, with later commits that size the machine (4 CPU / 16 GB / 32 GB) and forward/label
   all five web apps. The owner enrolled the issue (2026-10-24) after that merge.
   **Assumption (flagged, owner question Q1):** "not reused" meant the closed branch/PR, not merged `develop`.
   Deleting merged work to rewrite it would contradict ADR-0036's smallest change, so this spec **closes the gaps
   between `develop`'s devcontainer and the issue's acceptance**, nothing more.
2. **Already satisfied on `develop`:** .NET 10 via Feature (`global.json` 10.0.401, `rollForward: latestPatch`),
   Node 22 via Feature + pnpm via `corepack prepare` (reads `packageManager: pnpm@10.30.3`), `gh`, Docker-in-Docker,
   `postCreateCommand` with `dotnet restore` and `pnpm install --frozen-lockfile`, ports 15276/17069 and 5173-5177
   labelled, `hostRequirements`.
3. **Missing against the acceptance:**
   - no docs page (how to open, machine size, what does not work, how to rebuild);
   - no documented validation command, so the definition can rot unnoticed;
   - DinD-vs-DooD choice is not written down;
   - port visibility is not stated (the issue requires non-public by default);
   - only one machine size is stated; the issue wants build+unit and full-stack sized separately;
   - Node `22` Feature resolves to the latest 22.x, which must satisfy `engines.node` `^22.22.2` — asserted nowhere;
   - nothing has ever been observed: no `devcontainer build`, no in-container build/test, no Codespace (PR #2811's own
     test plan said "not yet done").

## User story (P1, the only one)

**As** a contributor without .NET 10, Node, pnpm or Docker installed, **I want** to open the repo in a Codespace on a
documented machine size and reach a green Release build, unit tests and the pnpm checks with no manual steps, **so
that** onboarding and review need nothing installed locally — and I know beforehand what will not work there.

### Acceptance scenarios

```gherkin
Scenario: the definition builds (happy)
  Given the repository at this branch and a Docker engine
  When `npx @devcontainers/cli@<pinned> build --workspace-folder .` runs
  Then it exits 0

Scenario: the toolchain matches the repo's pins (happy)
  Given a container created from the definition, post-create finished
  Then `dotnet --version` satisfies global.json (10.0.4xx, >= 10.0.401)
  And `node --version` satisfies package.json engines.node
  And `pnpm --version` is 10.30.3
  And `docker info` succeeds inside the container

Scenario: the lane's checks run in the container (happy)
  Given that container
  When `dotnet build -c Release`, the unit-test projects, and `pnpm lint`, `pnpm typecheck`, `pnpm test` run
  Then each exits 0, or a failure is shown to fail identically on Linux CI (`develop` head) and is recorded, not fixed here

Scenario: a toolchain drift is caught (conflict)
  Given the post-create toolchain assertion
  When the installed Node does not satisfy engines.node (counterfactual: assert against a higher bound)
  Then post-create fails with a message naming the expected and actual version

Scenario: a broken definition is caught (bad request)
  Given devcontainer.json with a syntax error or unknown Feature (counterfactual)
  When the documented validation command runs
  Then it exits non-zero

Scenario: forwarded ports are not public (auth)
  Given the definition
  Then no port is declared public, and the docs state that Codespaces forwards privately by default and that the
    Aspire dashboard (unauthenticated in dev) and Keycloak (dev-only-* secrets) must not be made public
```

### Independent end-to-end test procedure

1. On a machine with Docker: run the documented validation command → exit 0 (quote output).
2. `devcontainer up --workspace-folder .`, then `devcontainer exec` the toolchain assertion and the build/test/pnpm
   commands → quote exit codes and summary lines.
3. Commit a no-op inside the container to show the Husky hook (`node scripts/pre-commit.mjs`) runs.
4. Codespace boot and the Aspire stack inside a Codespace: **unobserved unless the owner runs them** — the
   verification note says so explicitly (issue comment 2026-10-24: "state the rest as unobserved"). Booting the full
   Aspire stack inside a local `devcontainer up` is attempted only if disk allows (memory: worktrees fill the disk and
   kill Docker); otherwise recorded as unobserved.

## Requirements

- **FR-1** Docs page `docs/codespaces.md`, linked from `README.md`: how to open; machine size for build+unit
  (4 CPU / 16 GB / 32 GB, the `hostRequirements` value) and for the full Aspire stack (stated as **unverified**, with
  the reason); what does not work (WebRTC ICE/UDP media from cameras — PR #2811's note; inter-display PTP; anything
  needing a host GPU); how to rebuild (`Codespaces: Rebuild Container`); DinD chosen over DooD because a Codespace has
  no host socket to share; same dev realm, no new secrets, Codespaces secrets for anything contributor-supplied
  (references #2808); prebuilds are a repository setting the owner may enable (not configured here); the container is
  Linux and so behaves like CI, not like the Windows dev machines.
- **FR-2** The documented one-line validation command (`npx @devcontainers/cli@<exact version> build --workspace-folder .`).
- **FR-3** A toolchain assertion at the start of post-create (after corepack) that fails loudly when `dotnet`, `node`
  or `pnpm` do not satisfy `global.json` / `engines.node` / `packageManager`.
- **FR-4** Port visibility: if the devcontainer schema supports declaring visibility, declare `private` on every
  forwarded port; if it does not, change nothing in JSON and state the default in FR-1's page. Verified against the
  schema, not assumed.
- **Non-goals:** CI job for devcontainer build (the acceptance allows a documented command; adding a job is a
  `ci.yml` contention-file change — owner question Q2); prebuild configuration; changing product ports, scripts or
  stack; the extension list (owner-curated in #3071, left as is).

## Latency budget impact

N/A — development tooling only; no leg of the event-to-overlay path is touched.

## Testing

No product behaviour changes, so there is no red test and nothing to characterise. The verifiable checks are the
end-to-end procedure above, each with a **counterfactual** (memory: prove a guard by counterfactual): the validation
command is seen to fail on a deliberately broken copy of the definition, and the toolchain assertion is seen to fail
against a tightened bound. Both counterfactuals run on scratch copies and are not committed.

## Owner questions (non-blocking; defaults applied)

- **Q1** Build on `develop`'s merged devcontainer (#3071) rather than rewriting it — default: **yes**.
- **Q2** Add a CI job that runs `devcontainer build` on `.devcontainer/**` changes — default: **no**, documented command only.
- **Q3** Full-stack machine size is not measured here; the docs page records it as unverified until someone boots the stack in a Codespace.
