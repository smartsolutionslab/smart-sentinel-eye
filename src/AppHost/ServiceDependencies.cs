namespace SmartSentinelEye.AppHost;

internal static class ServiceDependencies
{
    // Spec 270 (ADR-0160): every non-Identity API refreshes an in-process
    // revocation snapshot from Identity every 5 s
    // (ServiceDefaults.Revocation.RevokedClientRefresher). WithReference only —
    // not WaitFor — because the refresher is fail-static (Degraded, not down)
    // and must not gate host start on Identity being up (ADR-0160 section 3),
    // the same "must not gate host start" reasoning already applied to the
    // CameraCatalog attribution reference on stream-distribution in AppHost.cs.
    // One small loop rather than eight copies of the same three calls.
    internal static void WireRevocationListReaders(
        IEnumerable<IResourceBuilder<ProjectResource>> consumers,
        IResourceBuilder<ProjectResource> identity,
        IResourceBuilder<ParameterResource> revocationListReaderClientSecret)
    {
        foreach (var consumer in consumers)
        {
            consumer
                .WithReference(identity)
                .WithEnvironment("RevocationList__ClientId", "revocation-list-reader")
                .WithEnvironment("RevocationList__ClientSecret", revocationListReaderClientSecret);
        }
    }

    // Services must not boot until the schema exists — Wolverine builds its outbox
    // storage on startup (AutoBuildMessageStorageOnStartup), and this is the
    // services' only ordering against the data tier: WithReference injects a
    // connection string but waits for nothing.
    //
    // **Both lanes get this, and the `isE2ETests` conjunct is gone (#2137).** It
    // was scoped to the fixture on the reasoning that dev reuses a migrated volume
    // and gating would only cost startup time. That reads the cost and not the
    // failure: in run mode — a developer's `aspire run`, and the end-to-end CI job,
    // which also boots run mode — a `migrations` run that aborted let all nine
    // services start anyway, against a partial schema, reporting healthy. A stack
    // that misbehaves while claiming health is worse than one that refuses; the
    // fixture lane at least went red (#2064).
    //
    // The success path is not made more fragile by this. Every service already
    // waits for rabbitmq and keycloak, and `migrations` itself already waits for
    // all nine databases and keycloak, so nothing here can be reached that could
    // not be reached before — the services simply start a few seconds later, in an
    // order the graph half-imposed already. There is no database-less mode to
    // break: postgres is unconditional and every service references its own
    // database. `WaitForCompletion` defaults to expecting exit code 0, so an
    // aborted run is what stops them, not merely a finished one.
    internal static void GateOnMigrations(
        IEnumerable<IResourceBuilder<ProjectResource>> dependents,
        IResourceBuilder<ProjectResource> migrations)
    {
        foreach (var dependent in dependents)
        {
            dependent.WaitForCompletion(migrations);
        }
    }
}
