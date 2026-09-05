# Implementation Plan: The hook answers the action it was asked

**Spec**: `specs/074-the-hook-answers-the-action-it-was-asked/spec.md` · **Issue**: #2094
**Branch**: `fix/2094-the-hook-answers-the-action-it-was-asked` · **Base**: `origin/develop` @ `f2d97bd5`
**Lane**: the issue carries `agent:ready`, so the autonomous lane is eligible
(ADR-0144) — Declaration 2 is what decides that.

---

## The three declarations (ADR-0144)

### Declaration 1 — which engineer

**`backend-engineer`, alone, preceded by `test-writer` for phase 4a.**

Every file is C# inside one bounded context: one new value object in
`StreamDistribution.Domain`, three edits in `StreamDistribution.Application`,
two in `StreamDistribution.Api`, and tests in two existing test projects plus
one new file.

**Not `infra-engineer`.** FR-013 forbids the realm file, `mediamtx.yml`,
`AppHost.cs`, CI and containers. The MediaMTX facts this spec rests on were
*read*; nothing about MediaMTX's configuration is *changed*. A diff touching
`src/AppHost/` means the plan was misread.

**No frontend work.** `apps/` is untouched. The browser never publishes — spec
*Does anything publish through the hook?*, evidence 1 — so nothing it sends
changes.

### Declaration 2 — is the honest answer a new ADR?

**No.**

