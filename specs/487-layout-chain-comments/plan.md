# Implementation Plan: Correct two layout test comments that overstate the `currentChain` term

**Branch**: `487-layout-chain-comments` | **Date**: 2026-11-09 | **Spec**: [spec.md](./spec.md) | **Issue**: #3237

## Summary

Rewrite three comment passages so they say what spec 486 measured: the Retention cases are covered **jointly** by the
`currentChain === undefined` term and by `chainFetching` / `chainFailed`, and the term is redundant in the real store. Prove the change
is comment-only with a comment-stripped hash of each touched file.

## Technical Context

TypeScript / React, vitest under jsdom (`management-web`). Comments live in the doc blocks of two test files. The TypeScript compiler is
already a dev dependency.

## Constitution Check

§IV: not on the path. ADR-0036: comments say why, only when non-obvious; no drive-by edits. §Testing: behaviour-preserving, so
characterisation; there is no behaviour to observe red.

## Decisions

**D1. The comment-stripped hash is AST-based, not a regex.** `strip487.cjs` (scratchpad) runs `ts.transpileModule` with
`removeComments: true`, `jsx: preserve`, target ESNext, and prints `sha256` of the output. A regex would mis-handle `//` inside strings and
template literals; the compiler cannot. It is run on `git show origin/develop:<file>` (before) and on the working file (after).

**D2. The proof is shown able to fail (counterfactual), two ways.**

| Id  | Edit to a copy of the file                            | Expected hash |
| --- | ----------------------------------------------------- | ------------- |
| P   | rewrite a comment (positive control)                  | equal         |
| N1  | change one code token (e.g. a `toBe(7)` to `toBe(8)`) | **differs**   |
| N2  | change a string literal that contains `//`            | **differs**   |

N1/N2 are made on copies in the scratchpad, never in the tree, so there is nothing to restore.

**D3. The wording** (FR-002), written from the T1–T6 table in spec 486's `verification.md`:

- Retention (two places): once B's read settles with a 500, the gate is closed by `chainFailed`; `currentChain === undefined` is a second,
  redundant cover (T1 leaves the file green). The claim that remains true and worth keeping: this case discriminates `currentData` from
  `data` only together with the gate terms, which is why the third `it` exists. Kept short; no restatement of the matrix.
- Recovery (one place): the three Retention `it`s are covered **jointly**, not by the term alone; `chainFetching` can be deleted and the
  Retention file stays green (true), which is the gap this file's FR-005 case closes (T4 fails it). Drop the `:207` / `:332` / `:409` line
  numbers, which rot.

No line numbers in the new text. The matrix itself stays in the spec 486 / 487 notes, not in the code (ADR-0036: references belong in
the PR, not the code).

**D4. Order.** Write the comments, run `strip487.cjs` on both files against `origin/develop`, then P/N1/N2, then the layouts suite,
`eslint`, `tsc --noEmit`, `prettier --check` (prettier can reflow a comment: run it before hashing so the hash is of the final file).

**D5. Production code, assertions and test names are read-only.** `git diff` on the two files shows only comment lines.

## Phases and commits

1. `docs(487): plan`, `docs(487): tasks`.
2. `docs(management-web): correct two layout test comments that overstate the currentChain term` (comment-only; one commit).
3. `docs(487): verification note, tick tasks`.

Every commit leaves the layouts suite green and the stripped hash unchanged.

## Risks and how they are covered

- **A reworded comment is still wrong.** The wording is derived from the measured T1–T6 table and quotes none of it; the spec 486
  note is the single source. `/code-review` reads the claim against the table.
- **The hash tool hides a code change.** D2's N1/N2 show it differs on a token change and a string change.
- **Prettier reflows a comment after hashing.** D4 runs prettier first.

## Project Structure

```text
specs/487-layout-chain-comments/   spec.md, plan.md, tasks.md, verification.md, checklists/
apps/management-web/src/features/layouts/LayoutEditorDialogChainRetention.test.tsx   (comments only)
apps/management-web/src/features/layouts/LayoutEditorDialogChainRecovery.test.tsx    (comments only)
```
