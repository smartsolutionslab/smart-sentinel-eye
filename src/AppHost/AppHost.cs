using SmartSentinelEye.AppHost;

// Aspire composition root for Smart Sentinel Eye (ADR-0024).
//
// Resources wired here:
// - postgres: one database per bounded context ("camera-catalog-db", ...); Keycloak has none here
// - rabbitmq: shared broker with management plugin in dev
// - keycloak: realm "smart-sentinel-eye" imported from Realms/ folder
// - migrations: MigrationRunner that runs once on startup
// - one project per bounded context, each WithReference()'d to the resources it consumes
//
// Three lanes read this file: a developer's bare `aspire run` (persistent
// containers, data volumes, pgAdmin — the default); the end-to-end CI job,
// which boots the same run-mode shape but with `PersistentStack=false`, so
// nothing it creates outlives the job; and the integration fixture
// (`E2ETests=true`), which was already ephemeral and stays that way regardless
// of `PersistentStack`.

var builder = DistributedApplication.CreateBuilder(args);
var (isRunMode, isE2ETests, isScenarioSimulatorEnabled, isPersistentDevStack) = StackLanes.Read(builder);

var postgresUser = builder.AddOverridableParameter("PostgresUser", "postgres");
var postgresPassword = builder.AddOverridableParameter("PostgresPassword", "dev-only-postgres-password", secret: true);
var keycloakPassword = builder.AddOverridableParameter("KeycloakPassword", "dev-only-keycloak-admin", secret: true);
// Mirrors the `identity-admin` client secret seeded in
// Realms/smart-sentinel-eye-realm.json. The Identity API reads it as
// `Keycloak:AdminClientSecret` to mint the realm-management
// service-account token (spec 008 ADR-0100).
var identityAdminClientSecret = builder.AddOverridableParameter("IdentityAdminClientSecret", "dev-only-identity-admin-secret", secret: true);
// Spec 019: the migration job reads /fabs to provision an events partition per
// fab. Its own client, holding query-groups + view-users — the working minimum,
// since group-by-path answers 403 with query-groups alone (verified against
// Keycloak 26.5) — plus manage-realm, solely to narrow the realm's default
// roles on every boot (ADR-0181). That makes it the one realm-wide authority,
// so it authenticates by signed JWT (ADR-0186): this is the private half of the
// single public key Realms/smart-sentinel-eye-realm.json registers on the
// client (base64 PKCS#8). It is a dev/CI key, reaches only `migrations`, and
// is rotated together with that public key. A deployment is meant to override it with its own key (and register the matching public key); nothing here enforces that.
var migrationRunnerClientPrivateKey = builder.AddOverridableParameter("MigrationRunnerClientPrivateKey", "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCLOpN3STlUmx+xuXTR+JdASkgPul2Z02uTKQTVhRzxkxeFnHUcbCiLIgXSM7uaO4XXkj70ZYkJ8dvZ6nmYibpkgwLD9WWejQeo+bx2lp5Oy2anP8osZscpUUFwHfyeAMfdqlWW8ExljV/AZ2gWLUfAvvIxVqqpVSQYR+AalJoxt0v0rotVp0Za3ZEFPvZ6grE8D0A4DPoWPxnEEAOa5q7BE7Cg+9NrdEEEWJhLHYjsFnB9uiKB4Apy+YQ5syv3SQx9cIlW2yIG3BpWV7E1kJG0kaLCRlgZwIRp3xGme7VhYCwfTxOVcEwk20Pqwwh8+7qkWYW1TZUkbCXuBA1ZIaC/AgMBAAECggEAArUpDhZLZwAWX3C4jDgGF+9zfGJI7mMuEMwPSb2ecuGZTcZuo0nvMYRu779zrZ79p8LuWF1aNdlNFJf6pBH95VMMzHOcF8ib9FPZJ2B0odYO00iECV4W3GXpFAPpz8c6xn+eM9XvMRWZ1WECx3ddb1WDDVi4ZxNxvoablUJaC7n6KVOoidMpERPVVyXFyRDNHnScY57JiPMcqzG5BIMs9qq+031JNPzpVbnjMg6/Kaqo6I5I/r+VPwUYmfshrev2OuSdXsw+ReTqWlO1hAQHr3JcGY+cUQ8+Y5qpBfZtcPMLKFVuJ79XJ6EPK8TA2B6qNs1YU7gAfwkGz/MH0Lb2cQKBgQDD1AJyRSdtcZ5ZCOT/ZoM1/AUngt+4pjC+UNrcZvh6QkZzf3WLC66dlQeXg1vxDMUbNk+tpz1M9FIWZ3apU393EfP+fR19H7VS5SDREWd23g0foQEv9boS1gNkjtRpTbyjg5LHYMYWzAK7lyGb0Q3A/c1iK3q5UoagNFEAZOCm7QKBgQC2AmtJv8LxNRIdpoJFTEEVr15ypUYJfcx439wrtVnqo1YBGCDv8Q/q8jDePmhohVDDKJC8htsI+f4I2seSeFceJyHcyN2zQbovlc/F+lboKown0pC0NHMtprcnmQJWrFqkE61ITNeiohpA0NIEIWjVSCcHH8/Q5i9xH0jiXq+k2wKBgCkf5d+ffDc0rB9gic7E9Xsp0N9KUbdSV1wwWLX+feIW9vqcYs/vhR0cxfpgoLdPijk7v6HlRC9cA70mvzmyCku0HnxsQ/wZ45dj/O9K62KAIVtqEXjD7u6zuQSuJhdzNtFT+Ugbn537+/rvMySo022bHz2lieFPXmxYcEZX6QHFAoGBAIRT/e0AnpsAGsuz48K+ThOWDPUNdH4yGBdJqqmckGMnnb/t6wSIHyqSKzVp6j8TrkQ1mgV3hohgOXH2nmK/W5dwnA4JfCZ4AwXVxdoKyGTDimLplItwFs16CG+hrzGU3dGs/K80LxLvCGdXa1nVcIKgkr97QXx/3r5AIJ10ef9BAoGAadNETwfQbyOaWAdTrbUFSzTaFZgtDeT3eOi6N9/aynpoqamVSz6Z/AQE6VoDvSoDSxk955hxdQeSc138N7CIbqAEFHegMxFDAyCuH5Ll4iIG8TckfftBvWm38B6c1Ev5fAKNksoQSl6imp+Shtu9DzFRBo7L9JskO71vaHtYmP4=", secret: true);
var rabbitPassword = builder.AddOverridableParameter("RabbitMqPassword", "dev-only-rabbit-password", secret: true);
// Mirrors the dev-only `scenario-simulator` confidential client seeded in
// Realms/smart-sentinel-eye-realm.json. The Scenario Simulator worker
// (ADR-0111) reads it as `ScenarioSimulator:Runtime:ClientSecret` to mint a
// client_credentials token (scope sse.cameras.write) for seeding the catalog.
var scenarioSimulatorClientSecret = builder.AddOverridableParameter("ScenarioSimulatorClientSecret", "dev-only-scenario-simulator-secret", secret: true);
// Mirrors the dev-only `event-ingestion` confidential client seeded in
// Realms/smart-sentinel-eye-realm.json. The MQTT subscriber mints a
// client_credentials token and presents it as its broker password; the
// go-auth plugin (ADR-0100) enforces azp == the MQTT username.
var eventIngestionMqttClientSecret = builder.AddOverridableParameter("EventIngestionMqttClientSecret", "dev-only-event-ingestion-secret", secret: true);
// Mirrors the `system-variables-seeder` confidential client seeded in
// Realms/smart-sentinel-eye-realm.json. SystemVariables reads it as
// `ReverseIndexSeeder:ClientSecret` to mint a client_credentials token
// (scope sse.overlays.read) for the startup seed of the reverse index —
// the half of spec 005 T061 that never shipped (#2158, spec 126).
var systemVariablesSeederClientSecret = builder.AddOverridableParameter("SystemVariablesSeederClientSecret", "dev-only-system-variables-seeder-secret", secret: true);
// Mirrors the `revocation-list-reader` confidential client seeded in
// Realms/smart-sentinel-eye-realm.json (spec 270, ADR-0160 section 4). Every
// non-Identity API reads it as `RevocationList:ClientSecret` to mint a
// client_credentials token (scope sse.identity.revocations.read) for its
// 5-second revocation-snapshot refresh (ServiceDefaults.Revocation).
var revocationListReaderClientSecret = builder.AddOverridableParameter("RevocationListReaderClientSecret", "dev-only-revocation-list-reader-secret", secret: true);
// ADR-0171. Authorizes MediaMTX's control-API calls through the existing WHEP
// hook: StreamDistribution reads it as `MediaMtxApi:Secret` both to check the
// Basic-auth credential MediaMTX forwards for an `api` hook call and to
// attach that same credential to its own calls into MediaMTX's control API.
// Not a Keycloak client secret — MediaMTX itself never holds it, only forwards
// whatever Basic auth the caller presented.
var mediaMtxApiSecret = builder.AddOverridableParameter("MediaMtxApiSecret", "dev-only-mediamtx-api-secret", secret: true);

