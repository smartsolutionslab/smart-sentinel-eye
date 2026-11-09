# Verification — spec 487 (#3237)

Behaviour-preserving → **characterisation**, comment-only form (ADR-0139 / ADR-0144 phase 4a). **Only comment lines changed** in
`LayoutEditorDialogChainRetention.test.tsx` and `LayoutEditorDialogChainRecovery.test.tsx` (15 insertions, 14 deletions); no assertion,
test name or production line. Not observed in a browser; there is no behaviour to observe.

## §IV legs touched: none

Test comments in the operator's layout authoring form in `management-web`. No media, event, overlay-state or render-loop code is involved.

## SC-001 — the code is unchanged (comment-stripped hash)

`strip487.cjs` hashes `ts.transpileModule(source, { removeComments: true, jsx: preserve, target: ESNext })` with sha256. It works on the
syntax tree, so a `//` inside a string or template literal is not mistaken for a comment. "Before" is `git show origin/develop:<file>`;
"after" is the working file, taken after `prettier --write` so a reflowed comment cannot hide a difference.

| File                                        | Before             | After              |
| ------------------------------------------- | ------------------ | ------------------ |
| `LayoutEditorDialogChainRetention.test.tsx` | `d93576ac0650d72d` | `d93576ac0650d72d` |
| `LayoutEditorDialogChainRecovery.test.tsx`  | `70560feceee7f394` | `70560feceee7f394` |

`git diff origin/develop..HEAD -- apps`, with comment lines (`*`, `/**`, `*/`, `//`) filtered out, is empty.

### The hash is shown able to fail

Self-test (`node strip487.cjs --selftest`), on a small source with a `//` inside a string, a trailing comment, a block comment and a
template literal containing `//` and `/* */`:

| Edit                                            | Hash        |
| ----------------------------------------------- | ----------- |
| P: comments reworded and extended               | equal       |
| N1: code token `7` → `8`                        | **differs** |
| N2: string literal containing `//` changed      | **differs** |
| N3: template literal containing `/* */` changed | **differs** |

The first run of the self-test reported P as "differs". That was my test input, not the tool: P put `export const b` on the same line as
a new `// extra line`, commenting the export out. With the input fixed (the new comment ends in a newline) P is equal and N1–N3 differ.

## Correction to spec 486's note

`specs/486-layout-chain-gate-term/verification.md` says the Recovery comment "is contradicted by T4 as run: with `chainFetching` deleted,
`ChainRecovery`'s FR-005 case fails". That conflates two files. The comment is about the **Retention** file, which T4 leaves green, so
"`chainFetching` could be deleted and that file would stay green" was **true as written**. What was false is the word _alone_: T1, T5 and
T6 each also leave the Retention file green, so those cases are covered jointly. The `ChainRecovery` FR-005 case T4 fails is the test the
comment introduces to close the `chainFetching` gap. The merged 486 note is not edited; this section is the correction. The new
Recovery text follows the corrected reading.

## What was rewritten, and where it goes past the issue

| Passage                                        | Change                                                                                                        |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Retention header, "only thing left standing"   | `chainFailed` keeps Save closed; the term is a second cover, never true without one of the other two          |
| Retention third `it`, same claim               | same, in the `it`'s own doc block                                                                             |
| Recovery, "closed by the term alone"           | covered jointly (`chainFetching` for an in-flight read, `chainFailed` for a settled one); no line numbers     |
| Retention header, Save-gate predicate          | listed `currentChain === undefined \|\| chainFetching`; now includes `\|\| chainFailed`, which it already had |
| Retention third `it`, "actually distinguishes" | now "the case where `data` and `currentData` differ after the read has ended"                                 |

The last two are beyond the two claims the issue named. The predicate was stale (it omitted `chainFailed`, the very term the corrected
passage names). The "actually distinguishes `data` from `currentData`" sentence was the same kind of overstatement: under the current gate
`chainFailed` closes that case whichever field is read, as the `LayoutEditorDialogChainRead` comment already records. The new wording
claims no detection; `data` against `currentData` was not re-measured.

## Suites and checks

| Check                                          | Result                            |
| ---------------------------------------------- | --------------------------------- |
| `src/features/layouts` vitest                  | 213 passed (34 files), unchanged  |
| `management-web` full vitest                   | 740 passed (102 files), unchanged |
| `eslint src --max-warnings 0` (management-web) | exit 0                            |
| `tsc --noEmit` (management-web)                | exit 0                            |
| `prettier --check src` (management-web)        | exit 0                            |
| `Architecture.Tests` (foreground)              | 3254 / 3254 passed                |

## Code review (T006)

_Pending._
