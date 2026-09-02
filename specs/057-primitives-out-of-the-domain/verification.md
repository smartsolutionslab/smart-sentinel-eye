# Verification — 057 primitives out of the domain

**Branch**: `057-primitives-out-of-the-domain` · Feature issue: https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2017

Covers Phases 1–3 (T001–T012). Phases 4–8 are not started.

---

## T001 — baseline build

`dotnet build SmartSentinelEye.slnx` green before any edit. `0 Error(s)`,
elapsed `00:03:11.59`. `dotnet test` deferred to T012's regression rather than
run twice against an unchanged tree.

## T002 — baseline counts

| Category | Count |
|---|---|
| `ThrowIfNull` — `src/`, excluding migrations | **2** |
| `ThrowIfNull` — generated migrations (exempt) | 16 |
| `ThrowIfNull` — `src/AppHost` (exempt) | **0** |
| `ThrowIfNull` — `tests/` | 27 |
| `ThrowIfNullOrWhiteSpace` — `src/` | **27** |
| `ThrowIfLessThan` — `src/` | 1 |
| `ThrowIfNullOrWhiteSpace` + `ThrowIfLessThan` — `tests/` | 2 |
| `ThrowIfNullOrEmpty` / `ThrowIfGreaterThan` / `ThrowIfNegative` — anywhere | **0** |
| Domain text properties | 9 |
| Domain timestamp properties | 26 |
| `ExpectedVersion` — `src/` / `tests/` | 74 / 18 |
| EF `IsConcurrencyToken` mappings | 10 |

**Total guard conversions: 59** — not the ~54 the plan estimated. The string
figure is 27, not ~24.

Two findings that changed the work:

- **`AppHost` contains zero guards of any kind.** Its exemption is currently
  unexercised. It is kept because the *reason* has not changed — the project
  does not reference `Shared.Kernel`, so `Ensure` is unavailable there — but
  the exemption protects nothing today, and ADR-0139 says so.
- **Three banned APIs have zero call sites.** This reversed a decision in
  `contracts/enforcement-contract.md` §1, which had said to trim unused
  entries. They are banned anyway; the reasoning is recorded in that contract
  and in ADR-0139.

---

## T007 — the gate: observed RED

The build **failed**, as required. `31 Error(s)`, all `RS0030`, carrying the
ADR-0139 message.

```text
Rule.cs(75,9): error RS0030: The symbol 'ArgumentException.ThrowIfNullOrWhiteSpace(string?, string?)'
  is banned in this project: Argument guards use Ensure.That (ADR-0139). Use
  Ensure.That(x).IsNotNull().IsNotNullOrWhiteSpace() -- chain IsNotNull first, or a null
  input widens from ArgumentNullException to ArgumentException.

WolverineDefaults.cs(53,9): error RS0030: The symbol 'ArgumentOutOfRangeException.ThrowIfLessThan<T>(T, T, string?)'
  is banned in this project: Argument guards use Ensure.That (ADR-0139). Use Ensure.That(x).AtLeast(n).

AggregateVersions.cs(36,9): error RS0030: The symbol 'ArgumentNullException.ThrowIfNull(object?, string?)'
  is banned in this project: Argument guards use Ensure.That (ADR-0105, ADR-0139). Use
  Ensure.That(x).IsNotNull() -- it raises the same ArgumentNullException and captures the parameter name.
```

Nine projects reported it, and **which** nine is the evidence that the scope
widening works — ADR-0049's ban covers none of the last four:

`Automation.Domain`, `EventIngestion.Domain`, `Identity.Domain`,
`StreamDistribution.Domain`, `ServiceDefaults`, `Shared.Kernel.Tests`,
`LayoutComposition.Application.Tests`, `OverlayDesigner.Application.Tests`,
`SystemVariables.Application.Tests`.

`Shared.Kernel/Ensure.cs` did **not** trip, confirming the analyzer ignores the
banned idioms where they appear in XML doc comments. Prose is not a call site.

