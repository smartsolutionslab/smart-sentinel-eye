# Plan 188 — An override the AppHost hears

**Spec:** `specs/188-an-override-the-apphost-hears/spec.md`
**Issue:** [#2254](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2254)

---

## 1. Bounded context and layers

**None.** This change touches no bounded context.

| Layer | Touched |
|---|---|
| `<Context>/Domain` | no |
| `<Context>/Application` | no |
| `<Context>/Infrastructure` | no |
| `<Context>/Api` | no |
| `Shared.Kernel` / `Shared.Contracts` | no |
| `src/AppHost` | **yes** — the composition root (constitution §VI) |
| `tests/Integration.Tests` | **yes** |

No entity, no value object, no invariant, no domain event, no integration
event, no migration, no HTTP surface. The cross-context boundary rule
(constitution §III) is not engaged: `src/AppHost` already references every
context by design and NetArchTest exempts it accordingly. **No new project
reference is added.**

Because no bounded-context code is touched, **`backend-engineer` is not
required** — see §7.

---

## 2. The shape decision: bind through configuration, and why that shape

The issue offers two shapes and prefers option 1. Option 1 is confirmed
achievable and chosen, but *how* it is achieved was not obvious and is settled
here, against the decompiled assembly rather than against the documentation.

### 2.1 Three candidate mechanisms

| | Mechanism | Reads `Parameters:<name>` | Falls back to the literal | Sets `ParameterResource.Default` | New type |
|---|---|---|---|---|---|
| A | `AddParameter(name, builder.Configuration["Parameters:" + name] ?? literal, secret)` inline at all ten call sites | yes | yes | no | no |
| B | **Chosen.** One AppHost-local helper that derives the key from the name and calls the same overload | yes | yes | no | no |
| C | `AddParameter(name, ParameterDefault, secret)` with our own `ParameterDefault` subclass | yes, via Aspire's own `GetParameterValue` | yes | **yes** | yes |

### 2.2 Why not C, the most Aspire-native-looking one

C routes through Aspire's own `GetParameterValue` and is the overload the
framework designed for exactly this. It is rejected on a **secret-egress**
ground, established from the pinned assembly rather than assumed:

```csharp
// ManifestPublishingContext.WriteParameterAsync, Aspire.Hosting 13.5.3
Writer.WriteString("value", "{" + parameter.Name + ".inputs.value}");
Writer.WriteStartObject("inputs");
Writer.WriteStartObject("value");
Writer.WriteString("type", "string");
if (parameter.Secret) { Writer.WriteBoolean("secret", true); }
if (parameter.Default != null)          // <-- NOT guarded by parameter.Secret
{
    Writer.WriteStartObject("default");
    parameter.Default.WriteToManifest(this);   // ConstantParameterDefault writes the value
    Writer.WriteEndObject();
}
```

`Default` is written to the manifest **regardless of `Secret`**. Today all ten
parameters have `Default == null` (the literal overload with
`publishValueAsDefault: false`), so nothing is published. C would set `Default`
non-null on **nine `secret: true` parameters**, so `aspire publish` would begin
writing nine dev secrets into the generated manifest. That is a regression
this repo has just spent a spec closing in a neighbouring place (spec 186, a
trace that keeps its secrets), and it is not worth paying for an idiom.

B and A both keep `Default == null`, so **manifest output is byte-identical to
today**. That is an explicit acceptance criterion for phase 6.

### 2.3 Why B and not A

A repeats the string `"Parameters:"` ten times next to ten hand-written names.
A typo in one of them reproduces the exact defect this spec exists to remove —
a key that looks right, reads nothing, and says nothing. That is not a
hypothetical: a silently-wrong key is the whole issue.

B derives the key from the parameter's own name at the single place the name is
written, so the key **cannot** disagree with the name. Ten call sites change one
method name and nothing else.

### 2.4 The helper

New file `src/AppHost/OverridableParameters.cs`:

```csharp
internal static class OverridableParameters
{
    internal static IResourceBuilder<ParameterResource> AddOverridableParameter(
        this IDistributedApplicationBuilder builder,
        string name,
        string fallback,
        bool secret = false)
    {
        string? configured = builder.Configuration["Parameters:" + name];

        return builder.AddParameter(
            name,
            string.IsNullOrEmpty(configured) ? fallback : configured,
            secret: secret);
    }
}
```

Four points, each checked rather than assumed:

1. **`string.IsNullOrEmpty`, not `??`.** Aspire's own `GetValueWithNormalizedKey`
   returns null for an empty string and `GetParameterValue` then falls back. `??`
   would let `Parameters:X=` install an empty secret. Matching the framework's
   semantics is the least surprising behaviour and is an acceptance scenario in
   spec §3 (US1, the bad-request case).
2. **Plain indexer, no normalisation helper.** Aspire's
   `GetValueWithNormalizedKey` is `configuration[key]` plus one retry with `-`
   replaced by `_`. None of the ten parameter names contains a dash, so the
   indexer is **exactly equivalent** here. The helper is not a re-implementation
   of a framework behaviour it might drift from; it is the same lookup.
3. **No `Ensure.That` guard.** ADR-0105 exempts the AppHost by name. The house
   style is not to add one here.
4. **No `Option<T>`.** ADR-0141 scopes that preference to Domain and
   Application; this is neither, and `IConfiguration`'s indexer is natively
   nullable.

The name follows ADR-0091 (no shortcuts or aliases): `AddOverridableParameter`,
not `AddParam` or `AddCfgParameter`.

### 2.5 The ten call sites

`src/AppHost/AppHost.cs:28–64`. Each becomes:

```csharp
var postgresUser = builder.AddOverridableParameter("PostgresUser", "postgres");
var postgresPassword = builder.AddOverridableParameter("PostgresPassword", "dev-only-postgres-password", secret: true);
// ... and the eight others, unchanged but for the method name
```

**Every existing comment block above these lines stays.** They explain which
realm client each secret mirrors and are the reason an override is a working
failure injector; they are load-bearing.

**Nothing else in `AppHost.cs` changes.** No resource, no wiring, no
`WithReference`, no replica count, no image tag.

---

## 3. Messaging: domain event → integration event

**N/A.** No message, no queue, no Wolverine handler, no `Shared.Contracts` type.
Nothing is published or consumed by this change.

---

## 4. Boundary rules

- **No cross-context project reference is added.** The helper is `internal` to
  `SmartSentinelEye.AppHost` and referenced only from `AppHost.cs`.
- **It is not put in `Shared.Kernel`.** It is composition-root plumbing with one
  consumer; promoting it would be speculative generality (ADR-0036) and would
  put an `Aspire.Hosting` dependency into a project that has none.
- **The existing NetArchTest rules are unaffected** — they do not constrain
  `src/AppHost`, which by design references everything.
- **`StackStatusReport` is untouched.** Its `ParameterResource` filter is a
  *type* filter, so it keeps excluding all ten parameters whichever overload
  declares them, and its fixed three-field line shape still cannot carry a
  value. `AppHostStackStatusTests` must stay green unmodified — that is the
  check, not the claim.

---

## 5. Risks, each with the check that retires it

| # | Risk | Check |
|---|---|---|
| R1 | The fixture's live stack changes credentials (`dev-only-*` → `test*`) and something holds a second copy of the old literal | spec §4's literal search: none outside `AppHost.cs` except the e2e fallback, which runs on a boot line that passes no `Parameters:` |
| R2 | `aspire publish` starts emitting secrets | B keeps `Default == null`; T003/T010 diff the generated manifest before and after and require it byte-identical |
| R3 | `ParameterResource.GetValueAsync` blocks in a model-only test (`WaitForValueTcs` is non-null) and the new tests hang instead of failing | **Assumption, marked.** The decompiled `GetValueAsync` returns the lazy value when `WaitForValueTcs` is null, and that field is set by the run-time value-prompt path, not by model building. **T001 is a spike that proves it before any test is written**; if it is wrong, the tests read `ValueInternal` via the resource's own snapshot instead, or move to the fixture lane |
| R4 | Keycloak's realm import misbehaves when the admin password changes | E2ETests mode uses ephemeral containers, so there is no stale volume to keep an old realm (the repo's recorded trap applies to persistent dev volumes). T008 boots the fixture and checks |
| R5 | The new every-parameter test asserts its own input | The expected value is composed by the test; the subject is what the AppHost resolves. The subject can change without the assertion text changing, which is the test this repo applies |
| R6 | A green integration suite is taken as proof the override applied | It is not proof — the credentials are self-consistent either way (spec §1.3). T013 step 4 records the **observed** connection-string value, and T013 step 3 runs the counterfactual on `origin/develop` |