// Spec 449 / ADR-0184: the MigrationRunner rewrites camera URLs that audit rows stored before spec 377 only
// when this is "true"; "false" (the default) is a dry run that counts and changes nothing. Not a secret.
// Any other value is not a dry run: it fails to bind as a boolean, which fails the migrations job and, through
// GateOnMigrations, every service that waits on it.
var auditUrlRedactionApply = builder.AddOverridableParameter("AuditUrlRedactionApply", "false");

// Spec 009 ADR-0101: the postgres image carries the timescaledb
// extension so the audit-observability hypertable + compression
// work. We use the single-node `timescale/timescaledb` community
// image rather than the `-ha` (Spilo/Patroni) variant: it is far
// lighter for dev/CI and still provides hypertables AND compression
// (a TSL feature the audit migration requires). The `-oss` tags
// drop compression, so the community (non-oss) tag is required.
// Every other context's database remains plain Postgres tables on
// the same server.
var postgres = builder
    .AddPostgres("postgres", userName: postgresUser, password: postgresPassword)
    .WithImage("timescale/timescaledb")
    .WithImageTag("2.27.1-pg17")
    // Postgres defaults to 100, which nine contexts sharing one container had
    // already almost spent: measured at 97 idle connections before any load.
    // That is not a theoretical margin — driving audit ingest at 100 ev/s took
    // the cluster past the limit and system-variables started refusing writes
    // with "53300: sorry, too many clients already", which reads as a bug in
    // whichever service happens to ask next rather than as a shared budget
    // running out (ADR-0124, ADR-0125).
    //
    // 500 is PostgresConnectionBudget.ServerMaxConnections. It is written out
    // rather than referenced because an Aspire project reference exposes no
    // assembly to the AppHost, and dragging ServiceDefaults in for one integer
    // costs more than it saves. The two are held together by
    // PostgresConnectionBudgetTests, which asks the *running* server what it
    // allows and fails if it is under the budget — a stronger check than
    // sharing a constant, because it verifies what was deployed rather than
    // what was written.
    .WithArgs("-c", "max_connections=500");

