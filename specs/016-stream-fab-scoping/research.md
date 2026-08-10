# Research: Fab-scope stream distribution

**Feature**: `016-stream-fab-scoping` | **Date**: 2026-08-10

Two questions were left open by [plan.md](./plan.md). Both are resolved here.
Three further findings are recorded because they were established by reading
the code rather than assumed, and each one changed the design.

---

## 1. Where does the fab resolution for existing streams live?

**Decision**: a **separate `IHostedService`**, not inside `MediaMtxReconciler`.

**Rationale**: the reconciler exists to make MediaMTX's paths match the streams
table. Attribution is a different job with a different failure meaning: a failed
reconcile leaves video unreconciled and retries next restart, while a failed
attribution leaves streams *invisible to every operator* (FR-009). Folding them
together means one `try/catch` swallowing two failures that want different
responses, and the plan already flagged that the existing catch would otherwise
be inherited rather than chosen.

Separate also makes the ordering explicit. Attribution must be able to run
without MediaMTX being reachable, and reconciliation must not wait on
CameraCatalog.

**Alternatives considered**:

- *Inside `MediaMtxReconciler`.* Rejected. It already has the scope and the
  DbContext, which is the whole appeal, but it couples a video-plane concern to
  a control-plane one and gives both the same error handling.
- *On demand, at read time.* Rejected outright: it would put a CameraCatalog
  call on the request path, so a CameraCatalog outage would break listing
  streams. FR-009's window is a startup cost, not a per-request one.
- *A migration using `postgres_fdw`.* Rejected. It makes one context's schema a
  dependency of another's migration — a far worse coupling than an HTTP call,
  and invisible to ADR-0016's boundary rules because it lives in SQL.

---

## 2. Does FR-009's window need closing before first read?

**Decision**: **No.** The window stays, and is made observable rather than
eliminated.

**Rationale**: closing it means blocking host start until every stream is
attributed, which trades a brief incorrect-emptiness for an outage whose
duration depends on another context being reachable. That is the wrong trade
for a 24/7 system: a fab whose operator sees no streams for a few seconds
recovers by itself; a service that will not start does not.

The window is also small and self-limiting — bounded by the number of
unattributed streams, which is at most the 250-camera target and is zero after
the first successful pass.

What matters is that the window is **visible**: the count attributed and the
count unresolved are logged (FR-008, FR-010), so an operator seeing an empty
list can tell whether attribution has run.

**Alternatives considered**:

- *Block startup until attribution completes.* Rejected as above. It also makes
  StreamDistribution unable to start when CameraCatalog is down, which is a new
  hard dependency between contexts that ADR-0016 exists to prevent.
- *Show unattributed streams to everyone until filled.* Rejected. It is the
  defect this feature removes, reintroduced as a transitional state — and
  transitional states are exactly where such things survive.

---

## 3. The `DELETE` precedent does not transfer

**Finding**: `20260728210420_PersistStreamSourceUrl` met this same
cross-database wall and resolved it by deleting the unbackfillable rows,
reasoning that *a stream is derived state* rebuilt from `CameraRegisteredV1`.

**It does not apply here, and the difference is not stylistic.**

Those rows were **already broken**: `StreamSourceUrl.From("")` throws, so the
EF value converter faulted on *every read of the streams table*. Deleting cost
nothing because the table was unusable either way.

These rows are **functional**. Video is flowing; only the attribution is
unknown. And "derived state" is a statement about how a stream is *created*,
not a recovery mechanism that exists:

- `MediaMtxReconciler` reads **from** the streams table and pushes paths
  outward. It does not rebuild streams from cameras.
- The only creation path is `ProvisionStreamCommandHandler`, driven by
  `CameraRegisteredV1`.
- **Nothing republishes** `CameraRegisteredV1`.

So deleting would stop live video from every pre-existing camera until someone
re-registered it — trading a metadata gap for an outage. Verified by reading
the reconciler, not inferred from the comment.

---

## 4. No fab resolution, deliberately

**Decision**: this feature uses `FabClaims` for reads and **not**
`FabResolution`.

**Rationale**: `FabResolution.ResolveForWriteAsync` answers "which fab does this
caller's write apply to". There is no operator-driven write in this context —
the only creation path is an integration-event handler. There is nothing to
infer, nothing to name with `?fabId=`, and therefore no `STREAM_FAB_REQUIRED`
or `STREAM_FAB_AMBIGUOUS`.

Specs 013, 014 and 015 all have those errors, and adding them here by symmetry
would produce unreachable code — the precise failure mode that gave spec 015
three withdrawn requirements. Their absence is a decision, recorded so it does
not read as an omission.

Reads still resolve the caller's fabs, because a listing does have a caller.

---

## 5. Cameras and streams are in separate databases

**Finding, and the constraint the whole design turns on.** `AppHost` registers
`camera-catalog-db` and `stream-distribution-db` as distinct databases on one
server. Postgres cannot join across them without `dblink`/`postgres_fdw`.

This was established **before** the spec was written, which is why FR-008 is
runtime derivation rather than a SQL `UPDATE`, and why FR-009 exists at all. In
spec 015 the equivalent fact surfaced at implementation time and cost three
requirements.

---

## Settled before the spec (recorded for traceability)

Decided with the user rather than researched, and reflected in Assumptions:

- **The fab is derived from the camera**, never asked for.
- **`POST /streams/authorize` is not fab-scoped** — the caller is MediaMTX,
  which holds no fab groups. Scoping it would mean inventing a per-fab identity
  for the media server.
- **Existing streams are attributed at runtime**, not by a `munich` guess.

## Not researched, deliberately

**Whether the fab mechanism is right.** ADR-0114 settled it and three specs
have applied it. Re-opening it at the fourth application would be re-deciding a
closed question.
