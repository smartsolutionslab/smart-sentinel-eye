# Implementation Plan: The number no test ever read

**Spec**: [spec.md](./spec.md) · **Spec number**: 220 · **Branch**:
`2515-the-number-no-test-ever-read` · **Issue**: #2515

---

## Bounded context and layers

**None.** This spec adds no production code, so it crosses no context boundary
and touches no layer. That is not a formality — it is the design constraint the
rest of this plan is built around.

| Project | Touched | Why / why not |
|---|---|---|
| `StreamDistribution/Domain` | **no** | Nothing here has a domain meaning. A rate-limit ceiling is transport configuration. |
| `StreamDistribution/Application` | **no** | A `429` is produced by middleware before the handler. Spec 208 already established this and it has not changed. |
| `StreamDistribution/Infrastructure` | **no** | — |
| `StreamDistribution/Api` | **read only** | `appsettings.json` is the artifact under test; `Program.cs` is read as text by US2. **Neither is edited.** |
| `AppHost` | **no** | The `isE2ETests` override at `AppHost.cs:441-442` is correct as written and explicitly out of scope (spec §Scope). |
| `Architecture.Tests` | **yes — the only edit** | One new file. |

**No boundary is crossed, no project reference is added.** `Architecture.Tests`
already references `SmartSentinelEye.StreamDistribution.Api.csproj`, but this
guard does not use it: the files are read from disk, exactly as
`DatabaseCommandLogLevelTests` and `ContainerImagePinTests` do, for the reason
`ContainerImagePinTests:31-36` gives — a suite that runs in seconds without
Docker must stay that way. `NetArchTest`'s `BoundaryTests` are unaffected; if
they are not, something outside this design was touched.

