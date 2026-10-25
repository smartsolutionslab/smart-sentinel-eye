# Plan 461: A broken devcontainer.json fails validation

**Spec:** [spec.md](spec.md) · **Issue:** #3151

## Context and layers

No bounded context, no Domain/Application/Infrastructure/Api, no Shared.Kernel/Contracts, no AppHost, no messaging,
no `ci.yml`. Boundary rules (NetArchTest, `PrimitiveBoundaryTests`) untouched.

| File | Change |
|---|---|
| `scripts/check-devcontainer-json.test.mjs` (new) | FR-3 guard, `node:test` + `node:assert/strict` |
| `.devcontainer/check-devcontainer-json.mjs` (new) | FR-1, FR-2 |
| `docs/codespaces.md` | FR-4, Validate section only |
| `specs/456-a-codespace-that-builds/tasks.md` | FR-5, T002 evidence |

## Design decisions

1. **Mechanism: a hand-written strict JSONC normaliser + `JSON.parse`, no dependency.** No JSONC parser is locked
   (spec premise 4); adding one for ~40 lines contradicts ADR-0036 and the precedent in `check-toolchain.mjs` ("avoids
   adding a dependency"). `@devcontainers/cli`'s own parser is exactly the lenient one being worked around.
2. **Normaliser contract** (one pass, a small state machine over characters: `code | string | string-escape |
   line-comment | block-comment`):
   - comments outside strings are replaced by **spaces of equal length, newlines kept**, so `JSON.parse`'s reported
     position still maps to the original line:column;
   - a comma outside strings whose next non-whitespace, non-comment character is `}` or `]` is replaced by a space;
   - inside a string, `//`, `/*` and `,` are literal; `\"` does not end the string;
   - end of input inside a block comment → failure "unterminated block comment" (JSON.parse would not see it).
   Everything else is left for `JSON.parse` to reject — that is what makes it strict: missing comma, unclosed brace,
   unterminated string, single quotes, unquoted keys all fail there.
3. **Position mapping**: `JSON.parse`'s message carries `position N` (Node 22 V8; also `line L column C` in newer V8).
   Convert offset → line:column from the original text; when no offset is present, report the message alone. The
   test asserts non-zero/failed and that the message names the file — not V8's exact wording.
4. **Shape** (mirrors `scripts/render-leg-check.mjs`): export `parseJsonc(text)` returning
   `{ ok: true, value } | { ok: false, reason, line?, column? }` (no throw — the CLI formats it); the CLI entry is guarded
   by the `isMainModule` idiom at `render-leg-check.mjs:339`. Root must be a plain object (devcontainer.json is one).
5. **Location**: the script sits in `.devcontainer/` beside `check-toolchain.mjs` (it is about that folder); the test
   sits in `scripts/` because `test:guards` globs `scripts/**/*.test.mjs` — no `package.json` change.
6. **Documented command** (FR-4): `node .devcontainer/check-devcontainer-json.mjs && npx @devcontainers/cli@0.89.0 build
   --workspace-folder .` — check first, so a syntax error fails in milliseconds without Docker.

## Phase 4a (red, ADR-0139)

- **test-writer** writes `scripts/check-devcontainer-json.test.mjs` against the FR-2 contract, and a **stub**
  `.devcontainer/check-devcontainer-json.mjs` whose `parseJsonc` returns `{ ok: true, value: {} }` for any input —
  the module exists, so the red is for the right reason (the doubly-red idiom of `node-types-alignment.test.mjs`).
  Expected red: missing-comma, unclosed-brace, unterminated-comment, empty, non-object-root cases fail on `ok === true`;
  the JSONC-accepted case fails on value equality. Quote verbatim.
- **Counterfactual of the defect, re-observed:** the current documented command (`npx @devcontainers/cli@0.89.0 build`)
  on a scratch missing-comma copy exits 0 (Docker permitting; otherwise cite spec 456 T002's recorded observation).

## Verification shape (phase 5)

Spec §"Independent end-to-end test procedure" steps 1-5; scratch copies live in the scratchpad, never committed.
Plus the guard's own counterfactual: break the real `devcontainer.json` in the worktree (remove a comma), run
`pnpm test:guards` → the real-file case fails; restore (`git checkout -- .devcontainer/devcontainer.json`) → green.

## Risks

- Normaliser bug that eats a legitimate character inside a string → covered by the `"https://..."` / `"/*"` string
  case and by the real-file case.
- V8 message format differs across Node versions → decision 3: tests do not pin V8 wording.
