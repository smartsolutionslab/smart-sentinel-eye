# Plan 165 — Wiring that matches its consumer

**Spec:** `spec.md` · **Issue:** #2405 · **ADR:** 0152 (secondary: 0108, 0109, 0037, 0144)

---

## 1. Bounded context and layers — **none**

This change touches **no bounded context**. `src/AppHost/` is the Aspire
composition root: it has no Domain, no Application, no Infrastructure and no Api
layer, holds no aggregate, and is exempt from the guard set that governs those
layers.

Recorded explicitly because a plan with an empty context section looks like an
omission:

| Normally-applicable rule | Status here | Why |
|---|---|---|
| §II primitive ban / `PrimitiveBoundaryTests` | **Not engaged** | No domain model. The file is composition, and its "primitives" are resource names and environment-variable keys. |
| No cross-context project references (NetArchTest) | **Not engaged** | AppHost references every context by design — that is its job. |
| `Ensure.That(…)` argument guards (ADR-0105) | **Not engaged** | AppHost is a named exemption in CLAUDE.md's guards row. |
| `Option<T>` preference (ADR-0141) | **Not engaged** | Scoped to Domain and Application. |
| Handler destructuring | **Not engaged** | No handler. |
| Coverage gates (ADR-0065) | **Not engaged** | Domain/Application/Shared only; AppHost is in neither. |
| Code metrics (ADR-0084) | **Engaged, trivially** | The diff removes four lines and rewrites two comment sentences. `AppHost.cs` is already long; this makes it shorter. |

## 2. Entities, value objects, invariants — **none**

No type is added, removed or changed. The diff is four `IResourceBuilder`
method calls and a comment.

## 3. Messaging (domain → integration event) — **none**

No domain event, no integration event, no `Shared.Contracts` change. The
SignalR push this wiring nominally concerned is emitted by
`LayoutComposition.Application` via `ILayoutLifecycleBroadcaster` and is
untouched; `layout-composition`'s own resource declaration
(`AppHost.cs:388-404`, including `.WaitFor(rabbitmq).WaitFor(keycloak)`) is
untouched.

## 4. Boundary rules — **unchanged**

No project reference is added or removed. `SmartSentinelEye.slnx` is untouched.
The AppHost→context references stay as they are; only three *resource-builder*
calls on an npm-app resource go, which are composition metadata, not compile-time
references.

---

## 5. The change, line by line

Line numbers are at the branch tip; the engineer must match on **content**, not
number.

### 5.1 Deletions — `src/AppHost/AppHost.cs`, the `management-web` call only

```
 builder.AddNpmApp("management-web", "../../apps/management-web", "dev")
     .WithHttpEndpoint(env: "PORT", port: 5173, isProxied: false)
     .WithReference(apiGateway)
     .WithEnvironment("VITE_API_GATEWAY_URL", apiGateway.GetEndpoint("http"))
     .WithReference(keycloak)
     .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("http"))
-    .WithReference(layoutComposition)                                              // :579
-    .WithEnvironment("VITE_LAYOUT_HUB_ORIGIN", layoutComposition.GetEndpoint("http")) // :580
-    .WaitFor(layoutComposition)                                                    // :581
     .WithExternalHttpEndpoints()
     .WithParentRelationship(apiGateway);
```

**Three lines. Nothing else in that chain moves.** `.WithReference(apiGateway)`,
`.WithReference(keycloak)` and both surviving `WithEnvironment` calls stay —
they are the app's live wiring (spec §2.1's false-positive note).

### 5.2 The comment block — `AppHost.cs:542-572`

The block is **shared by all three `AddNpmApp` calls** and sits above the first.
Two passages become false on deletion and must be corrected in the same commit:

1. The `VITE_LAYOUT_HUB_ORIGIN` paragraph's parenthetical, which says
   management-web's "reference below is dead wiring, tracked by #2405". After
   this change there is no reference below to be dead. It should say that
   management-web has no hub wiring **because** it has no proxy — keeping the
   *fact* (which a reader still needs, to understand why two of three calls
   carry the alias and one does not) and dropping the defect report.
