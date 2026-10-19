# Spec 416: the audit's bracket scan folds onto `UrlAuthority`

**Feature Branch**: `refactor/3023-fold-bracket-scan` (cut from `origin/develop` at `d24ee84e`)

**Created**: 2026-10-19

**Status**: Specified, planned and tasked (phases 1-3). No `[NEEDS CLARIFICATION]` remains. One pre-declared
structural-test retarget (§4, FR-004) is called out for the orchestrator; it is not a behaviour assertion.

**Input**: Issue [#3023](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3023), item 6 ("The
bracketed-host scan exists twice (`UrlAuthority` and `AuditPayloadRedaction.CarriesZone`); fold onto one"), and the
owner's enrolment comment: "PR 2 only (behaviour-preserving fold of the bracket scan into
`UrlAuthority.BracketContentIsAddressOnly`, oracle-checked, characterisation first). Q1-Q3 (ASCII-only hosts,
invisible chars in path, closing item 4) stay with the owner and are NOT in scope."

**Predecessor.** Spec 411 planned this as its PR 2 (spec 411 §1.7, FR-008, plan §4, §6.5-§6.6, tasks T101-T180).
PR 1 of spec 411 merged as #3052. This spec carries PR 2 forward on today's code and corrects two things spec 411
missed (§1.3, §1.4).

