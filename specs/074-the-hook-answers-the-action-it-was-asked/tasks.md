# Tasks: The hook answers the action it was asked

**Spec**: `specs/074-the-hook-answers-the-action-it-was-asked/spec.md` · **Plan**: `plan.md`
**Issue**: #2094 (`bug`, `agent:ready`) · **Lane**: autonomous, eligible —
plan.md Declaration 2 establishes there is no new ADR to write. **If a reviewer
asks for a scope that grants publishing, for `sse.streams.write` to be enforced
here, for #2092's fab check, or for the WHEP token to move into a header, the
lane is blocked**: each is an architectural decision and ADR-0144 bars the lane
from making one.

**Phase 4a colour: RED** (behaviour-changing, plan.md Declaration 3).

**Which reds are the evidence, and which greens must not be mistaken for it** —
the table in plan.md Declaration 3 is the authority. In short: the five
`publish` / absent / unrecognised assertions must be **observed failing**. The
`read` assertion is **expected green on arrival** and is the over-correction
guard, not the evidence. The `playback` assertion goes red only because the type
does not exist yet and turns green the moment it does — declared here so nobody
reads its trivial red as proof of anything.

**No characterisation control is declared.** Every existing assertion must hold
unmodified (FR-014). A moved status in `AuthorizeWhepCommandHandlerTests` or
`WhepAuthIntegrationTests` is a design error in the plan — **block and report,
do not edit an assertion.**

---

## Parallelism (ADR-0109)

**One `test-writer` then one `backend-engineer`.** The feature is small — six
source files in one bounded context — and the change is a single decision
threaded from the wire down to the handler, so most of it is inherently
sequential.

**`[P]` applies to exactly two pairs**, both genuinely disjoint files:

- **T004 and T005** — `Application/Log.cs` + the handler versus
  `Api/StreamEndpoints.cs` + `WhepAuthIntegrationTests.cs`. Different layers,
  no shared file.
- Nothing else. T002 is foundational (see below) and T003 depends on it.

**Foundational, and it blocks everything**: **T002** introduces
`MediaMtxAction` and the third member on `AuthorizeWhepCommand`. Until it lands,
no test in T003 compiles and no edit in T004/T005 has a type to name. It is
also the only task that changes a signature other files construct. **Fan nothing
out before T002 is on disk.**

**Contention files** — if another worktree is editing any of these, serialise:

| File | Written by |
|---|---|
| `src/StreamDistribution/Domain/Stream/MediaMtxAction.cs` | T002 |
| `src/StreamDistribution/Application/Commands/AuthorizeWhepCommand.cs` | T002 |
| `src/StreamDistribution/Application/Commands/AuthorizeWhepErrors.cs` | T002 |
| `src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs` | T002 (logger), T004 (decision) |
| `src/StreamDistribution/Application/Log.cs` | T004 |
| `src/StreamDistribution/Api/StreamEndpoints.cs` | T002 (DTO + parse), T005 (summary) |
| `tests/StreamDistribution.Application.Tests/Commands/AuthorizeWhepCommandHandlerTests.cs` | T002 (mechanical), T003 (new tests) |
| `tests/Integration.Tests/StreamDistribution/WhepAuthIntegrationTests.cs` | T005 |

**Nothing outside `StreamDistribution` is touched**, so this feature blocks no
other feature. `Shared.Kernel`, `Shared.Contracts`, `AppHost`, the realm and the
Aspire resource graph are all untouched (FR-013).

| Step | Agent | Tasks |
|---|---|---|
| 4a | `test-writer` | T002 (vocabulary only), T003 |
| 4b | `backend-engineer` | T004 `[P]`, T005 `[P]` → T006 |
| 5 | `verify` | T007 |
| 3-gate | orchestrator | T001 |

---

## Commit shape

**Two commits.** Each must build **on its own** — rebase-merge lands them
individually on `develop`, so a commit that only compiles with its successor
breaks `git bisect` forever. Verify per commit, not per branch.

