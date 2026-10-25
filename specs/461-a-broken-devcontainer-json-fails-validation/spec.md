# Spec 461: A broken devcontainer.json fails validation

**Issue:** #3151 · **Branch:** `chore/3151-devcontainer-jsonc` · **Follows:** spec 456 (#3030), T002
**ADRs:** ADR-0037 (phases), ADR-0144 (autonomous lane), ADR-0139 (new behaviour starts red), ADR-0036 (smallest
change; no speculative generality).
**Lane:** autonomous · **Phase-4a colour:** **red** — the validation command gains a behaviour it does not have today
(rejecting a syntax error). · **Engineer:** infra-engineer.
**Security-sensitive:** no. Reads one local file; no network, no secrets. `infra-reviewer` at phase 6.
**New ADR needed:** **no.** Dev tooling; mirrors `.devcontainer/check-toolchain.mjs` (spec 456) and the
`scripts/*.mjs` + `node --test` guard pattern. No new dependency.
**Latency budget impact:** N/A — no runtime, no `src/` change.
**Written 2026-10-25 against `develop` at `aa32b550`.**

---

## Premise, checked

1. `docs/codespaces.md` "Validate the definition without a Codespace" documents
   `npx @devcontainers/cli@0.89.0 build --workspace-folder .` and says, correctly, that it **does not catch JSON syntax
   errors**: the CLI parses `devcontainer.json` leniently; a missing comma and an unclosed brace both exit 0.
2. Spec 456's scenario "a broken definition is caught (bad request)" is therefore half met; its `tasks.md` T002 is left
   unticked with "Syntax error: **NOT MET** ... follow-up". This spec is that follow-up and closes T002.
3. `devcontainer.json` is **JSONC**: the devcontainer spec allows `//` and `/* */` comments and trailing commas. The
   current file has neither, so `JSON.parse` passes today — and would wrongly reject a valid file the day someone adds
   a comment. Plain `JSON.parse` is not the check.
4. No JSONC parser is in `pnpm-lock.yaml` (`jsonc-parser`: 0 hits). Node has no built-in JSONC parser.
5. CI: a `.devcontainer/*` change classifies into the full ("other") bucket of `scripts/classify-ci-diff.sh`, and the
   frontend job runs `pnpm test`, which runs `test:guards` = `node --test "scripts/**/*.test.mjs" ...`. A guard test
   there runs on every full CI run with no `ci.yml` change.

## User story (P1, the only one)

**As** a contributor editing `.devcontainer/devcontainer.json`, **I want** the documented validation command to fail
on a JSON syntax error while still accepting comments and trailing commas, **so that** a broken definition is caught
before a Codespace silently ignores or misreads it.

### Acceptance scenarios

```gherkin
Scenario: the real definition passes (happy)
  Given .devcontainer/devcontainer.json at this branch
  When `node .devcontainer/check-devcontainer-json.mjs` runs
  Then it exits 0 and prints the path it checked

Scenario: JSONC the devcontainer spec allows is accepted (happy)
  Given a definition with // line comments, /* block */ comments and trailing commas in objects and arrays
  And string values containing "//" and "/*" (e.g. "https://example")
  When the check runs on it
  Then it exits 0, and the parsed value equals the comment-free, comma-free equivalent

Scenario: a missing comma is rejected (bad request)
  Given a scratch copy of the definition with one comma between two members removed
  When the check runs on it
  Then it exits non-zero, naming the file and a line:column

Scenario: an unclosed brace is rejected (bad request)
  Given a scratch copy with the final closing brace removed
  When the check runs on it
  Then it exits non-zero, naming the file

Scenario: other malformations are rejected (bad request)
  Given an unterminated /* comment, or an empty file, or a root that is not an object
  When the check runs on it
  Then it exits non-zero with a message naming the reason

Scenario: the documented command now fails on a syntax error (conflict — the defect itself)
  Given the scratch copy with the missing comma
  When the documented validation command runs (check first, then `devcontainers build`)
  Then it exits non-zero before the build starts
```

Auth: N/A — local file, no identity or network.

### Independent end-to-end test procedure

1. `node .devcontainer/check-devcontainer-json.mjs` on the real file → exit 0 (quote).
2. Scratch copy outside the repo (scratchpad), missing comma: `node .devcontainer/check-devcontainer-json.mjs <copy>`
   → exit 1, message quoted. Repeat for an unclosed brace.
3. Scratch copy with comments + trailing commas → exit 0 (quote).
4. `pnpm test:guards` → the new test file passes, and the case reading the real repository file is among them.
5. With Docker available: the full documented command on the real definition → exit 0; on the missing-comma copy
   → non-zero before the CLI runs. If Docker is unavailable, step 5's build half is recorded as unobserved; steps 1-4
   do not need it.

## Requirements

- **FR-1** A repo script `.devcontainer/check-devcontainer-json.mjs`, no dependency: strict JSONC parse — strips
  comments and trailing commas outside strings, then `JSON.parse`; rejects any other syntax error, an unterminated
  block comment, an empty file and a non-object root. Exit 0 on success, 1 with `path:line:column: reason` on failure.
  Optional path argument; default `.devcontainer/devcontainer.json`.
- **FR-2** The parse is an exported pure function over a string, so it is unit-tested without file I/O.
- **FR-3** A `node --test` guard under `scripts/` covering every scenario above plus one case that reads the real
  repository file (so CI fails on a broken definition with no `ci.yml` change).
- **FR-4** `docs/codespaces.md` Validate section: the documented command runs the check first, then the CLI build; the
  "does not catch JSON syntax errors ... follow-up" paragraph is replaced by what each half catches.
- **FR-5** `specs/456-a-codespace-that-builds/tasks.md` T002: record the syntax-error half as MET with quoted evidence,
  and tick it.

## Non-goals

- Schema validation of `devcontainer.json` keys (unknown/mistyped properties) — a different check; the CLI build covers
  unknown Features. Not requested by #3151.
- Duplicate keys (`JSON.parse` keeps the last) — not requested; recorded as an accepted gap.
- Running the check from `post-create.sh` — a definition that cannot parse never reaches post-create.
- A `ci.yml` change or a new CI job (premise 5: `test:guards` already covers it).

## Assumptions (flagged)

- **A1** "Strict about JSONC" means exactly comments + trailing commas are the permitted extensions over JSON (the
  brief's wording and the VS Code JSONC dialect). Anything else (single quotes, unquoted keys, hex numbers) is rejected.