**Spec number.** 416. `origin/develop` ends at 415 (412 intentionally skipped); no local or remote branch and no open
PR claims 416 (checked 2026-10-19: the only open PR is #2811). Re-check before the PR is opened.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation, observed green**.
Engineer: **backend-engineer**. Phase 6: `/code-review`. Not security-sensitive in the `/security-review` sense:
the origin that the audit stores (`UrlRedaction.ToOrigin`) is not touched; only the `DetailChanged` tri-state's
zone arm moves, and C-F1 pins it.

**ADRs.** ADR-0144 (lane; characterisation colour; "unmodified after"; no gate weakened), ADR-0139 (constitution
§Testing: refactors stay green), ADR-0036 (smallest change; no drive-by), ADR-0087 (each commit builds and passes on
its own), ADR-0105 (`Ensure.That` guard on the new public member), ADR-0084 (metrics advisory), ADR-0091 (no
shortcuts in names). **No new ADR**: the decision (one bracket scan, in `UrlAuthority`, under that member name) is
the owner's and is recorded in the issue.

**Latency budget (§IV):** N/A. The audit consumer is off the event-to-overlay path.

## 1. Problem and premise check (on `d24ee84e`)

### 1.1 The two scans

- **A (audit, today)**: `AuditPayloadRedaction.BracketContentIsAddressOnly` + `IsAddressText`
  (`src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs:157-202`). On `url.Trim()`: locate the
  authority (`UrlAuthority.TextFromTheAuthority`), cut at the first terminator, take the **last** `@` of that
  userinfo-and-host text; **a `[` or `]` before it means false**; the host after it must start with `[`, and the text
  between `[` and the first `]` must be non-empty ASCII hex, `:` or `.`.
- **B (shared)**: `UrlAuthority.BracketedHostReadings` (`src/Shared.Kernel/UrlAuthority.cs:308-321`, private), used by
  `TextFollowsTheClosingBracket`, `WrittenPortIsEmpty`, `SlashInsideTheBracketsFollowsNoAddress`,
  `DelimiterInsideTheBrackets`, `NonPrintableCharacterInsideTheZone`. On `url.TrimStart()`: same locator and
  terminator; readings start after the **first** and after the **last** `@` (or at 0 with no `@`, or at 0 when only
  parser-trimmed characters follow that `@`); only readings starting with `[` are kept.

`A` is reached only from `CarriesZone` (`:150-153`), and `CarriesZone` only after both origins rendered
(`DetailChanged`, `:121-143`), and only for `HostNameType == IPv6` with no `%` in `DnsSafeHost`.

The premise holds: authority location, userinfo end and bracket start are computed twice. `AuditPayloadRedaction.cs`
is byte-identical to `00a66722`, the commit spec 411 measured on. `BracketedHostReadings` is unchanged since then
(spec 411 PR 1 added callers, not edits).

### 1.2 A naive fold diverges; the fold that keeps A's userinfo-bracket arm does not

Spec 411 §1.7 measured, over the spec 394 fuzz corpus on the domain where the bracket check is reached (725 inputs):

- "every `BracketedHostReadings` content is address text": **diverges on 110**, all with a bracket in the userinfo
  (`rtsp://[]@[FE80::1]`, `rtsp://[x]@[::1]:/s?x#y`). Today these are zoned (`DetailChanged` = `null`).
- the same fold **keeping A's "bracket before the last `@` means false" arm**: **diverges on 0**.

**Re-derived on today's code (this spec), not only re-measured.** With the arm in place, no `[` or `]` occurs before
the last `@`. So B's first-`@` reading cannot start with `[` unless it coincides with the last-`@` reading; B's
start-0 fallback cannot start with `[` when an `@` exists; with no `@`, B's single reading is A's host. B's
`TrimStart` versus A's `Trim` differs only by trailing whitespace, which holds no `@`, `[`, `]` or terminator and so
moves none of the indices A reads. So the folded predicate equals A **on every input**, reached or not. The
characterisation (plan §4) is the evidence; this derivation is why it is expected to be green, and G3 (plan §4.4)
checks it on the whole corpus.

**Conclusion: no observable difference forces a choice.** The fold is preservable. Not blocked on behaviour.

### 1.3 Correction to spec 411: the corpus has six seeds, not five

`CameraUrlZoneDelimiterFuzzAdversarialTests.Corpora` (`tests/Architecture.Tests/...:49-57`) now lists six
(`tokens/3023`, `tokens/1`, `alphabet/3023`, `structured/3023`, `structured/11`, `tokens/3031`). The 725/110 figures
were five-seed figures. The reach floors are re-measured at T111 on the six seeds and written into the test.

### 1.4 Correction to spec 411: an existing structure assertion pins the shape the fold removes

`tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs:261-267`, `The_audit_redaction_asks_the_shared_locator`,
asserts `AuditPayloadRedaction.cs` **calls** `UrlAuthority.TextFromTheAuthority(`. After the fold the audit no longer
locates anything (that is the point of item 6), so this assertion goes red. Spec 411's G2 asserted the opposite
("contains no `TextFromTheAuthority(`") without noticing the conflict.

This assertion pins **source shape, not behaviour**: its intent (spec 392 US3) is "the audit does not write its own
authority locator". The fold satisfies that intent more strongly. It is therefore **retargeted, pre-declared here at
phase 3 before any code exists** (FR-004), to a strictly stronger assertion: the audit calls
`UrlAuthority.BracketContentIsAddressOnly(` and calls neither `TextFromTheAuthority(` nor `IndexOfTheTerminator(`.
`The_authority_locator_is_written_only_in_UrlAuthority` (the `IndexOfAnyExcept` copy guard) is untouched and keeps
guarding re-implementation.

**Orchestrator note.** ADR-0144 §4a item 4 says an assertion that must be edited is evidence of a behaviour change.
This is the one edit in the PR, it is to a structure guard, it is decided by the architect before the refactor, and
the owner's scope names the target member. If the orchestrator reads item 4 literally for structure guards, this
spec is **blocked on that reading** and goes to the owner; there is no behaviour-preserving fold that keeps that
assertion green, because the assertion requires the duplication item 6 removes.

## 2. User story

### US1 (P1): the audit asks `UrlAuthority` whether a bracketed host holds only an address

A maintainer changing how a bracketed host is read changes it in one place, `UrlAuthority`. The audit's
`DetailChanged` (`true` / `false` / `null`) is exactly what it is today for every address.

**Independent test.** Plan §4: C-F0 counts and C-F1 (oracle) green on `d24ee84e` before the fold, unmodified and
green after it, identical counts; the C-F1 counterfactuals red; G2/G3 green.

#### Acceptance scenarios

```gherkin
Scenario: an address-only bracketed host still compares by origin (happy path)
  Given previousUrl "rtsp://[fe80::1]/x" and url "rtsp://[fe80::1]/y"
  When CameraAddressChangedV1 is serialised for the audit
  Then DetailChanged is true, before and after the fold

Scenario: a zone behind a prefix length is still unknown (conflict: the parser drops the zone)
  Given previousUrl "rtsp://u:p@[fe80::1/64%25eth0]/x" paired with "rtsp://[fe80::1]/s" in either order
  Then DetailChanged is null, before and after the fold

Scenario: a bracket in the userinfo is still unknown (the 110-input arm)
  Given previousUrl "rtsp://[]@[FE80::1]" or "rtsp://[x]@[::1]:/s?x#y" paired with "rtsp://[fe80::1]/s"
  Then DetailChanged is null, before and after the fold

Scenario: an unlocatable or unreadable address is unchanged (bad request)
  Given an address ToOrigin renders as the placeholder
  Then DetailChanged is null, and the bracket scan is not reached, before and after the fold

Scenario: the whole corpus agrees with the oracle
  Given every input of the six fuzz corpora, each paired with "rtsp://[fe80::1]/s" in both directions, plus the explicit rows
  When DetailChanged is computed by the production code and by the oracle copy of today's code
  Then they are equal for every pair
  And the corpus reaches at least the measured floors of bracket-check evaluations and userinfo-bracket inputs

Scenario: auth
  Not applicable: no endpoint, scope or token is touched. The audit consumer's authorisation is unchanged.
```

## 3. Functional requirements

- **FR-001: One scan.** `UrlAuthority` gains `public static bool BracketContentIsAddressOnly(string url)`, guarded by
  `Ensure.That(url).IsNotNull()`, built on `BracketedHostReadings`, and keeping A's userinfo-bracket arm. Its answer
  equals today's private `AuditPayloadRedaction.BracketContentIsAddressOnly` on every input (§1.2).
- **FR-002: The audit asks it.** `AuditPayloadRedaction` deletes its private `BracketContentIsAddressOnly` and
  `IsAddressText` and `CarriesZone` calls `UrlAuthority.BracketContentIsAddressOnly(url)`. `CarriesZone`'s
  `Uri.TryCreate` / `HostNameType` / `DnsSafeHost` arm is untouched (#3031 item 4 is not enrolled).
- **FR-003: No behaviour moves.** Every behaviour assertion in every suite stays as written. No contract, event,
  error code, endpoint, migration or stored row changes.
- **FR-004: One pre-declared structure retarget** (§1.4): `The_audit_redaction_asks_the_shared_locator` becomes the
  stronger single-source assertion. No other existing test line changes except `Generate`'s `private` → `internal`.
- **FR-005: No new cross-context reference.** AuditObservability already references `Shared.Kernel`.

## 4. Out of scope

- Q1-Q3 (ASCII-only hosts, invisible characters in a path, closing item 4): the owner's.
- `CarriesZone`'s unguarded `DnsSafeHost` read (#3031 item 4). `UrlRedaction.ToOrigin`.
- Narrowing the visibility of `TextFromTheAuthority` / `IndexOfTheTerminator` (they keep direct tests; no drive-by).
- Any change to the value objects or what registration accepts.

## 5. Assumptions

- **A-1.** The six-seed corpus still reaches a few hundred bracket-check evaluations and about a hundred
  userinfo-bracket inputs. If T111 measures fewer than 500 / 60, the floor is set from the measurement and the
  figure is quoted; a reach below 100 / 20 is a block (the oracle would be near-vacuous), not a lowered floor.
- **A-2.** C-F1's runtime over ~300k inputs is under a minute (the fuzz test already walks the same corpus). If it is
  not, the pairing drops the backward direction for inputs that never reach the check (computed by the oracle), never
  the inputs that do.
