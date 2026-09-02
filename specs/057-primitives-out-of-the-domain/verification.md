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