The architectural decision this implements is already made and recorded:
constitution §VIII ("authorization is enforced by scope checks at every
endpoint"), ADR-0011 (MediaMTX as the SFU with an external auth hook), and spec
002 FR-007 (this hook exists to decide whether an operation is permitted).
Reading the field that names the operation is applying that decision to the
input it was always supposed to read — not making a new one.

The supporting choices are each covered by an existing ADR rather than being
decisions of their own:

- *A value object for a closed set of protocol strings* — ADR-0038, ADR-0046,
  ADR-0092, and `TranscodeMode` in the same folder as the precedent.
- *`Option<MediaMtxAction>` for "absent or unrecognised"* — ADR-0141, and
  `MediaMtxPath.IsCanonical` as the existing non-throwing probe.
- *Two new `403` error variants* — ADR-0047, ADR-0089, and the three variants
  already in `AuthorizeWhepErrors.cs`.
- *A warning through `[LoggerMessage]` on the context's `Log` class* — ADR-0050,
  and the four sibling command handlers that already take an `ILogger<T>`.
- *A test rather than a comment* — ADR-0139 verbatim.

**What would need an ADR, and would therefore block the lane** (stop and hand
back to a human; ADR-0144 bars this lane from writing one):

1. **Introducing a scope that grants publishing**, or enforcing
   `sse.streams.write` on this hook. That decides who may inject video into a
   CCTV wall, which is an architectural decision about the product's trust
   model, not an implementation detail. The spec refuses publish outright
   precisely so this plan does not have to make it.
2. **#2092's fab check on this handler.** Held for a human, needs its own ADR.
3. **Moving the WHEP token out of the JSON body**, or restructuring the hook.

If a reviewer asks for any of those, **block**.

### Declaration 3 — behaviour-changing or behaviour-preserving

**Behaviour-changing → phase 4a colour is RED.**

A hook call that received `200` yesterday receives `403` today whenever its
action is `publish`, absent, or unrecognised. That is the feature.

Precisely which assertions must be **observed failing**, and which must not be
mistaken for the evidence:

| Assertion | Colour on arrival | Why |
|---|---|---|
| `publish` + read scope → `ActionNotPermitted` | **RED** — the evidence | The gap itself. Today this returns success. |
| `publish` + `sse.management` → `ActionNotPermitted` | **RED** — the evidence | The grandfathered bundle must not be a way round it. |
| `publish` + no token → `ActionNotPermitted`, not `Unauthorized` | **RED** — the evidence | D4's ordering, and D3's "never 401". |
| absent action → `ActionUnknown` | **RED** — the evidence | The fail-closed decision (D2). |
| unrecognised action (`"api"`) → `ActionUnknown` | **RED** — the evidence | Same. |
| `read` → success | **GREEN on arrival** | The over-correction guard. |
| `playback` → success | **RED**, for a boring reason | It cannot be expressed until `MediaMtxAction` exists; once it does it passes immediately. Named here so its green is not read as evidence. |
| `MediaMtxAction` parse table | **RED**, compile-driven | New type. |

**The compile problem, and why it decides the commit shape.** None of the red
assertions can be *written* today: `MediaMtxAction` does not exist and
`AuthorizeWhepCommand` has two members. A test file that does not compile is not
a red test, it is a broken build, and commit 1 must build on its own
(CLAUDE.md, rebase-merge). Resolved exactly as spec 071 resolved the same
problem: **commit 1 introduces the vocabulary without the decision** — the value
object, the third command member, the wire field, the endpoint's parse — while
the handler still ignores the action. The tests then compile and fail on the
assertion, which is a real red. See `tasks.md` T001/T002.

**No characterisation control is declared**, because the existing suite's
assertions are all expected to hold unmodified (FR-014). Two categories of
existing test have their *input* corrected; if any of their *statuses* moves,
that is a design error in this plan — block and report, do not adjust the
assertion.

---

## Architecture

### Bounded context and layers

**StreamDistribution only.** All four layers, no other context.

| Layer | What changes |
|---|---|
| `Domain/Stream/` | New `MediaMtxAction` value object. Nothing else — no aggregate, no invariant, no event. |
| `Application/Commands/` | `AuthorizeWhepCommand` gains a member; `AuthorizeWhepErrors` gains two variants and two factories. |
| `Application/Commands/Handlers/` | `AuthorizeWhepCommandHandler` decides on the action first, and gains an `ILogger<T>`. |
| `Application/Log.cs` | Two `[LoggerMessage]` entries. |
| `Api/` | `MediaMtxAuthorizeRequest` gains `Action`; `AuthorizeWhep` parses it; `WithSummary` says so. |
| `Infrastructure/` | **Untouched.** The token validator, the gateway, the reconciler and the health watcher are all unaffected. |

### Entities, value objects, invariants

**No entity changes. No aggregate changes.** `Stream` is not touched, and the
action never enters a domain model — it is a property of a *request*, not of the
stream.

**New value object — `MediaMtxAction`** (`src/StreamDistribution/Domain/Stream/`):

- `sealed record MediaMtxAction(string Value) : IValueObject<string>`, mirroring
  `TranscodeMode` in the same folder line for line.
- Statics `Read` (`"read"`), `Publish` (`"publish"`), `Playback` (`"playback"`).
- `From(string)` — throws `ArgumentException` for anything else, like
  `TranscodeMode.From`.
- `TryFrom(string?)` → `Option<MediaMtxAction>`; `None` for null, empty,
  whitespace and every unrecognised value.
- `ToString()` returns `Value`, `sealed override`, as `TranscodeMode` does.

**Invariants it carries:**

1. The set is closed to the three actions MediaMTX routes to this hook. `api`,
   `metrics` and `pprof` are deliberately **absent**: `mediamtx.yml:46-49`
   excludes them, so they cannot arrive — and if someone deletes an exclusion,
   they arrive as `None` and are refused, which is the right answer rather than
   an accident.
2. Matching is **ordinal and case-sensitive**. MediaMTX sends three lowercase
   literals; accepting `"PUBLISH"` would accept a spelling MediaMTX never sends
   and widen the set for no caller.
3. The type is inert about policy. Which actions are *permitted* is the
   handler's business, not the value object's — `MediaMtxAction.Publish` is a
   perfectly valid value that the handler refuses.

**Why this is not a primitive on the command.** §II bans primitive-typed state
on a domain model, and ADR-0038 is maximalist beyond that. `BearerToken` is a
raw `string` on this command and stays one — it is opaque credential material
with no closed set and no invariant, and changing it is not this issue.

### Messaging

**None.** No domain event, no integration event, no Wolverine message, no
outbox row, no `Shared.Contracts` change. A refused authorization is a synchronous
HTTP answer to MediaMTX and nothing else observes it. Recorded explicitly
because "which integration event does this raise" is a question the plan
template asks and the answer here is *deliberately none*.

### Boundary rules

- **No cross-context project reference** is added or needed. Everything is
  inside `StreamDistribution` and `Shared.Kernel` (`Option<T>`,
  `IValueObject<T>`, `Ensure`, `Result`, `ApiError`). `BoundaryTests` /
  `NetArchTest` are unaffected.
- **Application stays ASP.NET-free** (ADR-0051). The two new error variants
  carry `HttpStatusCode`, which the existing three already do through
  `ApiError` — `System.Net`, not `Microsoft.AspNetCore`.
- **Domain stays framework-free.** `MediaMtxAction` references
  `Shared.Kernel.Primitives.IValueObject<T>` and nothing else, exactly as
  `TranscodeMode` does.
- **The Api layer translates, it does not decide** (D5). `MediaMtxAuthorizeRequest`
  keeps nullable primitives because it is a wire DTO — the same exemption
  `Token` and `Path` already use, and the one ADR-0141 gives Api and
  Infrastructure.

---

## The change, exactly

**1. `src/StreamDistribution/Domain/Stream/MediaMtxAction.cs`** — new, ~35 lines
including the XML doc. Shape mirrors `TranscodeMode.cs`.

**2. `src/StreamDistribution/Application/Commands/AuthorizeWhepCommand.cs`** —
third positional member `Option<MediaMtxAction> Action`, **no default value**
(D6: `Option<T>` is a `readonly struct` whose `None` is `default`, so a default
would silently mean "unknown" at any call site that forgot it — the compile
error is the point). The XML doc's stale sentence "checks the `sse.management`
scope" is left alone: it was already stale before this issue and correcting it
is a drive-by.

**3. `src/StreamDistribution/Application/Commands/AuthorizeWhepErrors.cs`** —
two variants and two factories:

| Variant | Code | Status |
|---|---|---|
| `ActionNotPermitted` | `WHEP_ACTION_NOT_PERMITTED` | `403` |
| `ActionUnknown` | `WHEP_ACTION_UNKNOWN` | `403` |

Both `403`, never `401` (D3): upstream documents that a `401` is how the auth
server *asks the client for credentials*, and an action refusal is not about
credentials.

**4. `src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs`**
— the shape:

- The primary constructor gains `ILogger<AuthorizeWhepCommandHandler> logger`,
  as `ProvisionStream`, `RepointStream`, `ReportStreamHealth` and
  `RetireStream` handlers already have.
- The deconstruction gains the third local (FR-007 — the handler now reads
  three fields, and the house rule already applies at two).
- **First decision after the guard**, before the token is touched:

  ```
  None      -> log warning, ActionUnknown
  Publish   -> log warning, ActionNotPermitted
  Read      -> fall through
  Playback  -> fall through
  ```

- Everything below is unchanged: empty token → `Unauthorized`, invalid token →
  `Unauthorized`, missing scope → `Forbidden`, offline stream →
  `StreamUnavailable`, otherwise success.
- **FR-010's comment goes on the `Publish` branch** and says the thing the
  issue asked to have said at the site: nothing in this product publishes
  through this hook, and until now the only thing stopping a publish was
  MediaMTX refusing publishers on a path with a static `source` — another
  component's configuration file, not this code.

**5. `src/StreamDistribution/Application/Log.cs`** — two `[LoggerMessage]`
warnings appended, following the file's existing style (no explicit `EventId`,
structured parameters, value objects passed whole):

