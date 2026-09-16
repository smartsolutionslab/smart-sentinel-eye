# Plan 168 — A gateway that says what it waits for

**Spec:** `spec.md` · **Issue:** #2407 · **ADR:** 0106, 0153 (secondary: 0037, 0144, 0086)

---

## 1. Bounded context and layers — **none**

Same posture as plan 165 §1, and for the identical reason: `src/AppHost/` is
the composition root, has no Domain/Application/Infrastructure/Api layer, and
is exempt from the guard set that governs those.

| Normally-applicable rule | Status here | Why |
|---|---|---|
| §II primitive ban / `PrimitiveBoundaryTests` | **Not engaged** | No domain model touched. |
| No cross-context project references (NetArchTest) | **Not engaged** | No project reference changes; AppHost already references every context by design. |
| `Ensure.That(…)` argument guards (ADR-0105) | **Not engaged** | AppHost is a named exemption. |
| `Option<T>` preference (ADR-0141) | **Not engaged** | Scoped to Domain/Application. |
| Handler destructuring | **Not engaged** | No handler. |
| Coverage gates (ADR-0065) | **Not engaged** | Domain/Application/Shared only. |
| Code metrics (ADR-0084) | **Engaged, trivially** | The diff adds one comment block near an already-long file; no method or file grows in a way that risks the 300 LOC / complexity limits. |

## 2. Entities, value objects, invariants — **none**

No type is added, removed or changed. No `IResourceBuilder` method call is
added, removed or reordered. The diff is a comment.

## 3. Messaging — **none**

No domain or integration event. No `Shared.Contracts` change.

## 4. Boundary rules — **unchanged**

No project reference changes. `SmartSentinelEye.slnx` untouched.

---

## 5. The change, in full

### 5.1 Where it goes

`src/AppHost/AppHost.cs:516-532` — immediately above or folded into the
existing `// ADR-0106: single YARP API gateway at the edge...` comment block
that precedes the `apiGateway` registration. The engineer should extend that
existing block rather than add a second, separate one — the two precedent
comments (§2.6 of the spec) each sit inline at their own call site, and this
one should too.

### 5.2 What it must say

Not prescribed as literal text (the engineer should write it in the voice of
the two existing precedents, not copy this verbatim), but it must cover, at
minimum, the four things the spec establishes:

1. **The absence is deliberate**, not an oversight — nine `WithReference`,
   zero `WaitFor`, and that this is intentional.
2. **The failure mode**: a request that reaches a not-yet-ready backend gets
   a fast `502 Bad Gateway` (no active health check, no destination ever
   marked unhealthy, no stuck state) — recovery is automatic on the very next
   request once the backend is listening.
3. **The reason it is not gated**: every downstream service already carries
   the real dependency waits (RabbitMQ, Keycloak, its database, migrations);
   chaining the gateway behind all nine would make it one of the last
   resources to reach `Running` in the whole stack, for a guarantee that
   would not even be complete (spec §3.2 — the SPAs do not wait for the
   gateway either).
4. **The pattern is not new here** — point at the same shape already recorded
   at `AppHost.cs:361-365` and `AppHost.cs:399-402` (or their line numbers at
   the time of the edit; match by content, not number, since intervening
   edits may have shifted them).

**Explicitly do not claim** that the browser retries a failed request in
general — spec §2.4 found that claim false for anything but a 401. If the
comment wants to describe browser-side behaviour at all, it should say a
failed request surfaces once and is not retried, and that the SPAs racing
ahead of the gateway itself (not just the gateway racing ahead of its
backends) is the larger, already-accepted gap — not invent a resilience story
the code does not have.

### 5.3 Nothing else

No `apps/**` file. No `src/ApiGateway/**` file — §2's YARP/resilience-handler
findings are read-only evidence for the comment's claims, not something to
change. No test file (§8.3 of the spec — no new test). No workflow, script,
or ADR.

---

## 6. Risk table

| Risk | Likelihood | Mitigation |
|---|---|---|
| **The engineer "fixes" the absence instead of recording it** — adds `.WaitFor` because that reads as the obvious completion of the pattern | Moderate — this is the dominant risk, mirroring plan 165's dominant risk in shape | Spec §3 argues the cost/benefit in full; tasks.md states in its header that adding `.WaitFor` is out of scope and why. `git diff --stat` must show comment-only lines, zero `.WaitFor`/`.WithReference` changes. |
| **The comment overclaims browser retry** (repeating the issue's own unverified "and the SPAs retry" line) | Moderate — it is the intuitive, wrong thing to write | Spec §2.4 is explicit that this is false as a general claim; plan §5.2 repeats the instruction not to write it. |
| **A guard is written asserting the comment's text or the annotation's absence** | Moderate — phase 4a instinctively wants a red/green pair | Spec §8.3 and CLAUDE.md's own named anti-pattern forbid it. If a reviewer wants one in phase 6, it is argued there, not added reflexively. |
| **The new comment duplicates rather than extends the existing ADR-0106 block**, leaving two redundant paragraphs above the same call | Low–moderate | Plan §5.1 directs extending the existing block. |
| **ADR-0153 is treated as something this change must alter or depend on**, since it is unmerged | Low | Spec §4 is explicit: cited as evidence only; nothing here is contingent on PR #2415 merging first, and this spec does not touch replica count or the rate limiter. |

---

## 7. Sequencing and parallelism (ADR-0109)

**No task is marked `[P]`.** There is exactly one file in play and the tasks
are strictly ordered (verify → write → verify-unchanged → commit). No
fan-out shape for the orchestrator to consider.

## 8. Phase-gate notes

- **Phase 3 gate:** the feature-level issue **#2407** must be on Project #13
  (it already carries `agent:ready`, which is how this spec was picked up).
  No per-task issues.
- **Phase 4a:** behaviour-preserving, comment-only — discharged with the
  characterisation baseline (spec §8.3), no new test.
- **Phase 5:** confirm the baseline suites still pass unmodified after the
  edit, and that `git diff` shows no code change. §IV latency is **N/A**.
- **Phase 6:** `/code-review`. Not security-sensitive — no endpoint, scope,
  token, secret or trust boundary touched — `/security-review` is skipped
  with that one-line reason in the PR body.
