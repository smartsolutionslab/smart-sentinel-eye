namespace SmartSentinelEye.AppHost;

// #2254 / spec 188: `builder.AddParameter(name, literal, secret)` — the
// overload every call site in AppHost.cs used — never reads
// `builder.Configuration`, so a `Parameters:<name>=<value>` command-line
// argument was silently inert. This wraps the same overload so the
// configuration key Aspire's own configuration-backed overloads would read
// takes precedence over the literal, without setting `ParameterResource.Default`
// (which would leak secret defaults into `aspire publish` manifests).
//
// `Parameters:<name>` is read from `builder.Configuration`, so anything that
// provider chain feeds it works: a `Parameters__<name>` environment variable
// or a user-secrets entry resolve to the same key, not just a literal
// `Parameters:<name>=...` command-line argument — and for a real secret,
// prefer one of those two, since a command-line argument sits in process
// listings and shell history on a shared machine.
//
// Trap for `PostgresPassword`, `KeycloakPassword` and `RabbitMqPassword`
// specifically: in run mode (`isRunMode && !isE2ETests` in AppHost.cs),
// postgres, keycloak and rabbitmq run with `ContainerLifetime.Persistent` +
// `WithDataVolume()`, and all three only apply a credential at
// container-initialization time (Postgres's `POSTGRES_PASSWORD` at initdb,
// Keycloak's admin bootstrap on an empty DB, RabbitMQ's `default_pass` on a
// fresh Mnesia directory). Overriding one of these three via `Parameters:`
// changes what the *service* expects but not what the *already-initialized
// persistent container* holds, so every consumer is handed a new credential
// against a server still running the old one — surfacing as stack-wide auth
// failures under a `FailedToStart` with no useful log. Fix: `docker rm` the
// affected container **and its data volume**, not just the container (see
// this repo's own recorded traps: a reused `keycloak-data` volume keeps a
// stale realm, and a persistent container keeps its old args). The other
// seven parameters (`PostgresUser` and the six `*ClientSecret` values) are
// injected into services only and have no such trap.
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