---

## 6. Phase 4a colour — declared per story (ADR-0144)

### US1: **RED**

Behaviour-changing, and not ambiguous. An argument that today has no effect
acquires one; the same command line produces a different composition before and
after. A test for it that arrives green is a phase-4 failure, not a shortcut —
the correct first observation is `dev-only-migration-runner-secret` where
`deliberately-wrong` was asserted.

Two of US1's scenarios are **green on both sides by design** and are written as
characterisation of behaviour that must *not* move: "no argument leaves the
default in place" and "an empty override is not an override". They are in the
same new file and must be observed green in the same first run that shows the
override assertions red. **An all-red first run is a stop condition** — it means
the fallback broke, which is the one thing this change must not do.

### US2: **no new tests; the existing suite is the green-stays-green net**

Nothing new is asserted. The obligation is the inverse one: the Docker-requiring
integration suite and the five `FixtureLogic` classes exist and are green today,
must be **captured green before** the change (T002) and must be green
**unmodified after** (T007, T008). An assertion that has to be edited is evidence the
behaviour moved somewhere it was not meant to — block, do not adjust.

### US3: **characterisation by code hash — comment-only**

No test can observe a comment. The obligation is proof that *only* comments
moved: strip comments and XML docs from each of the five files before and after,
hash the remainder, require the hashes equal file for file (T011, T012). Never assert
that the prose contains a string — that checks the change's own input.

