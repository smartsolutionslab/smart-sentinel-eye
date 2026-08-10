# Research: Fab-scope the camera catalogue

**Feature**: `015-camera-fab-scoping` | **Date**: 2026-08-10

Three questions were open after the spec. None reached it as
`[NEEDS CLARIFICATION]` because each has a defensible answer from the two
precedents; they are recorded here so the answers are inspectable rather than
implied.

---

## 1. Does the placeholder-fab bridge repeat here?

**Decision**: No. Domain, command and endpoint resolution land in one slice.

**Rationale**: Spec 014 required a fab on `Variable.Define` before any endpoint
could resolve one, which forced seven `munich` placeholders to exist across
four phases — each needing a marker comment, a grep-able string and an
individual deletion. The cost was real and the risk was that one survived.

That was not inherent; it came from the task ordering. Nothing forces the
aggregate change to precede the endpoint change. Sequencing them together means
there is never a commit where a fab is required and unobtainable.

**Alternatives considered**:

- *Repeat spec 014's ordering.* Rejected. It is a known cost with no benefit;
  the only argument for it is symmetry with a predecessor, which is not a
  reason.
- *Make the fab optional on the aggregate and tighten later.* Rejected. An
  optional fab is a nullable column and a second migration, and "tighten later"
  is exactly the follow-up that does not happen.

**Consequence**: each commit must still build alone (ADR-0087), which a larger
slice makes easier here, not harder.

---

## 2. Is the fab on the camera events additive or a versioned contract change?

**Decision**: Additive. `EventMetadata.Fab` is populated; no `V2` event.

**Rationale**: `EventMetadata` already carries a nullable `Fab`, and
CameraCatalog currently stamps `null` — verified in the handlers that construct
it. Populating an existing nullable field changes no shape, so ADR-0073's
versioning rule is not triggered: a consumer reading the old shape reads the new
one unchanged.

Automation and SystemVariables already do exactly this, so consumers that
already read `Metadata.Fab` from other publishers see nothing new in kind.

**Alternatives considered**:

- *`CameraRegisteredV2` carrying a first-class fab.* Rejected. It would force
  every consumer to migrate for a field they can already read, and leave two
  versions live for no behavioural difference.
- *Leave the events alone; let consumers query the catalogue.* Rejected. It
  makes StreamDistribution's fab scoping depend on a synchronous call into
  another context per stream — the coupling ADR-0016 exists to avoid.

---

## 3. Does a retired camera keep its name reserved?

**Finding, and it is not what the spec assumed.** The shipped index is
`ux_cameras_name_lower` — unique on `name`, **no partial filter**. Rules and
variables both filter on the non-terminal state, so their names are released;
cameras' are not. A decommissioned camera holds its name forever today.

So spec FR-003 ("retiring MUST release its name for reuse within its own fab")
is **a behaviour change beyond fab scoping**, not a restatement of current
behaviour. That was not visible when the spec was written and is flagged at
this gate rather than absorbed silently.

**Recommendation**: adopt the filter, making cameras consistent with rules and
variables. A 250-camera installation with hardware churn will replace devices,
and a name permanently consumed by a camera that no longer exists is an
operational annoyance with no stated benefit. The retired row still carries the
old name for audit; the index is not the audit trail.

**Decision (2026-08-10, confirmed by the product owner): adopt the filter.**
Cameras become consistent with rules and variables, and FR-003 stands as
written.

The rejected alternative — keeping retired names reserved and amending FR-003 —
was the smaller change but left cameras the odd one out of three fab-scoped
contexts for no stated benefit.

Migration consequence, and it is favourable: a partial unique index is strictly
**weaker** than the unfiltered one it replaces, so no existing row can violate
it. The forward migration cannot fail on data. `Down` still can, which
data-model.md records.

**Alternatives considered**: making it configurable — rejected outright as a
knob for a need nobody has expressed (constitution §IX, no speculative
generality).

**Superseded in part, 2026-08-10 (T005).** A camera cannot be retired at all —
`CameraStatus.Decommissioned` is a value nothing sets. The filter is kept
because it costs nothing and is correct the moment a retire behaviour lands,
but **FR-003 is withdrawn**: this spec does not deliver name reuse, because
nothing frees a name. The decision above stands; only the claim that it buys
FR-003 today does not.

**Also settled here**: the existing index is case-insensitive by intent (spec
001 marker 2). Whichever option is taken, that property must survive the swap —
it is the kind of thing a hand-corrected migration drops silently, and it gets
its own test.

## Settled before the spec (recorded for traceability)

These two were decided with the user rather than researched, and are in the
spec's Assumptions:

- **Scope is CameraCatalog alone.** StreamDistribution and LayoutComposition
  (#1397) follow as their own features and depend on this one.
- **A camera's fab is resolved from the operator**, per ADR-0114, not derived
  from a physical location. No location attribute exists, and inventing one to
  derive a fab is a larger change with its own correctness question.

## Not researched, deliberately

**Whether the fab mechanism itself is right.** ADR-0114 settled it, spec 013 and
spec 014 applied it, and `FabResolution` is driven against all four rows of the
decision table by existing tests. Re-opening it here would be re-deciding a
settled question at the third application.
