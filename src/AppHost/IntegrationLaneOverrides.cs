namespace SmartSentinelEye.AppHost;

internal static class IntegrationLaneOverrides
{
    // Spec 208: `/streams/authorize` is rate-limited by source address, and the
    // production ceiling (2000/min) is too high for a test to exhaust without
    // sending thousands of requests and poisoning every other test sharing the
    // fixture's one address for the rest of the window. Lower it for the
    // integration lane only.
    //
    // **Gated `isE2ETests` and deliberately not `isRunMode`**, matching the
    // asymmetry already documented in AppHost.cs's fixture-video block:
    // `E2ETests` is set by the *integration* fixture (`AspireFixture`) and by
    // `AppHostE2ESwitchTests`, but not by the end-to-end stack boot, which is a
    // plain `dotnet run` in `ci.yml`. That is exactly what is wanted here — the integration suite gets
    // a ceiling it can actually cross to observe a 429, while the Playwright e2e
    // wall keeps opening tiles against the real production ceiling so it proves
    // the limiter does not clip a real wall.
    //
    // PermitLimit=50, not 30 or 20 (spec 208 review S8): every test host request
    // to /streams/authorize inside AspireCollection's one serialised stack shares
    // this exact partition, and the accounting has to sum every consumer, not
    // just the one that first motivated raising it:
    //   - WhepHandshakeLatencyTests (spec 002/#2149): 1 warm-up + 20 measured
    //     authorize calls = 21. This was the original motivation — 20 left zero
    //     headroom, so its 21st call was refused with 429 every run (#2284
    //     phase-5 verification.md §2.1).
    //   - WhepAuthIntegrationTests: 9 more authorize POSTs, all from the same
    //     test host address. This was 6 when the ceiling below was first raised,
    //     then 12 after three fab-attribution facts (ADR-0161) and three
    //     `api`-action facts (ADR-0171) were added without this count being
    //     revisited. It is 9, not 12, as of #2862 blocker 2: the three
    //     `api`-action facts now key the "whep-authorize" policy's partition by
    //     action as well as source address (Program.cs's
    //     WhepAuthorizeActionClassification), so they draw from a separate
    //     budget and no longer belong in this sum at all. Re-count with a search
    //     for `/streams/authorize` in that file before trusting this figure
    //     again.
    //   - WhepAuthorizeRateLimitTests' own ExhaustWindowAsync (PermitLimit + 1
    //     requests) needs the ceiling itself to still be a small, fast number.
    //     Every request it sends carries a non-`api` action, so it too stays on
    //     this partition.
    // 21 + 9 = 30 against the ceiling of 50 below leaves comfortable margin.
    // #2862 blocker 2 is also why this sum no longer has to include the health
    // sweep's own batched control-API calls or this fixture's MediaMTX resets
    // (ResetMediaMtxAsync, MediaMtxControlApiAuthIntegrationTests,
    // MediaMtxReconcilerIntegrationTests, MediaMtxRtspGatewayIntegrationTests):
    // every one of those authorizes as the `api` action and now lands on its own
    // partition, so none of that traffic competes with this one any more. Keep
    // tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs's
    // own `PermitLimit` literal in sync with this value.
    //
    // #2871: "none of that traffic competes with this one any more" was true but
    // incomplete — it says the `api` partition stopped crowding the `whep`
    // partition, not that the `api` partition's OWN total was ever checked
    // against a ceiling. It wasn't, and under a full suite run it is not small:
    // the 2-second health sweep runs for the whole shard's lifetime, the
    // reconciler's startup pass adds a one-time burst, and every
    // StreamDistribution-touching test's own ResetMediaMtxAsync/add/list/delete/
    // repoint calls land in the same fixed window, all from this one test host
    // address. Before this fix `api` silently shared this exact
    // PermitLimit=50/Window=10s override (same policy, just a different
    // partition key), and that shared ceiling is what #2871 traced the
    // intermittent MediaMtxReconcilerIntegrationTests/StreamHealthTransitionTests
    // 401s to: MediaMTX reports ANY non-2xx answer from the authorize hook — a
    // 429 from us included — to its own caller as a flat 401 (confirmed by
    // hammering MediaMTX's control API with 150 concurrent, correctly-credentialed
    // calls under the old shared ceiling: 46 got through, 104 came back 401).
    // Deliberately NOT overridden here for `api`: Program.cs's
    // MediaMtxApiAuthorizeRateLimiting falls back to the same production
    // default as WhepAuthorizeRateLimiting (2000/min, appsettings.json), which
    // is already two orders of magnitude above the `api` partition's real
    // aggregate test-suite volume and needs no integration-lane-specific
    // ceiling the way the `whep` partition does.
    internal static void WithIntegrationLaneWhepCeiling(
        this IResourceBuilder<ProjectResource> streamDistribution,
        bool isE2ETests)
    {
        if (isE2ETests)
        {
            streamDistribution
                .WithEnvironment("WhepAuthorizeRateLimiting__PermitLimit", "50")
                .WithEnvironment("WhepAuthorizeRateLimiting__Window", "00:00:10");
        }
    }

    // Spec 378: spec 099's outage technique deliberately leaves a MediaMTX path
    // diverged or absent for seconds, and the unresolved-fab audit tests rely on
    // MediaMTX staying as they left it; a 10 s repair sweep would heal it under
    // them. The startup pass still repairs once; the sweep is parked at the largest
    // interval PeriodicTimer accepts (~49.7 days): any finite interval shorter than a
    // shard can tick inside the spec 099 outage windows and flake those tests.
    internal static void WithIntegrationLaneSourceRepairInterval(
        this IResourceBuilder<ProjectResource> streamDistribution,
        bool isE2ETests)
    {
        if (isE2ETests)
        {
            streamDistribution.WithEnvironment("MediaMtxSourceRepair__Interval", TimeSpan.FromMilliseconds(uint.MaxValue - 1).ToString());
        }
    }

    internal static void WithIntegrationLaneTuning(
        this IResourceBuilder<ProjectResource> auditObservability,
        IResourceBuilder<ProjectResource> eventIngestion,
        bool isE2ETests)
    {
        if (isE2ETests)
        {
            // Sweep retention every few seconds in the integration suite so the
            // round-trip test isn't waiting on the production daily timer.
            auditObservability.WithEnvironment("AuditObservability__Retention__TickInterval", "00:00:03");

            // The ingest breakdown's stamps, for the integration suite only. The type
            // default stays false (AuditMeasurementSwitchTests) because the stamps sit
            // on a write path and production does not pay for an instrument nobody
            // reads. But a run that has to remember a shell export is a run whose
            // breakdown silently reports zeros, so the fixture turns it on rather than
            // asking (spec 109 US1).
            auditObservability.WithEnvironment("AuditObservability__Measurement__RecordIngestBreakdown", "true");

            // The write limiter's 429 seam is unreachable at the production default of
            // 64 concurrent slots; pinning it to 1 for the integration suite is the
            // only channel the collection fixture has to reach it per-test-lane
            // (spec 223 US1). Stack-wide for every test in this collection: any other
            // integration test that fires concurrent /events/manual or
            // /events/webhook writes will now see 429s too. Only
            // IngestBackpressureIntegrationTests does today (spec 223 §*Blast
            // radius* swept all fifteen write-endpoint consumers) — if you add a
            // second one, it inherits this ceiling, the same way whep-authorize's
            // PermitLimit comment tracks its shared-partition consumers.
            eventIngestion.WithEnvironment("EventIngestion__IngestWrite__Concurrency", "1");
        }
    }
}
