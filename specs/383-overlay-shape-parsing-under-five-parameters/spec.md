# Spec 383: OverlayDesigner's shape-element parsing under five parameters

**Feature Branch**: `refactor/2973-overlay-shape-parsing-s107` (cut from `origin/develop` at `81f68a62`)

**Created**: 2026-10-16

**Status**: Draft (phases 1-3 written; Phase 4 not started). No `[NEEDS CLARIFICATION]` remains.
The issue's scope is **shrunk** from 11 hits to 1 (§5, D-1): the other 10 are route handlers in two
contexts whose route-table characterisation baselines pin every handler's parameter list, so
clearing them would edit a characterisation assertion, which is a block (ADR-0144). They stay open
for the owner decision already recorded on #2971 (spec 380 §6, options a-c).

**Input**: Issue [#2973](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2973), as
narrowed by its comments after the #2971 review (the triage comment names exactly this one helper).
Templates: spec 380 (#2971) and spec 382 (#2975, unmerged, PR #3002), which cleared their contexts'
reachable helpers and handed the handler hits back. Refs spec 300 (#2349: the kind switch, FR-002 to
FR-004 field naming), spec 366 (#2931: `OverlayDraftEndpointCharacterisationTests`).

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation,
observed green**. Engineer: **backend-engineer**. **Not security-sensitive**: the change reshapes a
private parsing helper behind two already-authorised write routes; no authentication, authorisation,
fab scoping, idempotency or secret handling is touched. Phase 6 runs `/code-review` only.

**Spec number.** 383. Checked 2026-10-16 after `git fetch`: highest spec on `origin/develop`
(`81f68a62`) is 381; 382 is claimed by the unmerged `refactor/2975-camera-identity-api-s107`
(PR #3002); no other local, remote or open-PR head (including `docs/adr-0084-s107-log-methods` and
`claude/beautiful-mendel-jz9d5x`) carries 382+ other than that branch. Re-check before the PR is
opened.

**ADRs.** **ADR-0084** (S107, at most 4 parameters; advisory `warning`, carved out of
`TreatWarningsAsErrors`), ADR-0144 (lane; characterisation colour; no gate weakened), ADR-0070
(Minimal APIs), ADR-0036 (smallest change), ADR-0087 (each commit builds on its own), ADR-0091 (no
shortcuts in names), ADR-0165 (element kinds; shapes carry no text).

**No new ADR.** ADR-0084 states the limit; this spec changes the shape of one private helper. The
ten blocked handlers wait on the decision already posed on #2971; this lane does not take it.

## 1. Problem: the re-measured hit list

Release, `--no-incremental`, measured 2026-10-16 on `81f68a62`
(`dotnet build SmartSentinelEye.slnx -c Release --no-incremental -flp:logfile=…metrics-2973.log;verbosity=normal;NoSummary`,
unique `file(line,col): warning S[0-9]+`): **110 S107 hits solution-wide, 11 under
`src/(OverlayDesigner|SystemVariables)/Api`**, and **no other Sonar rule fires in either Api
project** (no S138, S104, S1541, S134).

"Counted" is the parameters S107 counts: everything except `[FromServices]` (spec 380 §1, proven by
counterfactual).

| # | Hit | Method | Kind | n |
|---|---|---|---|---|
| H1 | `OverlayDesigner/Api/OverlayElementParsing.cs(72,52)` | `ParseShapeElement` | **private static helper**, one call site (`ParseElement`, line 28) | 5: `kind`, `request`, `position`, `size`, `color` |
| H2 | `OverlayDesigner/Api/OverlayEndpoints.Commands.cs(16,51)` | `CreateDraft` | route handler | 5 |
| H3 | `OverlayDesigner/Api/OverlayEndpoints.Commands.cs(80,47)` | `Publish` | route handler | 5 (+1 `[FromServices]`) |
| H4 | `OverlayDesigner/Api/OverlayEndpoints.Commands.cs(122,47)` | `Archive` | route handler | 5 (+1) |
| H5 | `OverlayDesigner/Api/OverlayEndpoints.Commands.cs(197,49)` | `EditDraft` | route handler | 5 (+1) |
| H6 | `OverlayDesigner/Api/OverlayEndpoints.Commands.cs(250,46)` | `Revert` | route handler | 5 (+1) |
| H7 | `SystemVariables/Api/SystemVariableEndpoints.Reads.cs(15,46)` | `GetOne` | route handler | ≥5 |
| H8 | `SystemVariables/Api/SystemVariableEndpoints.Reads.cs(56,44)` | `List` | route handler | ≥5 |
| H9 | `SystemVariables/Api/SystemVariableEndpoints.cs(173,46)` | `Define` | route handler | ≥5 |
| H10 | `SystemVariables/Api/SystemVariableEndpoints.cs(263,48)` | `SetValue` | route handler | ≥5 |
| H11 | `SystemVariables/Api/SystemVariableEndpoints.cs(310,47)` | `Archive` | route handler | ≥5 |

Not hits, recorded so nobody goes looking: `ParseElement` (1), `ParseColor`, `ParseKind` (1),
`RequireNoTextFields` (3), `ParseTextElement` (4, at the limit), `ParseCreateOverlay` (1),
`ParseDraftEdit` (2), `CreateExecution` (4), `BranchDraft` (4 counted).

## 2. User story

### US1 (P1): shape-element parsing takes at most four parameters and every element parses as before

**Why P1.** It is the only hit of #2973 reachable without editing a characterisation assertion
(§5, D-1). It is independent of the #2971 decision and forecloses none of its options.

**Independent test.** The plan §4 commands, including the characterisation tests T000a adds
(observed green on unchanged code at the characterisation commit `C0`), pass on `C0` and pass
again, **unmodified**, after the change (`git diff C0 -- tests/` empty); the §1 measurement no
longer lists H1, lists H2-H11 exactly as before (same methods; line numbers may shift), and no
other new warning appears under `src/OverlayDesigner/Api`.

#### Acceptance scenarios

Each applies to both routes that parse elements, `POST /overlays/` (create) and
`PATCH /overlays/{overlayIdentifier}/revisions/{revisionNumber}` (edit draft). Problem bodies are
compared as the **exact JSON observed on unchanged code** (title `OVERLAY_INVALID_INPUT`, status
400, the `type` member as observed).

```gherkin
Scenario: happy path - a Box keeps each of its five values in its own place
  Given an element {kind: "Box", normalizedX: 0.11, normalizedY: 0.12,
                    normalizedWidth: 0.23, normalizedHeight: 0.34, color: "#12345678"}
  When it is created (201) or edited into a draft (200)
  Then the persisted element has Kind Box, Position.X 0.11, Position.Y 0.12,
       Size.Width 0.23, Size.Height 0.34, Color "#12345678" and no Text

Scenario: happy path - an Ellipse is an Ellipse, not a Box
  Given the same values with kind "Ellipse" (and a colour distinct from the Box case)
  When it is created or edited
  Then the persisted element has Kind Ellipse and each of the five values in its own place

Scenario: happy path - each element of a mixed set keeps its own values and order
  Given [Box(a), Ellipse(b)] where every one of the ten numeric values and both colours differ
  When it is created or edited
  Then element 0 is a Box carrying exactly a's values and element 1 an Ellipse carrying b's

Scenario: happy path - a fully transparent shape colour is accepted
  Given a Box with color "#00000000"
  Then it persists (strokes carry no readability floor, ADR-0165 §2)

Scenario: bad request - a shape carrying text is refused naming text
  Given a Box (and, separately, an Ellipse) with text "x" and no fontSizePx
  Then the answer is 400 with detail "text must be absent for a <Kind> element. (Parameter 'text')"
       as observed, and the repository is never written

Scenario: bad request - a shape carrying a font size is refused naming fontSizePx
  Given a Box (and an Ellipse) with fontSizePx 24 and no text
  Then the answer is 400 naming fontSizePx exactly as observed

Scenario: conflict - a shape carrying both is refused on text first
  Given a Box with text "x" and fontSizePx 24
  Then the 400 names text, not fontSizePx

Scenario: conflict - geometry and colour are refused before the shape's text check
  Given a Box with text "x" and normalizedX 1.5
  Then the 400 names normalizedX (not text)
  And given a Box with text "x" and color "red", the 400 names color (not text)

Scenario: auth - unchanged and not exercised by the helper
  The two routes' authorisation (`sse.overlays.write` on each mapping) runs before any parsing
  and is pinned by OverlayDesignerRouteTableCharacterisationTests and EndpointScopeDeclarationTests;
  this spec adds no auth case because the helper is reached only after authorisation succeeded.
```

## 3. Requirements

- **FR-001** H1 `OverlayElementParsing.ParseShapeElement` takes at most 4 counted parameters.
  Shape: plan §2.1.
- **FR-002** Its outcomes are unchanged: `RequireNoTextFields(kind, request.Text, request.FontSizePx)`
  runs first and throws the same `ArgumentException`s (message and `paramName`) in the same order;
  a `Box` kind yields `OverlayElement.Box`, any other shape kind `OverlayElement.Ellipse`, each with
  the same position, size and colour values.
- **FR-003** `ParseElement` keeps its validation order (position, size, colour, kind, then the
  per-kind branch) and its text branch (`ParseTextElement`) is untouched.
- **FR-004** No route handler's parameter list changes. H2-H11 keep their exact signatures
  (`OverlayDesignerRouteTableCharacterisationTests` and `SystemVariablesRouteTableCharacterisationTests`
  pin them). No file under `src/SystemVariables` changes.
- **FR-005** Route templates, verbs, names, summaries, tags, policies, status codes, response
  bodies, the OpenAPI shape and log text are unchanged. The new record is `private`, nested in
  `OverlayElementParsing`, and is neither a handler parameter nor a wire type.
- **FR-006** No file under `tests/` changes after the characterisation commit `C0`. No suppression,
  threshold, `.editorconfig`, `SonarLint.xml`, `Directory.Build.props` or csproj change. No new or
  renamed file under `src/*/Api` (the guards pin `EndpointFileCount = 16`; the record lives in
  `OverlayElementParsing.cs`).
- **FR-007** Each commit builds on its own and leaves the §4 suite green (ADR-0087).

## 4. Characterisation, the safety net

**What exists.** `tests/Architecture.Tests/OverlayDraftEndpointCharacterisationTests` (spec 366, 22
facts, in-process over `OverlayDesignerEndpointHost`, real command handlers, mocked repository)
drives both routes, but for shapes it pins only `Kind` and `Color` (`A_valid_edit_replaces…`
checks an Ellipse's kind and colour; the unkeyed create checks a Box's kind and colour), with
**symmetric geometry** (`Element(...)` uses X = Y = 0.1, W = H = 0.2). Its only element refusal is an
unknown kind. `tests/Integration.Tests/OverlayDesigner/OverlayElementValidationIntegrationTests`
(Aspire, shard 2) pins the Box-with-text and Box-with-fontSizePx refusals, but only as
`detail.ShouldContain("'text'")`, create only, Box only. `OverlayRevisionLifecycleIntegrationTests`
round-trips an Ellipse with symmetric values (0.3/0.3/0.2/0.2). Domain tests
(`OverlayElementTests`, `OverlayElementConstructionCharacterisationTests`, `NormalizedPositionTests`,
`NormalizedSizeTests`) pin the factories, not the Api mapping.

**Not covered anywhere (the gaps T000a closes):** each of X, Y, Width, Height of a parsed shape
independently (a transposition X<->Y or Width<->Height, or position<->size component, is invisible to
every existing test); Ellipse versus Box on create; per-element value mapping in a mixed set; the
exact refusal bodies for a shape with text / fontSizePx / both, on both routes and both shape kinds;
the precedence of geometry and colour over the shape text check.

| Changed | Covered by (in-process, `tests/Architecture.Tests`) | Covered by (Aspire, CI shard 2 authoritative) |
|---|---|---|
| `ParseShapeElement` and its one call in `ParseElement` | **to add (T000a)**, into the existing `OverlayDraftEndpointCharacterisationTests`: the §2 scenarios on both routes | `OverlayElementValidationIntegrationTests`, `OverlayRevisionLifecycleIntegrationTests`, `OverlayLifecycleIntegrationTests` |
| every handler signature (must not move) | `OverlayDesignerRouteTableCharacterisationTests`, `SystemVariablesRouteTableCharacterisationTests` | - |
| the rest of the parsing file (unchanged; regression net) | the existing 22 facts of `OverlayDraftEndpointCharacterisationTests` | `OverlayGeometryValidationIntegrationTests`, `OverlayElementCheckConstraintIntegrationTests` |

Commands are in plan §4.

## 5. Guard constraints and decisions

**D-1. Ten hits are out of reach; the scope shrinks to H1.** OverlayDesigner and SystemVariables
both have a route-table baseline (`OverlayDesignerRouteTableCharacterisationTests`,
`SystemVariablesRouteTableCharacterisationTests`) rendering each handler's parameter list literally
via `RouteTableRendering`. Every counted parameter of H2-H11 is a request-bound value or an existing
`[AsParameters]` bundle; moving any of them edits that baseline: a **block** (ADR-0144). Bundling
`[FromServices]` parameters removes nothing S107 counts (spec 380 §1). H1 is private, invisible to
the route table, and named by no test (`grep -rn ParseShapeElement tests/` is empty; the
`ParseElement` in `OverlayPushIntegrationTests` is that test's own local JSON helper).

**D-2. The guards, checked one by one (all must pass unmodified; any red is a block).**

| Guard (`tests/Architecture.Tests`) | Reads | Reacts to this spec? |
|---|---|---|
| `OverlayDesignerRouteTableCharacterisationTests` | each handler's parameter list, literal baseline | No: no handler changes (FR-004). |
| `SystemVariablesRouteTableCharacterisationTests` | same, SystemVariables | No: nothing in SystemVariables changes. |
| `PreconditionDeclarationTests` | every `*.cs` under `src/*/Api` (class index, mappings), `*Endpoints.cs` count 16, `If-Match` helper calls in handler bodies | No: no file added/renamed; `OverlayElementParsing.cs` holds no mapping and no `If-Match` call. A nested record struct adds a type declaration to the class index, which is used only to resolve mappings. |
| `EndpointScopeDeclarationTests` | `*Endpoints.cs` count, mapping count, scope on chains | No: chains untouched. |
| `IdempotentKeyReuseDeclarationTests` | 422 declaration ⇔ `IdempotentRequest.Execute*` in body | No: no handler body changes. |
| `RouteValueRefusalDeclarationTests` | 400 producers in handler bodies ⇔ declared 400; mappings in every Api `*.cs` | No: the helper throws `ArgumentException`, mapped to 400 by `BoundaryParse` in the handlers, unchanged; no producer moves. |
| `StatusProducerDeclarationTests` | status producers in handler bodies ⇔ declared statuses | No. |
| `ConcurrencyConflictDeclarationTests` | 409 producers / mutating mappings | No. |
| `HandlerDeconstructionTests` | `Handle`/`HandleAsync` signatures | No: not a `Handle` method (the destructure-first convention is followed anyway, plan §2.1). |
| `BoundaryTests` | assembly references | No: the record uses only `OverlayDesigner.Domain.Overlay` types already imported. |
| `PrimitiveBoundaryTests` | Domain models | No: Domain untouched. |

**D-3. Characterisation is written first, by the test-writer, and observed green on unchanged
code.** The existing tests cannot tell X from Y or Width from Height (symmetric values) and never
assert a shape's geometry, so a refactor resting on them would be a rewrite (CLAUDE.md). T000a adds
the §2 scenarios to the **existing** class (no new class), each value **distinct** from every other
value in the same request, so that any swap of two same-typed values changes an assertion. Committed
as `C0` before any production change; green output quoted in the PR.

**D-4. Bundle the three values that travel together; keep `kind` and `request` separate.** Position,
size and colour are the three arguments every `OverlayElement` factory takes, in that order, and
they travel together from `ParseElement` to the factory. Rejected: (a) moving `RequireNoTextFields`
up into `ParseElement` so the helper drops `request` (4 parameters, no new type) - it relocates the
FR-004 guard away from the doc comment that explains it and restructures `ParseElement`'s ternary,
a larger diff in the validation-order hot spot; (b) bundling `kind` with `request` - pairs the raw
wire record with its parsed kind, which invites reading `request.Kind` again; (c) also routing
`ParseTextElement` through the record - it is at 4, not a hit (ADR-0036).

**Transposition risk.** The record's three fields have three **distinct** value-object types
(`NormalizedPosition`, `NormalizedSize`, `OverlayColor`), so a positional swap in construction or
deconstruction is a compile error. The real same-typed risk is one level down and **not changed by
this spec**: `NormalizedPosition.From(request.NormalizedX, request.NormalizedY)` and
`NormalizedSize.From(request.NormalizedWidth, request.NormalizedHeight)` take two `decimal`s each,
and `NormalizedPosition(decimal X, decimal Y)` / `NormalizedSize(decimal Width, decimal Height)` are
positional records. T000a pins all four independently anyway, because those lines sit two lines above
the edited call and an engineer reshaping the call is the likeliest person to disturb them.

## 6. Out of scope, and the decision handed back

- **H2-H11 (ten route handlers): blocked, owner decision needed**, exactly as spec 380 §6 posed it:
  (a) an ADR-0084 scope note that a minimal-API handler's request-bound parameters are structural;
  (b) an approved re-capture of the two route-table baselines with request-value `[AsParameters]`
  records, proven OpenAPI-neutral by a generated-document diff (a contract-surface change, not a
  refactor); (c) leave them advisory. The PR uses `Refs #2973`, not a closing keyword.
- `ParseTextElement` (at 4) and every other ADR-0084 metric in these files.
- Hits outside `src/(OverlayDesigner|SystemVariables)/Api`.

## Latency budget

N/A. Overlay **authoring** (operator-console create/edit of a draft) is off the event-to-overlay
path (constitution §IV); the change adds one stack-allocated record struct per shape element parsed.