2. The closing `WaitFor(layoutComposition) is ordering hygiene …` sentence,
   which now governs only the two calls below it. It must read as a statement
   about the kiosk instances, not about every app under the block.

**Everything else in the block stays verbatim.** The POSIX-shell hyphen
explanation, the spec 011 FR-006/FR-010 reference and the gateway/CORS/Keycloak
paragraphs are hard-won and remain true for the kiosk — the same instruction
spec 164's plan gave at row 7 of its comment table, and for the same reason.

### 5.3 Nothing else

No `apps/**` file. No `tests/**` file. No workflow, script, doc or ADR.
`specs/165-*/` is this artefact set and is created by phase 1-3, not phase 4.

---

## 6. Risk table

| Risk | Likelihood | Mitigation |
|---|---|---|
| **The engineer edits the kiosk calls too** — they are byte-identical three-line runs, twelve and thirty-eight lines below the target | **High. This is the dominant risk of the change.** | §5.1 pins the target by its enclosing `AddNpmApp("management-web", …)` call. Spec §5.3 step 2 requires kiosk-web and kiosk-wall to **still** show Waiting, and step 4 requires `/hubs/layouts/negotiate` to answer 200 — a kiosk edit fails both. `git diff` must show exactly three deleted `.With*`/`.WaitFor` lines. |
| **The comment block is over-edited**, taking the POSIX-shell paragraph with it | Moderate | §5.2 names what stays. Spec 164 gave the identical instruction; this plan repeats it rather than assuming it carried. |
| **The comment block is not edited at all**, leaving prose that describes deleted lines | Moderate — the diff looks complete without it | It is a listed task (T005), not a discretionary tidy. This is the precise defect spec 164 existed to remove; reinstating it in the same file would be ironic and is a phase-6 blocker. |
| **A model-shape guard is written** asserting the annotation's absence | Moderate — phase 4a's colour is red and an engineer will look for a red to produce | Spec §5.1 argues it out by name, and §8.3 forbids it in terms. If a reviewer wants one, it is argued in phase 6, not added in phase 4. |
| **management-web starting before Keycloak degrades the dev boot** | Low, and the change's only real cost | Spec §3.4 records it as accepted and §5.3 step 3 requires it to be *looked at* rather than assumed. A failure there is a finding to file — not a licence to add `.WaitFor(keycloak)` in this diff. |
| **Two Aspire stacks at once during §5.3's before/after** | Moderate — the procedure boots twice | One machine, one stack: shut the first down fully before booting the second. A concurrent boot produces `FailedToStart`, which reads exactly like a code defect. |
| **The `before` observation is skipped** because the `after` looks fine | Moderate | Without it there is no evidence the wait ever did anything, and the PR's central claim becomes unfalsifiable. T003 is a blocking task. |
| **CI's e2e job is read as proof the ordering change is safe** | Moderate | It is not: `scripts/wait-for-e2e-stack.sh` gates on migrations, then the three ports, then a gateway 401 — it would pass identically either way (spec §3.2). It proves nothing broke; §5.3 proves what changed. |

---

## 7. Sequencing and parallelism (ADR-0109)

**No task is marked `[P]`.** Every code task claims the same file —
`src/AppHost/AppHost.cs` — and the observation tasks claim the same singleton
resource, the machine's one Aspire stack. ADR-0109's disjoint-file basis does not
hold anywhere in this plan.

There is no foundational/fan-out shape to call out for the orchestrator: this is
one engineer, one file, strictly sequential.

## 8. Phase-gate notes

- **Phase 3 gate:** the feature-level issue **#2405** must be on Project #13.
  Per-task issues are not created (CLAUDE.md, post-spec-028 practice).
- **Phase 4a:** behaviour-changing colour, discharged without a new test —
  spec §5.1 and §8.3. The engineer receives the baseline output and the
  `before` observation as its brief.
- **Phase 5:** spec §5.3's `after` half, plus the manual browser checks.
  §IV latency is **N/A** and the verification note must say so rather than
  omit it.
- **Phase 6:** `/code-review`. Not security-sensitive — no endpoint, scope,
  token, secret or trust boundary is touched — so `/security-review` is skipped
  with that one-line reason in the PR body.