**No new package.** `Microsoft.Extensions.Configuration` and
`…Configuration.Json` are already resolvable in `Architecture.Tests` —
`DatabaseCommandLogLevelTests.cs:2` binds a JSON file through
`ConfigurationBuilder` today with no `PackageReference` of its own (they arrive
transitively through the referenced Api projects' framework reference).
`Directory.Packages.props` is not touched. **Verify this at task T001 before
writing anything else**; if it turns out a package reference is needed, that is
a one-line csproj addition, not a redesign, but it should be a deliberate step
rather than a surprise at compile time.

---

## Entities / value objects / invariants

**None, and deliberately.** Constitution §II bans primitives on *domain models*.
`PermitLimit` is an `int` in an options-shaped configuration section that binds
from JSON — the boundary §II exempts, and spec 208 §*Entities* already made this
exact ruling for this exact value ("wrapping `PermitLimit` in a value object
would put a domain type in an options class that binds from JSON"). Nothing
about adding a test changes that. `PrimitiveBoundaryTests` is not engaged: no
domain model gains a property here.

The only record introduced is a private test-local one describing what was read
from the file — the same shape `DatabaseCommandLogLevelTests` uses
(`private sealed record Settings(...)`, `:182`).

---

## Messaging

**None.** No domain event, no integration event, no Wolverine handler, no
outbox row, no `Shared.Contracts` change. `V1ResourceMap`'s corpus and
`BoundaryTests`' audit-coverage rule are unaffected.

---

## Boundary rules

- No cross-context project reference is added.
- Nothing is added to `Shared.Contracts`.
- The guard reads two files under `src/StreamDistribution/Api/` **from disk**,
  by relative path from `RepositorySource.Root()`. It does not load the Api
  assembly, does not construct a `WebApplicationBuilder`, and does not boot a
  host.

---

## The design

### 1. Home and name

**`tests/Architecture.Tests/WhepAuthorizeCeilingTests.cs`** — one new file, one
new class, no changes to any existing file.

The name is chosen so `--filter "FullyQualifiedName~WhepAuthorizeCeiling"` picks
out exactly this guard for the counterfactual in spec §*Independent end-to-end
test procedure*, and so it sorts next to nothing it could be confused with.

**Why `Architecture.Tests` and not a StreamDistribution unit-test project.**
Three existing precedents, all in this directory, all doing the same kind of
thing:

- `DatabaseCommandLogLevelTests` — binds `appsettings*.json` from disk through
  `ConfigurationBuilder` and asserts on what the bound configuration produces.
  This is the closest precedent and the one to mirror.
- `ContainerImagePinTests` — regexes `AppHost.cs` from disk for literals.
  Precedent for US2's source scan.
- `RepositorySource` — the shared root-walk and separator-normalising relative
  path, extracted by spec 190 precisely so the next guard does not carry a 32nd
  copy.

`tests/StreamDistribution.Application.Tests` and friends test *code*, referenced
as assemblies. A shipped configuration file is not code of any layer; it is a
repository artifact, which is what this directory's file-reading guards are for.
Putting it there also keeps it out of the ADR-0065 coverage gates
(`Architecture.Tests` is not a coverage-gated project), which is correct — this
guard covers a JSON file, and counting it toward a layer's line coverage would
be noise.

### 2. US1 — the shipped ceiling (P1)

Mirror `DatabaseCommandLogLevelTests.Read(...)` (`:125-154`) line for line in
shape:

```
IConfigurationRoot configuration = new ConfigurationBuilder()
    .AddJsonFile(<absolute path to src/StreamDistribution/Api/appsettings.json>, optional: false)
    .Build();

int? permitLimit = configuration.GetValue<int?>("WhepAuthorizeRateLimiting:PermitLimit");
TimeSpan? window = configuration.GetValue<TimeSpan?>("WhepAuthorizeRateLimiting:Window");
```

**`GetValue<T?>` with the same key strings and the same generic arguments as
`Program.cs:21-24`.** This matters more than it looks. The failure being
guarded against is a *wrong value*, but the failure mode of a guard that reads
the file its own way — `JsonDocument`, a regex, a hand-rolled section walk — is
that it can disagree with the host about what the file says: about key casing,
about `TimeSpan` parsing, about a duplicate key, about a numeric string
`"2000"`. Binding through the same mechanism the host binds through removes that
whole class. It is the same argument `DatabaseCommandLogLevelTests:24-33` makes
for asking a logger rather than comparing spellings, and it is why FR-003 is
worded as it is.

Then, in order:

1. **Presence.** `permitLimit.ShouldNotBeNull(...)` and
   `window.ShouldNotBeNull(...)`, each with a message distinguishing *absent*
   from *unparseable* (FR-004, spec AS4/AS7). A `GetValue<int?>` over
   `"two thousand"` throws `InvalidOperationException` from the binder rather
   than returning null, so the read is wrapped and the exception is translated
   into a message that says "present but not readable as a number" — not
   swallowed (CLAUDE.md: no drive-by error handling; this is a trust-boundary
   translation at the only boundary this guard has).
2. **Value.** `permitLimit.ShouldBe(2000, Derivation)` and
   `window.ShouldBe(TimeSpan.FromMinutes(1), Derivation)`.
   **`TimeSpan`, not string** (FR-002): `"00:01:00"`, `"0:01:00"` and
   `"00:01:00.0000000"` are the same window and only one of them is the spelling
   in the file today.

**The expected values are `const`s with the derivation in their doc comment**,
and the assertion message — `Derivation` — carries: the observed value, the
expected value, `≈800 POST/min` as the worst computed minute, `2.5×` as the
margin, and the citation
`specs/208-a-ceiling-the-hook-never-had/spec.md` §*Sizing the ceiling*
(FR-005).

**On naming a value, when two neighbouring guards say not to.**
`ContainerImagePinTests:20-27` and `DatabaseCommandLogLevelTests:36-41` both
carry an explicit "this bans a category, never a value" paragraph, with a good
reason: *a guard that obstructs the legitimate change it governs gets deleted
within a month, taking its protection with it.* This guard does the opposite of
what those two say, and the difference is worth writing into the file rather
than leaving for a reviewer to catch:

> The category **is** the value here. There is no property of "a well-sized
> `PermitLimit`" a test could check without redoing spec 208's derivation —
> which would mean encoding 100 sessions, an 8-attempt ladder and a 2.5× margin
> in a test, so that a change to any of them fails a guard nobody can read. The
> number is the conclusion of an argument that lives in prose, and the only
> honest guard is one that pins the conclusion and **points at the argument**.
>
> The deletion risk the other two guards fear is answered not by banning a
> category but by making the failure message do the teaching: an engineer who
> legitimately wants a different ceiling is told, in the assertion message,
> where the 2000 came from and what else must change (the spec's derivation) for
> a new number to be defensible. That converts "delete the annoying test" into
> "update the spec and the test together", which is the outcome wanted.

This paragraph belongs in the class doc comment, not only in this plan — a
future reader meets the file, not the plan.

### 3. US2 — the code fallback (P2)

`Program.cs:22` and `:24` carry a second copy of the production pair:

```
builder.Configuration.GetValue<int?>("WhepAuthorizeRateLimiting:PermitLimit") ?? 2000;
builder.Configuration.GetValue<TimeSpan?>("WhepAuthorizeRateLimiting:Window") ?? TimeSpan.FromMinutes(1);
```

These are not reflectable — top-level statements compile into a generated entry
point with no accessible members — so the guard reads `Program.cs` as text and
matches the `??` fallback on the line carrying each key. Precedent:
`ContainerImagePinTests` regexes `AppHost.cs` for exactly this reason.

**Match the fallback, not the whole line.** Two regexes, each anchored on the
configuration key string so a reordering or reformatting of the file does not
break them:

- `WhepAuthorizeRateLimiting:PermitLimit"\)\s*\?\?\s*(\d+)` → the captured
  integer must equal the bound `PermitLimit`.
- `WhepAuthorizeRateLimiting:Window"\)\s*\?\?\s*TimeSpan\.From(\w+)\((\d+)\)` →
  resolved to a `TimeSpan` and compared to the bound `Window`.

**Zero matches is a failure, not a pass** (FR-007, spec US2 AS3). This is the
single most common way a source-scanning guard rots: the code is reshaped, the
regex stops matching, and the guard goes green forever. Assert the match count
before asserting the value.

**State the limit in the doc comment.** A literal scan cannot see a fallback
whose value comes from a constant, a different overload, or a refactor into a
helper method. If `Program.cs`'s shape changes such that these regexes stop
matching, the guard fails loudly and whoever changed it repoints it — which is
the correct outcome and is why the count assertion comes first.

**US2 is severable.** If phase 6 judges the source scan not worth its
fragility, deleting the single `[Fact]` and its two regex constants leaves US1
whole and the issue closed. Nothing in US1 depends on US2.

### 4. What is deliberately *not* built

- **No `WhepAuthorizeRateLimitingOptions` class.** Binding into a POCO would be
  speculative generality (CLAUDE.md) and would add a production type to serve a
  test. `Program.cs` reads two scalars; so does the guard.
- **No sweep over every `appsettings*.json`.** `DatabaseCommandLogLevelTests`
  sweeps because its rule is categorical and a fourteenth service should be
  governed the day it is added. This rule is about one endpoint's one ceiling,
  and a sweep would pull in `appsettings.Development.json` and fail on the
  `AppHost` override. **Name the one file** (FR-006). The corresponding risk —
  the file being renamed or moved out from under a hard-coded path — is covered
  by `optional: false`, which throws rather than passing on absence.
- **No rate-exercising integration test.** Spec 208 §*Test-mode ceiling* ruled
  this out with reasons that still hold, and this spec is the compensating
  control it named, not a second attempt at the thing it declined.

---

## Reuse inventory — read before writing

| Need | Existing thing to use | Do **not** |
|---|---|---|
| Repository root | `RepositorySource.Root()` | write a 32nd `RepositoryRoot()` walk (NFR-003; spec 190 exists for this) |
| Relative path for messages | `RepositorySource.RelativePath(root, file)` | use `Path.GetRelativePath` raw — it returns the platform separator, green on Windows and red on Linux CI |
| Binding a settings file | the `ConfigurationBuilder().AddJsonFile(file, optional: false).Build()` shape at `DatabaseCommandLogLevelTests.cs:127-129` | `JsonDocument`, `JsonNode`, or a regex over JSON |
| Source-text scanning of a `.cs` file | the regex-with-timeout shape in `RepositorySource.cs` (`ContainerImagePinTests`'s own regexes carry no timeout) | a regex without a `TimeSpan` timeout (the repo's convention is an explicit one) |
| Asserting a scan matched something | `DatabaseCommandLogLevelTests.cs:76-81`'s "a passing guard that checks nothing is indistinguishable from one that holds" | assert only the value |

---

## Constitution and ADR alignment

| Rule | How this plan complies |
|---|---|
| ADR-0037 | Phases 1–3 here; 4a is characterisation-green with a mandatory counterfactual (spec §Phase-4a colour). |
| ADR-0052 | xUnit + Shouldly. No new framework. |
| ADR-0053 | Sentence-style names with underscores, e.g. `The_shipped_whep_authorize_ceiling_is_the_number_spec_208_derived`. |
| ADR-0103 | No Testcontainers **and no Aspire fixture** — this runs in the fast lane. |
| ADR-0105 | Not engaged (no argument guards added); the existing `Program.cs:33-35` guards are the thing this complements, and are not edited. |
| ADR-0084 | One file, well under 300 LOC; each `[Fact]` well under 30 LOC. Advisory in any case. |
| ADR-0065 | `Architecture.Tests` is not coverage-gated; no gated project's coverage moves. |
| ADR-0141 | `int?`/`TimeSpan?` from `GetValue<T?>` are the framework's own shape at a configuration boundary, not Domain or Application parameters. `Option<T>` does not apply and introducing it here would be wrong. |
| Constitution §II | No domain model gains a primitive; see §Entities. |
| Constitution §IV | N/A — nothing on a latency leg (spec §Latency budget impact). |
| Constitution §Testing | Behaviour-preserving → captured green, must pass unmodified. The evidence burden moves to the counterfactual. |
| CLAUDE.md house rules | Private fields carry no underscore; collections declared with explicit type + collection expression; no drive-by error handling beyond the single binder translation, which is justified above. |

**No new ADR required.** Recorded explicitly because every spec must either cite
an ADR or flag that one is needed, and this one cites eight without needing a
ninth.

---

## Phase-6 note for the dispatcher: is `/security-review` warranted?

**Recommendation: no, and here is the reasoning so the dispatcher can overrule
it rather than guess.**

Against: the change adds zero lines that execute in a hosted process. There is
no new endpoint, no new scope, no new trust boundary, no secret, no input from a
network. A security review's standard checklist — Keycloak/OIDC wiring,
`RequireScope`, fab authorization, idempotency-key scoping, retry safety,
secrets — has nothing to bind to. Running it would produce a report about code
that does not exist.

For: the *subject* is security-adjacent. The value being pinned is the sizing of
an availability control on the one anonymous, internet-shaped endpoint in the
system, and getting it wrong is a self-inflicted denial of service. A reviewer
might reasonably want a second opinion on whether 2000 is still the right number
given that #2355's non-terminating retry ladder — the amplifier spec 208 sized
against — is still open.

**The tie-break**: that second opinion is a question about spec 208's
derivation, not about this diff. If the dispatcher wants it asked, the right
instrument is an issue against the derivation, not a security review of a test
file. `/code-review` at phase 6 is sufficient and is not optional.

---

## Risks

| Risk | Mitigation |
|---|---|
| The guard passes without checking anything (missing key silently null) | `optional: false` on the file; explicit `ShouldNotBeNull` before the value assertion; US2 asserts match count before value. |
| US2's regex rots when `Program.cs` is reshaped | Match count asserted first, so rot is red rather than green. Doc comment states the limit. US2 is severable. |
| A reviewer reads the pinned value as violating the neighbouring "never a value" convention | Answered head-on in the class doc comment (§2), not left implicit. |
| `git checkout --` during the counterfactual leaves a stale timestamp and MSBuild skips the rebuild | Called out in spec §*Independent end-to-end test procedure*. |
| The value turns out **not** to match at implementation time | Spec §Phase-4a colour: stop and raise it. Do not adjust the expected value to match reality. |
