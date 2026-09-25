# ADR-0155: Replace MinIO with Azure Blob Storage (Azurite) for the audit cold archive

**Status:** Accepted
**Date:** 2026-09-25
**Supersedes:** The storage-vendor choice in ADR-0101 and ADR-0130 (both
keep their TimescaleDB / founding-decisions content; only the object-store
vendor named for AuditObservability's cold archive changes)
**Superseded by:** (none)

## Context

AuditObservability's retention worker (spec 009 FR-014/FR-016) archives
each hypertable chunk that crosses the 90-day boundary to an object store
before dropping it from Postgres. ADR-0101/ADR-0130 chose MinIO for this,
wired in `AppHost.cs` via `CommunityToolkit.Aspire.Hosting.Minio` +
`CommunityToolkit.Aspire.Minio.Client`.

On 2026-09-11, `minio/minio` and `minio/mc` disappeared from Docker Hub
(#2265, #2266); `AppHost.cs` was repinned to the quay.io mirror the same
day. On 2026-09-25 that mirror also started answering 401 UNAUTHORIZED on
every tag (#2267) — confirmed independently via `docker pull`, a raw
registry v2 token+manifest fetch, and a control image (`quay.io/prometheus
/prometheus`) pulling successfully in the same environment, proving the
failure is scoped to the `minio/minio` repository, not a quay.io-wide
outage. GHCR also denies `ghcr.io/minio/minio` ("denied"). There is no
third official MinIO registry to fall back to.

This blocks 100% of backend integration and end-to-end CI, repo-wide,
including on `develop` itself — every Aspire stack boot fails on
`minio: FailedToStart`, and `audit-observability` never leaves `Waiting`
on it. Roughly sixteen PRs were parked behind this outage at the time of
writing. `CommunityToolkit.Aspire.Hosting.Minio` 13.5.0's own nuspec
already reads "DEPRECATED: The MinIO OSS project has been archived and is
no longer maintained" — the registry outage is a symptom, not the root
cause; the project itself is no longer a going concern upstream.

Choosing a successor is explicitly ADR-level per ADR-0144 (the autonomous
delivery lane may not make this call) and per this repository's own
practice of recording every storage-vendor choice with an ADR. This ADR
is written by direct human instruction, not by the autonomous lane.

## Decision

Replace MinIO with **Azure Blob Storage**, using Aspire's own
`Aspire.Hosting.Azure.Storage` / `Aspire.Azure.Storage.Blobs` integrations
and the **Azurite** emulator — never a real Azure subscription.

`AppHost.cs` composes it as:

```csharp
var storage = builder
    .AddAzureStorage("storage")
    .RunAsEmulator(azurite => { /* persistent lifetime + data volume in dev */ });
var auditArchiveBlobs = storage.AddBlobs("blobs");
```

**`RunAsEmulator()` is called unconditionally — not gated by
`isRunMode`/`isE2ETests`.** This is the load-bearing detail for
constitution §I (on-prem first, no outbound internet dependency, no cloud
DB): Aspire's `RunAsEmulator()`/`RunAsContainer()` family of calls forces
container-based emulation in every execution mode the AppHost supports,
including a future `aspire publish`. Without this call, `AddAzureStorage`
implicitly pulls in `AddAzureProvisioning` and would generate Bicep for a
real `Microsoft.Storage/storageAccounts` resource at publish time — an
actual Azure dependency, which this repository does not want and the
constitution forbids. With it, this resource behaves exactly like every
other stateful container in `AppHost.cs` (Postgres, RabbitMQ, Keycloak,
Mosquitto): self-hosted, image pulled once, no outbound calls at runtime.
Whether a self-hosted Azurite container is an acceptable **production**
answer, versus a different on-prem S3-compatible store, is explicitly
deferred — see Consequences.

The Infrastructure-layer rewrite:

- `MinioOptions` → `AuditArchiveOptions` (`Bucket` → `ContainerName`,
  `SectionName` → `"AuditArchive"`; the object-key template is unchanged).
- `MinioAuditChunkArchiver` → `AzureBlobAuditChunkArchiver`, using
  `BlobServiceClient`/`BlobContainerClient`/`BlobClient` in place of
  `IMinioClient`. The idempotency check changes from comparing MinIO's
  ETag (which happens to equal Content-MD5 for a single-part PUT) to
  comparing Azure Blob's own `BlobProperties.ContentHash` — the SDK's
  direct exposure of the uploaded Content-MD5, which is a more precise
  match for what the check has always meant than MinIO's ETag ever was.
  The `#1808` trace-suppression workaround (a HEAD that legitimately 404s
  on first archive was marking the whole span as an error) is carried
  over unchanged: Azure SDK's `RequestFailedException` on a 404 routes
  through the same OpenTelemetry HTTP instrumentation and is expected to
  reproduce the identical symptom, but this has not been independently
  re-verified against a live trace — flagged in the PR for phase 5.
- `IAuditChunkArchiver.ChunkArchiveResult.MinioObjectKey` and
  `Shared.Contracts.AuditObservability.AuditChunkArchivedV1.MinioObjectKey`
  → `ArchiveObjectKey` on both. This is a rename of an already-shipped
  `V1` integration event's field, done without a `V2` bump: the event has
  never had an external consumer (no production deployment exists yet;
  the only reader is AuditObservability's own audit trail of itself), so
  this is pre-production schema churn, not a breaking change to a
  contract anyone depends on.
- DI: `builder.AddMinioClient("minio")` → `builder.AddAzureBlobServiceClient("blobs")`.

## Consequences

**Fixes the CI blocker.** Every integration/e2e job that boots the Aspire
stack can pull `mcr.microsoft.com/azure-storage/azurite` (Microsoft's own
registry, image pinned by the `Aspire.Hosting.Azure.Storage` package
version the same way Postgres's and RabbitMQ's base images are — no
hand-written literal in `AppHost.cs` for `ContainerImagePinTests` to read,
matching the existing precedent for package-supplied image defaults).

**Not S3-compatible.** Azure Blob Storage's wire protocol differs from
MinIO's (which was S3-compatible). Nothing else in this repository speaks
S3 directly — the only consumer was `MinioAuditChunkArchiver`, wrapped
behind `IAuditChunkArchiver` — so this is contained to the rewrite above.

**Production's object store is explicitly still open.** This ADR settles
AuditObservability's archiver and the dev/CI stack; it does not settle
what runs in a real k3s deployment. `RunAsEmulator()`'s unconditional use
keeps this on-prem-safe today (Azurite is a container, not a cloud
service), but Microsoft does not support Azurite as a production
data store. Constitution §I's "no outbound internet dependency" is
satisfied either way (a self-hosted Azurite container makes no outbound
calls), but a genuine production decision — stay on self-hosted Azurite,
or pick a different on-prem S3-/Blob-compatible product — is deferred
until a production deployment is actually being built (per
`infra-engineer.md`: "the Aspire k8s publisher has never been run").
Recorded here so the next reader does not mistake "fixed for CI" for
"settled for production."

**Wire contract renamed pre-production.** `AuditChunkArchivedV1`'s
`MinioObjectKey` → `ArchiveObjectKey` field rename is safe only because
nothing external consumes this event yet. If a production deployment or
external consumer exists by the time this is read, treat any further
rename to this contract as requiring a `V2`, per ADR-0073.

## Alternatives Considered

Evaluated hands-on (container pulled and run, not just documentation),
before choosing Azure Blob Storage:

- **AIStore** (`aistorage/aisnode`): looked promising from Docker Hub
  metadata alone (S3-compatible, no auth, small image, NVIDIA-backed).
  Rejected on closer inspection of the project's own
  `deploy/prod/docker/compose/docker-compose.yml`: the real production
  shape is a two-container cluster (separate `proxy` and `target`
  services) built from repository context, not a simple single-container
  pull — too much unverified operational complexity for a same-day fix.
- **LocalStack** (`localstack/localstack`): the `latest` tag as tested
  (`2026.8.4`) refuses to boot at all without a `LOCALSTACK_AUTH_TOKEN` —
  confirmed by booting the container and reading its own startup log
  ("License activation failed"), not by documentation, which left this
  ambiguous. Ruled out conclusively.
- **Azurite**: pulled, booted (`azurite-blob --blobHost 0.0.0.0`), and
  confirmed listening (clean startup log, an HTTP response — even a 400
  to a malformed unauthenticated root request — proves it is genuinely
  serving). Chosen. Its known trade-off (not S3-compatible; Azure Blob's
  own API) was accepted because nothing else in this repository depends
  on the S3 wire protocol.

A fourth option — staying on MinIO by self-hosting a private registry
mirror of the image — was not seriously pursued: it trades one
registry-availability risk for another (this repository would then own
keeping that mirror current against an upstream project that has already
stopped being maintained), and does not address the underlying signal
that MinIO OSS is no longer a going concern.

## Implementation Notes

- Packages: `CommunityToolkit.Aspire.Hosting.Minio` / `.Minio.Client`
  (13.5.0) removed; `Aspire.Hosting.Azure.Storage` / `Aspire.Azure.Storage.Blobs`
  (13.5.3, matching every other pinned Aspire package in this solution)
  added.
- `docs/runbooks/audit-observability.md` rewritten for Azure CLI /
  Azurite's well-known development account in place of the MinIO client
  (`mc`) instructions.
- Phase 5 (verify) must independently confirm: Azurite starts cleanly in
  a fresh Aspire stack boot, `RetentionRoundtripIntegrationTests` passes
  against it, and the `#1808` trace-suppression carry-over actually
  prevents the same false-error-span symptom under the new SDK.