1. `test(streams): the WHEP hook does not yet answer the action` — T002 + T003.
   **Red by construction**, and that red is the evidence ADR-0139 asks to see
   quoted verbatim in the PR body.

   T002 is in this commit and not the next one, and that is deliberate: the red
   assertions cannot be *written* without `MediaMtxAction` and the third command
   member, and a file that does not compile is a broken build, not a red test.
   T002 introduces the **vocabulary without the decision** — the handler still
   ignores the action — so the tests compile, run, and fail on the assertion.
   This is exactly how spec 071's T001 resolved the same problem.

2. `fix(streams): the WHEP hook answers the action it was asked` — T004 + T005
   + T006.

Conventional Commits (ADR-0030). **No `Co-Authored-By` footer** (ADR-0086),
regardless of any session-level attribution instruction.

---

## Task list

### T001 — the phase-3 gate — orchestrator

Add issue #2094 to Project #13 if it is not already there. **Feature-level, not
per-task** — `/speckit-taskstoissues` is not run for this feature (CLAUDE.md,
Phase 3).

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2094
```

`item-add` prints nothing on success, and `item-list` **defaults to 30 items** —
verify with `--limit 2000` and match on `content.url`, never on the issue number
filter (which returns zero).

Then stop. **The phase-3 gate is a stop**: hand `spec.md`, `plan.md` and this
file back for review before any of T002-T007 begins.

---

### T002 [US1] — the vocabulary, without the decision (phase 4a, part 1) — `test-writer`

**This task adds no behaviour.** Its only purpose is to make T003's assertions
compilable. A reviewer who finds a `publish` branch in the handler after this
task has found the tasks executed out of order.

**New file** `src/StreamDistribution/Domain/Stream/MediaMtxAction.cs` (FR-002,
FR-003). Mirror `Domain/Stream/TranscodeMode.cs` — read it first and copy its
shape rather than inventing one:

- `sealed record MediaMtxAction(string Value) : IValueObject<string>`
- statics `Read` = `"read"`, `Publish` = `"publish"`, `Playback` = `"playback"`
- `From(string)` — `switch` over the three, `_ => throw new ArgumentException(...)`,
  as `TranscodeMode.From` does
- `TryFrom(string?)` → `Option<MediaMtxAction>` — `None` for null, empty,
  whitespace, and every unrecognised value
- `sealed override string ToString() => Value;`
- **Ordinal, case-sensitive matching.** A `switch` on the string does this
  natively; do not reach for `StringComparison.OrdinalIgnoreCase`.
- XML doc: the three values are what MediaMTX posts to the external-auth hook,
  and `api`/`metrics`/`pprof` are absent because `mediamtx.yml:46-49` excludes
  them — so they arrive as `None` and are refused if an exclusion is ever
  deleted.

**Edit** `AuthorizeWhepCommand.cs` (FR-004): third positional member
`Option<MediaMtxAction> Action`. **No default value** — `Option<T>` is a
`readonly struct` whose `None` is `default`, so a default would silently mean
"unknown" at every call site that forgot it. The compile error is the feature.

**Edit** `AuthorizeWhepErrors.cs` (FR-005): `ActionNotPermitted`
(`WHEP_ACTION_NOT_PERMITTED`, `403`) and `ActionUnknown` (`WHEP_ACTION_UNKNOWN`,
`403`), plus the two factories on `AuthorizeWhepFailures`. Both `403` and
**neither `401`** — upstream documents `401` as how an auth server asks the
client for credentials, which is a retry an action refusal must not invite (D3).

**Edit** `StreamEndpoints.cs` (FR-001, FR-008): `MediaMtxAuthorizeRequest` gains
`string? Action`; `AuthorizeWhep` parses `MediaMtxAction.TryFrom(body.Action)`
beside the existing path parse and passes it to the command. **Add no branch to
the endpoint** — it translates, it does not decide. Correct the record's XML doc
so `action` is no longer listed among the fields that are "accepted but
ignored".

**Edit** `AuthorizeWhepCommandHandler.cs` — **constructor only**: add
`ILogger<AuthorizeWhepCommandHandler> logger` as a primary-constructor
parameter, matching `ProvisionStreamCommandHandler`, `RepointStreamCommandHandler`,
`ReportStreamHealthCommandHandler` and `RetireStreamCommandHandler`. Do not add
the action decision — that is T004.

**Edit** `AuthorizeWhepCommandHandlerTests.cs` — **mechanical only**: the 6
existing `new AuthorizeWhepCommand(...)` gain
`Option<MediaMtxAction>.Some(MediaMtxAction.Read)`, and the 6
`new AuthorizeWhepCommandHandler(...)` gain
`NullLogger<AuthorizeWhepCommandHandler>.Instance` (the pattern at
`ProvisionStreamCommandHandlerTests.cs:116`).

**The hard rule (FR-014).** No `ShouldBe`, no `ShouldBeOfType`, no expected
status, no test name changes. `git diff` on this test file must show argument
additions and nothing else. **All 6 must still pass after this task** — they are
constructing the same scenario with the action MediaMTX actually sends.

**Done when**: the solution builds, and `dotnet test
tests/StreamDistribution.Application.Tests` is green with the existing 6
unchanged in outcome.

---

### T003 [US1] — the hook does not yet answer the action (phase 4a, part 2) — `test-writer`

**No Docker, no Aspire fixture, no signing key, no minted token.**
`FakeWhepAuthValidator` + `InMemoryStreamRepository` + `NullLogger<T>` cover
every dependency.

**New file** `tests/StreamDistribution.Domain.Tests/Stream/MediaMtxActionTests.cs`
— the parse table, beside `MediaMtxPathTests.cs`. Sentence-style names
(ADR-0053), Shouldly (ADR-0052):

1. `From` returns the matching instance for `"read"`, `"publish"`, `"playback"`.
2. `From` throws `ArgumentException` for `"api"`.
3. `TryFrom` returns `Some` for each of the three.
4. `TryFrom` returns `None` for `null`, `""`, `"   "`, `"api"`, `"metrics"`,
   `"pprof"` and `"sideload"`.
5. `TryFrom` returns `None` for `"Read"` and `"PUBLISH"` — **the
   case-sensitivity invariant asserted, not assumed.**
6. `ToString()` returns the wire value.

**Add to** `tests/StreamDistribution.Application.Tests/Commands/AuthorizeWhepCommandHandlerTests.cs`
— seven tests. Their expected outcomes are the spec's acceptance scenarios:

| Test | Subject scopes | Action | Expects |
|---|---|---|---|
| `Authorize_a_publish_with_the_read_scope_is_refused` | kiosk persona | `Some(Publish)` | `ActionNotPermitted` — **RED, evidence** |
| `Authorize_a_publish_with_the_grandfathered_bundle_is_refused` | `sse.management` | `Some(Publish)` | `ActionNotPermitted` — **RED, evidence** |
| `Authorize_a_publish_with_no_token_is_refused_on_the_action_not_the_token` | n/a, token `""` | `Some(Publish)` | `ActionNotPermitted`, **not** `Unauthorized` — **RED, evidence**; this is D4's ordering and D3's "never 401" |
| `Authorize_with_no_action_is_refused` | kiosk persona | `Option<MediaMtxAction>.None` | `ActionUnknown` — **RED, evidence** |
| `Authorize_with_an_unrecognised_action_is_refused` | kiosk persona | `MediaMtxAction.TryFrom("api")`, i.e. `None` | `ActionUnknown` — **RED, evidence** |
| `Authorize_a_playback_with_the_read_scope_returns_success` | kiosk persona | `Some(Playback)` | success — red only until T002's type exists |
| `Authorize_a_read_with_the_read_scope_returns_success` | kiosk persona | `Some(Read)` | success — **GREEN on arrival, the over-correction guard** |

Reuse the file's existing `AKioskPersona` array and `SomeCamera()` helper rather
than adding new ones.

**Run them and capture the output verbatim.** The five evidence reds are what
goes in the PR body under ADR-0139. Quote the failure text, not a summary of it.

**Done when**: the five evidence tests are observed failing with their assertion
text captured, `Authorize_a_read_...` passes, and no pre-existing test's outcome
has moved.

---

### T004 [P] [US1] — the handler answers the action — `backend-engineer`

**Brief**: T003's verbatim failure output. **You may not edit those tests to
make them pass.**

**Edit** `AuthorizeWhepCommandHandler.cs` (FR-006, FR-007, FR-010):

- Deconstruct all three members into locals as the first statement after
  `Ensure.That(command).IsNotNull()` — the handler now reads three fields, and
  the house rule already applied at two. Discard nothing; all three are used.
- **First decision, before the token is inspected**: `None` → log + `ActionUnknown`;
  `Publish` → log + `ActionNotPermitted`; `Read` or `Playback` → fall through.
- Everything below stays byte-for-byte as it is: empty token → `Unauthorized`,
  invalid token → `Unauthorized`, missing scope → `Forbidden`, offline stream →
  `StreamUnavailable`, otherwise success.
- **FR-010's comment on the `Publish` branch.** Say what the issue asked to have
  said at the site: nothing in this product publishes through this hook, and
  until now the only thing stopping a publish was MediaMTX refusing publishers
  on a path with a static `source` (`MediaMtxRtspGateway.cs:32`) — another
  component's configuration file, not this code. Keep it to why, not what
  (CLAUDE.md: no drive-by comments, but this *is* the non-obvious why).

**Edit** `src/StreamDistribution/Application/Log.cs` (FR-011): two
`[LoggerMessage(Level = LogLevel.Warning, ...)]` entries in the file's existing
style — no explicit `EventId`, structured parameters, value objects passed
whole.

- Refused publish: names the path and the action. A refused publish on a CCTV
  wall is security-relevant and must not be silent.
- Unknown action: names the value received (or that it was absent) **and points
  at MediaMTX version skew as the thing to check**. This message is the entire
  mitigation for the fail-closed decision (D2) — without it, the day MediaMTX
  stops sending the field is an outage with no explanation. Do not shorten it
  to "invalid action".

**Do not touch** `IFabAuthorizationGuard`, `FabResolution`, `FabIdentifier` or
`sse.streams.write`. Those are #2092 and D1, and a diff mentioning any of them
has drifted (FR-012).

**Done when**: all seven T003 tests pass, all six pre-existing handler tests
still pass unmodified, and `dotnet test tests/StreamDistribution.Domain.Tests
tests/StreamDistribution.Application.Tests` is green.

---

### T005 [P] [US1] — the endpoint says what it answers, and the fixture posts what MediaMTX posts — `backend-engineer`

Disjoint from T004: different files, either order.

**Edit** `src/StreamDistribution/Api/StreamEndpoints.cs` (FR-009): one sentence
appended to `AuthorizeWhep`'s `WithSummary` — the hook answers on MediaMTX's
`action`; `read` and `playback` are admitted with `sse.streams.read`; `publish`
is refused outright because nothing in this product publishes through the hook;
an absent or unrecognised action is refused too. **No new `Produces` /
`ProducesProblem` call** — `403` is already declared (FR-009).

**Keep it to one sentence.** The file is 402 lines against ADR-0084's 300-LOC
ceiling. **Do not split the file** — that is a separate, behaviour-preserving
refactor and mixing it in is forbidden (plan.md Risk 5). If the Release build
fails on S104 because of these lines, stop and report.

**Edit** `tests/Integration.Tests/StreamDistribution/WhepAuthIntegrationTests.cs`
(FR-014):

- The five existing request bodies gain `action = "read"`. These fixtures stand
  in for MediaMTX and today post a body MediaMTX never sends; adding the field
  makes the fake faithful. **Every asserted status stays identical** — 401, 403,
  200, 401, 200 in file order. If any of them moves, the plan is wrong: block
  and report.
- **One new test**: `Authorize_a_publish_with_a_valid_admin_token_returns_403`
  — mint through `aspire.GetAccessTokenAsync`, post
  `{ token, path = $"cam-{Guid.CreateVersion7()}", action = "publish" }`,
  assert `403` through the existing `AssertStatusAsync` helper.

**This test needs the Aspire fixture and Docker, which is unresponsive on this
machine.** Mark it for CI in the task report the way #91's and #2070's
stack-dependent checks were marked — it is written here and observed green in
CI, not locally.

**Done when**: the file compiles, the five statuses are unchanged in the source,
and the new test is written and marked for CI.

---

### T006 [US1] — the suite, and the diff — `backend-engineer`

1. `dotnet test tests/StreamDistribution.Domain.Tests` — green.
2. `dotnet test tests/StreamDistribution.Application.Tests` — green.
3. `dotnet test tests/Architecture.Tests` — green. `BoundaryTests`,
   `PrimitiveBoundaryTests` and `HandlerDeconstructionTests` all have an opinion
   about this diff: a new Domain type, a handler whose deconstruction gained a
   local, and no new cross-context reference.
4. `dotnet build -c Release` on the touched projects — SonarAnalyzer metrics and
   the collection-expression analyzer fail the Release build, not Debug. If S104
   fires on `StreamEndpoints.cs`, stop and report rather than restructuring
   (plan.md Risk 5). Note the standing caveat that a Release analyzer error can
   be flaky — re-run the same SHA once before concluding.
5. `dotnet format --verify-no-changes` on the touched projects.
6. **Read the diff of the two edited test files** and confirm only inputs moved:
   no `ShouldBe`, no `ShouldBeOfType`, no expected status, no test name. This is
   the check that keeps FR-014 honest, and it is a task rather than a hope.
7. Confirm the tripwires are absent: `git diff | grep -E
   "IFabAuthorizationGuard|FabResolution|FabIdentifier|streams\.write"` returns
   nothing (FR-012, Risk 4).

**Integration tests are not run here** — Docker is unresponsive; they run in CI.
Say so in the report rather than implying a full local pass.

---

### T007 [US1] — phase 5 verification — `verify`

**Steps 1-2 of the spec's procedure are Docker-free and are the whole of the
automated evidence.** Steps 3-6 need a working Docker and a booted stack.

The two things the note must contain that a test cannot supply:

- **The observed hook body.** With a wall playing, read `stream-distribution`'s
  structured logs in the Aspire dashboard for a `/streams/authorize` request and
  **record the `action` value MediaMTX actually sent**. This converts D2's
  documented assumption into an observed fact for the image running here. If it
  is anything but `read`, the plan's central assumption is wrong — stop
  (plan.md Risk 2).
- **The hand-posted negative.** `action: "publish"` with a real viewer token →
  `403` `WHEP_ACTION_NOT_PERMITTED`; `action: "read"` → `200`; the `action` key
  omitted → `403` `WHEP_ACTION_UNKNOWN` with the version-skew warning visible in
  the dashboard. Record all three statuses and bodies.

Mint through the **Aspire proxied endpoint**, not the container's mapped port,
or the issuer will not match and everything `401`s for the wrong reason. If the
stack has a stale Keycloak data volume, `docker volume rm` it first — spec 071
recorded the same trap.

Finally, confirm the wall from the observation step is **still playing** after
the negatives: the refusals are per-request and must not disturb an open
session.

**Latency**: N/A to all six legs of constitution §IV (spec, *Latency budget
impact*). State that explicitly in the note rather than omitting it — this
endpoint is on the WHEP open, not on the `event → overlay` path.

---

## Dependencies

```
T001 (gate — STOP, hand back for review)
  └── T002  (foundational: the type + the signatures; blocks everything)
        └── T003  (the red; needs T002 to compile)
              ├── T004 [P]  (handler + Log.cs)
              └── T005 [P]  (endpoint summary + integration fixture)
                    └── T006  (suite + diff audit)
                          └── T007  (phase 5)
```

**T002 is the only foundational task.** After it, T004 and T005 are the sole
parallel pair — disjoint files, either order.

**On failure**: retry once, then park — comment with the verbatim failure, add
`agent:blocked`, move the card back to Todo, and continue to the next issue
(ADR-0144). A `publish` observed in T007's hook body, or a moved status in
T006's diff audit, is a **block**, not a retry.
