using System.Globalization;
using Aspire.Hosting.Eventing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSentinelEye.AppHost;

// Writer for #2268 / spec 181. `scripts/wait-for-e2e-stack.sh` waited on three
// ports, a gateway route and nine databases' migration history and never asked
// whether the resources the AppHost actually composed reached a started
// state, so minio FailedToStart and audit-observability stuck Waiting on it
// (#2264) satisfied every probe and the gate opened over a stack that was
// missing a service. This writes what the gate reads: every resource the
// AppHost composed, and the state each reached.
//
// Format (plan.md §2.4 — fixed, because the gate parses it byte-for-byte):
//
//   # stack-status v1 <ISO-8601 UTC timestamp>
//   <name>\t<state>\t<exit code or empty>
//
// One header line the gate ignores, then one tab-separated line per resource,
// sorted by name (ordinal) so a diff between two boots reads cleanly. Nothing
// else ever goes in — no properties, URLs, connection strings or parameter
// values — because this file is printed into a public CI log on a red run.
public static class StackStatusReport
{
    private const string notReported = "(not reported)";

    /// <summary>
    /// The resource set a status report should name: every resource in
    /// <paramref name="resources"/> except two categories that can never
    /// reach <c>Running</c> on their own, both excluded by Aspire's own
    /// type/annotation markers rather than a hand-written name list
    /// (spec.md §5's rejected options are all hand-written lists of one
    /// shape or another):
    ///
    /// <list type="number">
    /// <item><see cref="ParameterResource"/> — the type
    /// <c>builder.AddParameter(...)</c> produces for every secret and
    /// credential in this AppHost (<c>PostgresPassword</c>,
    /// <c>KeycloakPassword</c>, the four <c>*ClientSecret</c> parameters,
    /// etc.). Not <see cref="IResourceWithoutLifetime"/>, which plan.md §2.2
    /// originally named: T001's live-boot spike (#2268 PR body) found that
    /// nothing in Aspire.Hosting 13.5.3 implements that marker any more —
    /// <see cref="ParameterResource"/> included — so filtering on it would
    /// have excluded nothing and every secret parameter would have landed in
    /// the file. This AppHost has no <c>AddConnectionString</c> resource, so
    /// <see cref="ParameterResource"/> is the only lifetime-less type this
    /// composition ever produces.</item>
    /// <item>Resources carrying <see cref="ExplicitStartupAnnotation"/> —
    /// Aspire's own marker for "does not start unless told to", which
    /// <see cref="ResourceNotificationService"/> itself reads for exactly
    /// this reason (it is why the fixture's own wait helpers tolerate
    /// <c>NotStarted</c> for such a resource). T007's live boot (PR body)
    /// found every <c>AddProject&lt;T&gt;</c> resource gets a same-named
    /// <c>&lt;name&gt;-rebuilder</c> companion (Aspire's built-in
    /// "Rebuild"-command resource, internal <c>ProjectRebuilderResource</c>)
    /// that sits at <c>NotStarted</c> for the life of the process — it only
    /// runs if someone invokes the dashboard's Rebuild command. Without this
    /// exclusion the resource gate would time out on every single boot,
    /// including a fully healthy one, since these companions never reach
    /// <c>Running</c>. This is plan.md §5's "resource legitimately not
    /// Running" risk, resolved by an annotation filter rather than the name
    /// list the risk's own mitigation warned against.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> ExpectedResourceNames(IEnumerable<IResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        List<string> names =
        [
            .. resources
                .Where(resource => resource is not ParameterResource)
                .Where(resource => !resource.HasAnnotationOfType<ExplicitStartupAnnotation>())
                .Select(resource => resource.Name),
        ];
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Wires the writer into a built application. Seeds <paramref
    /// name="path"/> with every composed resource at <c>(not reported)</c>
    /// and writes it immediately — a gate that finds the file already
    /// present and full of that state is reading a live boot; a gate that
    /// finds nothing knows the switch was not set — then watches
    /// <see cref="ResourceNotificationService"/> and rewrites the whole file
    /// on every event for as long as the application runs.
    ///
    /// <para>
    /// Does nothing when <paramref name="path"/> is null, empty or
    /// whitespace: the <c>StackStatusFile=</c> switch was not passed, which
    /// is the case for a developer's plain <c>aspire run</c>.
    /// </para>
    ///
    /// <para>
    /// The watch is started from <see cref="AfterResourcesCreatedEvent"/>,
    /// not <see cref="Microsoft.Extensions.Hosting.IHostApplicationLifetime.
    /// ApplicationStarted"/> — both were wired side by side for T001's spike
    /// and fired within 4ms of each other for a <c>dotnet run</c>-driven
    /// boot, so either works; <c>AfterResourcesCreatedEvent</c> is Aspire's
    /// own purpose-built hook for this rather than a generic host lifecycle
    /// event that happens to coincide. Subscribing any earlier does not
    /// work: <c>AspireFixture.cs:305</c> records that
    /// <c>ResourceNotificationService.WatchAsync</c> called before the host
    /// has started "has nothing to watch and completes" immediately.
    /// </para>
    /// </summary>
    public static void Attach(DistributedApplication app, string? path)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        DistributedApplicationModel model = app.Services.GetRequiredService<DistributedApplicationModel>();
        IReadOnlyList<string> names = ExpectedResourceNames(model.Resources);

        Dictionary<string, ResourceStatusLine> lines = new(StringComparer.Ordinal);
        foreach (string name in names)
        {
            lines[name] = new ResourceStatusLine(name, notReported, string.Empty);
        }

        WriteReport(path, lines.Values);

        app.Services.GetRequiredService<IDistributedApplicationEventing>()
            .Subscribe<AfterResourcesCreatedEvent>((evt, cancellationToken) =>
            {
                _ = WatchAndWriteAsync(app.ResourceNotifications, path, lines, cancellationToken);
                return Task.CompletedTask;
            });
    }

    private static async Task WatchAndWriteAsync(
        ResourceNotificationService notifications,
        string path,
        Dictionary<string, ResourceStatusLine> lines,
        CancellationToken cancellationToken)
    {
        await foreach (ResourceEvent resourceEvent in notifications.WatchAsync(cancellationToken))
        {
            string name = resourceEvent.Resource.Name;
            if (!lines.ContainsKey(name))
            {
                // Not part of the expected set (e.g. a ParameterResource, or
                // a Vite "-rebuilder" companion resource added after Attach
                // seeded the file) — not this report's business (spec.md §3's
                // "no secrets" scenario, and ExpectedResourceNames already
                // decided what belongs).
                continue;
            }

            lines[name] = new ResourceStatusLine(
                name,
                resourceEvent.Snapshot.State?.Text ?? notReported,
                resourceEvent.Snapshot.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

            WriteReport(path, lines.Values);
        }
    }

    /// <summary>
    /// Writes atomically: a temp file in the same directory, then
    /// <see cref="File.Move(string, string, bool)"/> with overwrite. The gate
    /// polls this file every few seconds, so a half-written read must be
    /// impossible, not merely unlikely.
    /// </summary>
    private static void WriteReport(string path, IEnumerable<ResourceStatusLine> lines)
    {
        List<string> reportLines = [$"# stack-status v1 {DateTime.UtcNow:O}"];
        reportLines.AddRange(
            lines
                .OrderBy(line => line.Name, StringComparer.Ordinal)
                .Select(line => $"{line.Name}\t{line.State}\t{line.ExitCode}"));

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        File.WriteAllLines(tempPath, reportLines);
        File.Move(tempPath, path, overwrite: true);
    }

    private readonly record struct ResourceStatusLine(string Name, string State, string ExitCode);
}