### A false start worth recording

The first gate run failed with **`NU1201` — "does not support any target
frameworks"** across ~66 projects, which looks like a catastrophic dependency
break. It was `MSB4024`: the XML comment added to `Directory.Build.props`
contained `--`, which is illegal inside an XML comment, so the props file could
not load and every project lost its `TargetFramework`.

This is exactly the failure mode the gate exists to catch, in reverse. **A red
build is not the gate — a red build *with `RS0030`* is.** Had the task said
only "confirm the build fails", this run would have passed it while the ban
was never evaluated at all.

### What the gate did not reach

`CameraCatalogFabGuard.cs:41-42` — the two live production violations —
**did not appear**. `LayoutComposition.Infrastructure` never compiled, because
`ServiceDefaults` failed first and MSBuild skips dependents of a failed
project.

The red is therefore genuine but incomplete against the task as written. Those
two sites are observed separately at T008 below, after the blocking projects
are converted and the build reaches them.

---

## T008 — the two named sites, observed red

After the blocking projects were converted, the build reached
`LayoutComposition.Infrastructure` and failed with **exactly two errors**,
nothing else:

```text
src\LayoutComposition\Infrastructure\Cameras\CameraCatalogFabGuard.cs(41,9): error RS0030:
  The symbol 'ArgumentNullException.ThrowIfNull(object?, string?)' is banned in this project:
  Argument guards use Ensure.That (ADR-0105, ADR-0139).
src\LayoutComposition\Infrastructure\Cameras\CameraCatalogFabGuard.cs(42,9): error RS0030:
  (same)

Build FAILED.
    2 Error(s)
```

Lines 41 and 42, as the task predicted. Both then converted.

## T009 / T010 — conversions

59 sites across 30 files. Guard conversions used
`Ensure.That(x).IsNotNull().IsNotNullOrWhiteSpace()` for the string cases —
the chained form, per research R3 — so a null input keeps raising
`ArgumentNullException` rather than widening to `ArgumentException`.

## T011 — green, and only exemptions survive

`dotnet build SmartSentinelEye.slnx`: **0 Errors, 0 Warnings.**

Every surviving occurrence of a banned idiom:

| Location | Count | Exemption |
|---|---|---|
| `src/*/Infrastructure/Persistence/Migrations/` | 16 | generated; path exemption |
| `src/Shared.Kernel/Ensure.cs` | 3 | XML doc comments, not call sites |
| anything else | **0** | — |

`AppHost` contributes nothing because it had no guards to begin with.

## T012 — the wiring guard, and proof it can fail

`GuardBanWiringTests`, 4 tests, all passing. Four passing tests prove nothing
on their own, so both failure modes were provoked:

| Mutation | Applied? | Result |
|---|---|---|
| Remove `ThrowIfNegative` from the ban list | 6 → 5 lines, confirmed | **Failed 1 of 4** — the missing-helper test |
| Repoint the `AdditionalFiles` entry at a non-existent file | confirmed by checksum | **Failed 1 of 4** — the wiring test |

Both files were restored and their checksums confirmed to match the originals.

**A third mutation attempt reported "Passed" and was discarded.** The `perl`
one-liner aborted on an escaping error, so the file was never modified and the
suite passed against unmutated input. The checksum guard caught it. This is the
failure this repository has hit twice before; the check is worth keeping in the
loop precisely because the false pass is indistinguishable from a real one
without it.

---

## Test suite

**28 test projects, 1818 tests, 0 failures**, all passing **unmodified** — the
green-throughout obligation the amended §Testing places on behaviour-preserving
work. No test was edited to accommodate a conversion.

That count includes `Architecture.Tests` at 105 (101 existing plus the 4 added
at T012) and covers every project that was touched.

### Integration.Tests could not be run here, and did not pass

`Integration.Tests` reported **302 failed / 57 passed**. This is **not**
evidence about the change, and it is not being claimed as green.