// A persistent postgres keeps the args it was created with, so a changed WithArgs
// (e.g. max_connections) needs `docker rm` of the container; the full note is on
// DeveloperLanePersistence.WithPostgresDeveloperLanePersistence.
postgres.WithPostgresDeveloperLanePersistence(isPersistentDevStack);

var cameraCatalogDb = postgres.AddDatabase("camera-catalog-db");
var streamDistributionDb = postgres.AddDatabase("stream-distribution-db");
var layoutCompositionDb = postgres.AddDatabase("layout-composition-db");
var overlayDesignerDb = postgres.AddDatabase("overlay-designer-db");
var systemVariablesDb = postgres.AddDatabase("system-variables-db");
var eventIngestionDb = postgres.AddDatabase("event-ingestion-db");
var automationDb = postgres.AddDatabase("automation-db");
var identityDb = postgres.AddDatabase("identity-db");
var auditDb = postgres.AddDatabase("audit-db");

var rabbitmq = builder
    .AddRabbitMQ("rabbitmq", password: rabbitPassword)
    .WithImageTag("4-management-alpine")
    .WithManagementPlugin();

rabbitmq.WithRabbitMqDeveloperLanePersistence(isPersistentDevStack);

// ADR-0189: ages the dead-letter queue out after 14 days; see RabbitMqDeadLetterPolicy.cs for why this is
// a management-API call and not a mounted definitions file.
rabbitmq.WithDeadLetterAgeOutPolicy(builder.AppHostDirectory);

// Image, tag and registry are spelled out because Aspire.Hosting.Keycloak
// supplies all three coordinates and this file named none of them: a
// Directory.Packages.props bump could move the image, tag or registry with no
// diff here, and Keycloak decides realm import, scope issuance and the `iss`
// claim, so that drift would surface as an opaque invalid_scope or a blanket
// 401 (issue #2296, spec 195). `26.6.4` is the same manifest the package's
// baked-in `26.6` default resolved to on 2026-09-20 —
// sha256:0aae0de7fca85525f727d3354df17896092de8bb26ae4c12d89c77e5df8cbce4 for
// both tags — so this pin changes nothing about what runs today while closing
// the patch-channel `26.6` leaves open. Order matters: `WithImage` re-parses
// the reference and resets the tag to `latest` when it is given none, so it
// must come before `WithImageTag`.
var keycloak = builder
    .AddKeycloak("keycloak", adminPassword: keycloakPassword)
    .WithImage("keycloak/keycloak")
    .WithImageTag("26.6.4")
    .WithImageRegistry("quay.io")
    .WithRealmImport("../AppHost/Realms");

// The Realms directory holds two imports: smart-sentinel-eye-realm.json (the
// application realm) and master-realm.json (spec 324, #2508), which replaces
// Keycloak's bootstrap-created `master` so the admin-cli account gets the same
// brute-force protection as the application realm. master-realm.json must keep
// the bootstrap-parity fields (accessTokenLifespan, display name, user-profile
// component) alongside the brute-force block — without them the imported realm
// falls back to realm-import defaults that break the bootstrap admin's own
// password grant (plan.md §2 for spec 324).

