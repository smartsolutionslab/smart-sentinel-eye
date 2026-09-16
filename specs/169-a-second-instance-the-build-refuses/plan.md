# Plan 169 — A second instance the build refuses

**Spec:** `specs/169-a-second-instance-the-build-refuses/spec.md`
**Issue:** #2404
**Decision of record:** ADR-0153 clauses 1 and 2.

---

## 1. Bounded context and layers — deliberately none

**This slice adds no bounded context, no aggregate, no value object, no entity,
no command, no query, no handler, no endpoint and no integration event.**

That is not an omission; it is the shape of the work. ADR-0153 clause 1 is a
property of the **composition root**, not of any domain. The artefact is one
test class that reads the AppHost application model, plus a comment in
`AppHost.cs`.

The usual plan sections are therefore answered as follows, explicitly rather
than by silence:

| Section | Content |
|---|---|
| Bounded context | None. `src/AppHost` is the composition root, not a context. |
| Domain / Application / Infrastructure / Api layers | Untouched in all nine contexts. |
| Entities, value objects, invariants | None. No `IValueObject<T>`, no `Identifier`, no primitive-boundary surface (constitution §II is not engaged). |
| Domain → integration event | None. No `Shared.Contracts` change, no `V<N>` message. |
| Persistence / migrations | None. No EF model, no `MigrationRunner` change. |
| Messaging | None. No Wolverine queue, handler or outbox change. |
| Concurrency / idempotency | N/A. No `POST`, no `If-Match`, no `Idempotency-Key`. |
| Auth / scopes | N/A. No endpoint, no `RequireScope`, no trust boundary. |

**Boundary rules hold trivially.** No cross-context project reference is
created. `tests/Integration.Tests` is already the **only** test project that
references `src/AppHost`, and the new class lives there, so NetArchTest's
boundary rules see no new edge.

---

## 2. Where the code goes

### 2.1 The guard — `tests/Integration.Tests/AppHostReplicaCountTests.cs` (new)

**Why this project and not `tests/Architecture.Tests`.** The guard must
*execute* the composition root, which means referencing
`Projects.SmartSentinelEye_AppHost` and pulling in the Aspire hosting and DCP
dependency graph. `Architecture.Tests` deliberately avoids that: its own
`IntegrationTestSelectionTests` explains that *"a project reference would drag
the Aspire hosting and DCP dependency graph into a project that today runs in
seconds with no Docker"*. `Integration.Tests` already carries that reference and
already hosts the model-composing precedent.

**Why it is nonetheless cheap.** `DistributedApplicationTestingBuilder.CreateAsync`
composes the model and starts nothing. `AppHostE2ESwitchTests` establishes this
and states it in its own doc comment. So the class belongs in `Integration.Tests`
by reference graph but in the **Docker-free CI step** by trait.

### 2.2 The pointer — `src/AppHost/AppHost.cs`, the `// HA (#1005)` comment only

One comment amendment recording that `apiGateway.WithReplicas(2)` is ADR-0153's
recorded clause-2 exception, that #2283 owns its fate, and that a guard pins it.
`WithReplicas(2)` and the `if (!isE2ETests)` gate are **not** changed — the
runtime shape of this slice is identical to today's.

---

## 3. Design of the guard

### 3.1 Mechanism

```
DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(args)
  → builder.Resources                       (IResource per composed resource)
  → resource.GetReplicaCount()              (ResourceExtensions; defaults to 1)
```

`ResourceExtensions.GetReplicaCount(IResource)` is the supported public accessor
and is documented as *"Gets the number of replicas for the specified resource.
Defaults to 1 if no `ReplicaAnnotation` is found."* Both it and
`ReplicaAnnotation` (with its `Replicas` property) are confirmed present in
`Aspire.Hosting` 13.5.3, the version pinned in `Directory.Packages.props`.

Prefer the accessor to poking `Annotations.OfType<ReplicaAnnotation>()`
directly: the accessor is the API the orchestrator itself uses, it already
encodes the default-to-1 rule, and it is correct if Aspire ever adds a second
way to set the count.

### 3.2 The three assertions

Two lanes, and a population gate that both share.

**(a) Run mode — the shape a developer and the Playwright e2e job boot.**

Arguments: the four `Parameters:*` values, **without** `E2ETests`. Expected:
the set of resources whose replica count exceeds 1 is **exactly**
`{ api-gateway → 2 }`.

Assert the whole set in one comparison rather than looping with individual
checks: a set comparison names every offender in one failure, and cannot pass by
checking nothing.

**(b) E2E-fixture mode — the shape the integration suite boots.**

Arguments: the same four plus `E2ETests=true`. Expected: **no** resource exceeds
1 replica, **and** `api-gateway` is present at exactly 1.

The second half is what makes this assertion mean something: "nothing above one"
is satisfied by an empty model.

**(c) The population gate — shared by both.**

Before the replica assertions: the composed model must be non-empty and must
contain `api-gateway`. A model that failed to compose, or a filter that matched
nothing, must **fail** rather than pass silently. This is the same defect
`IntegrationTestSelectionTests` guards against in its own scan, and the same
reasoning `AppHostE2ESwitchTests.BootLine` uses when it insists the absence of
its target line is itself a failure.

