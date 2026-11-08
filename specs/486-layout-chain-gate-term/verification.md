# Verification — spec 486 (#3232)

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). **No production file and no test file changed**: this PR is
documentation only. The `currentChain === undefined` term in `useSaveGate.ts` is **equivalent** (redundant with
`chainFetching || chainFailed`) in the real RTK Query store, so no test of it is warranted (FR-005, ADR-0036). Not observed in a
browser; the evidence is a real-store probe and the existing suites under jsdom.

## §IV legs touched: none

`LayoutEditorDialog` is the operator's layout authoring form in `management-web`. No media, event, overlay-state or render-loop code is
involved, and nothing in this change touches a source file.

## SC-002 — reachability, observed in the real store

A throwaway test (deleted before any commit) rendered `useLayoutChain` against `createApiStore([layoutsApi])` with `fetch` stubbed by
hand-resolved promises, and logged `(currentData present, isFetching, isError)` at every distinct render. Target state: data absent,
not fetching, not failed, query not skipped.

| #   | Scenario                                             | Tuples observed, in order                                                                         |
| --- | ---------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| S1  | first mount, held, then resolved                     | (no, fetching, ok) → (yes, idle, ok)                                                              |
| S2  | first mount, rejected 500                            | (no, fetching, ok) → (no, idle, **error**)                                                        |
| S3  | A resolved, switch to B, held, then resolved         | (no, fetching, ok) → (yes, idle, ok)                                                              |
| S4  | A resolved, `refetch()` rejected                     | … → (yes, **fetching**, ok) → (yes, idle, **error**)                                              |
| S5  | A resolved, tag invalidated with a live subscriber   | … → (yes, **fetching**, ok); data kept while refetching                                           |
| S6  | A resolved, `resetApiState()` under the mounted hook | (no, fetching, ok) → (yes, idle, ok); the hook starts a new read itself (store entry `fulfilled`) |
| S7  | `skipToken`, then an id                              | (no, idle, ok, **skipped**) → (no, fetching, ok) → (yes, idle, ok)                                |
| S8  | A resolved, unmounted, evicted, remounted            | (no, fetching, ok) → (yes, idle, ok)                                                              |

**No tuple matched the target in any scenario** (`grep -c TARGET` = 0 on both runs; S6 re-run without de-duplication to be sure).
Every real state with `currentData` absent is either fetching, failed or skipped, and `skipped` is outside `isEdit`. The term can
differ from `chainFetching || chainFailed` only on a state the real store does not produce. `resetApiState()`, the one exotic path
considered, also re-reads rather than idling empty.

## SC-001 — the break matrix (two passes, identical)

`src/features/layouts`, 213 tests on the unmodified tree. Each break an exactly-once substitution, restored from git after each run.

| Id  | Break                                                    | Result                                                                                      |
| --- | -------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| T1  | `useSaveGate.ts`: drop `currentChain === undefined \|\|` | 213 pass, **0 fail**                                                                        |
| T2  | `useLayoutEditorForm.ts`: guard → `throw new Error('x')` | 213 pass, **0 fail**                                                                        |
| T3  | T1 + T2 together                                         | 213 pass, **0 fail**                                                                        |
| T4  | control: drop `chainFetching \|\|`                       | 3 fail (`ChainRecovery` FR-005; `SaveGate` focus-through-conflict, Enter-while-closed)      |
| T5  | control: drop `chainFailed`                              | 1 fail (`SaveGate` "keeps Save unavailable when the re-read itself is refused", FR-007/008) |
| T6  | control: drop both covers, keep the term                 | 4 fail (the union of T4 and T5)                                                             |

Messages (T4/T6): `expect(element).toHaveAttribute("aria-disabled", "true")`, and for Enter-while-closed
`expected "vi.fn()" to not be called at all, but actually been called 1 times`.

T1–T3 are the evidence for "nothing notices"; the probe above is the evidence that nothing _can_ — hence equivalent, not a gap. T2 turns
the handler's silent return into an observable throw on purpose, so a test that reached the handler with no chain would have shown it:
none does.

## The two overstating comments (US2) — reported, not edited

- `LayoutEditorDialogChainRetention.test.tsx:40`: "`currentChain === undefined` is the only thing left standing between the operator
  and A's version going out under B's identity" (repeated at `:354`, "once the response lands … `chainFetching` drops back to `false`").
- `LayoutEditorDialogChainRecovery.test.tsx:451`: "… is closed by the `currentChain === undefined` half of the predicate alone —
  `chainFetching` could be deleted and that file would stay green".

The second is contradicted by T4 as run: with `chainFetching` deleted, `ChainRecovery`'s FR-005 case fails. The first holds in no state
the real store produces (a settled read that ends with no data is `isError`, so `chainFailed` also closes the gate; T5 shows a test that
depends on it). Both are descriptions, not assertions; correcting them is a comment-only change outside this spec's scope (FR-001).

## Suites and checks

| Check                                          | Result                                       |
| ---------------------------------------------- | -------------------------------------------- |
| `management-web` full vitest                   | 740 passed (102 files), unchanged vs. before |
| `shared` full vitest                           | 1395 passed, unchanged                       |
| `eslint src --max-warnings 0` (management-web) | exit 0                                       |
| `tsc --noEmit` (management-web)                | exit 0                                       |
| `prettier --check src` (management-web)        | clean                                        |
| `Architecture.Tests` (foreground)              | 3254 / 3254 passed                           |
| `git diff origin/develop -- apps src`          | empty                                        |

## Follow-up worth recording

If the hook is ever replaced by something that can report "no data, idle, no error" for a non-skipped read, T1+T2 stay green and the
term becomes load-bearing with nothing pinning it. That is a reason to re-run this matrix on such a change, not a reason to add a test
of a state that does not exist today.

## Code review (T008)

_Pending._