**No gate is weakened anywhere in this plan.** No test is deleted, no assertion
relaxed, no analyzer narrowed, no suppression added.

---

## 7. Phase 4 and phase 6 role assignments

| Phase | Role | Why |
|---|---|---|
| 4a | **test-writer** | Writes `AppHostParameterOverrideTests`, runs it, returns verbatim output. Does not touch `AppHost.cs` |
| 4b | **infra-engineer** | The entire production diff is `src/AppHost/` — Aspire composition root wiring, which is this role's stated scope. No bounded-context code, no domain model, no EF, no Wolverine |
| 6 | **infra-reviewer** (primary) | Reviews the composition change, the helper, and whether the green runs actually prove anything |
| 6 | **security-reviewer** (secondary, narrowly scoped) | Nine of the ten parameters are `secret: true` and this change adds a **new source** for their values. Scope the review to exactly three questions: does the manifest gain a `default` for any secret (R2); can an overridden value reach the stack-status report, a log line or the dashboard in a new way; does the helper's empty-string handling permit an empty secret |

**`backend-engineer` is explicitly not involved.** The audit found no
bounded-context configuration or secrets code in the diff: the ten parameters
are consumed through Aspire's `WithReference` / environment injection, which is
unchanged, and no service reads `Parameters:` itself (spec §1.5 — the only
configuration reads in `src/AppHost` are the three working switches).

**`frontend-engineer` / `frontend-reviewer` are not involved.** No file under
`apps/` or `e2e/` changes. `e2e/wall-withdrawal.spec.ts` is *read* during the
audit and stays exactly as it is.

---

## 8. Parallelism (ADR-0109)

Foundational and blocking:

- **T001** (the `GetValueAsync` spike) blocks all of phase 4a — it decides
  whether the new tests can live in the Docker-free lane at all.
- **T005** (the helper) blocks **T006** (the ten call sites), a compile
  dependency in fact.

Genuinely disjoint, marked `[P]`:

- **T002** (capture the existing suites green) and **T003** (record the
  pre-change manifest) touch no source and each other's outputs not at all.
- **T011/T012** (US3's five comment files) are disjoint from every US1 file and
  could be done by a different agent in a different worktree.
- **T015** (`security-reviewer`) runs alongside **T014** (`infra-reviewer`);
  both are read-only.

Everything else is sequential because it is one file after another in one small
diff; marking it `[P]` would be a fiction.

---

## 9. Definition of done (stated up front, ADR-0036)

1. `Parameters:MigrationRunnerClientSecret=deliberately-wrong` on a real boot
   makes `migrations` fail, and the same command line on `origin/develop` does
   not (spec §7 steps 1–4).
2. The every-parameter test passes for all ten and reads the count from the
   composition.
3. Omitting an argument still yields the declared literal, for all ten.
4. The integration suite and the five `FixtureLogic` classes are green,
   **unmodified**.
5. The generated manifest is byte-identical to `origin/develop`'s.
6. The five corrected comment files hash-identically once comments are stripped.

Not done when it compiles. Not done when the tests are green alone — item 1 is
an observation, and it is the one the issue was filed for.