### 3.3 The liveness property, and why (a) asserts a positive

Assertion (a) does not merely permit `api-gateway`; it **requires** it at
exactly 2. That is deliberate and it is the guard's self-test:

- If the model failed to compose → empty set → the positive assertion fails.
- If `GetReplicaCount` were broken and always returned 1 → the positive
  assertion fails.
- If someone raises the gateway to 3 → fails, and #2283 is named.

A purely negative guard ("nothing above one") would pass in the first two cases
having observed nothing. The known-broken exception is thus load-bearing: it is
the only resource in the system whose count is above one, so it is the only
available witness that the guard can see a count above one at all.

Spec §5.4 records the consequence for whoever closes #2283.

### 3.4 Failure messages

Every assertion carries a message that routes the reader, because the person who
trips this guard is usually mid-incident and reaching for replicas as a remedy:

- Name the resource and the count observed.
- State that ADR-0153 clause 1 pins every service to one instance, and name the
  **mechanism** — that at least five of nine contexts hold per-instance state a
  second replica corrupts.
- State clause 3 as the **only** route to an exception: enumerate and resolve the
  context's per-instance state, and add a test that runs two instances and fails
  without the fix.
- For `api-gateway` specifically, state that its count is a recorded exception
  owned by **#2283** and must not be changed by an unrelated slice.

Cite `AppHost.cs` by quoted text (`// HA (#1005): …`), never by line number —
spec §2.1.

### 3.5 Conventions this class must follow

- `[Trait("Category", "FixtureLogic")]` on the class — mandatory, enforced by
  `IntegrationTestSelectionTests`, and only that exact spelling is credited.
- No `[Collection(AspireCollection.Name)]`.
- Sentence-style test names with underscores (ADR-0053).
- Shouldly assertions (ADR-0052). `ShouldBe` with a `customMessage` for the set
  comparisons.
- Collection expressions with explicit types — `string[] names = [..]` — not
  `new()` (constitution / `dotnet_style_prefer_collection_expression`, warning in
  Release).
- No leading underscore on private fields.
- `Path.Combine` over literal separators if any path is touched at all
  (backslash literals are green on Windows and red on Linux CI).
- SonarAnalyzer limits: ≤ 300 LOC/file, ≤ 30 LOC/method, complexity ≤ 10. Three
  test methods plus two small helpers stay far inside these.
- Argument guards: none needed — no public API is added.

---

## 4. What this plan deliberately does not do

| Not done | Authority |
|---|---|
| A SignalR backplane (Redis or bespoke RabbitMQ relay) | ADR-0153 clause 5; ADR-0071 and ADR-0088 on operational surface |
| Change `api-gateway`'s replica count, either direction | ADR-0153 clause 2 — #2283's decision, not this slice's |
| A startup assertion inside a service reading a configured replica count | ADR-0153 Implementation Notes name this as the weakest class of guard: it proves only that configuration was read |
| A source-text scan of `AppHost.cs` | Spec §5.1(A) — defeated by any equivalent spelling |
| A running-model / resource-snapshot check | Spec §5.1(C) — costs the 30-minute Docker job and cannot see the one replica count that exists, because the fixture boots with `E2ETests=true` |
| A scan over `deploy/` | Spec §5.3 — a guard whose subject does not exist, passing by matching nothing |
| Clause 3's two-instance harness | Spec §3.2 — the ADR says sizing it is its own slice |
| Fix per-instance state in any of the five contexts | Spec §3.3 |
| Edit ADR-0153 or the constitution | ADR-0144; §Availability already amended; PR #2417 already owns that file |

---

## 5. Risks

| Risk | Mitigation |
|---|---|
| The guard is built as a source scan and nobody notices | **CF-2** in spec §6 is mandatory and exists solely to catch this. It must be quoted in the PR. |
| The guard lands without the trait and runs only in the 30-minute Docker job | `IntegrationTestSelectionTests` fails the build for a class carrying neither declaration. Phase 5 additionally observes it in the `Category=FixtureLogic` step. |
| The guard passes green having composed nothing | The population gate (§3.2c) and the positive `api-gateway` assertion (§3.3). |
| Composing the model is slower or heavier than believed | `AppHostE2ESwitchTests` already composes it five times in the same CI step; this adds two more. If it proves slow, that is a finding about an existing test, not this one. |
| Building conflicts with the live Aspire stack (pid 3312) holding service binaries | Known hazard (MSB3027 reads as a broken build). Phase 4 must **report** a blocked build rather than stopping the stack. |
| PR #2417 conflicts | It edits `docs/adr/0153-*.md` only; this slice edits neither that file nor anything it touches. Disjoint. |

---

## 6. Verification (phase 5, forward reference)

1. The four counterfactuals of spec §6, each observed red, output quoted.
2. The guard green on unmodified `develop`.
3. CI: the guard observed running in the **Docker-free `Category=FixtureLogic`
   step** — the job name in the log, not merely a green run.
4. Latency: **N/A**, per spec §7. No leg cited because no leg is touched.