- refused publish: names the path and that the action was `publish`;
- unknown action: names the value received (or that it was absent) and points at
  MediaMTX version skew as the thing to check.

The second is the entire mitigation for D2's fail-closed cost. **Without it the
fail-closed decision is not defensible** — an outage with no message is exactly
the failure D2 traded for.

**6. `src/StreamDistribution/Api/StreamEndpoints.cs`** — two edits, both small,
because the file is already 402 lines against ADR-0084's 300-LOC ceiling (see
*Risks*):

- `MediaMtxAuthorizeRequest(string? Token, string? Path, string? Action)`, with
  the XML doc's "other fields are accepted but ignored" corrected to stop
  listing `action` among them.
- In `AuthorizeWhep`, one line beside the existing path parse:
  `Option<MediaMtxAction> action = MediaMtxAction.TryFrom(body.Action);` passed
  into the command. **No branch is added to the endpoint** — the endpoint does
  not decide (D5, FR-008).
- One sentence appended to `WithSummary` (FR-009).

### Why the tests need no Docker

Everything red is asserted at the handler, and the handler's two dependencies
are already fakeable: `FakeWhepAuthValidator`
(`tests/StreamDistribution.Application.Tests/Fakes/FakeWhepAuthValidator.cs`)
returns a scripted `Option<WhepAuthSubject>` with no signing key, no issuer and
no network, and `InMemoryStreamRepository` holds the stream state. The third
dependency, the logger, is `NullLogger<T>.Instance` — the pattern
`ProvisionStreamCommandHandlerTests.cs:116` already uses.

`MediaMtxAction`'s parse table is a pure function over strings, asserted in
`StreamDistribution.Domain.Tests` beside `MediaMtxPathTests`.

**Nothing in the red needs the Aspire fixture**, so nothing is marked for CI
the way #91's and #2070's stack-dependent checks were. The integration tests
that *do* use the fixture are only touched to keep them faithful (FR-014) and
assert nothing new — their run is deferred to CI, where Docker works.

---

## Files touched

### Source (6 — 1 new, 5 edited)

| File | Change |
|---|---|
| `src/StreamDistribution/Domain/Stream/MediaMtxAction.cs` | **new** |
| `src/StreamDistribution/Application/Commands/AuthorizeWhepCommand.cs` | +1 member |
| `src/StreamDistribution/Application/Commands/AuthorizeWhepErrors.cs` | +2 variants, +2 factories |
| `src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs` | +logger, +action decision |
| `src/StreamDistribution/Application/Log.cs` | +2 `[LoggerMessage]` |
| `src/StreamDistribution/Api/StreamEndpoints.cs` | +1 DTO member, +1 parse line, +1 summary sentence |

