# Spec 342: The simulator's two tangled methods

**Feature Branch**: `refactor/2897-scenario-simulator-complexity` (cut from `origin/develop` at `5ecc3007`)

**Created**: 2026-10-13

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. One judgement call is
marked explicitly in §6 (how `BindRuntime` is reached by a test without moving it first).

**Input**: Issue [#2897](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2897),
*Refactor ScenarioSimulator's two S1541 hits: ValidateOneCue and BindRuntime (ADR-0084)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Spec number.** 342. Checked on 2026-10-13 against `origin/develop`, every remote branch
(`git ls-tree`), every local branch and worktree, and the open PRs (#2811, #2837): the highest
spec directory anywhere is 340. A sibling worktree (`refactor/2896-api-endpoint-shape`) was
choosing a number at the same moment, so this spec takes highest + 2 rather than + 1 to leave 341
free. Re-check before the PR is opened (a parked PR can merge a 341 or 342 in between).

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S1541 complexity <= 10, S138 <= 30 lines,
S107 <= 4 parameters, advisory `warning`), ADR-0144 (autonomous lane, phase 4a colours), ADR-0111
(the simulator is dev-only), ADR-0036 (smallest change; a refactor changes shape, not behaviour).

---

## 1. What was measured (premise verified, not assumed)

Re-measured on `5ecc3007` (= `origin/develop`, 2026-10-13):

```sh
dotnet build src/ScenarioSimulator -c Release --no-incremental -p:TreatWarningsAsErrors=false
```

0 errors. The two S1541 hits the issue names are **current**, at exactly the lines it quotes:

| File:line | Method | Rule | Value |
|---|---|---|---|
| `src/ScenarioSimulator/Cues/ClipManifestValidation.cs:97` | `ValidateOneCue` | S1541 | 20 |
| `src/ScenarioSimulator/Program.cs:125` | `BindRuntime` (static local function) | S1541 | 22 |

The other metric hits on the same two files:

| File:line | Method | Rule | Value | In scope? |
|---|---|---|---|---|
| `ClipManifestValidation.cs:97` | `ValidateOneCue` | S138 | 31 lines | Yes: expected to clear as a side effect |
| `ClipManifestValidation.cs:97` | `ValidateOneCue` | S107 | 5 parameters | Yes, if the extraction clears it without a new type (plan §3.1) |
| `Program.cs:125` | `BindRuntime` | S138 | 39 lines | Yes: expected to clear as a side effect |
| `ClipManifestValidation.cs:26` | `Validate` | S138 | 34 lines | **No** (not one of the two methods; see §8) |

`ClipManifestLoader.cs:25` `Load` (S138, 35 lines), which the issue also mentions, is a different
file and is out of scope. No stale hit was found.

## 2. User story

### US1 (P1): The two most complex methods in the simulator read as one decision per method

As a maintainer of the dev-only scenario simulator, I want `ValidateOneCue` and `BindRuntime` broken
into named methods so that each reads as one decision, **without** any change a manifest author or
the AppHost could observe.

**Why P1 / independently shippable.** It is the whole issue; two private methods in one project,
no contract, no migration, no runtime resource.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: A manifest violating every manifest-level rule reports them in the same order and wording
  Given a manifest whose Clip differs from the sidecar, whose DurationMs is 0 and whose Cues is null
  When it is validated
  Then the violations are exactly, in order:
    | CueIndex | Message                                                              |
    |          | Clip '<manifest clip>' does not match the sidecar's own clip '<clip>'. |
    |          | DurationMs 0 must be positive.                                       |
    |          | Cues must not be null.                                               |

Scenario: One cue breaking all seven per-cue rules reports them in the same order and wording
  Given a valid cue at index 0 and, at index 1, a cue that is out of range, not ascending,
    has a blank Class, an out-of-range Confidence, a Box beyond the frame, an unknown Source
    and a blank Kind
  When it is validated
  Then the seven violations for index 1 appear exactly in that order with today's exact text

Scenario: Earlier cues are reported before later ones, and a null entry does not reset ordering
  Given cues [AtMs 5000, null, AtMs 1000]
  When it is validated
  Then index 1 reports "Cue must not be null." before index 2 reports
    "AtMs 1000 is not strictly ascending after 5000."

Scenario: Rule boundaries hold where they hold today (conflict / edge cases)
  Then AtMs == DurationMs is refused, AtMs == 0 and DurationMs - 1 are accepted,
    an AtMs equal to its predecessor is refused, Confidence 0 and 1 are accepted,
    a Box ending exactly at 1 is accepted, a null Box is accepted, a 128-character Kind
    is accepted and a null Source is reported as "Source '' must be 'plc' or 'inference'."
  And with DurationMs <= 0 no range or density violation is reported

Scenario: Aspire-injected endpoints win over the ScenarioSimulator:Runtime fallbacks
  Given both services:<name>:... and ScenarioSimulator:Runtime:<Name> keys for every endpoint
  When BindRuntime runs
  Then every option takes the services:... value

Scenario: Keycloak precedence is connection string, then http, then https
Scenario: Realm, ClientId and RtspHost default when unset and are read when set
Scenario: An empty-string Aspire value is taken as-is (no fall-through to the fallback key)

Scenario: A missing required setting fails with today's exact message (bad request)
  Given every setting is present except <setting>
  When BindRuntime runs
  Then an InvalidOperationException is thrown with exactly <message>
  # nine rows: seven endpoints, Keycloak, client secret

Scenario: With nothing configured, the first setting in today's order is the one reported
  Then the message names camera-catalog

Scenario: ScenarioSimulator=false still disables the simulator (AppHostE2ESwitchTests, unmodified)
```

**Auth scenario: N/A.** Neither method sits behind an authorisation boundary. `BindRuntime` reads
the client secret but its handling (read once, assigned to the options) is pinned by the scenarios
above and is not changed.

#### Independent test procedure (end to end)

1. `dotnet test tests/ScenarioSimulator.Tests -c Release` green before and after; the new
   characterisation classes are byte-identical between the two runs (`git diff` on `tests/` empty
   across the refactor commits).
2. `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~AppHostE2ESwitchTests"`
   green before and after (application model only; starts no containers).
3. The S1541 grep in plan §5 prints nothing for `ScenarioSimulator` after the change.
4. Optional runtime check (phase 5): `aspire run` with no arguments composes `scenario-simulator`
   and it seeds as before. Not required for the gate: the unit pins exercise the exact method, and
   the stack is shared on this machine.

## 3. Functional requirements

- **FR-001** `ValidateOneCue` and `BindRuntime` each report no S1541 after the change.
- **FR-002** The S138 hits on those two methods clear. If the chosen extraction leaves one, the PR
  says so with the measured value (it must not be suppressed).
- **FR-003** `ClipManifestValidation.Validate` returns the identical `IReadOnlyList<CueViolation>`
  (same records, same order) for every input it accepted before.
- **FR-004** `BindRuntime` assigns the identical values, reads keys in the identical precedence,
  and throws the identical `InvalidOperationException` messages in the identical evaluation order.
- **FR-005** Only extracted **named private methods** (static local functions in `Program.cs`).
  No new type, no lookup table, no interface, no configuration knob.
- **FR-006** No suppression, `#pragma`, threshold edit, `.editorconfig` or `SonarLint.xml` change;
  no guard edited.
- **FR-007** `ClipManifestValidationTests`, every other existing `ScenarioSimulator.Tests` test and
  `AppHostE2ESwitchTests` pass **unmodified**.

## 4. Success criteria

- **SC-001** Before/after S1541 lists quoted in the PR body; the after-list for `ScenarioSimulator`
  is empty.
- **SC-002** Characterisation tests observed green on the unrefactored code and green, unmodified,
  after; both outputs quoted in the PR body.
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the
  touched projects.

## 5. Phase 4a colour: **characterisation, observed green**

Behaviour-preserving. The issue is a pure refactor and no defect was found while reading either
method, so this is not a refactor-plus-fix (which would be two issues). Two coverage gaps exist
and are closed **before** any production edit:

- `ClipManifestValidationTests` asserts only `CueIndex` (or non-emptiness), never message text,
  never the order of several violations, never which cue is reported first. The issue states those
  three are a contract with manifest authors. They are pinned by a new class.
- `BindRuntime` has **no covering test at all**. `AppHostE2ESwitchTests` builds the AppHost's
  application model only; it never executes the simulator's `Program.cs`. A refactor with no
  covering test is a rewrite, so `BindRuntime` is pinned first (§6).

A characterisation test that arrives red on the unrefactored code is a test defect (it encodes a
guess, not the behaviour) and is fixed in the test before any production edit; it is never
"fixed" by changing production code in this spec.

## 6. Judgement call (marked): reaching `BindRuntime` without moving it

`BindRuntime` is a static local function of the top-level `Program.cs`. It cannot be called by
name from a test. Two ways to pin it were considered:

- **A (chosen).** Reach it by reflection on the entry-point type: the single non-public static
  method whose compiler-generated name contains `g__BindRuntime|` (today `<<Main>$>g__BindRuntime|0_6`;
  the numeric suffix shifts when local functions are added, so the match is on the stable part).
  The characterisation runs against the **unchanged** production code and survives the refactor
  unmodified, which is exactly what §Testing asks. Cost: it depends on Roslyn's local-function
  naming. If that ever changes, `Single` throws and the test fails loudly, never vacuously.
- **B (rejected).** First move `BindRuntime` into a public static class so a test can call it. That
  is a production change made before any covering test exists, and a new type the issue's rules
  ("extract named private methods ... no new abstraction") do not allow.

The extracted helpers stay static local functions in `Program.cs`, and `BindRuntime` keeps its
name and signature `(IConfiguration, SimulatorOptions)`; the plan makes both binding.

## 7. Latency budget impact (constitution §IV)

**N/A.** The scenario simulator is dev-only (ADR-0111), gated off in CI, E2E and production, and
neither method is on the event-to-overlay path. No leg is affected.

## 8. Out of scope

- `Validate`'s own S138 (34 lines). It is not one of the two named methods; the issue says not to
  widen. Recorded so it is not lost: #2887-#2895 skip `ScenarioSimulator`, so no open issue owns it.
- `ClipManifestLoader.Load` S138 and every S107/S138 elsewhere in `src/ScenarioSimulator`.
- Any change to message wording, culture formatting (`Confidence` is interpolated with the current
  culture today; the pins avoid fractional values so they are culture-independent, and the
  behaviour is left exactly as it is).

## 9. Locked tech choices

xUnit + Shouldly (ADR-0052), sentence-style test names (ADR-0053), `Ensure.That` guards unchanged
(ADR-0105), `var` wherever it compiles, collection expressions, no leading underscores, one
parameter per line when wrapping (CLAUDE.md house rules). No Testcontainers (ADR-0103).
