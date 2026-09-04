# Tasks: A page is not the list

**Spec**: `specs/065-a-page-is-not-the-list/spec.md`
**Plan**: `specs/065-a-page-is-not-the-list/plan.md`
**Issue**: #1982 — feature-level, on Project #13. No per-task issues (see plan §Gate).

**Phase 4a colour: RED.** Behaviour-changing — a build that failed on nothing now
fails on something. The guard must be observed failing against a deliberately
reintroduced defect and that failure quoted in the PR (ADR-0139, constitution
§Testing).

**Parallelism**: almost none, and that is a property of the feature rather than a
decomposition failure. There is one production artefact — `PaginatedConsumerTests.cs` —
and every implementation task edits it. Only the documentation tasks own disjoint
files. `[P]` is marked where it is genuinely true (ADR-0109) and nowhere else;
inventing more would be a fan-out the orchestrator cannot actually take.

---

## Story 1 (P1) — A new caller cannot discard the boundary in silence

### Foundational — blocks everything below

- **[T001]** `[Story 1]` Create `tests/Architecture.Tests/PaginatedConsumerTests.cs`
  with the class, the file-reading helper (mirroring the relative-path helper in
  `OverlayFrameClaimTests`), and the register as `[Theory]` `InlineData` — the
  three rows in plan.md §The register. No assertions yet.

  *Done when*: the file compiles and the suite runs with the new class present and
  no test bodies. Establishes the shared surface every later task edits, so
  nothing below can start in parallel with it.

  **Path normalisation**: `Path.GetRelativePath` returns the platform separator.
  A backslash literal in a path comparison is green on Windows and red on Linux
  CI — normalise to `/` before comparing or matching.

### Assertion 1 — every consumer reads the boundary

- **[T002]** `[Story 1]` Implement the consumer sweep: for a given hook name,
  enumerate files under `apps/*/src` and `apps/shared/src` naming that hook,
  excluding `*.test.ts`, `*.test.tsx`, and `apps/shared/src/api/*.api.ts`.

  *Done when*: for `useListCamerasQuery` it returns exactly
  `apps/management-web/src/features/cameras/CamerasPage.tsx`; for
  `useGetResourceTimelineQuery` it returns the empty set.

  *Depends on*: T001.

- **[T003]** `[Story 1]` Assert each swept file names its row's boundary field.
  Failure message names the file, the hook, and the field, and says in one
  sentence why it matters. Sentence-style test name with underscores (ADR-0053).

  *Done when*: green on the tree as it stands, for all three register rows.

  *Depends on*: T002.

### Assertion 2 — the register is complete

- **[T004]** `[Story 1]` Assert that every exported interface in
  `apps/shared/src/api/*.api.ts` carrying a bounded shape — a list field with
  `count` **and** `offset`/`limit`, or a list field with `nextCursor` — appears in
  the register.

  *Done when*: green today (finds exactly `CameraListPage`, `CameraChoices`,
  `AuditPage`); and, with a throwaway fourth interface pasted into a scratch
  `*.api.ts`, red — then the scratch file is deleted.

  *Depends on*: T001. Independent of T002/T003 in logic but shares the file, so
  **not** `[P]`.

### Assertion 3 — no exception mechanism

- **[T005]** `[Story 1]` Assert the guard's own source contains no allowlist,
  skip-list or exception collection. Keep it to a handful of lines; document in
  the XML comment that removing it is a human decision with a stated reason, per
  FR-005.

  *Depends on*: T001.

### The red — phase 4a proper

- **[T006]** `[Story 1]` **Observe the guard green** on the untouched tree and
  capture the output. This is the control; without it a later red proves only
  that something changed.

  *Depends on*: T003, T004, T005.

- **[T007]** `[Story 1]` **Reintroduce the defect in the working tree only.** In
  `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx`, remove the
  truncation-notice block and every reference to `cameras.complete` and
  `cameras.count`, leaving it rendering `cameras.items` alone.

  *Done when*: `git diff` shows changes to that one file and nothing else.

  *Depends on*: T006.