// A persistent keycloak keeps its data volume, so an edited Realms/*.json is not
// re-imported until that volume is dropped; the full note is on
// DeveloperLanePersistence.WithKeycloakDeveloperLanePersistence.
keycloak.WithKeycloakDeveloperLanePersistence(isPersistentDevStack);

// MediaMTX SFU brings RTSP ingest + WHEP playback (spec 002 T003, ADR-0011).
// MediaMTX is the runtime source of truth for live paths; the stream-distribution
// service is the durable source of truth and reconciles paths on startup.
//
// Pinned, and pinned harder than a typical image, because MediaMTX composes
// inputs this repository's code reads and decides outcomes it relies on: the
// `token`/`path`/`action` fields the WHEP authorization hook receives, what
// `authHTTPExclude` means in mediamtx.yml, the refusal to publish to a path
// whose `source` is not `publisher` (the safety that makes #2094's remaining
// gap unexploitable), and the treatment of 401 as a challenge that made #2094
// answer 403. None of the four is written down here, and on a floating tag all
// four could change with no diff and no PR — arriving as a red run on a branch
// that changed nothing, on whichever machine pulled first (#2103, spec 113).
//
// 1.21.0-ffmpeg is sha256:9d148b5f29906618dee627ea3232c75d4ed60da8d40a3ad7276ca87c1ec4f19e,
// the image `latest-ffmpeg` resolved to when it was pinned, so the pin changed
// the running bytes not at all. The version is named rather than the digest
// because every image this repository pins, it pins by version tag, and because
// a reader reasoning about MediaMTX's behaviour needs a number they can take to
// a changelog; the digest is recorded so the exact artifact stays recoverable.
// Bumping is by hand — no Dependabot or Renovate config exists in this tree.
// ContainerImagePinTests fails the build when any of the three MediaMTX
// literals written here drifts apart; AppHostContainerImagePinTests is what
// checks the resolved claim, by reading the ContainerImageAnnotation the
// composed model actually carries (issue #2270, spec 187).
var mediamtx = builder
    .AddContainer("mediamtx", "bluenviron/mediamtx", "1.21.0-ffmpeg")
    .WithBindMount("Resources/mediamtx.yml", "/mediamtx.yml")
    .WithHttpEndpoint(targetPort: 9997, name: "api")
    // Spec 024: the SFU measures its own RTP ingest; exposing the endpoint is
    // what makes camera → SFU readable at all (§VII, ≤ 80 ms).
    .WithHttpEndpoint(targetPort: 9998, name: "metrics")
    .WithHttpEndpoint(targetPort: 8889, name: "whep")
    .WithEndpoint(targetPort: 8554, name: "rtsp", scheme: "tcp")
    // ADR-0177: declares what "ready" means for MediaMTX instead of inferring
    // it from container-runtime state (#2060). Targets `metrics`, not `api`:
    // ADR-0171 routes every `api` request through StreamDistribution's
    // /streams/authorize hook, and `stream-distribution` does
    // `.WaitFor(mediamtx)` below — a health check against `api` would need
    // stream-distribution already answering that hook to ever report
    // healthy, which is a dependency cycle (mediamtx can't go healthy until
    // stream-distribution is up, which can't start until mediamtx is
    // healthy). `metrics` stays excluded from the hook (mediamtx.yml's
    // authHTTPExclude) and is already proven reachable unauthenticated by
    // SfuIngestMetricsTests, so it is a true "MediaMTX is answering HTTP"
    // signal with no such cycle.
    .WithHttpHealthCheck(path: "/metrics", statusCode: 200, endpointName: "metrics");

// fixture-video (spec 056, spec 076) — one looping clip over RTSP, so an
// automated check can see a picture. Before spec 056 every overlay fixture
// pointed its camera at an address nothing served, so the tiles rendered
// `WHEP returned 404` and a tile that drew its label ONLY when the video
// failed passed the whole suite.
//
// **Gated `isRunMode` — deliberately NOT `camera-sim`'s gate**, which since
// #2013 carries a third conjunct (`isScenarioSimulatorEnabled`). The
// end-to-end job needs a picture and does not need a simulator, so the two
// blocks must not be folded together: adding that conjunct here would delete
// spec 056's video source from CI, which is exactly what
// `The_simulator_argument_leaves_the_web_apps_and_the_fixture_video_in_place`
// exists to prevent.
//
// **Both lanes get this, and the `!isE2ETests` conjunct is gone (spec 076,
// #198).** `E2ETests` is set by the *integration* fixture (`AspireFixture`)
// and by `AppHostE2ESwitchTests`, but not by the end-to-end stack boot, which
// is a plain `dotnet run` in `ci.yml`. The exclusion was right while nothing
// in the integration lane consumed the picture — an integration run has no
// browser, so it would otherwise have paid for a container, a 46 MB bind
// mount and a permanently looping FFmpeg it never read. That condition is
// gone: the integration lane now reads this source.
// `AspireFixture.RtspTestSourceUrl` points a camera at
// `rtsp://fixture-video:8554/loop`, which is how a test observes a stream
// reaching `Healthy` rather than only the failure half. What the integration
// run now pays is one container start plus a `-c copy` FFmpeg loop against an
// already-H.264 clip, so nothing transcodes — and **no image pull at all**,
// because `bluenviron/mediamtx:1.21.0-ffmpeg` is the same tag the ungated
// `mediamtx` above already pulls.
// `E2ETests_argument_excludes_the_dev_only_resources` now asserts the
// presence, so folding this back under `!isE2ETests` fails there.
if (isRunMode)
{
    builder
        .AddContainer("fixture-video", "bluenviron/mediamtx", "1.21.0-ffmpeg")
        .WithBindMount("Resources/fixture-video.yml", "/mediamtx.yml")
        // The whole clips directory, mounted where the config expects it.
        .WithBindMount("Resources/clips", "/media")
        .WithEndpoint(targetPort: 8554, name: "rtsp", scheme: "tcp");
}

