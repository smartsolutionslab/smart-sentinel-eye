namespace SmartSentinelEye.AppHost;

// #2254 / spec 188: `builder.AddParameter(name, literal, secret)` — the
// overload every call site in AppHost.cs used — never reads
// `builder.Configuration`, so a `Parameters:<name>=<value>` command-line
// argument was silently inert. This wraps the same overload so the
// configuration key Aspire's own configuration-backed overloads would read
// takes precedence over the literal, without setting `ParameterResource.Default`
// (which would leak secret defaults into `aspire publish` manifests).
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
