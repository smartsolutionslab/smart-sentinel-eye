# Plan — Spec 072, a precondition that declares itself

**Phase:** 2 (Plan) · **Spec:** `spec.md` · **Issue:** #2088
**ADRs:** ADR-0113, ADR-0119, ADR-0070, ADR-0139, ADR-0084, ADR-0052, ADR-0103,
ADR-0109, ADR-0144.

## Shape of the change

Two halves, in a fixed order.

1. **One new guard** — `tests/Architecture.Tests/PreconditionDeclarationTests.cs`.
   Reads source, resolves each route mapping to its handler, and asserts the
   `If-Match` ⇄ 428 biconditional.
2. **Twelve fluent lines** in three `src/*/Api` files, plus one comment each
   explaining the pair — the same one-sentence justification
   `CameraEndpoints.cs:68-70` already carries.

**No bounded context is entered.** There is no Domain, Application or
Infrastructure work, no entity, no value object, no invariant, no message, no
migration, no Aspire resource, and no frontend. The Api layer is touched only in
its *metadata* — the fluent `ProducesProblem` chain, which is registration-time
endpoint description, not request-handling code.

That is why the usual plan sections are thin, and saying so is more useful than
filling them: this spec adds a build-time rule over an existing convention and
the twelve declarations that rule forces.

## Boundary rules (ADR-0109, NetArchTest)

Nothing crosses a context boundary. `Architecture.Tests` already references
every `Api` project to read their source — it globs files, it does not link
against the endpoint types, so no new project reference is added and
`BoundaryTests` sees no change. The three edited `src/*/Api` files gain no
`using`: `StatusCodes` and `ProducesProblem` are already in scope in all three,
which is verifiable from the sibling declarations that already use them.

## Where the guard lives, and why it is its own file

**A new file, not an extension of `EndpointScopeDeclarationTests`.**

That file is **1827 lines** — the brief's "~1250" is out of date, which is itself
the reason to check rather than assume. `S104` is in the test projects' `NoWarn`
list (`Directory.Build.props:108`), so length is not a build argument. The
argument is cohesion and blast radius:

- `EndpointScopeDeclarationTests` answers one question — *does the prose name the
  scope the chain enforces?* Its whole machinery is about **the chain**: group
  binding by receiver variable, the reflected scope catalogue, two exemption
  registers. This guard asks a different question, about **the handler body**,
  and needs none of that.
- What it does need from that file is a **technique**, not a dependency: the
  `Map*` regex shape, `MaskLiterals` / `WithoutComments`, `StatementEnd`. Those
  are ~40 lines to restate, against the cost of making a 1827-line file that six
  other specs depend on into a merge hot-spot for a seventh.
- The precedent is `StaleCodeConventionTests` (147 lines, ADR-0119): a
  single-claim guard in its own file, reading source for a stated reason.

**Do not extract shared helpers into a third file in this spec.** That is a
refactor of a guard six specs depend on, it would put the two files' assertions
in one blast radius, and CLAUDE.md's smallest-possible-change rule says a fix and
a refactor are two changes. If the duplication becomes a problem it is its own
issue.

## Reading the mapping to its handler — the crux

The brief asks whether the mapping↔handler link can be made reliably. It can,
and the reason is specific rather than hopeful: **the handler is named at the
mapping site.** Every route in this repository is registered as

```csharp
group.MapPost("/{name}/publish", Publish)
```

— a route literal and a **method group**. The name the guard needs is the second
argument, in the same statement as the `Produces` chain it is judging. There is
no inference step.

Checked rather than assumed: extracting the second argument of every `Map*` site
under `src/*/Api` yields **bare identifiers and nothing else** —

```sh
grep -rhoE "\.Map(Get|Post|Put|Patch|Delete)\(\s*\"[^\"]*\"\s*,\s*[^)]{0,40}" \
  --include=*.cs src/*/Api | sed -E 's/.*",\s*//' | sort | uniq -c | sort -rn
```

Four facts establish that this is reliable here, each checked rather than assumed:

| Risk | Reality on `0f20dcd` | How the guard handles it |
|---|---|---|
| Handler is a lambda, so there is no name | **Zero** lambda mappings in `src/*/Api` — the shape does not occur | FR-009: unreadable ⇒ **fail**, never pass |
| Handler lives in another file | Normal, not exceptional: 10 of 17 call sites are in a `*.Commands.cs` partial while the mapping is in `*Endpoints.cs` | Resolve across every file declaring that class in the project |
| Two handlers share a name | Real and common: across `src/*/Api` the name `List` is used **9** times, `GetOne` 5, `Archive` 4, `Publish` 3; within a single project, `Identity/Api` has two `List` and two `Disable` in **different classes** | Resolve **within the declaring class**, which is what C# does; ambiguity ⇒ fail |
| Handler delegates the header read to a helper | **Zero** — all 17 calls are lexically inside the mapped method | FR-006's independent sweep count disagrees ⇒ **fail** |

**The fourth row is the one that makes the guard honest.** Resolution by
lexical body scan is the guard's real limit, and rather than being documented and
forgotten it is *cross-checked*: the guard counts `If-Match`-requiring endpoints
by walking mappings, and separately counts `ConcurrencyHeaders.TryRead*`
occurrences by sweeping files. When someone hoists a call into a helper, those
two numbers stop agreeing and the build fails — the reader does not silently
start returning the wrong answer. That is `PaginatedConsumerTests`' property, and
it is why the answer here is a guard rather than prose.

**Confidence: high on the link, deliberately narrow on the claim.** The guard
does not claim to know what a handler does; it claims to know whether a specific
helper is called inside a specific method body, and it fails loudly in every
shape where it cannot tell.