mediamtx.WithRunLaneIcePublish(isRunMode, isE2ETests);

mediamtx.WithContainerDeveloperLaneLifetime(isPersistentDevStack);

// Mosquitto MQTT broker for spec 006 EventIngestion (ADR-0095). Each
// PLC and inference device publishes on a per-device topic; the
// event-ingestion service subscribes with a fab-scoped wildcard.
//
// Spec 008 ADR-0100: the image is built from `mosquitto/Dockerfile`,
// which adds a custom Go auth plugin (`mosquitto/plugin/`) that
// validates Keycloak-minted RS256 JWTs against the realm JWKS with no
// per-CONNECT round-trip. The upstream iegomez/mosquitto-go-auth
// plugin can't do this (HMAC-only signatures, no JWKS) — see the ADR.
// Non-JWT passwords fall through to the password_file, so the spec 006
// EventIngestion subscriber + station-4 / camera-12 seeds keep working.
//
// The plugin needs the realm JWKS, so AppHost injects the
// container-reachable Keycloak URL as SSE_JWT_JWKS_URI and waits for
// Keycloak so the set is fetchable on first connect (the plugin also
// retries until it is).
var mosquitto = builder
    .AddDockerfile("mosquitto", "mosquitto")
    .WithBindMount("mosquitto/mosquitto.conf", "/mosquitto/config/mosquitto.conf")
    .WithBindMount("mosquitto/passwords.txt", "/mosquitto/config/passwords.txt")
    .WithBindMount("mosquitto/acl.txt", "/mosquitto/config/acl.txt")
    .WithEndpoint(targetPort: 1883, name: "mqtt", scheme: "tcp")
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["SSE_JWT_JWKS_URI"] = ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/smart-sentinel-eye/protocol/openid-connect/certs");
    })
    .WaitFor(keycloak);

mosquitto.WithMosquittoDeveloperLanePersistence(isPersistentDevStack);

// Azure Blob Storage (Azurite emulator in dev/CI) — ADR-0155. Used by
// AuditObservability (spec 009 ADR-0101) for the per-chunk cold archive once
// a hypertable chunk crosses the 90-day boundary. Aspire's own Azure Storage
// hosting integration injects `ConnectionStrings:blobs` into every consumer
// that `WithReference`s the blob resource; the Infrastructure project
// resolves a `BlobServiceClient` from it via `AddAzureBlobServiceClient("blobs")`.
//
// Replaces MinIO — ADR-0155 supersedes the storage choice ADR-0101/ADR-0130
// made, not the TimescaleDB decision around it. On 2026-09-11 `minio/minio`
// and `minio/mc` disappeared from Docker Hub (#2265, #2266); this AppHost
// switched to the quay.io mirror, and on 2026-09-25 that mirror also started
// answering 401 UNAUTHORIZED on every tag (#2267), confirmed via `docker
// pull`, a raw registry v2 token+manifest fetch, and a control image
// succeeding in the same environment. Every integration/e2e run repo-wide
// failed on `minio: FailedToStart` — there is no image-pin workaround for a
// registry that refuses the pull outright, so the choice of vendor itself had
// to change.
//
// `RunAsEmulator()` runs Microsoft's own `azurite` container. Its image and
// tag are pinned by the Aspire.Hosting.Azure.Storage package version
// (Directory.Packages.props), the same way Postgres's and RabbitMQ's base
// images are — there is no `WithImage`/`WithImageTag` literal here for
// ContainerImagePinTests to read, and none is needed. Persistent lifetime +
// data volume mirror every other stateful container in this file.
var storage = builder
    .AddAzureStorage("storage")
    .RunAsEmulator(azurite =>
    {
        azurite.WithAzuriteDeveloperLanePersistence(isPersistentDevStack);
    });
