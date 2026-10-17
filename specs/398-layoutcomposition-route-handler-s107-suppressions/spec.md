# Spec 398: the LayoutComposition route handlers carry ADR-0084's S107 suppression

**Feature Branch**: `chore/2972-layoutcomposition-s107-suppressions` (cut from `origin/develop` at `7d2d6af3`)

**Created**: 2026-10-18

**Status**: Draft (phases 1-3). No `[NEEDS CLARIFICATION]` remains; two marked assumptions (A-1, A-2).

**Input**: Issue [#2972](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2972), as narrowed by
its comments. The body (bundle request values like spec 341) is **superseded**: comment 1 proved S107
does not count `[FromServices]` parameters, comment 3 blocked the issue because every hit is a mapped
route handler pinned by `LayoutCompositionRouteTableCharacterisationTests`, and comment 4 (owner
decision, 2026-10-17) re-enrolled it with the mechanism this spec applies. Refs spec 380 (#2971, the
evidence), #2970 (the ADR amendment, merged as `c7a6e586`).

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation, observed
green**. Engineer: **backend-engineer**. Not security-sensitive: no executable line, signature, route,
policy or fab-resolution path changes (§3), so Phase 6 is `/code-review` only.

**Spec number.** 398, assigned by the orchestrator (397 is #2971's, 396 is #2927's). Highest on
`origin/develop` at `7d2d6af3` is 394.

**ADRs.** **ADR-0084** (S107; the 2026-10-16 amendment, accepted 2026-10-17: "S107 does not apply to the
route-handler methods themselves", mechanism "a `[SuppressMessage]` for S107 with a justification on each
of the 45 handlers"), ADR-0144 (lane; this is the one suppression the owner authorised; characterisation
colour), ADR-0070 (Minimal APIs), ADR-0036 (smallest change), ADR-0087 (each commit builds on its own).

**No new ADR.** The decision is already made and accepted; this spec applies it to one context.

## 1. Premise check — the re-measured hit list

Measured 2026-10-18 on `7d2d6af3`, scoped to the LayoutComposition Api project's build closure (a
full-solution build is too heavy on the shared machine):

```sh
dotnet build src/LayoutComposition/Api/SmartSentinelEye.LayoutComposition.Api.csproj -c Release --no-incremental \
  "-flp:logfile=lc.log;verbosity=normal;NoSummary"
grep -oE '[A-Za-z]:[^ (]+\([0-9]+,[0-9]+\): warning S107: [^[]*' lc.log | sed 's#.*src#src#' | sort -u | grep -v Migrations
```

**10 S107 hits under `src/LayoutComposition/Api`, all 10 mapped route handlers, 0 helpers.** Identical
to the issue body and to comment 3. The premise holds.

| # | File (line) | Method | Counted | Mapped at |
|---|---|---|---|---|
| H1 | `LayoutEndpoints.Commands.cs` (17) | `CreateDraft` | 6 | `LayoutEndpoints.cs:53` `MapPost("/")` |
| H2 | `LayoutEndpoints.Commands.cs` (91) | `Publish` | 5 | `LayoutEndpoints.cs:90` |
| H3 | `LayoutEndpoints.Commands.cs` (132) | `Archive` | 5 | `LayoutEndpoints.cs:105` |
| H4 | `LayoutEndpoints.Commands.cs` (215) | `EditDraft` | 6 | `LayoutEndpoints.cs:133` `MapPatch` |
| H5 | `LayoutEndpoints.Commands.cs` (258) | `Revert` | 5 | `LayoutEndpoints.cs:148` |
| H6 | `LayoutEndpoints.Queries.cs` (14) | `GetOne` | 5 | `LayoutEndpoints.cs:67` |
| H7 | `WallEndpoints.Commands.cs` (18) | `CreateWall` | 6 | `WallEndpoints.cs:34` |
| H8 | `WallEndpoints.Commands.cs` (89) | `EditScenes` | 5 | `WallEndpoints.cs:71` `MapPut` |
| H9 | `WallEndpoints.Commands.cs` (134) | `Switch` | 5 | `WallEndpoints.cs:86` |
| H10 | `WallEndpoints.Queries.cs` (14) | `GetOne` | 5 | `WallEndpoints.cs:48` |

Every one is `private static async Task<IResult>`. `List`, `BranchDraft` (Layouts) and `List` (Walls) are
handlers too but are not hits, so they get no attribute.

**The rest of the closure, which must be byte-identical after (§4, SC-2)** — 19 distinct warnings:
S107 ×8 outside the Api (`LayoutComposition/Application/EventHandlers/WallSceneSwitchRequestedV1Handler.cs:30`,
`Domain/Layout/Layout.cs:129`, `Domain/Layout/Tile.cs:71`, `Domain/Wall/Wall.cs:100`,
`ServiceDefaults/Authentication/ClientCredentialsTokenProvider.cs:88`,
`ServiceDefaults/Authorization/FabResolution.cs:49`, `ServiceDefaults/Idempotency/IdempotencyScope.cs:46`,
`ServiceDefaults/WolverineDefaults.cs:40`) and S138 ×1 (`ServiceDefaults/WolverineDefaults.cs:40`). The
Domain/Application hits are explicitly out of the amendment's scope and stay advisory.

## 2. User story

### US1 (P1) — the S107 count for LayoutComposition's handlers measures nothing nobody can act on

As the maintainer reading ADR-0084's S107 count, I want the ten LayoutComposition route handlers to
carry the sanctioned suppression, so that the count stops including HTTP contracts the amendment
declared out of scope, while every pinned contract stays exactly as it is.

**Independent test**: the Release build of the Api closure reports 0 S107 under
`src/LayoutComposition/Api` and the other 9 warnings unchanged; `LayoutCompositionRouteTableCharacterisationTests`
and the six source-scanning guards pass with `git diff 7d2d6af3 -- tests/` empty.

**Acceptance scenarios**

```gherkin
Scenario: the ten handlers are no longer counted
  Given the Api closure is built in Release with --no-incremental
  When the S107 warnings under src/LayoutComposition/Api are listed
  Then there are none

Scenario: nothing else moved (conflict: a suppression that leaks, or a file pushed over a limit)
  Given the same build
  When every distinct warning in the closure is listed
  Then the list equals §1's nine non-Api warnings exactly
  And no S104 hit appears on LayoutEndpoints.Commands.cs

Scenario: the HTTP contract is unchanged (the attribute reaches no metadata)
  Given the LayoutComposition route table rendered from the mapped endpoints
  When it is compared with the baseline captured on 0296275e
  Then LayoutCompositionRouteTableCharacterisationTests passes unmodified

Scenario: the source-scanning guards still read the handlers (bad shape: an attribute the readers mis-parse)
  When ConcurrencyConflict-, EndpointScope-, IdempotentKeyReuse-, Precondition-, RouteValueRefusal-
       and StatusProducerDeclarationTests run
  Then every one passes unmodified

Scenario: a helper is not exempted (auth/scope of the exemption)
  Given a method in these files that is not passed to a Map call
  Then it carries no S107 suppression
```

## 3. Why this is behaviour-preserving, and the two places it could leak

`SuppressMessageAttribute` is `[Conditional("CODE_ANALYSIS")]`. The Release compile line defines
`TRACE;RELEASE;NET;…` and **not** `CODE_ANALYSIS` (checked in the build log; nothing in any
`.props`/`.csproj` defines it), so the attribute is **not emitted**: Roslyn honours it at analysis time
and the compiled IL carries nothing. This matters because minimal APIs copy a handler method's custom
attributes into endpoint metadata, and `RouteTableRendering.Render` prints any unknown metadata as
`other <Type>`. Were the attribute emitted, the baseline would gain a line per handler and the
characterisation would fail — which is exactly why it is the right covering test.

The source-scanning guards read text, not IL. Checked:

- `RouteChainReader.MethodBodies` finds a declaration by `(?:private|…)[^;{}()\n]*?\bName\s*\(` on the
  **same line**; an attribute on the preceding lines does not intervene, provided the attribute and the
  declaration are on separate lines.
- `ConcurrencyConflictDeclarationTests.The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask`
  forbids `@"`, `"""`, `\"` and `'"'` anywhere in `src/*/Api`; the masker it guards
  (`CommentsOnlyLiteralsIntact`) leaves literal **content** visible and ends a literal at the next quote on
  the same line. Hence the justification is plain `"…"` literals, each closed on its own line, and
  contains no `( ) { } [ ] ;` and no quote or backslash.
- The `The_guard_offers_no_way_to_excuse_an_endpoint` rows (`"suppress"`, `"#pragma warning disable"`)
  scan only the guards' own sources and `RepositorySource.cs`/`SourceMask.cs`/`RouteChainReader.cs`, not
  `src/`.
- `EndpointScopeDeclarationTests.DeclaredPolicy` reads `AuthorizeAttribute` data on a type; unaffected.

## 4. Success criteria

- **SC-1**: S107 hits under `src/LayoutComposition/Api` go from 10 to 0 (§1 command).
- **SC-2**: the closure's distinct warning list is otherwise identical to §1 (8 S107 + 1 S138), in
  particular no new S104 (LayoutEndpoints.Commands.cs is ~269 code lines today; five 4-line attributes take
  it to ~289, under 300).
- **SC-3**: `git diff 7d2d6af3 -- tests/` is empty and the characterisation set (tasks T001) passes before
  and after.
- **SC-4**: exactly 10 occurrences of the prescribed attribute in `src/LayoutComposition/Api`, one per row of
  §1, and none elsewhere in the diff.

## 5. Locked choices and the prescribed form

The ADR prescribes the mechanism and that each handler carries a justification; it does not fix the
literal text. This spec fixes it (A-1), so all ten — and, if the orchestrator adopts it, the sibling
issues #2971/#2973/#2974/#2975 — read the same and can be counted with one grep:

```csharp
[SuppressMessage(
    "Major Code Smell",
    "S107:Methods should not have too many parameters",
    Justification = "Route handler: its parameters are the HTTP contract, pinned by the route-table baseline. ADR-0084 amendment of 2026-10-16.")]
private static async Task<IResult> CreateDraft(
```

- Placed directly above the `private static async Task<IResult>` line, below any doc comment, never on
  the same line as the declaration.
- `using System.Diagnostics.CodeAnalysis;` added to each of the four files (not implicit), in sorted
  position, which IDE0005 will then see as used.
- The justification line is 150 columns at 8-space indent, under `.editorconfig`'s 160.
- Not `.editorconfig`, not `#pragma`, not a file-glob section, not a shared `const` (the ADR rejects the
  glob explicitly; a const would add an abstraction for one string).

**Latency budget**: N/A — no runtime change; nothing on the event-to-overlay path.

## 6. Assumptions

- **A-1** (unavoidable guess): the justification wording above. The ADR names no text; this is drawn from its
  Decision paragraph. If #2971's spec 397 picks different wording, the orchestrator should align one to
  the other before either merges, so SC-4's grep works across contexts.
- **A-2**: Sonar's category string `"Major Code Smell"` is cosmetic — Roslyn matches suppressions on the
  check ID before the colon. If the build still reports a hit after T010, the form is wrong: stop, do not
  try another mechanism.

## 7. Out of scope (what the lane must not take)

- Any handler signature, `*Services` record, route, OpenAPI metadata, summary or policy.
- Any file under `tests/` — an assertion that would need editing is a **block**.
- The three non-hit handlers (`List` ×2, `BranchDraft`), and any helper.
- The 8 non-Api S107 and the S138 in the closure; other contexts' handler hits (#2971, #2973, #2974, #2975).
- `.editorconfig`, `SonarLint.xml`, `Directory.Build.props`, csproj files, ADRs, the constitution, CLAUDE.md.
- The PR closes #2972 (`Closes #2972`): the amendment names this issue as closed by the suppressions, and all
  ten of its hits are covered.
