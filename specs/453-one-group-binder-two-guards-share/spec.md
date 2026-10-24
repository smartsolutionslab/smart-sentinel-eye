# Spec 453: One group binder the two guards share

**Issue:** #3133 (feature-level, on Project #13) · **Branch:** `refactor/3133-route-group-binding`
**ADRs:** ADR-0139 (characterisation colour for a behaviour-preserving change; these guards fail the build), ADR-0036
(smallest change — a refactor changes shape, not behaviour), ADR-0087 (each commit builds on its own), ADR-0091 (no
shortcuts in the new names), ADR-0109 (task parallelism), ADR-0144 (lane). Constitution §Testing (covering tests green
*before* and *throughout*; a red test during a refactor is a regression).
**Specs built on:** 190 (`SourceMask` / `RouteChainReader` extraction — the precedent this follows), 444 (call-site +
block-scope binding in `EndpointScopeDeclarationTests`), 447 (its port into `StatusProducerDeclarationTests`).
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** **characterisation** — nothing new is asserted; the existing
cases are the net and pass **unmodified**. A refactor that changes an asserted value is a **block**, not an adjustment.
**Security-sensitive:** no — test code only, no runtime change; `security-reviewer` not needed.
**Latency budget:** N/A — no leg touched. **New ADR needed:** no (plan §"Why no ADR").
**Written 2026-10-25 against `develop` at `a9eaef85`.**

---

## Premise, checked at `a9eaef85`

1. **The binder exists twice.** `EndpointScopeDeclarationTests.cs` carries `ParameterScopes` (`:1817`), `EnclosingBlock`
   (`:1866`), `ParameterListEnd` (`:1887`), `BodyAfter` (`:1915`), `ResolveHelperGroup` (`:1941`), the records
   `ParameterScope`/`GroupScope`/`GroupBinding` with `Resolve` + `IsRedeclared` (`:2729-2782`) and the regexes
   `RouteBuilderDeclaration`/`GroupHelperDeclaration`/`EqualsAssignment` (`:480-512`). `StatusProducerDeclarationTests.cs`
   carries the port: `RouteBuilderParameter`/`GroupHelperDeclaration`/`EqualsAssignment` (`:1095-1108`), `BoundGroup`
   (`:1123`), `IsRedeclared` (`:1149`), `GroupScopes` (`:1164`), `ParameterScopes` (`:1188`), `EnclosingBlock`,
   `ParameterListEnd`, `BodyAfter`, `ResolveHelperGroup` (`:1227-1332`), records `:1403-1405`. **Holds.**
2. **Identical apart from the three known differences — verified line by line.** Regex patterns are byte-equal;
   `EnclosingBlock`, `ParameterListEnd`, `BodyAfter` are byte-equal; `ParameterScopes` differs only in the helper
   resolver it calls; `Resolve`/`BoundGroup` and both `IsRedeclared` are the same logic (444 reads `Source`, which is
   `masked`). The differences:
   - **D1 — failed helper.** 444 returns an *Unreadable* `RouteGroup` naming method, parameter and one of four reasons;
     the port returns `null` at the same four points, in the same order.
   - **D2 — where scopes come from.** 444 builds a `GroupScope` per `LocalDeclaration` (`RouteGroupBuilder|var name =`)
     whose statement contains `.MapGroup(`, `DeclarationFrom` = the match start; the port builds one per its own
     `GroupDeclaration` match (`name = x.MapGroup(`, so a bare reassignment counts) with `DeclarationFrom` walked back to
     the type token, and the block anchored at the variable.
   - **D3 — duplicate names.** 444's dictionary maps a second declaration to an *Unreadable* group (and reports it);
     the port's `TryAdd` keeps the first.