var auditArchiveBlobs = storage.AddBlobs("blobs");

// MigrationRunner orchestrates all per-context migrations and exits (ADR-0067).
var migrations = builder
    .AddProject<Projects.SmartSentinelEye_MigrationRunner>("migrations")
    .WithReference(cameraCatalogDb)
    .WithReference(streamDistributionDb)
    .WithReference(layoutCompositionDb)
    .WithReference(overlayDesignerDb)
    .WithReference(systemVariablesDb)
    .WithReference(eventIngestionDb)
    .WithReference(automationDb)
    .WithReference(identityDb)
    .WithReference(auditDb)
    .WaitFor(cameraCatalogDb)
    .WaitFor(streamDistributionDb)
    .WaitFor(layoutCompositionDb)
    .WaitFor(overlayDesignerDb)
    .WaitFor(systemVariablesDb)
    .WaitFor(eventIngestionDb)
    .WaitFor(automationDb)
    .WaitFor(identityDb)
    .WaitFor(auditDb)
    // Spec 019: partitions are provisioned per fab, and the fabs come from the
    // realm. WaitFor rather than a bare reference — an unreachable realm makes
    // the run fail (FR-011), so waiting is what keeps the ordinary case from
    // looking like the failure case.
    .WithReference(keycloak)
    .WithEnvironment("Keycloak__AdminClientId", "migration-runner")
    .WithEnvironment("Keycloak__AdminClientPrivateKey", migrationRunnerClientPrivateKey)
    .WithEnvironment("AuditObservability__UrlRedaction__Apply", auditUrlRedactionApply)
    .WaitFor(keycloak)
    // Dashboard grouping: nest the one-shot migration runner under postgres (the
    // data tier) — it migrates every per-context database, then exits.
    .WithParentRelationship(postgres);

var cameraCatalog = builder
    .AddProject<Projects.SmartSentinelEye_CameraCatalog_Api>("camera-catalog")
    .WithHttpEndpoint()
    .WithReference(cameraCatalogDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);

var streamDistribution = builder
    .AddProject<Projects.SmartSentinelEye_StreamDistribution_Api>("stream-distribution")
    .WithHttpEndpoint()
    .WithReference(streamDistributionDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithReference(mediamtx.GetEndpoint("api"))
    .WithReference(mediamtx.GetEndpoint("whep"))
    .WithEnvironment("MediaMtxApi__Secret", mediaMtxApiSecret)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak)
    .WaitFor(mediamtx);

streamDistribution.WithIntegrationLaneWhepCeiling(isE2ETests);
streamDistribution.WithIntegrationLaneSourceRepairInterval(isE2ETests);

mediamtx.WithRunLaneAuthorizeHookAddress(streamDistribution, isRunMode);

