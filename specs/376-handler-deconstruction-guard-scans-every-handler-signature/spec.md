# Spec 376: The handler-deconstruction guard scans every handler signature

**Feature Branch**: `test/2984-handler-signature-coverage` (cut from `origin/develop` at `6acde2b9`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Two scope judgements are
marked in §5 (`public static` is included; non-`public` and `void` are not).

**Input**: Issue [#2984](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2984),
*HandlerDeconstructionTests only scans 'public async Task Handle' handlers; non-async, ValueTask and
single-parameter handlers are unguarded*. Found by the review of #2914 (spec 374). Owner decision in
the issue (2026-10-15): **enforce** — widen `HandlerSignature`, no ADR, do not edit CLAUDE.md.

**Lane.** Autonomous (ADR-0144). **Production code unchanged; test-only.** The guard's behaviour
changes (it reaches handlers it used to skip), so phase 4a is **red**: constructed transpositions in
non-async, `ValueTask`, single-parameter and `static` handlers are shown passing unnoticed through
today's guard, then reported once it is widened. The real tree is green before and after.

**Spec number.** 376. Checked 2026-10-15 against `origin/develop` (highest: 374), every local and
remote branch head (`git ls-tree … specs/`) and open PRs: 375 is claimed by the unmerged
`fix/2959-per-authority-circuit-breaker`; nothing claims 376. Re-check before the PR is opened.

**ADRs.** ADR-0144 (autonomous lane; red colour), ADR-0139 (a rule fails the build, not the review —
a guard that silently skips a class of handler is a review-only rule for that class), ADR-0036
(smallest change), ADR-0040 / ADR-0073 (integration events in `Shared.Contracts` carry primitives —
why a same-typed transposition compiles). The rule is CLAUDE.md's house rule *"Handlers destructure
their input first"*, which names `HandlerDeconstructionTests` as its guard. **No new ADR**: this
closes a hole in an enforced rule; it decides nothing.

**Latency budget.** N/A. A source-scanning architecture test; nothing on any §IV leg.

---

## 1. What was measured (premise verified, not assumed)

On `6acde2b9` (= `origin/develop`, 2026-10-15, spec 374 merged).

**The guard today.** `tests/Architecture.Tests/HandlerDeconstructionScanner.cs`, `HandlerSignature`:

```
public\s+async\s+Task[^(\n]*?\s(?:Handle|HandleAsync)\s*\(\s*([A-Za-z0-9_]+)\s+(\w+)\s*,
```

It requires `async`, rejects `ValueTask` (`\s+Task` needs whitespace before `Task`), rejects any
modifier between `public` and `async`, and requires a comma after the first parameter.

```
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~HandlerDeconstructionTests"
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11
```

**Every `Handle`/`HandleAsync` declaration in `src/`** (throwaway inventory script, outside the repo;
grouped by modifiers + return type, parameter count, body):

| Spelling | Params | Body | Count | Scanned today |
|---|---|---|---|---|
| `public async Task<…> HandleAsync` | 2 | block | 77 | yes |
| `public async Task Handle` | 2 | block | 27 | yes |
| `public async Task HandleAsync` | 4 | block | 1 | yes (`AuditingMessageHandler`) |
| `public Task Handle` | 2 | **expression** (`=>`) | 28 | no |
| `public Task Handle` | 2 | block | 3 | **no** |
| `public Task<…> HandleAsync` | 2 | block | 1 | **no** |
| `Task<…> HandleAsync` / `Task Handle` (interface members) | 2 | `;` | 3 | no |

Grep for the other spellings the issue and the brief name — `ValueTask … Handle`, `static … Handle`,
`internal`/`private`/`protected … Handle`, `void Handle`, `Consume`/`ConsumeAsync`, explicit
interface implementations, single-parameter handlers: **0 occurrences in `src/`**. All handlers are
public instance methods; explicit interface implementations do not occur.

**The widened rule against the real tree.** The scanner's logic replicated in a throwaway script
(validated: it reproduces today's figures exactly — 85 checked, 40 parenthesised, 0 failures, 105
signatures matched), run with the widened signature of plan §3:

| | Signatures matched | Deconstructions checked | Parenthesised | Failures |
|---|---|---|---|---|
| Today | 105 | 85 | 40 | 0 |
| Widened, block-body rule applied | 137 | **86** | 40 | **0** |
| of which newly matched | 32 | 1 | 0 | — |

The 32 newly matched signatures:

- **4 block-bodied, now scanned:**
  - `src/SystemVariables/Application/EventHandlers/OverlayRevisionPublishedV3Handler.cs:25` — the
    one newly **checked** deconstruction: `var (overlay, revisionNumber, _, elements, _, _, _) =
    message;` against `OverlayRevisionPublishedV3(Guid Overlay, int RevisionNumber, string Name,
    IReadOnlyList<OverlayElementV3> Elements, DateTimeOffset PublishedAt, Guid PublishedBy,
    EventMetadata Metadata)`. Resolves **uniquely** to
    `src/Shared.Contracts/OverlayDesigner/OverlayRevisionPublishedV3.cs` (SystemVariables declares no
    same-named record). Binds correctly: **passes**.
  - `src/SystemVariables/Application/EventHandlers/OverlayRevisionArchivedV1Handler.cs:18`,
    `src/StreamDistribution/Application/EventHandlers/StreamProvisionedDomainEventHandler.cs:17`,
    `src/StreamDistribution/Application/Commands/Handlers/AuthorizeMediaMtxApiCommandHandler.cs:12` —
    reached, no deconstruction of their parameter (0 checked).
- **28 expression-bodied, matched and skipped** (no statement body to deconstruct in): 24 in
  `src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:36-59`, and the
  four `CameraCatalog/Application/EventHandlers/Camera{AddressChanged,Registered,Renamed,Retired}DomainEventHandler.cs`.

**Record resolution for the newly reached handlers** (spec 374's own-context-first / unique-elsewhere
/ ambiguous rule): 30 distinct parameter types; the 5 domain events and `AuthorizeMediaMtxApiCommand`
resolve in their own context, the 24 `Shared.Contracts` messages each resolve to a **single**
declaration elsewhere. **No ambiguity**: no `Shared.Contracts` message used by a newly reached
handler shares its simple name with a record in any context. (The handler class name
`OverlayRevisionPublishedV3Handler` exists in both SystemVariables and LayoutComposition; the
*record* is declared once.)

**No real transposition exists.** 0 failures with the widened rule. This is not a bug fix and nothing
is split off.

**The widening exposes a latent hazard: borrowed bodies.** `BodyAfter` takes the first `{` after the
signature. For an expression-bodied handler (`=> …;`) or a bodiless declaration (`…;`) that `{`
belongs to the **next member**, and its deconstructions would be checked against this handler's
parameter. Today no `async` handler is expression-bodied, so the hazard is unreachable; the moment
`async` becomes optional it is reachable 28 times in the real tree. It produces no false failure
today only because no member following those 28 deconstructs a variable named `message` /
`domainEvent`. Constructed counterfactual (throwaway script):

| Source | Widened, naive | Widened + block-body rule |
|---|---|---|
| `public Task Handle(SampleV1 message, …) => Task.CompletedTask;` then `public void Other(SampleV1 message) { var (device, kind) = message; }` | checked 1, **2 false failures** | checked 0, 0 failures |

So the widening must come with the rule *"a signature whose parameter list is not followed by `{`
has no body and is skipped"*.

**Counterfactual for the widening itself** (fixture record `SampleV1(string Kind, string Device)`,
body `var (device, kind) = message; return Task.CompletedTask;`, throwaway script):

| Handler signature | Today | Widened |
|---|---|---|
| `public async Task Handle(SampleV1 message, CancellationToken cancellationToken)` (control) | checked 1, 2 failures | checked 1, 2 failures |
| `public Task Handle(SampleV1 message, CancellationToken cancellationToken)` | **checked 0** | checked 1, 2 failures |
| `public Task<int> HandleAsync(SampleV1 message, CancellationToken cancellationToken)` | **checked 0** | checked 1, 2 failures |
| `public async ValueTask Handle(SampleV1 message, CancellationToken cancellationToken)` | **checked 0** | checked 1, 2 failures |
| `public async Task Handle(SampleV1 message)` | **checked 0** | checked 1, 2 failures |
| `public static Task Handle(SampleV1 message, CancellationToken cancellationToken)` | **checked 0** | checked 1, 2 failures |

With the correct body `var (kind, device) = message;` every row is checked 0 today and checked 1 /
0 failures widened. `private Task Handle(…)`, `public void Handle(…)` and an interface member
`Task Handle(…);` stay unscanned under the widened rule (checked 0).

## 2. User story

### US1 (P1): A transposition fails the build whatever the handler's signature

As a maintainer, I want `HandlerDeconstructionTests` to check a handler's deconstruction of its input
whether the handler is `async` or not, returns `Task`, `Task<T>` or `ValueTask`, takes one parameter
or several, or is `static`, so that a transposed same-typed field cannot reach `develop` merely
because the handler's signature differs from `public async Task Handle(T message, …)` — and I want
handlers with no block body to be skipped rather than checked against the next member's body.

**Why P1 / only story.** The rule is enforced (owner decision); a handler escaping it by signature is
the whole defect.

**Independent test.** `dotnet test tests/Architecture.Tests --filter
"FullyQualifiedName~HandlerDeconstructionTests"` — every fact and theory row passes; reverting the
signature regex alone turns the new-signature rows and the real-tree reach fact red; widening without
the block-body rule turns the no-body rows red.

### Acceptance scenarios

```gherkin
Scenario Outline: a transposition is reported whatever the handler signature
  Given a record SampleV1(string Kind, string Device)
  And a handler "<signature>" whose body is "var (device, kind) = message;"
  When the scan runs
  Then one deconstruction is counted as checked
  And two failures are reported, at positions 0 and 1
  Examples:
    | signature                                                                   |
    | public Task Handle(SampleV1 message, CancellationToken cancellationToken)   |
    | public Task<int> HandleAsync(SampleV1 message, CancellationToken cancellationToken) |
    | public async ValueTask Handle(SampleV1 message, CancellationToken cancellationToken) |
    | public async Task Handle(SampleV1 message)                                  |
    | public static Task Handle(SampleV1 message, CancellationToken cancellationToken) |

Scenario Outline: a correct deconstruction is reached and passes whatever the signature
  Given the same record and signatures
  And the body "var (kind, device) = message;"
  Then one deconstruction is counted as checked
  And no failure is reported

Scenario Outline: a handler without a block body does not borrow the next member's body
  Given the same record
  And "<declaration>" followed by "public void Other(SampleV1 message) { var (device, kind) = message; }"
  Then no deconstruction is counted as checked
  And no failure is reported
  Examples:
    | declaration                                                                                    |
    | public Task Handle(SampleV1 message, CancellationToken cancellationToken) => Task.CompletedTask; |
    | public Task Handle(SampleV1 message, CancellationToken cancellationToken);                       |

Scenario: the async Task spelling is unaffected (control)
  Given "public async Task Handle(SampleV1 message, CancellationToken cancellationToken)"
  And the body "var (device, kind) = message;"
  Then two failures are reported, as today

Scenario: the real tree's non-async handler is reached and passes
  When the scan runs over src/
  Then src/SystemVariables/Application/EventHandlers/OverlayRevisionPublishedV3Handler.cs is among the checked sources
  And no failure is reported
```

(Gherkin's "conflict / bad-request / auth" categories have no meaning for an architecture test; the
negative and no-body cases above are their analogue.)

## 3. Requirements

- **FR-001** `HandlerSignature` matches `public`, optionally `static`, optionally `async`, a return
  type starting with `Task` or `ValueTask` (generic or not), a method named `Handle` or
  `HandleAsync`, whose first parameter `Type name` is followed by `,` **or** `)`.
- **FR-002** A matched signature whose parameter list is not followed (after whitespace) by `{` has
  no statement body and is skipped — never given the body of a later member.
- **FR-003** Everything else is unchanged: deconstruction regex, record resolution (spec 374
  FR-004), `Verify` and its message texts, the `/`-normalised source keys.
- **FR-004** The scan result additionally exposes the source path of every checked deconstruction
  (`CheckedSources`), so a fact can assert that a named real-tree handler was reached. Paths are the
  `/`-normalised keys already used.
- **FR-005** No file under `src/` changes. No existing handler is rewritten. CLAUDE.md is not edited.
- **FR-006** The existing facts keep their assertions verbatim and stay green.

## 4. Success criteria

- **SC-001** The five transposition rows, the five correct-binding rows and the real-tree reach fact
  are observed **red** against today's signature (after the `CheckedSources` seam lands), the failure
  output quoted in the PR (ADR-0139/0144).
- **SC-002** The two no-body rows and every existing fact are observed **green** at the same point —
  the no-body rows prove the fixture shape; the existing `async Task` control proves the fixture
  record/handler are well-formed, so the red is the hole, not the fixture.
- **SC-003** Counterfactual for FR-002: with the widened signature but **without** the block-body
  rule, the two no-body rows are observed red (a temporary local edit, not committed); output quoted
  in the PR.
- **SC-004** After the change all `HandlerDeconstructionTests` cases pass; the real-tree scan checks
  **86** deconstructions (85 + 1), 40 parenthesised, 0 failures — quoted from a temporary diagnostic,
  not pinned in a test. A different figure is reported, not adjusted.
- **SC-005** `git diff --stat origin/develop` touches only `tests/Architecture.Tests/` and this spec
  directory.

## 5. Judgement calls and assumptions

- **`public static` is included (judgement).** Not in the issue body. Zero occurrences in `src/`, but
  it is Wolverine's documented convention-handler shape and sits in exactly the modifier position the
  issue is about (`async` today). It costs one optional group and produces no false positive (a
  `public static Task Handle(T x…)` with a block body is a handler). If the reviewer disagrees, drop
  the group and the `static` theory rows; nothing else depends on it.
- **Not widened (judgement, recorded):** `internal`/`private`/`protected` (0 in `src/`; Wolverine
  discovery and the `ICommandHandler`/`IQueryHandler`/`IDomainEventHandler` implementations all
  require `public`; admitting them would also admit private helpers named `Handle`), `void Handle`
  (0 in `src/`; every handler interface returns `Task`), `Consume` (0), explicit interface
  implementations (0). Private helpers that deconstruct the command (spec 374 §1) remain out of reach,
  as today.
- **The block-body rule rides along (judgement).** Not in the issue text; required because the
  widening makes the borrowed-body hazard reachable (§1). Test-code defect, not a production bug, so
  the "refactor that is also a bug fix is two issues" rule does not apply.
- **`CheckedSources` seam (assumption).** Needed so a real-tree fact can be red before the widening
  without pinning a total count. Added by the test-writer as a characterisation step (existing facts
  green) before any red fact.
- **No new test class.** New facts go in the existing `HandlerDeconstructionTests`;
  `Architecture.Tests` has no shard filter.
- **CLAUDE.md** still reads as if every handler is guarded; after this spec the measured gaps are
  non-`public`, `void`, and helper methods, all with zero handler occurrences. Rewording is left to
  the owner, as the issue requires.
