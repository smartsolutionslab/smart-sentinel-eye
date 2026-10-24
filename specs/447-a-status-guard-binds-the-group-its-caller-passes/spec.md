# Spec 447: A status guard binds the group its caller passes

**Issue:** #3127 (feature-level, on Project #13) · **Branch:** `test/3127-sibling-guards`
**ADRs:** ADR-0139 (rules that fail the build — these guards are two), ADR-0054 (hand-written in-file fixtures),
ADR-0053 (test naming), ADR-0036 (smallest change), ADR-0144 (lane). Constitution §Testing (red for new behaviour,
characterisation for what must not move).
**Specs built on:** 130 (`StatusProducerDeclarationTests`), 091 (`RouteValueRefusalDeclarationTests`), 190 (shared
`SourceMask` / `RouteChainReader`), 444 (call-site binding in `EndpointScopeDeclarationTests`, the pattern ported here).
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** **red** for the new binding in `StatusProducerDeclarationTests`;
**characterisation** for its 33 existing cases and for `RouteValueRefusalDeclarationTests`' 21 (plan §"Phase 4a").
**Security-sensitive:** no runtime change; `security-reviewer` optional. **Latency budget:** N/A — test code only.
**New ADR needed:** no (plan §"Why no ADR").
**Written 2026-10-24 against `develop` at `9b8b33a3`.**

---

## Premise, checked at `9b8b33a3`

1. **StatusProducer binds by name, and the binding decides a verdict.** `Mappings`
   (`StatusProducerDeclarationTests.cs:866-868`) picks the group whose `Variable` equals the mapping's receiver;
   `Classify` (`:886-909`) then credits that group's `RequireAuthorization`/`AllowAnonymous`. A helper parameter
   `RouteGroupBuilder reads` is credited with the local *named* `reads`, whatever its caller passes. **Holds.**
