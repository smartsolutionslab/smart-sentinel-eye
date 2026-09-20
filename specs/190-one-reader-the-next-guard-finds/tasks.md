# Tasks 190 — One reader the next guard finds

**Spec:** `specs/190-one-reader-the-next-guard-finds/spec.md`
**Plan:** `specs/190-one-reader-the-next-guard-finds/plan.md`
**Issue:** [#2257](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2257)
— **already on Project #13** ("Smart Sentinel Eye"), status **Todo**, labels
`tech-debt`, `agent:ready`, no `agent:blocked`. Verified 2026-09-20 via
`gh project item-list 13 --owner smartsolutionslab --limit 2000`. **No
`item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **GREEN (characterisation)**

Behaviour-preserving, and not ambiguous. Nothing about what any guard reports
changes; the whole deliverable is that six files stop each carrying their own
copy of the same plumbing. The issue exists **because** spec 130 refused to fold
this into a behaviour-changing commit — running it as a red here would recreate
exactly the confusion the phase-4 split prevents.

**The covering suite already exists: it is the six guards themselves.** They are
captured passing before the change and must pass **unmodified** after.

> **An assertion that has to be edited is evidence the behaviour moved. Block,
> do not adjust.** No pinned count, no expected-message string, no `Should*`
> line, no `[Fact]` body in any of the six files may change. A diff that touches
> one is a blocked outcome and goes back with the verbatim failure — it is not a
> judgement call.

**Captured before anything was written** (2026-09-20, this worktree, cut from
`origin/develop`):

```
Passed!  - Failed:     0, Passed:   189, Skipped:     0, Total:   189, Duration: 3 s
  - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

So the baseline is "every assertion passes". No guard currently reports a
violation, which is what makes byte-identical output a usable proof rather than
a wish.

### Roles

| Phase | Agent | Why |
|---|---|---|
| 4a | **`test-writer`** | Writes the fixture corpus and the characterisation harness, captures the six guards' verbatim baseline, returns it. Does **not** perform the extraction — the phase-4 split is the point. |
| 4b | **`backend-engineer`** | See below. |
| 5 | `/verify` | Spec §4's procedure. |
| 6 | **`backend-reviewer`** | See below. |

**Why `backend-engineer` and not `infra-engineer` or `test-writer` for 4b.**
The artefact is C# in a .NET test project, and the skill it needs is this
repository's C# conventions — `internal` visibility, collection expressions with
an explicit type, no leading underscore on private fields, NRT, XML docs that
carry the reasoning. `infra-engineer`'s brief is AppHost wiring, CI workflows,
Docker, Keycloak, the gateway, observability and k3s; **none of those is
touched, and no csproj or workflow changes at all** (plan §1). `test-writer`
writes tests; this is plumbing under tests, and the same agent must not both
write the characterisation and perform the change it characterises.

A judgement call, made and justified rather than defaulted: `tests/` work has no
dedicated role in `.claude/agents/`, and `backend-engineer` is the closest brief
by language and convention.

**Why `backend-reviewer` and not `security-reviewer`.** Spec AS-7: the diff
touches no `src/` assembly, no endpoint, no scope, no realm, no token, no trust
boundary and no secret. Nothing in it can be reached by a request. Recorded in
writing so a reviewer does not have to infer the omission.

### File contention (ADR-0109)

Checked 2026-09-20: every remote branch diffed against `origin/develop` for the
six paths — **zero hits**. The only open PR is **#2463**
(`2274-client-delete-assertion`), which touches `Integration.Tests` and
Identity. `sse-2270`'s `ContainerImagePinTests.cs` work is merged and is not one
of the six. **Re-check before pushing**, because a parked PR may have merged
since.

### Parallelism

**Almost none, and that is honest rather than pessimistic.** All six files live
in one project and three of the stories edit the same six files in sequence.
`[P]` is marked only where two tasks genuinely own disjoint files.

**T1 and T2 are foundational**: nothing in US1–US3 can start until the
characterisation exists and is green.

---

## Phase 4a — characterisation (agent: `test-writer`)

| ID | P | Story | Task | Done when |
|---|---|---|---|---|
| **T1** | | 4a | Create `tests/Architecture.Tests/SourceScanFixtures.cs` — the 13 fixtures of plan §6 as `internal const string` raw string literals. `RawStringLiteral` needs a **four**-quote delimiter; say so in a comment. | File compiles; every fixture in plan §6's table exists. |
| **T2** | | 4a | Create `tests/Architecture.Tests/SourceScanCharacterisationTests.cs`. **M1**: for every fixture × every `MaskStrictness`, the expected output written out **literally** (not hashed). Plus the three **disagreement** assertions — the strictnesses must be shown to differ on `VerbatimString`, `EscapedQuote`, `QuoteAsCharLiteral`. **M2**: frozen verbatim copies of the four pre-refactor maskers and the two pre-refactor `StatementEnd` shapes, named `…AsSpec072Wrote` / `…AsSpec075Wrote` / `…AsSpec070Wrote`, swept char-for-char over every `.cs` under `src/*/Api`; first mismatch reported with file, offset and **both** characters. | Both observed **GREEN**. `SourceMask`/`RouteChainReader` do not exist yet, so M2 compares frozen copy against frozen copy at this point — that is expected, and T3 is what makes it meaningful. |
| **T3** | | 4a | Capture the baseline: `dotnet test tests/Architecture.Tests/… --logger "console;verbosity=detailed" --nologo > before.txt`, then `grep -E '^\s+(Passed\|Failed)\s' before.txt \| sort > before-tests.txt`. Also run the one-off surface capture of spec §4 step 2 and save it. Return **both files' contents verbatim**. | Verbatim output returned to the orchestrator. It is quoted in the PR body; a measurement reported only to the orchestrator is invisible to every later reader. |

**T2 is where this spec earns its keep.** A characterisation that only asserted
"the three maskers agree on today's corpus" would pass just as well against one
collapsed masker and would have caught nothing.

---

## Phase 4b — US1: one repository-root finder (agent: `backend-engineer`)

| ID | P | Story | Task | Done when |
|---|---|---|---|---|
| **T4** | | US1 | Create `tests/Architecture.Tests/RepositorySource.cs` — `internal static class` with `Root()`, `RelativePath(root, file)`, `ExecutableLines(source)` and the `StringLiteral` regex, bodies **byte-identical** to what they replace. XML docs per plan §3.1, including the platform-separator warning verbatim. | Compiles; suite green. |
| **T5** | | US1 | Repoint `ConcurrencyConflictDeclarationTests.cs` — delete `RepositoryRoot` (`:1278`) and `Relative` (`:1275`), call `RepositorySource`. **Add the call first, delete second.** | That class's tests green; its console output byte-identical to T3's for those tests. |
| **T6** | | US1 | Same for `EndpointScopeDeclarationTests.cs` (`:1875`, `:1860`, `ExecutableLines` `:2227`, `StringLiteral` `:510`). | As T5. |
| **T7** | | US1 | Same for `PreconditionDeclarationTests.cs` (`:1159`, `:1156`). | As T5. |
| **T8** | | US1 | Same for `RouteValueRefusalDeclarationTests.cs` (`:979`, `:976`). | As T5. |
| **T9** | | US1 | Same for `StatusProducerDeclarationTests.cs` (`:967`, `:960`). | As T5. |
| **T10** | | US1 | Same for `PaginatedConsumerTests.cs` (`:625`, `:602`, `ExecutableLines` `:610`, `StringLiteral` `:153`). This is guard 6's **only** appearance — it has no masker and no chain reader. | As T5. |
| **T11** | | US1 | Full Architecture.Tests run; diff the sorted test-name file against T3's. Commit: `refactor(tests): one repository-root finder for the six source-scanning guards`. | Files byte-identical; commit builds and passes **on its own** (ADR-0087). |

T5–T10 edit six different files but share `RepositorySource.cs`'s API and must
land in one commit, so they are **not** `[P]` — running them concurrently would
split one commit across six agents for no gain.

---

## Phase 4b — US2: one masker, three named strictnesses

| ID | P | Story | Task | Done when |
|---|---|---|---|---|
| **T12** | | US2 | Create `tests/Architecture.Tests/SourceMask.cs` — `MaskStrictness` (four members, plan §3.2, **no default anywhere**) + `Apply(text, strictness)` + `UnhandledForms(strictness)`. Each enum member's XML doc names the guard it serves and what it cannot read. | Compiles. M1 and M2 now compare shared-against-frozen and are **green**. |
| **T13** | | US2 | Extend `SourceScanCharacterisationTests` M2 to assert `SourceMask.Apply(text, …)` char-for-char equals each frozen copy over the real corpus. | Green. This is the proof; T14–T18 are the consequence. |
| **T14** | | US2 | Repoint `ConcurrencyConflictDeclarationTests` → `CommentsOnlyLiteralsIntact`. Move `UnmaskableLiteralForms` (`:220`) into `SourceMask.UnhandledForms`; the guard's own assertion at `:772` keeps its message and its shape and iterates the shared list. **Hardest first — if this one cannot be repointed without an assertion moving, stop here.** | Class green; output byte-identical; `The_api_sources_use_only_…` message unchanged. |
| **T15** | | US2 | Repoint `EndpointScopeDeclarationTests` → `CommentsBlankedLiteralsIntact` (stage 1, `WithoutComments` `:1900`) and `LiteralInteriorsOnly` (stage 2, `MaskLiterals` `:1964`); `Masked` (`:1888`) becomes the composition of the two. Delete `EndOfLiteral` (`:2007`) into `SourceMask`. **Both stages must survive** — this guard reads literal content at masked offsets. | As T14. |
| **T16** | | US2 | Repoint `PreconditionDeclarationTests` → `CommentsAndLiteralInteriors`; delete `Mask` + `MaskBlockComment` + `MaskVerbatim` + `MaskLiteral` + `Next` + `Blank` (`:1031`ff). | As T14. |
| **T17** | | US2 | Same for `RouteValueRefusalDeclarationTests` (`:851`ff). | As T14. |
| **T18** | | US2 | Same for `StatusProducerDeclarationTests` (`:838`ff). | As T14. |
| **T19** | | US2 | **Counterfactual (AS-4).** Temporarily change `CommentsOnlyLiteralsIntact` to also blank literal interiors. `ConcurrencyConflictDeclarationTests`' register assertions must fail; `The_api_sources_use_only_…` must still pass (it is a corpus assertion, not a masker assertion). **Quote both outcomes verbatim**, then revert. | Both observed and quoted. A shared masker whose weakest mode is never shown load-bearing is one somebody collapses in six months. |
| **T20** | | US2 | Full run; diff against T3. Commit: `refactor(tests): one masker with three named strictnesses`. | Byte-identical; commit builds and passes on its own. |

---

## Phase 4b — US3: one chain reader

| ID | P | Story | Task | Done when |
|---|---|---|---|---|
| **T21** | | US3 | Create `tests/Architecture.Tests/RouteChainReader.cs` per plan §3.3 — `ChainEndSentinel`, `ChainLiteralHandling` (both **required**, no defaults), `StatementEnd`, `Balanced`, `SplitArguments`, `Unquote`, `LineOf`, `HandlerBodyFor`, `HandlerBody`, `ClassSpan`. Carry EndpointScope's 22-line "do not fix this to -1" doc onto `EndOfText` and #2183's two-holes story onto `StepOverStringAndCharLiterals`. | Compiles; M2's chain-end sweep green. |
| **T22** | | US3 | Repoint `ConcurrencyConflictDeclarationTests` → `(NotFound, StepOverStringAndCharLiterals)`; delete `StatementEnd` (`:1064`), `EndOfStringLiteral`, `EndOfCharLiteral`, `LineOf` (`:1223`). **Hardest first: the only literal-aware user.** | Class green; output byte-identical. |
| **T23** | | US3 | Repoint `EndpointScopeDeclarationTests` → `(EndOfText, AlreadyMasked)`; delete `StatementEnd` (`:2103`). **The only `EndOfText` user.** | As T22. |
| **T24** | | US3 | Repoint `PreconditionDeclarationTests` → `(NotFound, AlreadyMasked)`; delete `StatementEnd` (`:941`), `SplitArguments` (`:968`), `Balanced` (`:1003`), `LineOf` (`:1133`), `MethodBodies` (`:881`), `BodyAfter` (`:914`) → `HandlerBodyFor`. | As T22. |
| **T25** | | US3 | Same for `RouteValueRefusalDeclarationTests` (`:758`, `:785`, `:816`, `:823`, `:953`, `:698`, `:731`). Its `MethodBodies`/`BodyAfter` are byte-identical to T24's but for one doc-comment word — **verify that before deleting**, do not assume it. | As T22. |
| **T26** | | US3 | Same for `StatusProducerDeclarationTests` (`:751`, `:775`, `:806`, `:810`, `:940`). | As T22. |
| **T27** | | US3 | **Check and record**: are `EndpointScopeDeclarationTests`' and `StatusProducerDeclarationTests`' `MapGroup`-resolution walks byte-identical? If yes, extract; if no, leave both and write the difference into `RouteChainReader`'s class doc. Plan §3.3 leaves this open on purpose — it was not checked at phase 2. | Answer recorded either way in `verification.md`. |
| **T28** | | US3 | **Counterfactual (AS-5).** Temporarily pass `NotFound` where `EndpointScopeDeclarationTests` passes `EndOfText`. At least one slice-bound call site must throw `ArgumentOutOfRangeException` naming the **`length`** parameter. **Quote it verbatim**, then revert. | Observed and quoted. The existing doc asserts this in prose; nobody has ever seen it. |
| **T29** | | US3 | Delete M2 and the frozen `…AsSpecNNNWrote` copies from `SourceScanCharacterisationTests`. **M1 stays.** A permanent frozen copy of the thing just deduplicated is the defect this spec closes. | Suite green with M1 only. |
| **T30** | | US3 | Full run; diff against T3. Commit: `refactor(tests): one chain reader with the sentinel and literal-awareness as arguments`. | Byte-identical; commit builds and passes on its own. |

---

## Phase 4b — gates

| ID | P | Story | Task | Done when |
|---|---|---|---|---|
| **T31** | | all | `git diff -U0 origin/develop -- <the six files> \| grep -E '^[+-]' \| grep -vE '^[+-]{3}' \| grep -cE 'Should[A-Z]'` | Prints **`0`**. If it does not, the change touched an assertion — **block**, do not adjust. |
| **T32** | | all | `dotnet build -c Release` | Clean under `TreatWarningsAsErrors`. On an S125-style analyzer error in a file outside the diff, re-run once before treating it as this branch's fault. |
| **T33** | | all | Full `dotnet test` on `tests/Architecture.Tests` **and** the unit-test projects that reference it indirectly; confirm no other guard's pinned count moved. | All green; the sorted test-name file byte-identical to T3's. |

---

## Phase 5 — verify

| ID | P | Story | Task | Done when |
|---|---|---|---|---|
| **T34** | | all | Run spec §4's six-step procedure end to end as a reviewer would, from the captured before-state. | Steps 3 and 4 identical/zero; step 5's counterfactuals fail as predicted; step 6 clean. |
| **T35** | | all | Write `specs/190-one-reader-the-next-guard-finds/verification.md`: the before/after surface hashes, the two counterfactual outputs verbatim (T19, T28), T27's answer, and an honesty block naming spec A4 (the fixture corpus's completeness is unverifiable by construction; M2 was the backstop and M2 is now deleted). | Written. Every measurement recorded as observed — a figure reported only to the orchestrator is invisible to every grep and reviewer. |

