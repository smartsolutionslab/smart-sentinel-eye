# Implementation Plan: Is the layout Save gate's `currentChain === undefined` term pinned?

**Branch**: `486-layout-chain-gate-term` | **Date**: 2026-11-08 | **Spec**: [spec.md](./spec.md) | **Issue**: #3232

## Summary

Decide whether `isEdit && currentChain === undefined && !chainFetching && !chainFailed` can happen at all. If it cannot, the gate term is
redundant with `chainFetching || chainFailed` and is recorded as equivalent. If it can, break the term, the handler check at
`useLayoutEditorForm.ts:72` and both through the real dialog, and pin whatever nothing notices. No production file changes (FR-001).

## Technical Context

TypeScript / React, vitest + Testing Library under jsdom (`management-web`). The layout tests mock `useGetLayoutQuery`; the real
RTK Query store is `createApiStore(apiSlices)` (`app/store`), already used by the overlay form tests. The hook under test is
`useLayoutChain` (`useGetLayoutQuery(id ?? skipToken, { refetchOnMountOrArgChange: true })`, `currentData` as `currentChain`).

## Constitution Check

§IV: not on the path. §Testing: behaviour-preserving → characterisation; a new test is green on today's code first (ADR-0139).
ADR-0036: no test of an unreachable state, no production change.

## Decisions

**D1. Reachability is observed, not argued.** A throwaway test (scratchpad copy, never committed) renders a probe component that calls
the real `useGetLayoutQuery` against the real store, with the network stubbed at `fetch`, and records the tuple
`(currentData !== undefined, isFetching, isError)` at **every render** through these scenarios:

| #   | Scenario                                                      | Why                                                |
| --- | ------------------------------------------------------------- | -------------------------------------------------- |
| S1  | first mount, fetch held open, then resolved                   | the uninitialised first render                     |
| S2  | first mount, fetch rejected (500)                             | `isError` with no data                             |
| S3  | mount on A (resolved), switch to B (held open), then resolved | `currentData` goes undefined on an argument change |
| S4  | A resolved, then a refetch (`refetch()`) rejected             | `currentData` kept, `isError` set                  |
| S5  | A resolved, `invalidateTags` evicts it with a live subscriber | data removed, a refetch starts                     |
| S6  | A resolved, `resetApiState()` while subscribed                | the store emptied under a mounted hook             |
| S7  | `skipToken` (create mode), then an id (open on a layout)      | `isEdit` flips; the query was skipped              |
| S8  | A unmounted and the entry evicted, remounted on A             | the `useLayoutChain` comment's "Window 3"          |

A state counts as the target if any render shows `currentData` absent with `isFetching === false` and `isError === false` while the
query is not skipped. Every observed tuple goes into `verification.md`, not only the target.

**D2. If the target state never occurs** — the term is equivalent (FR-005). The mocked tests that drive `currentChain === undefined`
with `chainFetching` or `chainFailed` already true are then not wrong, only redundant; the overstating comments are reported (not
edited). No test is written. The matrix of D3 is still run, so the record shows what each break does.

**D3. The break matrix** (`probe486.cjs`; gate file **G** = `useSaveGate.ts`, handler file **F** = `useLayoutEditorForm.ts`):

| Id  | Break                                                                                                                                                                                                        |
| --- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| T1  | G: drop `currentChain === undefined \|\|`                                                                                                                                                                    |
| T2  | F: drop `if (currentChain === undefined) return;` (the `.version` read then needs a non-null assertion, so the substitution replaces the guard with `if (currentChain === undefined) throw new Error('x');`) |
| T3  | G and F together                                                                                                                                                                                             |
| T4  | G: drop `chainFetching \|\|` (the cover, as a control)                                                                                                                                                       |
| T5  | G: drop `chainFailed` (the cover, as a control)                                                                                                                                                              |
| T6  | G: drop both covers (`chainFetching` and `chainFailed`), term kept                                                                                                                                           |

Run against the whole `src/features/layouts` folder, one vitest at a time, foreground, batches of about five. T2 as written changes
observable behaviour on purpose (it turns a silent return into a throw) so a test that reaches the handler with no chain would show it;
the plain deletion would not compile (`currentChain.version`). T4–T6 are controls: they show which tests are the covers, so the
Retention/Recovery comments can be answered with a table, not an opinion.

**D4. If the target state is reachable** — the test goes through the real dialog and store (not the mock), opens an edit on a layout,
brings the query to the target state by the scenario D1 found, and asserts Save is `aria-disabled` and presses it: no PATCH is issued.
It lives in a **new** file (`LayoutEditorDialogChainAbsent.test.tsx`), green on today's code, then proved by T1+T2 together (T3).

**D5. Production code and existing tests are read-only.** The runner restores only the two source files, from git, and refuses to start
if either is dirty. The D1 probe lives in the scratchpad plus one temporary file under `src/` that is deleted before any commit
(checked with `git status`).

## Phases and commits

1. `docs(486): plan`, `docs(486): tasks`.
2. If D4 applies: `test(486): pin that Save stays closed with no chain version` (one commit, green on today's code).
3. `docs(486): verification note, tick tasks`.

Every commit leaves the layouts and overlays suites green.

## Risks and how they are covered

- **The real hook behaves differently from my reading of RTK** (for example `isFetching` false on the uninitialised render): that is
  exactly what D1 observes; the answer decides D2 vs D4.
- **A real state exists only through a path no operator takes** (`resetApiState()` under a mounted dialog): recorded with the path;
  whether to pin it is raised with the user before a test is written, since it asserts a state the product does not produce.
- **T2's substitution changes behaviour on purpose**: it is a probe, restored from git, never committed.
- **A throwaway file left in the tree**: `git status --short -- apps` is checked after every probe, and before every commit.

## Project Structure

```text
specs/486-layout-chain-gate-term/   spec.md, plan.md, tasks.md, verification.md, checklists/
apps/management-web/src/features/layouts/LayoutEditorDialogChainAbsent.test.tsx   (only if D1 finds the state reachable; new)
```