2. **Demonstrated, not reasoned — StatusProducer stays green over an open route.** In
   `src/EventIngestion/Api/EventsEndpoints.cs` the group `writes` (`:19`) carries **no** `RequireAuthorization`; `reads`
   (`:24-26`) carries `Scope.Sse.Events.Read`. `MapListEvents(reads)` (`:28`) was moved under `writes` as
   `MapListEvents(writes)`; `GET /events` then has no authorization at all (its chain declares none, `:100-107`). Running
   `StatusProducerDeclarationTests|RouteValueRefusalDeclarationTests|EndpointScopeDeclarationTests`:
   **`Failed: 1, Passed: 175`** — the one failure is `EndpointScopeDeclarationTests.Every_mapping_in_an_endpoint_file_is_read_rather_than_skipped(EventsEndpoints.cs)`
   (spec 444's fix). **All 33 StatusProducer cases stayed green**: L5 (`Every_mapping_is_either_authorized_or_explicitly_anonymous`)
   classified the now-open route as `Authorized`. Reverted (`git checkout -- src/`).
3. **RouteValueRefusal: the defect does not reach a verdict.** `PrefixOf` (`RouteValueRefusalDeclarationTests.cs:745-750`)
   binds by name, but its only consumer is `RouteMapping.FullRoute` in the G1 failure *message* (`:600`). G1-G4 read the
   mapping's own chain, its handler body and the groups' own chains — never the binding. The same planted move left its
   21 cases green, **correctly**: the route's 400 obligation does not depend on its group. Further, every file with two
   groups gives both the same prefix (`/rules`, `/cameras`, `/events`, `/devices`, `/kiosks`), so on today's corpus a
   misbinding cannot even change the message. The issue's "same defect" holds for the mechanism, not the consequence.
4. **The corpus already agrees with call-site binding.** Spec 444 premise 3 measured every in-file helper called once
   with an argument spelled like its parameter, and `EndpointScopeDeclarationTests` is green with call-site binding over
   the same 16 files and 71 mappings. Call-site binding in StatusProducer is therefore expected green on the corpus.
5. **Runtime backstop exists.** Each context's `*RouteTableCharacterisationTests` baseline records `authorize policy=`
   per route, and spec 444's guard now catches the planted shape. This spec still fixes StatusProducer: a guard that
   reports an open route as authorized is false evidence (same reasoning as spec 444 premise 5).
6. **Out of scope, recorded:** StatusProducer's file-wide `FirstOrDefault` also binds a group name declared twice in one
   file to the first declaration (EndpointScope refuses that shape in every `*Endpoints.cs`, and all 71 mappings live in
   those files, so it cannot occur unrefused today). `ConcurrencyConflictDeclarationTests.PrecedingGroupPrefix`
   (`:1037`) binds a prefix positionally — message/route text only, same identical-prefix argument as premise 3.

## User stories

### US1 (P1) — StatusProducer credits a helper's mappings with the group its caller passed

As the maintainer relying on `StatusProducerDeclarationTests`, I want a mapping written on a helper's parameter to be
classified by the group passed at the helper's call site, and to be left unclassified (red) when that cannot be
established, so the guard can never report an open route as authorized.

**Why P1 / only code story:** it is the verdict-bearing half of the issue. Testable through fixtures fed to the
existing `RouteGroups` / `Mappings` readers.

#### Acceptance scenarios

```gherkin
Scenario: happy (control) — a helper called with the group its parameter is named after
  Given "var reads = app.MapGroup(\"/b\").RequireAuthorization()"
  And a helper "MapX(RouteGroupBuilder reads)" mapping "reads.MapGet(\"/\", H)" with no authorization of its own
  And the only call "MapX(reads)"
  When the surface is read
  Then the mapping is Authorized with full route "/b/"

Scenario: conflict — the parameter's name and the caller's argument disagree (the planted shape)
  Given groups "writes" ("/a", no authorization) and "reads" ("/b", RequireAuthorization)
  And a helper "MapX(RouteGroupBuilder reads)" mapping "reads.MapGet(\"/\", H)"
  And the only call "MapX(writes)"
  When the surface is read
  Then the mapping is Unclassified with full route "/a/"

Scenario: conflict — one helper called with two different groups
  Given the calls "MapX(writes)" and "MapX(reads)"
  Then the mapping is Unclassified

Scenario: bad request — the argument is not a group declared in the file
  Given the only call is "MapX(app.MapGroup(\"/c\").RequireAuthorization())"
  Then the mapping is Unclassified

Scenario: bad request — the helper is never called in the file
  Given "MapX(RouteGroupBuilder reads)" with no call site
  Then the mapping is Unclassified rather than bound to the local named "reads"

Scenario: auth — a parameter is never bound by name
  Given a method "MapY(RouteGroupBuilder reads, string label)" mapping on "reads", called "MapY(reads, \"x\")"
  And an authorized group local named "reads"
  Then the mapping is Unclassified (no group credited)
```

#### Edge cases

- A mapping on a group local in the method that declares it binds exactly as today.
- Anonymity/authorization on the mapping's own chain still wins over any group (`Classify` order unchanged).
- A mapping on `this IEndpointRouteBuilder app` keeps today's behaviour (no group).
- A helper calling another helper with its own parameter is unresolved (Unclassified) — no transitive resolution,
  as in spec 444.

### US2 (P2) — RouteValueRefusal says what its group binding is for

As the next reader of `RouteValueRefusalDeclarationTests`, I want `PrefixOf`'s doc to state that it binds by name, is
used only to word the G1 failure, and must not be used for a verdict without call-site binding (spec 444/447), so the
diagnostic shortcut is not later promoted into a rule.

#### Acceptance scenario

```gherkin
Scenario: the change is comment-only
  Given RouteValueRefusalDeclarationTests.cs before and after
  When comments are stripped and the code hashed
  Then the hashes are equal, and the 21 cases pass unmodified
```

## Functional requirements

- **FR-001** In `StatusProducerDeclarationTests`, a mapping whose receiver is a route-builder parameter
  (`IEndpointRouteBuilder|WebApplication|RouteGroupBuilder`) of an enclosing method MUST NOT be bound to a group by name. A group local binds only inside the block that declares it and not once the name
  is redeclared (parameter, lambda parameter, out variable).
- **FR-002** For a method with exactly one parameter, typed `RouteGroupBuilder`, a mapping on that parameter MUST be
  bound to the group passed by every in-file call site, when there is ≥1 call, each passes one bare identifier, and all
  name the same group local of that file. The bound group supplies both classification and prefix.
- **FR-003** Every other case in FR-002's population MUST bind no group (→ `Unclassified` unless the chain itself
  decides), so L5 fails; L5's message MUST name an unresolved helper binding as a cause.
- **FR-004** The 33 StatusProducer and 21 RouteValueRefusal cases MUST pass unmodified; `RouteHandlerMappingCount = 71`
  and `EndpointFileCount = 16` MUST not move.
- **FR-005** StatusProducer's class doc (`:62-70`, "the MapGroup statement whose bound variable is the mapping's
  receiver") MUST describe call-site binding and block scoping. US2's doc edit MUST be comment-only.

## Success criteria

- **SC-001** Premise 2's planted move turns `StatusProducerDeclarationTests` red at L5, naming `GET /events/` (quoted).
- **SC-002** The five non-control fixture facts are observed red on `9b8b33a3`'s reader and green after (quoted).
- **SC-003** Full `Architecture.Tests` green; no `src/` file changed; `RouteValueRefusalDeclarationTests.cs`
  comment-stripped hash unchanged.

## Independent end-to-end test procedure

1. `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests"` — green (33 + 21 + new facts).
2. Scratch edit premise 2 (`MapListEvents(writes)`); re-run: L5 red naming `GET /events/`. Revert.
3. Scratch edit: add a second call `MapListEvents(writes);` beside `MapListEvents(reads);` — L5 red (two groups). Revert.
4. `git diff --stat src/` empty.

## Locked choices

Source-scan guards in `tests/Architecture.Tests` (ADR-0139); shared masker/reader from spec 190, unchanged; xUnit +
Shouldly; raw-string fixtures (ADR-0054); no new test class (no `ci-shards` filter entry); binding **ported** into
StatusProducer, not extracted (plan §"Port, not extract").

## Out of scope

Extracting a shared binder across the three guards; StatusProducer's duplicate-name binding (premise 6);
`ConcurrencyConflictDeclarationTests`' positional prefix (premise 6); cross-file and transitive helpers; any `src/`
change.