### Class scoping, concretely

Twelve endpoint classes exist across `src/*/Api`; four are partial across two or
three files:

```
EventsEndpoints          .cs / .Reads.cs / .Writes.cs
LayoutEndpoints          .cs / .Commands.cs / .Queries.cs
OverlayEndpoints         .cs / .Commands.cs / .Queries.cs
(the other nine are single-file)
```

Build the index once: for each Api project, map `class name -> [files]` from the
`public static (partial )?class X` declarations, then map `(class, method) ->
body` from the method signatures within those files. A mapping's declaring class
is the class whose brace range contains the `Map*` site. Then
`(declaring class, handler name)` resolves to exactly one body, or the guard
fails.

## Assertion inventory

Seven assertions, one per FR, each with its own failure message.

| # | FR | Claim | Today |
|---|---|---|---|
| A1 | FR-001/002 | Every mapping under `src/*/Api` resolves to exactly one handler body | passes |
| A2 | FR-009 | An unresolvable mapping fails, naming the shape | vacuous today; provable by mutation |
| A3 | FR-004 | Every `If-Match`-requiring mapping declares 428 | **red: 9** |
| A4 | FR-005 | No mapping declares 428 without requiring `If-Match` | passes (0) |
| A5 | FR-006a | Mappings found requiring `If-Match` == sweep for `TryRead*` | 17 == 17 |
| A6 | FR-006b | 428 declarations found == sweep for the constant | 8 before, 17 after |
| A7 | FR-008 | The corpus is exactly 17 endpoints across 7 files | pinned |

A3 is the one that starts red. A4 starts green **and must**: it is the control
that shows handler resolution is not failing open, because a resolver that
returned "requires nothing" for everything would turn all 8 correct declarations
into mirror violations.

## Failure messages

Two distinct messages, because they are different defects and the spec's
acceptance scenarios require a reader to tell them apart.

- **A3, omission** — *`{file}:{line} — {VERB} {route} (handler `{Name}`) calls
  {helper} and answers 428 when If-Match is absent, but its Produces chain does
  not declare Status428PreconditionRequired. The generated OpenAPI asserts a
  status this endpoint routinely returns cannot happen. See ADR-0113 Layer 1.*
- **A4, mirror** — *`{file}:{line} — {VERB} {route} (handler `{Name}`) declares
  Status428PreconditionRequired, but its handler reads neither
  TryReadExpectedVersion nor TryReadUpsertPrecondition. A declared precondition
  no handler enforces tells a caller to send a header that will be ignored.*

Both name file, line, verb, route and handler (FR-007). A message that a reader
has to open the file to act on has not done its job.

## The twelve declarations

Placed to match each file's existing ordering, so the diff reads as a
continuation rather than an addition.

**Nine 428s:**

| File | Endpoints |
|---|---|
| `src/Automation/Api/RulesEndpoints.cs` | `PublishRule`, `ArchiveRule` |
| `src/OverlayDesigner/Api/OverlayEndpoints.cs` | `PublishOverlayRevision`, `ArchiveOverlayRevision`, `BranchDraftOverlayRevision`, `EditDraftOverlayRevision`, `RevertOverlayRevision` |
| `src/SystemVariables/Api/SystemVariableEndpoints.cs` | `SetSystemVariableValue`, `ArchiveSystemVariable` |

**Three 409s**, on the three archives whose `*_STALE` refusal is undeclared:
`ArchiveRule`, `ArchiveOverlayRevision`, `ArchiveSystemVariable`.

**One comment per file, not per endpoint**, stating the pair once — following
`CameraEndpoints.cs:68-70`, which is the existing house style for this exact
explanation. Twelve copies of the same sentence is drive-by commenting; one
placed where a reader meets the group is the *why* CLAUDE.md asks for.

**Note the file split in OverlayDesigner.** The declarations go in
`OverlayEndpoints.cs` (the mapping), not `OverlayEndpoints.Commands.cs` (the
handlers). Same for LayoutComposition, which is why its five correct
declarations already sit in `LayoutEndpoints.cs`.

## What is deliberately not built

- **No guard on the 409/412 stale half.** Reaching the status would mean
  following the mapping to the handler, the handler to the command handler, the
  command handler to its error type, and the error type to its reachable
  variants — four hops across the Application boundary, for a value ADR-0119
  leaves legally variable. The three misses are fixed by hand and the limit is
  written into the spec.
- **No exemption register.** Spec 070 needed one because #2070 is open work with
  an issue number. Nothing here is legitimately exempt.
- **No change to `ConcurrencyHeaders`.** It is correct: 428 for absent, 400 for
  malformed, in one place, already unit-tested (`ConcurrencyHeadersTests`,
  `UpsertPreconditionTests`).
- **No ADR** (ADR-0144). See spec, *Does ADR-0113 need amending*.

## Risks

1. **The guard is written to pass rather than to check.** Mitigated by the
   phase-4a split: `test-writer` must produce a red naming exactly 9 endpoints
   across 3 files, with LayoutComposition's 5 **absent**. An absent
   LayoutComposition is the single cheapest proof that cross-file handler
   resolution works.
2. **A4 red on the unmodified tree.** Means resolution is failing open — treat
   as a defect in the guard, never as a finding about `src/`.
3. **Spec 071 lands first and moves the counts.** FR-008's pinned numbers would
   need re-measuring; the three grep commands in the spec do it in one line.
4. **Scope creep into the Layer 2 conflict.** ~28 endpoints, a central
   `IExceptionHandler`, and a genuinely different fix. It is named in the spec
   as out of scope with a reason, and it gets its own issue.