3. **All three differences live outside the shared core.** D1 is how each guard *renders* a failed helper; D2 is each
   guard's *declaration discovery*; D3 is each guard's *name → group dictionary*. The core — "is this receiver a helper
   parameter, a non-helper route-builder parameter, or a visible, un-redeclared group local, and which group name does
   it reach" — is the same in both. A shared binder that returns a **group name** or a **reason it could not bind**, and
   takes the declaration sites as input, absorbs all three with **no verdict change in either guard** (plan §"Absorbing
   the differences"). **No stop condition reached.**
4. **The net, run green at `a9eaef85`:** `EndpointScopeDeclarationTests` 122, `StatusProducerDeclarationTests` 46,
   `RouteValueRefusalDeclarationTests` 21, `ConcurrencyConflictDeclarationTests` 13 (untouched; tripwire), nine
   `*RouteTableCharacterisationTests` 12 — **`Passed: 214, Failed: 0`**. Both guards carry a parallel binding suite
   (ES `:1472-1680`, SP `:764-899`), so a core change reaches both.
5. **The guards scan their own source.** ES's `The_guard_offers_no_way_to_excuse_an_endpoint` scans `GuardSource` plus
   `SharedSourceScanFiles` (`:331-336`, spec 190's list). Moving the binder out of `GuardSource` would take its lines out
   of that scan unless the new file joins the list. SP's scan reads `GuardSource` only — as it already does for
   `RouteChainReader`/`SourceMask` (spec 190 precedent); the moved lines stay scanned by ES's list.

## Out of scope — and why, against the issue title

The title names a third binder, `ConcurrencyConflictDeclarationTests.PrecedingGroupPrefix`, and the body mentions
`RouteValueRefusalDeclarationTests.PrefixOf`. Neither is moved onto the shared binder here:

- `PrecedingGroupPrefix` (`:1037`) is **lexical by design** (its class doc) — nearest `MapGroup` before the mapping.
  Binding it by call site / block scope would be a different rule, i.e. new behaviour (red), not a refactor.
- `PrefixOf` (`:751`) binds by name and words a G1 failure *message* only (spec 447 premise 3). Porting it changes that
  message for a misbinding — a behaviour change, however small, and so not characterisation.

Either can be its own red issue if wanted. Recorded so "three guards" is not read as delivered.

## User stories

### US1 (P1) — One binder, read by both guards

As the maintainer of the declaration guards, I want the call-site/block-scope group binder in one internal file next to
`RouteChainReader` and `SourceMask`, so that the next fix to it lands once and cannot drift between two copies.

**Acceptance (Gherkin)**

```gherkin
Scenario: Happy — both guards read the shared binder and keep every verdict
  Given the 214-case net captured green at the base commit
  When the binder is extracted to tests/Architecture.Tests/RouteGroupBinding.cs and both guards call it
  Then the same 214 cases pass with the same names
  And no [Fact]/[Theory] member, InlineData, fixture text or assertion differs (comment-stripped hash)

Scenario: Conflict — a guard keeps a private copy
  Given the shared binder is changed so a helper called with two groups binds the first
  When the net runs
  Then StatusProducer's A_helper_called_with_two_different_groups_binds_no_group fails
  And EndpointScope's A_helper_called_with_two_different_groups_is_unreadable_and_names_the_helper_and_both_groups fails

Scenario: Bad request — the extraction changes a verdict
  Given any of the 214 cases goes red after a step
  Then the step is a regression: the engineer stops and reports it verbatim; no assertion is edited

Scenario: Auth — a failed helper still refuses rather than credits
  Given a helper whose callers pass no single declared group
  Then EndpointScope still reports it Unreadable with the same text
  And StatusProducer still classifies it Unclassified
```

## Independent end-to-end test procedure

1. At the base commit: run the net (filter in tasks T001); save the TRX test-name/outcome list and the comment-stripped
   per-member hashes of both guard files (tasks T001).
2. After each commit: `dotnet build -c Release` clean; the same filter → 214 passed, identical name list.
3. After the last commit: member-hash diff shows only the plan's allowed members changed; the four reason strings are
   byte-identical in `RouteGroupBinding.cs`; counterfactuals C1-C3 (plan) each go red as predicted, then revert;
   `git diff --stat origin/develop -- src/` empty.
