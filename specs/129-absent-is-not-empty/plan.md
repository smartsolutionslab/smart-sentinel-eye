# Plan 129 — Absent is not empty

## The shape, and why

`Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(string parentPath, CancellationToken cancellationToken)`

Three states, and the interface already promised all three — it just had a
return type that could only spell two:

| State | Value |
|---|---|
| The parent group is not in the realm | `Option.None` |
| The parent group is there; these are its children (possibly none) | `Option.Some(names)` |
| Could not tell — unreachable, 403, 5xx | throws (unchanged) |

**Why `Option<T>` and not a small result type.** A result type would carry a
third case, and the third case is already a throw by the interface's own
documented contract. Giving the one caller a `CouldNotTell` arm nothing can
produce is a state it must handle and can never reach — the "assertion
nothing can fail" defect, moved into a type. The question here has exactly
two answers, and `Option<T>` spells exactly two. It is also the type
ADR-0141 and ADR-0048 name for this, and it already lives in
`Shared.Kernel`, so nothing new is introduced.

## ADR-0141's Application/Infrastructure split — how it resolves

ADR-0141 prefers `Option<T>` in Domain and Application, and exempts
Infrastructure and Api "because they translate wire and framework shapes
where `null` is the native vocabulary". The interface is
`Identity.Application`; the implementation is `Identity.Infrastructure`. The
split reads as a conflict only if the exemption is taken to govern the
*contract*.

It does not. **The exemption is about the vocabulary Infrastructure may use
in its own signatures and its own internals — not a licence to choose the
shape of a contract another layer owns.** `IKeycloakAdminClient` is declared
in Application; Application decides what the answer means.
`HttpKeycloakAdminClient` is precisely the translation point the exemption
describes, and here it translates `HttpStatusCode.NotFound` — the wire's way
of saying absent — into `Option.None`, Application's way of saying it. The
translation runs *toward* the contract's vocabulary, which is the exemption
doing its job rather than being overridden.

Put the other way: if Infrastructure's exemption could set the shape of a
contract, every Application interface with an Infrastructure implementation
would inherit the wire's vocabulary, and ADR-0141's preference would apply
to nothing that crosses a boundary — which is all of it.

## Commit sequence, and the two colours

Three commits, each building on its own (ADR-0087 rebase-merge — a commit
that compiles only with its successor breaks `git bisect` forever).

**C1 `refactor` — behaviour-preserving, observed GREEN.** The shape change
alone: the interface, the 404 arm of the implementation, and all five fake
implementations across four test files. `KeycloakProvisionedFabSource` maps
`None` and `Some([])` to the **same existing hedged message**, so no
behaviour moves. The six `KeycloakProvisionedFabSourceTests` and three
`MigrationRunTests` are the characterisation net and must pass
**unmodified** — an assertion that had to be edited would be evidence the
behaviour moved (CLAUDE.md, refactors stay green).

**C2 `test` — behaviour-changing, observed RED.** The discriminating test,
plus contract pins one layer down.

**C3 `fix` — GREEN.** Three verdicts.

## Why C1 comes before C2, and what that costs the red

A test that can *express* "the `/fabs` group is absent" needs a type that
can express it, so no test of this change compiles against the current
signature. The alternative ordering — tests first — produces a commit that
does not build, which the rebase-merge rule forbids outright.

So the red is bought a different way, and the cost is stated rather than
hidden:

- **The caller-level red is a genuine runtime assertion failure** against
  C1's tree, because C1 deliberately leaves the two causes sharing one
  message. That is SC-001, and it is the red that matters — the message is
  the whole observable outcome.
- **The Infrastructure contract pins go green in C1**, since C1 is what they
  pin. They are therefore proved by **counterfactual** instead — the repo's
  own technique, used by `95a2cf4f` in this very test file: the 404 arm is
  reverted to `Some([])` on a throwaway copy of the tree and both must go
  red there. Recorded in `verification.md`. Anything less would be a ninth
  cannot-fail assertion, and this session has already seen eight.

## Files

| File | Change |
|---|---|
| `src/Identity/Application/KeycloakAdmin/IKeycloakAdminClient.cs` | return type; the doc paragraph that is currently false |
| `src/Identity/Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.cs` | 404 goes to `None`; success to `Some` |
| `src/MigrationRunner/KeycloakProvisionedFabSource.cs` | three verdicts |
| `tests/Identity.Application.Tests/Fakes/FakeKeycloakAdminClient.cs` | fake |
| `tests/Identity.Infrastructure.Tests/Fakes/EnrolledKiosksKeycloakAdminClient.cs` | fake |
| `tests/Identity.Infrastructure.Tests/Fakes/UnreachableKeycloakAdminClient.cs` | fake |
| `tests/MigrationRunner.Tests/KeycloakProvisionedFabSourceTests.cs` | two inline fakes; **one test added, none edited** |
| `tests/Identity.Infrastructure.Tests/KeycloakAdmin/FabGroupTreeTests.cs` | new — the contract pins |

`src/Shared.Kernel`, `src/Shared.Contracts` and `src/AppHost/AppHost.cs` are
contention files (ADR-0109) and are **not touched**. `Option<T>` is consumed
as it stands.

## Risks

| Risk | Mitigation |
|---|---|
| The shape change quietly alters a fake's meaning | Each fake's mapping is justified one by one in `verification.md`; two of the three named fakes only throw, and the third's `SubGroups` dictionary is read by no test at all |
| The new verdicts read as one, as they did before `95a2cf4f` | The added test asserts the discriminator across all three, not the prose |
| Someone later collapses 404 back to `Some([])` | The Infrastructure pins go red; proved by counterfactual |
| Declining the wait is mistaken for skipping work | The evidence is in `spec.md`, quoted from the commits that removed it |

## Out of scope

- Restoring the wait (declined, with evidence in `spec.md`).
- `Option<T>`'s own docstring, which still says "NRT is disabled at the
  solution level" and has been stale since ADR-0141. `Shared.Kernel` is a
  contention file; reported, not touched.
- The `FabGroupParentMissing` warning, which stays. It is Infrastructure
  saying what it saw at the point it saw it, and is now accompanied by a
  return value that says the same thing to the caller.
