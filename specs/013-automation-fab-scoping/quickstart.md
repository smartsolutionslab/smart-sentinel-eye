# Quickstart: verifying Automation fab scoping

**Feature**: `013-automation-fab-scoping` | **Date**: 2026-08-03

How to see the two defects closed, by hand, against a running stack.

> **The realm now seeds two fabs.** `smart-sentinel-eye-realm.json` defines
> `/fabs/munich` and `/fabs/dresden`, with three operator shapes to sign in
> as:
>
> | Account | Password | Fabs |
> |---|---|---|
> | `op-3@munich.test` | `Operator1234` | munich |
> | `op-dresden@dresden.test` | `Operator1234` | dresden |
> | `op-multi@smart-sentinel-eye.test` | `Operator1234` | munich + dresden |
>
> Every section below is now runnable as written.
>
> **One trap when picking this up in an existing environment:** the
> persistent `keycloak-data` volume keeps a stale realm, so the second fab
> will simply not be there until the volume is dropped. The symptom is a 403
> with no obvious cause, or a sign-in that fails for the new accounts.
>
> The walkthrough is a way to *see* the behaviour, not the only evidence for
> it. What is verified automatically today:
>
> | Walkthrough | Covered by |
> |---|---|
> | §1 cross-fab firing | `RuleEvaluatorTests`, `FabEventIngestedV1HandlerTests` — unit, checked against two reproductions of #1252 |
> | §2 unreachable across fabs | `CrossFabEvaluationIntegrationTests` — real stack |
> | §3 inference | `Authoring_without_naming_a_fab_infers_the_operators_own` — real stack; `FabResolutionTests` — all four rows; `RuleFabResolutionIntegrationTests` — the multi-fab rows over HTTP |
> | §4 same name in two fabs | `The_same_rule_name_is_accepted_in_two_fabs` — real stack; `A_name_held_in_two_of_the_callers_fabs_is_refused_as_ambiguous` — the read side of the collision |
> | migration | `CrossFabEvaluationIntegrationTests`, plus the SQL check below |

Bring the stack up with `dotnet run --project src/AppHost` and wait for
`migrations` to reach **Finished** and `automation` to reach **Running**.

## 1. A rule no longer fires for another fab (#1252, User Story 1)

This is the one that happens with nobody watching, so it is worth doing
first.

1. Author a rule in **munich** that reacts to `plc` / `PlcCycleStart` and
   sets `oeeLine1`, then publish it.
2. Author a rule in **dresden** with the same trigger, a predicate that
   matches the same payload, and a *different* target variable — say
   `oeeLine9`. Publish it.
3. Send a `plc` / `PlcCycleStart` event from **munich**.

**Before this feature**: both rules fire. `oeeLine9` changes even though no
dresden event occurred, and the resulting change is recorded against munich.

**After**: only `oeeLine1` changes. `oeeLine9` is untouched, and nothing in
the dresden rule's history shows activity.

Repeat with an event carrying no fab: nothing changes anywhere (FR-012).

## 2. An operator cannot reach another fab's rules (User Story 2)

1. Sign in as an operator assigned to **dresden** only.
2. `GET /rules` — only dresden's rules are listed. The munich rule from
   step 1 is absent.
3. `GET /rules/<the munich rule name>` — **404**, byte-identical to the
   response for a name that was never used. Confirm by requesting a
   nonsense name and comparing.
4. `POST /rules/<munich rule>/publish` — **404**. Check from a munich
   operator that the rule is unchanged.
5. `POST /rules/<munich rule>/dry-run` — **404**. A trial run must not be a
   side channel.

## 3. Authoring picks up the operator's fab (User Story 3)

1. As the dresden-only operator, author a rule **without** naming a fab. It
   is created in dresden.
2. As an operator assigned to both fabs, author without naming one —
   **400**, saying a fab must be chosen. Nothing is created.
3. Same operator, naming `munich` — created in munich.
4. Any operator naming a fab they lack — **403**.

## 4. The same name works in both fabs (User Story 4)

1. Author `high-oee` in munich. Accepted.
2. Author `high-oee` in dresden. Accepted — this is the behaviour that was
   impossible before.
3. Author `high-oee` in munich again. **409**.

## Checking the migration

Against a database with rules created before the change:

```sql
SELECT fab, count(*) FROM rules GROUP BY fab;
```

Every pre-existing rule reports `munich`, and no row has a null fab
(SC-005). The unique index is on `(fab, name)`:

```sql
\d rules
```

## What this does not cover

A rule in one fab can still point its action at a variable belonging to
another. That is out of scope by design (spec Assumptions) and no step above
will surface it — worth knowing so its absence is not read as a passing test.
