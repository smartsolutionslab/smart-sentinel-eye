using SmartSentinelEye.Architecture.Tests.Persistence;

namespace SmartSentinelEye.Architecture.Tests.HealthChecks;

/// <summary>
/// Spec 339 (#2571), plan §2 and §4. The ten hosts under <c>src/</c> that call
/// <c>app.MapDefaultEndpoints()</c> and so serve <c>/health</c> — ApiGateway
/// and the nine context APIs. <c>MigrationRunner</c> and
/// <c>ScenarioSimulator</c> are worker hosts that map no endpoint and are out
/// of scope (spec §1.4). <see cref="ServedHealthHostCompletenessTests"/> is
/// the guard that this list stays complete as new hosts are added.
/// </summary>
internal static class ServedHealthHosts
{
    /// <summary>
    /// One host: its real entry-point assembly name, and the project
    /// directory <see cref="ComposedHost"/> uses as the content root so the
    /// host reads its own shipped <c>appsettings.json</c> (plan §4 — this
    /// matters for ApiGateway, whose <c>ReverseProxy</c> section is read at
    /// registration time).
    /// </summary>
    internal sealed record Host(string AssemblyName, string ProjectDirectory);

    private static readonly Host[] Hosts =
    [
        new("SmartSentinelEye.ApiGateway", "src/ApiGateway"),
        new("SmartSentinelEye.AuditObservability.Api", "src/AuditObservability/Api"),
        new("SmartSentinelEye.Automation.Api", "src/Automation/Api"),
        new("SmartSentinelEye.CameraCatalog.Api", "src/CameraCatalog/Api"),
        new("SmartSentinelEye.EventIngestion.Api", "src/EventIngestion/Api"),
        new("SmartSentinelEye.Identity.Api", "src/Identity/Api"),
        new("SmartSentinelEye.LayoutComposition.Api", "src/LayoutComposition/Api"),
        new("SmartSentinelEye.OverlayDesigner.Api", "src/OverlayDesigner/Api"),
        new("SmartSentinelEye.StreamDistribution.Api", "src/StreamDistribution/Api"),
        new("SmartSentinelEye.SystemVariables.Api", "src/SystemVariables/Api"),
    ];

    /// <summary>
    /// The placeholder configuration every host is composed with. Reuses
    /// <see cref="PostgresComposition.ConfigurationFor"/>'s key set rather
    /// than duplicating it (FR-008): the union of what each of the nine
    /// contexts resolves, given to every host including ApiGateway, which
    /// reads none of it. An unread key is inert (spec §1.3). Every value is
    /// syntactically valid and never dialled.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string?> PlaceholderConfiguration = BuildConfiguration();

    internal static IReadOnlyList<Host> All => Hosts;

    public static TheoryData<string> AllHosts()
    {
        TheoryData<string> data = [];
        foreach (Host host in Hosts)
        {
            data.Add(host.AssemblyName);
        }

        return data;
    }

    internal static Host Resolve(string assemblyName) =>
        Hosts.FirstOrDefault(host => host.AssemblyName == assemblyName)
        ?? throw new ArgumentOutOfRangeException(
            nameof(assemblyName), assemblyName, "Not one of the ten hosts ServedHealthHosts lists.");

    internal static string ContentRootOf(Host host) =>
        Path.Combine(RepositorySource.Root().FullName, host.ProjectDirectory);

    private static Dictionary<string, string?> BuildConfiguration()
    {
        Dictionary<string, string?> configuration = [];
        foreach (string contextName in PostgresComposition.ContextNames)
        {
            foreach (KeyValuePair<string, string?> entry in PostgresComposition.ConfigurationFor(contextName))
            {
                configuration[entry.Key] = entry.Value;
            }
        }

        return configuration;
    }
}
