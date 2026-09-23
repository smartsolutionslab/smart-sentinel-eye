# Tasks 230 — The orphan the vocabulary keeps

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2503

**Colour: behaviour-preserving, characterisation, observed green** (ADR-0144 phase 4a).
**Engineer:** backend-engineer (phase 4a run only). There is **no phase-4b
implementation**, because FR-002 is that no file under `src/` or `tests/` changes.

No foundational tasks, and no `[P]` fan-out. The tasks are sequential and small.

## US1 (P1): The remove-or-keep question has an answer on record

**[T001] [US1] Characterise the vocabulary: observed green**
Run
`dotnet test tests/AuditObservability.Domain.Tests --filter "FullyQualifiedName~ResourceKindTests"`
on the branch tip. Return the **verbatim** output. It must show
`Accepts_every_member_of_the_v1_vocabulary(member: "webhook")` and
`All_returns_the_full_vocabulary` passing. Do **not** edit the test file. An
assertion that has to change is evidence that the behaviour moved: block.
*Done when:* the output is captured and green.

**[T002] [US1] Prove the diff is decision-only** (depends on T001)
`git diff --stat origin/develop -- src tests` prints nothing. The PR diff is limited to
`specs/230-the-orphan-the-vocabulary-keeps/`.
*Done when:* SC-2 holds.

**[T003] [US1] Record the decision on the issue** (depends on T002)
The PR body quotes T001's output, links `spec.md` §2, and uses `Closes #2503`. After
the merge, check that #2503 is actually closed (a mention rarely auto-closes it). If it
is still open, close it with a comment that summarises the decision: *kept, because
removal is a 200→400 break no caller needs, nothing depends on exact producibility, and
`Rule` is in the same state and kept by spec 226.*
*Done when:* #2503 is closed with the decision linked.

## Phase notes for the PR body

- Phase 4b: skipped, because the decision requires no source change (FR-002).
- Phase 5: skipped, because it is documentation-only (T001 is the observation).
- Phase 6: `/code-review` on a spec-only diff. `/security-review` is not applicable.