var layoutComposition = builder
    .AddProject<Projects.SmartSentinelEye_LayoutComposition_Api>("layout-composition")
    .WithHttpEndpoint()
    .WithReference(layoutCompositionDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    // The one cross-context HTTP call this service makes, and only on the
    // write path: a tile's camera must be in its layout's fab (spec 017
    // FR-014), and only CameraCatalog knows a camera's fab. It carries the
    // caller's own token, so no service account is involved — unlike
    // the retired ADR-0116 account, this exception needs no standing credential.
    //
    // Not a WaitFor: a CameraCatalog outage must stop layout *authoring*
    // only. Reading layouts, the hub pushes and video all carry on.
    .WithReference(cameraCatalog)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
var eventIngestion = builder
    .AddProject<Projects.SmartSentinelEye_EventIngestion_Api>("event-ingestion")
    .WithHttpEndpoint()
    .WithReference(eventIngestionDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithReference(mosquitto.GetEndpoint("mqtt"))
    .WithEnvironment("Mosquitto__ClientSecret", eventIngestionMqttClientSecret)
    // Spec 020 FR-005. Five minutes is the shipped default, sized for a plant
    // whose failover takes minutes. Ninety seconds here so the bounded escape
    // is something a developer can actually watch happen — at five minutes
    // nobody ever sees a dead letter written, and an escape nobody has seen
    // work is an escape nobody knows is broken.
    .WithEnvironment("EventIngestion__IngestRetry__MaximumRetryWindow", "00:01:30")
    .WaitFor(rabbitmq)
    .WaitFor(keycloak)
    .WaitFor(mosquitto);
var overlayDesigner = builder
    .AddProject<Projects.SmartSentinelEye_OverlayDesigner_Api>("overlay-designer")
    .WithHttpEndpoint()
    .WithReference(overlayDesignerDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
var systemVariables = builder
    .AddProject<Projects.SmartSentinelEye_SystemVariables_Api>("system-variables")
    .WithHttpEndpoint()
    .WithReference(systemVariablesDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithReference(overlayDesigner)
    .WithEnvironment("ReverseIndexSeeder__ClientSecret", systemVariablesSeederClientSecret)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak)
    .WaitFor(overlayDesigner);
var automation = builder
    .AddProject<Projects.SmartSentinelEye_Automation_Api>("automation")
    .WithHttpEndpoint()
    .WithReference(automationDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
var identity = builder
    .AddProject<Projects.SmartSentinelEye_Identity_Api>("identity")
    .WithHttpEndpoint()
    .WithReference(identityDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    // Spec 318 (#2628): rotating a webhook client asks EventIngestion whether
    // the integration is revoked. Reference, not WaitFor — an EventIngestion
    // outage must stop rotation only, not Identity's own boot, its
    // token-adjacent endpoints, revocation-list serving (ADR-0160), or
    // kiosk/device flows. EventIngestion already references identity below
    // (ADR-0160), so this makes the reference mutual; Aspire accepted the
    // cycle on boot.
    .WithReference(eventIngestion)
    .WithEnvironment("Keycloak__AdminClientSecret", identityAdminClientSecret)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);
var auditObservability = builder
    .AddProject<Projects.SmartSentinelEye_AuditObservability_Api>("audit-observability")
    .WithHttpEndpoint()
    .WithReference(auditDb)
    .WithReference(rabbitmq)
    .WithReference(keycloak)
    .WithReference(auditArchiveBlobs)
    .WithEnvironment("AuditArchive__ContainerName", "audit-archive")
    .WaitFor(rabbitmq)
    .WaitFor(keycloak)
    .WaitFor(auditArchiveBlobs);

ServiceDependencies.WireRevocationListReaders(
    new[]
    {
        cameraCatalog, streamDistribution, layoutComposition, eventIngestion,
        overlayDesigner, systemVariables, automation, auditObservability,
    },
    identity,
    revocationListReaderClientSecret);

ServiceDependencies.GateOnMigrations(
    new[]
    {
        cameraCatalog, streamDistribution, layoutComposition, eventIngestion,
        overlayDesigner, systemVariables, automation, identity, auditObservability,
    },
    migrations);

auditObservability.WithIntegrationLaneTuning(eventIngestion, isE2ETests);

// ADR-0106: single YARP API gateway at the edge — fronts all nine context REST
// APIs via service discovery (#1002). CORS/TLS (#1003) and rate limiting (#1004)
// follow. Realtime SignalR (ADR-0152) and WebRTC media stay direct, off the
// gateway, so the latency budget (constitution §IV) is untouched.
//
// Not a WaitFor, on any of the nine below (#2407): YARP resolves each
// destination's address per request, through Aspire service discovery
// (AddServiceDiscoveryDestinationResolver, ApiGateway/Program.cs) — the same
// mechanism that makes a WithReference address resolvable regardless of
// ordering. The address is populated in api-gateway's environment at launch
// whether or not the referenced service has started. No cluster in
// ApiGateway/appsettings.json declares a HealthCheck, so a request that
// reaches a backend before it is listening gets one fast, explicit 502 — it
// is never marked unhealthy, so there is nothing to recover; the very next
// request, once the backend is listening, just succeeds. Every one of the
// nine already waits on its own dependencies — RabbitMQ and Keycloak, plus
// MediaMTX, Mosquitto, Azure Blob Storage or overlay-designer where one applies; not its
// database, which only the migration runner waits on — so chaining
// api-gateway behind all nine as well would inherit the union of those nine
// chains and make it one of the last resources in the stack to reach Running, for
// a guarantee that would still be incomplete — none of the five JavaScript apps below
// wait for api-gateway either (WithReference only), so a request that beats
// a backend here surfaces once, unretried, the same shape of race one layer
// further out that this change does not touch. Same reasoning already
// recorded above on the WithReference(cameraCatalog) call site on
// layout-composition ("a CameraCatalog outage must stop layout authoring
// only") — this is that pattern at nine references instead of one.
var apiGateway = builder
    .AddProject<Projects.SmartSentinelEye_ApiGateway>("api-gateway")
    .WithHttpEndpoint()
    .WithExternalHttpEndpoints()
    .WithReference(cameraCatalog)
    .WithReference(streamDistribution)
    .WithReference(layoutComposition)
    .WithReference(eventIngestion)
    .WithReference(overlayDesigner)
    .WithReference(systemVariables)
    .WithReference(auditObservability)
    .WithReference(automation)
    .WithReference(identity);

// One instance (#2283): ADR-0153 clause 1 pins api-gateway here like every
// other service — its rate limiter is an in-memory FixedWindowRateLimiter,
// per process, with no shared store behind it, so a second replica would
// multiply every caller's budget rather than share it. The HA exception
// clause 2 once allowed for this resource is not exercised; AppHostReplicaCountTests
// pins one instance for every service, api-gateway included.

apiGateway.WithRunLaneRateBudget(isRunMode, isE2ETests);

// React apps per ADR-0074: five Vite dev-server resources (management-web, management-cameras,
// management-event-ingestion, kiosk-web, kiosk-wall) over four pnpm-workspace
// apps under apps/. Skipped in test mode so the integration suite doesn't start
// five Node dev servers.
// Endpoints are proxyless (isProxied: false): the Vite dev server binds the
// fixed PORT (5173-5177) directly, so DCP must NOT also proxy that port — a
// shared port makes Vite fail to start with "Port is already in use".
if (isRunMode && !isE2ETests)
{
    builder.AddWebApplications(apiGateway, keycloak, layoutComposition);
}

// Scenario Simulator (ADR-0111 M1) — dev-only, and it takes three conjuncts to
// say that, because no two of them exclude the same thing (#2013):
//
// - `isRunMode` — absent from publish mode, so it never reaches prod output.
// - `!isE2ETests` — absent from the integration fixture, which drives no
//   browser and would pay for a container and a bind mount nothing there
//   reads. No FFmpeg is wasted: camera-sim's paths are `runOnDemand`, so one
//   starts only when a reader connects, and no reader ever does.
// - `isScenarioSimulatorEnabled` — absent where `ci.yml` passes
//   `ScenarioSimulator=false`, which is the end-to-end job. That job boots a
//   *run-mode* stack, so neither of the other two conjuncts reached it, and the
//   simulator seeded twelve cameras (3 scenarios x 4 assets) and three walls
//   into the catalogue the Playwright specs share — so the kiosk picker's first
//   entry and every camera dropdown carried data no spec put there.
//
// The main `mediamtx.yml` stays clean in all three cases.
//
// - camera-sim: a second, config-clean MediaMTX. Holds NO static paths; the
//   worker provisions a `runOnDemand` loop path per catalog camera via the HTTP
//   API (9997). The loop clip is bind-mounted at /media.
// - scenario-simulator worker: seeds the camera catalog over HTTP
//   (client_credentials via Keycloak), then — driven by CameraRegisteredV1 over
//   RabbitMQ — provisions the looping video on camera-sim. Waits for the things
//   it calls on startup.
if (isRunMode && !isE2ETests && isScenarioSimulatorEnabled)
{
    var cameraSim = builder
        .AddContainer("camera-sim", "bluenviron/mediamtx", "1.21.0-ffmpeg")
        .WithBindMount("Resources/camera-sim.yml", "/mediamtx.yml")
        // The whole clips directory, not one file: an asset names its own clip
        // (spec 044) and mounting them individually would mean editing AppHost
        // every time a scenario gains a station. sim-loop.mp4 lives in here too
        // and stays the default for any camera with no scenario asset.
        .WithBindMount("Resources/clips", "/media")
        .WithHttpEndpoint(targetPort: 9997, name: "api")
        .WithEndpoint(targetPort: 8554, name: "rtsp", scheme: "tcp");

    cameraSim.WithContainerDeveloperLaneLifetime(isPersistentDevStack);

    var scenarioSimulator = builder
        .AddProject<Projects.SmartSentinelEye_ScenarioSimulator>("scenario-simulator")
        .WithReference(cameraCatalog)
        .WithReference(cameraSim.GetEndpoint("api"))
        .WithReference(overlayDesigner)
        .WithReference(automation)
        .WithReference(layoutComposition)
        .WithReference(systemVariables)
        .WithReference(mosquitto.GetEndpoint("mqtt"))
        .WithReference(rabbitmq)
        .WithReference(keycloak)
        .WithEnvironment("ScenarioSimulator__Runtime__ClientSecret", scenarioSimulatorClientSecret)
        // FR-007. The worker validates that an asset's clip exists before
        // provisioning a path for it, and it cannot see camera-sim's bind mount —
        // so the side that owns the mount tells it where the clips are.
        .WithEnvironment("ScenarioSimulator__ClipsDirectory", Path.GetFullPath("Resources/clips"))
        .WaitFor(cameraCatalog)
        .WaitFor(cameraSim)
        .WaitFor(overlayDesigner)
        .WaitFor(automation)
        .WaitFor(layoutComposition)
        .WaitFor(systemVariables)
        .WaitFor(mosquitto)
        .WaitFor(rabbitmq)
        .WaitFor(keycloak);

    // Dashboard grouping: nest the dev-only camera-sim MediaMTX under the
    // simulator worker that provisions + drives its loop paths.
    cameraSim.WithParentRelationship(scenarioSimulator);
}

var app = builder.Build();
StackStatusReport.Attach(app, builder.Configuration["StackStatusFile"]);
await app.RunAsync();