The project boots the real Aspire stack via `AspireFixture` (ADR-0103), which
requires Docker. On this machine Docker Desktop's processes are present but the
daemon does not answer — `docker info` returned nothing inside a 5-second cap
and `docker ps` hung until killed. Every failure landed at the same ~34 s mark,
consistent with fixture startup timing out rather than with assertions failing.

**What that leaves unverified**: nothing in Phases 1–3 changes runtime
behaviour, and the guard conversions are covered by the 1818 unit tests. But
"the integration suite is green after this change" is a claim nobody has
established, and it should be established in CI before merge rather than
assumed from the reasoning above.

---

## Phases 1–3: what is and is not proved

| Claim | Proved by | **Not** proved by |
|---|---|---|
| The ban is wired | T007's red, then T008's two named errors | the list existing |
| The ban stays wired | T012 + both mutations | T012 passing, which an inert check also does |
| No banned guard survives | T011's audit | the green build, which the exemptions also produce |
| Migrations are exempt | 16 sites present, build green | the `.editorconfig` stanza existing |
| Behaviour is unchanged | 1818 tests, unmodified | — |
| **The integration suite is green** | **nothing** | 1818 unit tests passing |
| **The primitive rule cannot drift** | **nothing** | this phase; it stays review-enforced |
| **The TDD rule cannot drift** | **nothing** | this phase; it stays review-enforced |

The last three are the honest ones. Phases 1–3 make the **guard** rule
mechanical. The other two rules now state their scope and their evidence, which
is strictly better than before, and remain enforced by review.

---

# Phase 4 — US2: text types (T013–T022)

## The spec's premise was wrong for six of nine

The spec said "a caller can put `""` into a rule's trigger source". Measuring
before writing the tests showed otherwise, and changed what they assert.

| Property | Guarded before | What the type actually adds |
|---|---|---|
| `Rule.TriggerSource` / `TriggerKind` | non-empty | **type safety** — both were `string` and adjacent in `Rule.Create`; a bound |
| `DeadLetter.Topic` / `Error` | non-empty | length bound, all construction paths |
| `DeadLetter.RawPayload` | **null only** | distinctness only — see below |
| `WebhookIntegration.KeycloakClientId` | non-empty | length bound |
| `AuditEvent.ActorUsername` / `Payload` | **nothing** | non-empty, length bound |
| `Stream.LastError` | non-empty | length bound |

Only three gain `""` rejection. The general win is that every length limit
lived **only** in the EF configuration, so an over-long value was
constructible and failed as a `DbUpdateException` at the far end of the
request.

## T013–T016 — red observed

All nine types absent; the tests failed to **compile**, which is the strongest
red available.

```text
error CS0103: The name 'TriggerSource' does not exist in the current context
error CS0103: The name 'DeliveryTopic' does not exist in the current context
error CS0103: The name 'ActorUsername' does not exist in the current context
error CS0103: The name 'StreamError' does not exist in the current context
```

## T022 — the probe caught a real defect

`EventIngestion` did **not** come back empty on the first run:

```text
migrationBuilder.AlterColumn<string>(
    name: "keycloak_client_id", table: "webhook_integrations",
    nullable: false, defaultValue: "", oldNullable: true);
```

Retyping `KeycloakClientId` dropped its `?`, and EF read the non-nullable
annotation as required. That is the defect the empty-diff check exists to
catch. Restoring the nullable reference made the probe empty.

Final state — every context probed, every `Up()`/`Down()` empty, no migration
committed. The only model-snapshot difference anywhere was EF's own
`ProductVersion` annotation (10.0.10 → 10.0.11), which is tooling, not schema.

| Context | Probe |
|---|---|
| Automation | empty |
| EventIngestion | empty (after the fix above) |
| AuditObservability | empty |
| StreamDistribution | empty |

## Two invariants the code disproved