- **[T008]** `[Story 1]` **Observe the guard fail** and capture the **verbatim**
  output. It must name `LayoutEditorDialog.tsx`, `useListAllCameraChoicesQuery`
  and `complete`. This output is what phase 4a returns to the engineer and what
  the PR body quotes — nothing else satisfies the gate.

  *Done when*: the failure text is captured. A failure that names a different
  file, or names nothing, means the sweep in T002 is wrong — fix T002, not the
  message.

  *Depends on*: T007.

- **[T009]** `[Story 1]` **Revert the demonstration.**
  `git checkout -- apps/management-web/src/features/layouts/LayoutEditorDialog.tsx`,
  then confirm `git status` is clean for `apps/` and the file is byte-identical to
  `HEAD`. Re-run the guard and confirm green.

  *Done when*: `git diff HEAD -- apps/` is empty **and** the guard passes.

  **This is not a formality.** Committing the demonstration would re-ship the
  exact defect this feature exists to prevent, in the file it was originally
  found in. It is a separate task so that it is separately verified.

  *Depends on*: T008.

---

## Story 2 (P2) — The audit is readable a year from now

- **[T010]** `[P]` `[Story 2]` The survey is written — `spec.md` §The survey,
  complete with the endpoint table, the classification table, the
  boundary-does-not-apply section, and the out-of-scope backend finding. Owns
  `specs/065-a-page-is-not-the-list/spec.md`; disjoint from every other file in
  this feature.

  *Status*: **done at phase 1**. Listed so the tasks account for the whole
  deliverable rather than only the code.

- **[T011]** `[P]` `[Story 2]` Add the feature issue #1982 to Project #13:

  ```sh
  gh project item-add 13 --owner smartsolutionslab --url <issue-url>
  ```

  Needs the `project` scope (`gh auth refresh -s project,read:project`).
  `item-add` prints nothing on success; verify with `--limit 2000`, because
  `item-list` defaults to 30 and a filled board reads as empty otherwise. Touches
  no file — genuinely parallel with everything.

---

## Story 3 (P3) — Leave the follow-ups filed, not remembered

- **[T012]** `[P]` `[Story 3]` File a follow-up issue for the
  `CameraCatalogFabLookup` skip-under-concurrent-write finding (spec.md §One
  finding outside the stated scope): offset paging under `registeredAt desc` can
  skip a row when a camera is registered mid-walk, leaving one stream without a
  fab attribution until the next startup. Include the file, the mechanism, and
  that it is startup-only and self-healing on restart.

  *Why a task rather than a note*: the finding's whole value is that someone acts
  on it. A survey that records a defect and files nothing has moved the defect
  from the code into a document.

  **Judgement, not automation**: whether to file, and at what priority, is a human
  call. If the lane cannot file it, say so in the PR rather than dropping it.

- **[T013]** `[Story 3]` Decide — with a human — whether the unbounded list
  endpoints (`/layouts`, `/overlays`, `/rules`, `/system-variables`, …) warrant
  an issue for response size at scale. spec.md names the exposure and states
  that nothing tracks it. **This is explicitly not a defect of the audited
  shape**, and filing it without a decision would inflate a clean survey into a
  backlog.

  *Depends on*: T012 (same conversation), and on a human. May close as
  "no action, recorded in spec 065".

---

## Dependency summary

```
T001 ──┬── T002 ── T003 ──┐
       ├── T004 ──────────┤
       └── T005 ──────────┴── T006 ── T007 ── T008 ── T009
T010 [P]   (done)
T011 [P]
T012 [P] ── T013
```

Everything from T001 to T009 is one chain through one file. T010–T012 are
genuinely parallel with it and with each other.

---

## What would make this two features

Recorded because the honest answer to "is this too large?" is *no*, and a plan
should say what would change that.

If the survey had found even one silently-truncating caller, this would split:
**the survey and the fixes** (behaviour-changing, one issue per truncating
caller, since each has its own operator-visible consequence and its own test),
then **the guard** once the tree is clean — because a guard shipped over known
violations needs an allowlist, and an allowlist is the gate-weakening move
ADR-0144 forbids the lane from making.

It did not, so it does not. The survey is the evidence for keeping it whole.