### Tests (1 new file, 2 edited)

| File | Change |
|---|---|
| `tests/StreamDistribution.Domain.Tests/Stream/MediaMtxActionTests.cs` | **new** — the parse table |
| `tests/StreamDistribution.Application.Tests/Commands/AuthorizeWhepCommandHandlerTests.cs` | +7 tests; 6 existing constructions gain the action and the logger |
| `tests/Integration.Tests/StreamDistribution/WhepAuthIntegrationTests.cs` | 5 bodies gain `action = "read"`; +1 new test for the publish refusal |

**No file outside `src/StreamDistribution` and `tests/` is touched.** FR-013.

---

## Risks

### Risk 1 — the existing-test edits could hide a real regression

Eleven existing test bodies are edited (6 handler constructions, 5 integration
request bodies). That is the largest single hazard in this plan, because "I
edited the tests and now they pass" is the exact anti-pattern §Testing and
ADR-0139 exist to stop.

**Mitigation, and it is a hard rule for the engineer**: only the *input* moves.
No `ShouldBe`, no `ShouldBeOfType`, no expected status code, no test name may
change. `git diff` on those two files must show additions of `action`/logger
arguments and nothing else. If a status has to move to get green, **the plan is
wrong — block and report.**

### Risk 2 — a `publish` really does happen somewhere and nobody found it

The spec gives four independent lines of evidence that nothing publishes through
this hook, the strongest being that the hook already `401`s on an empty token,
so any internal use would already be broken. **But all four are static.**

**Mitigation**: phase 5 step 4 reads the real hook body from the Aspire
dashboard while a wall is playing and records the `action` value observed. If it
is anything other than `read`, stop — the plan's central assumption is wrong.
This is the step that converts a documented assumption into an observed fact,
and it is why the verification note must quote the body, not merely say video
played.

### Risk 3 — MediaMTX version skew under a floating tag

All three MediaMTX containers run `bluenviron/mediamtx:latest-ffmpeg`
(`AppHost.cs:134`, `:166`, `:552`). The payload was verified against upstream
`main` today, not against a pin. If a future image stops sending `action`, this
change turns every WHEP open into a `403`.

**Mitigation**: that is D2's deliberate choice, and its cost is bounded by
FR-011's warning naming version skew by name. **Pinning the image is the real
fix and is explicitly out of scope** — it is an `AppHost.cs` change with its own
blast radius across three containers and belongs in its own issue.

### Risk 4 — scope creep into #2092

#2092 is the missing *fab* check on this same handler, in the same method, three
lines below where this change lands. It needs an ADR and is held for a human.

**Mitigation**: FR-012 and the spec's *Out of scope* name the tripwires — a diff
touching `IFabAuthorizationGuard`, `FabResolution`, `FabIdentifier` or
`sse.streams.write` has drifted. The reviewer should grep for those four names.

### Risk 5 — coverage and code metrics

`StreamDistribution.Domain` is under the ≥ 90% gate and `Application` under
≥ 80% (ADR-0065). `MediaMtxAction` is small and fully covered by its parse
table; the handler's new branches are covered by the new handler tests; `Log.cs`
carries `[ExcludeFromCodeCoverage]` already. Coverage should rise.

`StreamEndpoints.cs` is **402 lines against ADR-0084's 300-LOC ceiling** and is
therefore already in whatever state the build tolerates. This change adds
roughly three lines to it. **The engineer must not "fix" the file size** — a
drive-by split of a 402-line endpoint file is a separate refactor, would be
behaviour-preserving work mixed into a behaviour-changing commit, and CLAUDE.md
forbids the mix. If the Release build fails on S104 *because of these three
lines*, stop and report; do not restructure.

### Risk 6 — Docker is unresponsive on this machine

Phase 5 steps 3-6 and the integration-test run cannot happen here. Steps 1-2 are
Docker-free and carry the whole of the phase-4a evidence. The integration test
added in T005 runs in CI. The verification note must say which steps were run
where rather than implying a full local pass.

---

## What is deliberately not done

- **#2092's fab check.** Separate issue, needs an ADR, held for a human.
- **Pinning the MediaMTX image.** Real problem, wrong blast radius, own issue.
- **Any `mediamtx.yml` change** (D8) — including a comment. The config is
  correct; this spec moves the safety out of it, not into it.
- **Reading MediaMTX's other seven payload fields** (D7).
- **A publish scope, or enforcing `sse.streams.write`** (D1) — and asking for
  either blocks the lane (Declaration 2).
- **Splitting `StreamEndpoints.cs`** (Risk 5).
- **Correcting `AuthorizeWhepCommand`'s stale XML doc** about `sse.management`,
  or `Option<T>`'s stale "NRT is disabled" doc. Both were stale before this
  issue; both are drive-bys.
