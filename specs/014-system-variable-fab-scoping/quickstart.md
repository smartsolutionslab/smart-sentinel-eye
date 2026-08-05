# Quickstart: verifying system-variable fab scoping

**Feature**: `014-system-variable-fab-scoping` | **Date**: 2026-08-05

How to see the defect closed, by hand, against a running stack.

> **The realm already has two fabs.** `/fabs/munich` and `/fabs/dresden`, with
> three operator shapes to sign in as:
>
> | Account | Password | Fabs |
> |---|---|---|
> | `op-3@munich.test` | `Operator1234` | munich |
> | `op-dresden@dresden.test` | `Operator1234` | dresden |
> | `op-multi@smart-sentinel-eye.test` | `Operator1234` | munich + dresden |
>
> **Trap:** the persistent `keycloak-data` volume keeps a stale realm, so in an
> existing environment the second fab will simply not be there until that
> volume is dropped. The symptom is a 403 with no obvious cause.
>
> **Second trap:** driving events by hand through `POST /events/manual` stamps
> `source = manual`, not `plc`. Author rules against `manual` if that is the
> route you use, or they will never fire.

Bring the stack up with `dotnet run --project src/AppHost` and wait for
`migrations` to reach **Finished** and `system-variables` to reach **Running**.

## 1. Two fabs keep their own values (US1 — the defect)

1. Define `oeeLine1` in **munich**, and `oeeLine1` in **dresden**. Both
   accepted — this is the behaviour that was impossible before.
2. Author and publish a rule in each fab, on the same trigger, each setting its
   own fab's `oeeLine1`.
3. Send a matching event from **munich**.

**Before this feature**: one row exists; whichever rule ran last wins, and both
fabs read that value.

**After**: munich's `oeeLine1` changes and dresden's does not. Read both back
to confirm.

Then send a matching event from **dresden** and confirm the reverse: dresden's
changes, munich's still holds the value from step 3.

## 2. A kiosk shows only its own fab's value (US2)

1. Publish an overlay in each fab referencing `oeeLine1`.
2. Open a kiosk in munich. It shows munich's value.
3. Change munich's value. Only the munich kiosk updates.
4. Change dresden's value. The munich kiosk does not move.

Step 4 is the one worth watching. Steps 1–3 pass even if resolution is still
global, because munich's value is the one that changed.

## 3. An operator cannot reach another fab's variables (US3)

1. Sign in as the dresden-only operator.
2. `GET /system-variables` — only dresden's are listed.
3. `GET /system-variables/<a munich variable>` — **404**, byte-identical to the
   response for a name never used. Confirm by requesting a nonsense name and
   comparing.
4. `POST /system-variables/<munich variable>/archive` — **404**. Check from a
   munich operator that it is unchanged.

> Archive needs an `If-Match` header, and the precondition is read before the
> fab is resolved. Without it the answer is **428**, not 404 — for your own
> variables too, so nothing leaks, but the step only shows what it means to
> show if you send one.

## 4. Authoring picks up the operator's fab (US4)

1. As the dresden-only operator, define a variable **without** naming a fab. It
   is created in dresden.
2. As the multi-fab operator, define one without naming a fab — **400**,
   saying a fab must be chosen. Nothing is created.
3. Same operator, naming `munich` — created in munich.
4. Any operator naming a fab they lack — **403**.

## 5. A rule pointing at another fab's variable is visibly ignored (US5)

1. Author a rule in **munich** whose action sets a variable that exists only in
   **dresden**. Publish it.
2. Send a matching event from munich.

**Expect**: dresden's variable is unchanged, and the service log carries a
warning naming both the fab and the variable name.

The log is the point. Without it this is indistinguishable from a rule that
correctly did not match, which is exactly how #1252 survived a release.

## Checking the migration

Against a database with variables created before the change:

```sql
SELECT fab, count(*) FROM system_variables GROUP BY fab;
```

Every pre-existing variable reports `munich`, and no row has a null fab
(SC-003). The unique index is on `(fab, name)`:

```sql
\d system_variables
```

The migration should also have said what it did, in the `migrations` output:

```
WARNING:  <n> pre-existing variable(s) attributed to fab 'munich'.
```

If your database was created after this feature, the backfill touches nothing
and the warning does not appear — that is correct, and it means this step
proves less than it looks. Run it against a database that predates the change
if you want it to mean something.

## What this does not cover

Whether the resolution path still meets its latency budget. That is SC-005 and
needs the measurement, not a walkthrough — see `research.md`, which records
that no such measurement existed before this feature.
