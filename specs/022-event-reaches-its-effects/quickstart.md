# Quickstart: A plant-floor event reaches the things it is supposed to drive

**Feature**: `022-event-reaches-its-effects`

"Done" is the observations, not the walk. Record them on the PR.

**Read this first.** Unlike the last two features, the deliverable here is a
test — so a green run is not evidence. 228 green tests coexisted with the break
this exists to catch. **The evidence is step 3**, where the journey is broken on
purpose and this test is watched failing. A walk that skips it has demonstrated
nothing.

## 1. The journey works

Seed a rule, activate it, publish a matching event over the broker.

```sh
POST /rules            # mints a Draft
POST /rules/{name}/publish   # Active — and only now can it fire
mosquitto_pub -t 'fab/munich/plc/station-4' -m '<event matching the trigger>'
```

| Expect | |
|---|---|
| the rule | reads back as **Active** before the event is sent |
| the variable | its value changes to what the rule computes |
| the hub | a highlight frame arrives on a connected client |
| how long | recorded — see step 5 |

```
GET /variables/{name}   -> the new value
hub connection          -> the highlight frame
```

**Neither is read from the database.** The point is to see what an operator
sees; a correct row behind a broken API is exactly the kind of thing that passes
tests while the screen stays wrong.

## 2. An event nobody asked about changes nothing

Publish an event whose trigger or predicate matches no active rule.

| Expect | |
|---|---|
| the variable | unchanged |
| the hub | no frame |

Without this, a test asserting "the value is 82.5" would pass on a completely
dead system if the value happened to be 82.5 already. This is what makes step 1
mean something.

## 3. Break the journey and watch the test fail — the step that cannot be skipped

The cheapest honest break is the one that actually happened. In
`OutboxEventBus`, route the ambient publish back through the DbContext outbox:

```csharp
if (ambient.Envelope is not null)   // ← make this unreachable
```

Then run this test.

| Expect | |
|---|---|
| the test | **fails** |
| the failure message | says the effect never arrived, not that a message was missing |
| every other test | still passes — which is the whole point |

**If it passes, the test is worthless and the feature is not done.** That is not
rhetoric: the assertion that would have passed here is the one a reasonable
person writes first, and it is why this path went uncovered while looking
covered.

Restore the line afterwards and say so in the note.

## 4. The same event twice

Publish an identical event a second time.

| Expect | |
|---|---|
| the variable | the effect applied once, not twice |

Redelivery stopped being rare with spec 020, so this is now an ordinary case
rather than an edge one.

## 5. How long it took, and what that is worth

Record arrival-to-effect for step 1, and cite it against the
`event → overlay state ≤ 200 ms` leg of the end-to-end budget.

Then state the caveat rather than omitting it: the fixture runs nine services
and a broker on one host. Spec 020 measured p50 146 ms there under a saturating
burst, and was explicit that the figure does not establish what a fab would do.
The same applies. **Report the number and what it does not prove.**

## 6. It runs where it will be noticed

Run the routine build three times.

| Expect | |
|---|---|
| this test | included, and green each time |

If it cannot be made reliable, that is a reportable outcome (FR-008) — record
the reason **and** the cost, which is that this path returns to having no
automated coverage at all. Specs 020 and 021 each excluded one test for
defensible reasons. A third exclusion here would put the system back exactly
where the break got through.