**`RawPayload` does not refuse empty.** `MqttSubscriberHostedService` builds it
from `Encoding.UTF8.GetString(body.Span)`, so a zero-length MQTT delivery gives
`""` — and a zero-length delivery is exactly the sort of malformed message that
gets dead-lettered. Refusing it would throw inside the capture path, be
swallowed by the surrounding handler as though the database were down, and lose
the dead letter for one of the most likely rejection causes. The invariant would
have suppressed the evidence it was meant to protect.

**`AuditPayload` keeps its non-empty rule**, and the asymmetry is reachability
rather than taste: it comes from `JsonSerializer.Serialize`, which yields at
least `{}` for any object.

## A rule this phase broke, and did not hide

`StreamError.Truncating` was written **before** its test — a violation of the
red-first obligation ADR-0139 introduced two commits earlier.

It is disclosed rather than quietly corrected. What was done instead: the
truncation was removed by mutation, the test was confirmed **failing**, and the
file was restored to its original checksum. That proves the test has power; it
does **not** prove the test came first, and the two are not the same claim.

The factory exists because the alternative was worse. An over-long gateway
error previously failed as a `DbUpdateException`; refusing it in `From` would
have thrown `ArgumentException` into a background loop instead — losing the same
health report, for the same input, by a new route.

## Query filters that must still translate

Three EF predicates compared a converted column to a raw string. Unwrapping to
`.Value` would have stopped translating and fallen back to client evaluation —
passing tests while scanning the table. Each now parses the filter into the
value object and compares type to type.

All three return an **empty page** rather than a `400` when the filter cannot be
a valid value, because that is what comparing it to the column did before, and
this feature moves no status code:
`ListRulesQueryHandler` (×2) and `SearchAuditQueryHandler` (×1).

The webhook `azp` authorization check keeps `string.Equals` against `?.Value`
rather than record equality — its null handling is preserved exactly, including
the null-versus-null case, which this refactor is not the place to change.

## CI caught what every local check missed: Debug is not the gate

PR #2019's first CI run **failed**. Six errors, all `CS8602` / `CS8625` —
nullable-reference violations in the **Release** build:

```text
tests/AuditObservability.Domain.Tests/AuditEvent/AuditEventTests.cs(54,9):
  error CS8602: Dereference of a possibly null reference.
tests/EventIngestion.Domain.Tests/DeadLetter/DeadLetterValueObjectTests.cs(59,44):
  error CS8625: Cannot convert null literal to non-nullable reference type.
src/AuditObservability/Infrastructure/Persistence/Configurations/AuditEventConfiguration.cs(92,40):
  error CS8602: Dereference of a possibly null reference.
src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs(78,37):
  error CS8602: Dereference of a possibly null reference.
src/EventIngestion/Infrastructure/Persistence/Configurations/WebhookIntegrationConfiguration.cs(65,38):
  error CS8602: Dereference of a possibly null reference.
```

Every local build in this phase was **Debug**. Debug was green throughout, and
green Debug says nothing at all about Release — they are different gates, not
strong and weak versions of one.

A seventh surfaced only after fixing the first six
(`WebhookIntegrationRotatedV1HandlerTests.cs:42`), because the compiler stops
per project.

**All six live where a nullable value object is dereferenced**, which is the
new construct this phase introduced and therefore exactly where the two
configurations were always going to diverge. The fix is the idiom the codebase
already used and which was not carried across: `DeadLetterConfiguration` has
written `fab => fab!.Value` since spec 018. It was read during this work and not
generalised.

Three things follow, and only one of them is the code fix:

1. The converters and assertions take `!` where the property is nullable.
2. `quickstart.md`'s full-regression step now says `-c Release`, because it said
   plain `dotnet build` and that is what licensed the mistake. A verification
   document that names a weaker check than CI runs is not a verification
   document.
3. The claim "28 projects, 1864 tests, no failures" in the previous commit was
   **true and insufficient**. It was measured in Debug. Restated below against
   Release.

This is the same defect this whole feature exists to correct — a check that
passes on half a system, believed because it was green — committed inside the
fix for it. Recorded rather than quietly amended.