---

## Phase 6 — QA

| ID | P | Story | Task | Done when |
|---|---|---|---|---|
| **T36** | | all | `/code-review` via **`backend-reviewer`**, with T31's check re-run independently by the reviewer. | Findings resolved or accepted in writing. |
| **T37** | | all | **No `/security-review`** — spec AS-7. Record the decision and its reason in the PR body. | Recorded. |

---

## Phase 7 — PR

| ID | P | Story | Task | Done when |
|---|---|---|---|---|
| **T38** | | all | `gh pr create --base develop`, body quoting **T3's verbatim baseline**, the after-run, T19's and T28's counterfactual outputs, and the `0` from T31. Phase-4a colour stated as **green/characterisation** with ADR-0144 cited. | PR open against `develop`, never `main`. |
| **T39** | | all | Close #2257 with a closing keyword; **verify the issue state after the merge** — a PR mention auto-closes roughly one time in three. | Issue closed, card moved. |

---

## Not in this spec, recorded so it is not lost

- **The other 25 copies of the repository-root finder.** 31 files under `tests/`
  contain the canonical walk (28 `Architecture.Tests`, 3 `Integration.Tests`);
  this spec migrates six. **Needs its own issue**, quoting the figure and the
  command:

  ```sh
  grep -rl 'while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))' tests/ --include=*.cs | wc -l
  ```

  Deliberately not swept here: a 31-file behaviour-preserving diff collides with
  every branch in flight and proves nothing the six do not.
- **Teaching any masker raw string literals.** All three blind spots are
  identical and all three are now documented on `MaskStrictness`. Closing the
  blind spot is a **behaviour change** with a red-first obligation — a different
  issue, and it must not ride this one. That is the same reasoning spec 130 used
  to defer *this* work, and it is the reason the deferral was worth writing down.
- **`ExecutableLines` in guards 1, 2, 4 and 5.** Only guards 3 and 6 carry the
  identical form; the other four `GuardSource` self-scans are shaped differently
  and are left alone.
- **A guard over the duplication itself.** The obvious next move — an
  Architecture.Test that fails when a seventh copy of the root finder appears —
  is **not** proposed here. It would be a new rule (ADR-0139 territory), which
  is a behaviour change, and it would fire on 25 existing copies on its first
  run. It belongs with the sweep issue above, not with this refactor.
