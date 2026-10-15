# Spec 374: The handler-deconstruction guard scans `(var a, var b) = x;`

**Feature Branch**: `test/2914-widen-handler-deconstruction-guard` (cut from `origin/develop` at `38c35d72`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. One scope judgement is
marked in §5 (the same-name record resolution fix rides along, because the widening cannot be green
without it).

**Input**: Issue [#2914](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2914),
*HandlerDeconstructionTests does not scan handlers that deconstruct as (var a, var b) = command;*.
Owner decision in the issue (2026-10-15): **enforce** — widen the guard, do not rewrite the handlers,
no ADR.

**Lane.** Autonomous (ADR-0144). **Production code unchanged; test-only.** The guard's own behaviour
changes (it rejects code it used to wave through), so phase 4a is **red**: constructed transpositions
in the new spelling are shown failing against today's guard, then passing once it is widened.
Production behaviour is not touched, and the real tree is shown green both before and after.

**Spec number.** 374. Checked 2026-10-15 against `origin/develop` (highest: 372), every local and
remote branch head (`git ls-tree … specs/`) and open PRs: 373 is claimed by the unmerged
`fix/2908-redact-camera-url-in-logs` (PR #2980); nothing claims 374. Re-check before the PR is
opened.

**ADRs.** ADR-0144 (autonomous lane; red colour for a behaviour-changing test), ADR-0036 (smallest
change), ADR-0040 / ADR-0073 (integration events in `Shared.Contracts` carry primitives — why a
same-typed transposition compiles), ADR-0119 (cites this guard as the mechanism protecting
deconstruction order). The rule itself is CLAUDE.md's house rule *"Handlers destructure their input
first"*, which already names this test as its guard. **No new ADR**: this closes a hole in an
enforced rule; it decides nothing.

**Latency budget.** N/A. A source-scanning architecture test; nothing on any §IV leg.

---

## 1. What was measured (premise verified, not assumed)

On `38c35d72` (= `origin/develop`, 2026-10-15).

**The guard today.** `tests/Architecture.Tests/HandlerDeconstructionTests.cs`, one `[Fact]`
`Handler_deconstructions_bind_their_records_fields_in_order`:

- finds `public async Task… Handle|HandleAsync(Type parameter, …` in every `src/**/*.cs`;
- in that method body takes the **first** match of `var\s*\(([^)]*)\)\s*=\s*(\w+);` and proceeds
  only if its right-hand side is the handler's parameter;
- resolves `Type` by searching all sources for `record Type(` — **by simple name, first file found** —
  plus any hand-written `Deconstruct` overloads, picks the overload by arity;
- fails when a local is named (case-insensitively) after a **different** field of the record;
  renames to non-field names and `_` pass. Also asserts `checkedCount > 0`.

```
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstructionTests"
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

**The hole is real.** Grepping `src/` for `(var a, var b, …) = x;` (including two multi-line forms)
and running the guard's own logic with a widened regex in a throwaway script (outside the repo):

| | Handlers scanned |
|---|---|
| Today's regex (`var (…) = p;`) | 45 |
| Widened (`var (…)` **or** `(var a, …, _) = p;`) | 85 |
| of which the parenthesised form, newly scanned | **40** |

The 40 newly scanned handlers, by context: AuditObservability 2 (GetResourceTimeline, SearchAudit),
Automation 1 (CreateRule), CameraCatalog 3 (ListCameras, RegisterCamera, RetireCamera),
EventIngestion 2 (GetEvent, RegisterWebhookIntegration), Identity 6 (DisableDevice, DisableKiosk,
DisableWebhookClient, EnrollKiosk, RegisterDevice, RotateWebhookClient), LayoutComposition 12
(Archive/BranchDraft/EditDraft/Publish/Revert Revision, CreateLayoutDraft, CreateWall,
EditWallScenes, GetLayout, GetWall, ListLayouts, SwitchWallScene), OverlayDesigner 6
(Archive/BranchDraft/EditDraft/Publish/Revert Revision, CreateOverlayDraft), StreamDistribution 5
(AuthorizeWhep, GetStream, ListStreams, ProvisionStream, ReportStreamHealth), SystemVariables 3
(ArchiveVariable, DefineVariable, SetVariableValue).

**No real transposition exists.** With the widened regex and context-scoped record resolution (§5),
all 85 pass: **0 failures**. Every one of the 40 binds each local either to its own field's name, to
a non-field rename (`layoutIdentifier` for `Layout`, `wallIdentifier` for `Wall`, `identifier` for
`Layout`, `newScenes` for `Scenes`, `rawPageSize`/`rawCursor`), or to `_`. So this is not a bug fix
and nothing is split off.

**The widening exposes a latent guard defect.** With the regex widened but record resolution
unchanged, the real tree goes **red with 5 false failures** — every OverlayDesigner revision handler:

```
src/OverlayDesigner/…/ArchiveRevisionCommandHandler.cs: ArchiveRevisionCommand arity 4 none
(likewise BranchDraftRevision, EditDraftRevision, PublishRevision, RevertRevision)
```

`ArchiveRevisionCommand`, `BranchDraftRevisionCommand`, `EditDraftRevisionCommand`,
`PublishRevisionCommand` and `RevertRevisionCommand` are each declared in **both** LayoutComposition
and OverlayDesigner. The guard resolves by simple name to whichever file it enumerates first, so it
checks the OverlayDesigner handlers against LayoutComposition's records. Where the arities differ that
is a false failure; where they coincided it would be a **silent false pass** against the wrong field
list. `Directory.EnumerateFiles` order is not guaranteed on Linux, so which record wins is also not
stable across CI and a Windows box.

**Counterfactual (constructed, not committed).** A fixture record `SampleV1(string Kind, string
Device)` and handler `Handle(SampleV1 message, …)`, run through the throwaway script:

| Handler body | Today's regex scans it? | Widened result |
|---|---|---|
| `(var device, var kind) = message;` | no (0 checked) | 2 failures (positions 0, 1) |
| `(var device, _) = message;` | no | 1 failure (position 0) |
| `var (device, kind) = message;` | yes | 2 failures — control: today's guard already catches it |
| `(var kind, var device) = message;` | no | scanned, 0 failures |

**Out of the issue's reach, recorded not changed.** Private helpers that deconstruct the command
(e.g. `RotateWebhookClientCommandHandler.PrepareAsync`, line 115) are not handler signatures and stay
unscanned, as they are today for the `var (…)` form. Explicitly typed tuples (`(string a, int b) =
x;`) do not occur in `src/` handlers (grep, 0 hits) and stay out of scope — `var` is the house style.

## 2. User story

### US1 (P1): A transposition in either deconstruction spelling fails the build

As a maintainer, I want `HandlerDeconstructionTests` to check handlers that deconstruct as
`(var a, var b) = message;` (and with discards, `(var a, _) = message;`) exactly as it checks
`var (a, b) = message;`, so that a transposed same-typed field cannot reach `develop` merely because
the handler used the other spelling — and I want it to check each handler against **its own
context's** record, so a same-named command in another context neither fails nor masks it.

**Why P1 / only story.** The rule is enforced (owner decision); half the handlers escaping it is the
whole defect.

**Independent test.** `dotnet test tests/Architecture.Tests --filter
"FullyQualifiedName~HandlerDeconstructionTests"` — every fixture fact and the real-tree fact pass;
reverting the regex alone turns the parenthesised fixture facts red; reverting the resolution alone
turns the same-name fixture facts and the real-tree fact red.

### Acceptance scenarios

```gherkin
Scenario: a transposition in the parenthesised spelling fails
  Given a record SampleV1(string Kind, string Device)
  And a handler whose body is "(var device, var kind) = message;"
  When the scan runs
  Then it reports position 0 'Kind' bound as 'device', naming position 1
  And it reports position 1 'Device' bound as 'kind', naming position 0

Scenario: a transposition with a discard fails
  Given the same record and "(var device, _) = message;"
  Then exactly one failure is reported, at position 0

Scenario: a correct parenthesised deconstruction is scanned and passes
  Given the same record and "(var kind, var device) = message;"
  Then one handler is counted as checked
  And no failure is reported

Scenario: the existing spelling is unaffected (control)
  Given the same record and "var (device, kind) = message;"
  Then two failures are reported, as today

Scenario: a same-named record in another context is not used
  Given src/Alpha declares record ArchiveCommand(string Kind, string Device)
  And src/Beta declares record ArchiveCommand(string Device, string Kind)
  And a src/Beta handler binds "var (device, kind) = message;"
  Then no failure is reported (Beta's own order)
  And a src/Beta handler binding "var (kind, device) = message;" reports a transposition

Scenario: a record from outside the handler's context still resolves when unique
  Given a record declared only in src/Shared.Contracts
  And a handler in src/Automation deconstructing it
  Then it is checked against that record

Scenario: an ambiguous name outside the handler's context is reported, not guessed
  Given two contexts other than the handler's declare the same record name, and its own does not
  Then the scan reports the record as ambiguous

Scenario: the real tree passes and the new spelling is actually reached
  When the scan runs over src/
  Then no failure is reported
  And at least one handler in the parenthesised spelling is counted as checked
```

(Gherkin's "conflict / bad-request / auth" categories have no meaning for an architecture test; the
negative cases above are their analogue.)

## 3. Requirements

- **FR-001** The scan recognises, as a handler deconstruction of its parameter `p`, both
  `var (a, b, …) = p;` and `(var a, var b, …) = p;` where each element is `var <name>` or `_`, in any
  mix, across line breaks.
- **FR-002** Bound names from the parenthesised form are the identifiers after `var`; `_` stays `_`.
  Checking is identical for both spellings (same arity selection, same message text).
- **FR-003** Every deconstruction of `p` in the handler body is checked, not only the first match in
  the body — widening the pattern must not let an earlier unrelated deconstruction shadow the one of
  `p`.
- **FR-004** A record is resolved first among files of the handler's own context (`src/<Context>/`);
  if none, the unique declaration elsewhere in `src/`; if several elsewhere, the scan reports the
  ambiguity as a failure. The cache is keyed by context and name.
- **FR-005** Source paths are normalised to `/` before any path-prefix comparison (Linux CI vs
  Windows).
- **FR-006** No file under `src/` changes. No existing handler is rewritten.
- **FR-007** The real-tree fact keeps its two existing assertions verbatim (`checkedCount > 0`,
  `failures` empty) and passes on `38c35d72`'s `src/`.

## 4. Success criteria

- **SC-001** The parenthesised-transposition, discard-transposition, correct-parenthesised and
  same-name fixture facts are observed **red** against today's guard, the failure output quoted in
  the PR (ADR-0139/0144).
- **SC-002** The control fixture (`var (device, kind)`) is observed **green** against today's guard —
  proving the fixture's record/handler are well-formed, so the red above is the hole, not the fixture.
- **SC-003** After the change, all facts in `HandlerDeconstructionTests` pass; the real-tree scan
  checks 85 handlers (45 + 40) with 0 failures — the figure quoted from the test's output or a
  diagnostic, not asserted as a pinned number.
- **SC-004** `git diff --stat origin/develop` touches only `tests/Architecture.Tests/` and this spec
  directory.

## 5. Judgement calls and assumptions

- **Resolution fix in scope (judgement).** FR-004 is not in the issue's text. It is included because
  the widened guard is red on the real tree without it (§1), and the issue's acceptance is "green on
  the real tree". It is a test-code defect, not a production bug fix, so the lane's "refactor that is
  also a bug fix is two issues" rule does not apply. If the reviewer disagrees, the alternative is a
  separate issue landing first; this spec would then depend on it.
- **Fixture seam (assumption).** To prove the guard on constructed input without committing a
  transposed production handler, the scan is lifted out of the `[Fact]` into a method taking the
  source map. That lift is test-only and characterised green (the existing fact, assertions verbatim)
  before any red fact is added.
- **No new test class** in a sharded project: `Architecture.Tests` is not in
  `tests/Integration.Tests/ci-shards/`, and the new facts live in the existing class. A new
  non-test helper file is allowed (plan §3).
