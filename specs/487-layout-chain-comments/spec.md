# Spec 487 — Correct two layout test comments that overstate the `currentChain` Save-gate term

**Feature Branch**: `487-layout-chain-comments`
**Created**: 2026-11-09
**Status**: Draft
**Issue:** #3237 (feature-level; follow-up of #3232 / spec 486, merged in #3233).
**ADRs:** ADR-0036 (smallest change, comments say _why_ only when non-obvious), ADR-0139 / ADR-0144 (phase 4a), ADR-0037 (phases).
**Refs:** spec 486 `verification.md` (the T1–T6 matrix), spec 153 / 156 / 160 (the chain read and `saveBlocked`).
**No new ADR.** Comment-only.
**Latency budget (constitution §IV): NOT on the path.** Test comments in the operator's layout authoring form in `management-web`; no
leg between event arrival and overlay render is touched. The PR says so.
**Phase 4a colour:** behaviour-preserving → **characterisation**, in its comment-only form: the proof is that the code of each touched
file, with comments stripped, hashes identically before and after, shown able to fail by a counterfactual. No assertion is added,
removed or edited.

## Problem

Spec 486 showed that `currentChain === undefined` in `layoutEditor/useSaveGate.ts` is redundant in the real RTK Query store (no render has
no data, idle and not failed), and that no test in `src/features/layouts` depends on it: dropping it (T1) leaves all 213 green. Two test
comments say otherwise.

1. `LayoutEditorDialogChainRetention.test.tsx` — the third `it`'s doc block (around `:40`) and the comment at around `:354` say that once B's
   read _settles_, "`currentChain === undefined` is the only thing left standing between the operator and A's version going out under
   B's identity". The settled-with-a-500 read is `isError`, so `chainFailed` stands there too; T1 leaves the file green.
2. `LayoutEditorDialogChainRecovery.test.tsx` (around `:451`) says the Retention `it`s whose Save-unavailable assertion runs with
   `currentChain === undefined` are "closed by the `currentChain === undefined` half of the predicate alone — `chainFetching` could be
   deleted and that file would stay green".

**A correction to spec 486's own note, recorded here because this spec would otherwise repeat it.** `specs/486-…/verification.md` says the
second comment "is contradicted by T4 as run: with `chainFetching` deleted, `ChainRecovery`'s FR-005 case fails". That conflates two
files. The comment is about the **Retention** file, which T4 leaves green, so "chainFetching could be deleted and that file would stay
green" is **true as written**. What is false is the word _alone_: those Retention cases are covered jointly by the term and by
`chainFetching`/`chainFailed`. T1, T5 and T6 each leave the Retention file green too, so no single term is what closes them. The
`ChainRecovery` FR-005 case that T4 fails is the test this very comment introduces to close the `chainFetching` gap. This spec's wording
follows the corrected reading; the correction to the 486 note is made in this spec's `verification.md`, not by editing the merged note.

## Scope

**In:** the wording of the two comments (three places), nothing else.

**Out (explicitly):** any assertion, test name, production file or behaviour; the 486 note (corrected by reference); the overlay form.

## User Scenarios & Testing

### User Story 1 (P1) — the comments say what carries each case

```gherkin
Scenario: a reader trusts the comment
  Given a settled failed read in the Retention file
  When they read why Save is unavailable
  Then the comment names chainFailed as closing it, with the currentChain term as a second, redundant cover

Scenario: the comment is rewritten
  Given only comments changed
  When each touched file is stripped of comments and hashed
  Then the hash equals the unmodified file's, and a one-token code change makes it differ
```

## Requirements

- **FR-001** Only comment text changes in `LayoutEditorDialogChainRetention.test.tsx` and `LayoutEditorDialogChainRecovery.test.tsx`.
- **FR-002** The new wording matches the T1–T6 evidence of spec 486 (jointly covered; `chainFetching` deletable in the Retention file;
  the term redundant in the real store) and cites no line numbers that the edit itself moves.
- **FR-003** Proof: comment-stripped hash of each touched file equal before and after; a counterfactual (one code token changed) makes
  the hash differ. Never assert on prose.
- **FR-004** The existing layouts suite (213 tests) passes unmodified; `eslint`, `tsc --noEmit`, `prettier --check` clean.
- **FR-005** The PR states that no §IV leg is touched.

## Success Criteria

- **SC-001** Equal hashes before/after for both files; unequal for the counterfactual.
- **SC-002** 213 layouts tests pass, count unchanged.

## Assumptions

- Spec 486's matrix (T1–T6, two identical passes) is the evidence; this spec does not re-run it.

## Dependencies

Independent. Builds on spec 486.
