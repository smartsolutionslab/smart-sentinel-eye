using System.Globalization;
using Aspire.Hosting.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
// sorted by name (ordinal) so a diff between two boots reads cleanly. This is
// what actually keeps a secret out of the file that a red CI run prints
// publicly — the shape has exactly three fields, and none of them is
// Snapshot.Properties, an endpoint URL, or a parameter value. Phase 6 review
// corrected an earlier version of this comment that credited the
// ParameterResource filter below with that guarantee: the filter decides
// *which resources* get a line, but it is this fixed shape that decides
// *what a line can ever say*. Whoever adds a fourth field later is the actual
// risk; the type filter was never it.
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
    /// <c>builder.AddOverridableParameter(...)</c> produces for every secret and
    /// credential in this AppHost (<c>PostgresPassword</c>,
    /// <c>KeycloakPassword</c>, the four <c>*ClientSecret</c> parameters,
    /// etc.). Not <see cref="IResourceWithoutLifetime"/>, which plan.md §2.2
    /// originally named: T001's live-boot spike (#2268 PR body) found that
    /// nothing in Aspire.Hosting 13.5.3 implements that marker any more —
    /// <see cref="ParameterResource"/> included — so filtering on it would
    /// have excluded nothing. This filter keeps parameter *names* out of the
    /// report's resource set entirely (there is no reason to poll a resource
    /// that can never be <c>Running</c>); it is the fixed 3-field line shape
    /// above, not this filter, that keeps a parameter's *value* out even if a
    /// filter like this one were ever wrong again. This AppHost has no
    /// <c>AddConnectionString</c> resource, so <see cref="ParameterResource"/>
    /// is the only lifetime-less type this composition ever produces.</item>
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
        HashSet<string> expectedNames = new(names, StringComparer.Ordinal);

        // Keyed by "the most specific identity known so far": the bare
        // resource name until a real event arrives, then that event's
        // ResourceId. A resource with WithReplicas(2) (api-gateway,
        // ADR-0153 clause 2 — the e2e job's own run-mode shape,
        // AppHostReplicaCountTests) publishes one ResourceEvent per replica,
        // all sharing Resource.Name but each carrying a distinct ResourceId
        // (AspireFixture.cs:1154's TryResolveResourceId is why this repo
        // already knows ResourceId, not Name, is the per-instance key).
        // Keying this dictionary by Name alone let one replica's later event
        // silently overwrite the other's — phase 6 review's finding. Folded
        // back to one line per name in WriteReport via "worst state wins" so
        // the report's one-line-per-resource-name shape (plan.md §2.4, which
        // the gate parses byte-for-byte) never has to change.
        Dictionary<string, ResourceStatusLine> instances = new(StringComparer.Ordinal);
        foreach (string name in names)
        {
            instances[name] = new ResourceStatusLine(name, notReported, string.Empty);
        }

        WriteReport(path, instances.Values);

        // ILogger<StackStatusReport> does not compile — this is a static
        // class, and static types cannot be generic type arguments (CS0718)
        // — so the category name is created directly instead.
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(StackStatusReport).FullName ?? nameof(StackStatusReport));

        app.Services.GetRequiredService<IDistributedApplicationEventing>()
            .Subscribe<AfterResourcesCreatedEvent>((evt, cancellationToken) =>
            {
                _ = WatchAndWriteAsync(app.ResourceNotifications, path, expectedNames, instances, logger, cancellationToken);
                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// Watches for as long as the application runs. Wrapped end to end: this
    /// task is started fire-and-forget (<c>Attach</c> cannot await it without
    /// blocking the AppHost's own startup), so an unhandled exception here
    /// would otherwise freeze the file at its last content with nothing
    /// recorded anywhere — silently masking a since-degraded stack if it
    /// froze after everything reached <c>Running</c>, or producing a stale,
    /// causeless timeout if it froze earlier (phase 6 review's finding). On
    /// a genuine fault this logs via <see cref="ILogger"/> and appends a
    /// <c>&#35; watch ended: &lt;reason&gt;</c> line so a report frozen by a
    /// crash reads differently from one that is simply between events.
    /// <see cref="OperationCanceledException"/> from normal application
    /// shutdown is not logged as a fault — it is the expected way this loop
    /// ends.
    /// </summary>
    private static async Task WatchAndWriteAsync(
        ResourceNotificationService notifications,
        string path,
        HashSet<string> expectedNames,
        Dictionary<string, ResourceStatusLine> instances,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ResourceEvent resourceEvent in notifications.WatchAsync(cancellationToken))
            {
                string name = resourceEvent.Resource.Name;
                if (!expectedNames.Contains(name))
                {
                    // Not part of the expected set (e.g. a ParameterResource,
                    // or a "-rebuilder" companion) — not this report's
                    // business; ExpectedResourceNames already decided what
                    // belongs.
                    continue;
                }

                string instanceKey = string.IsNullOrEmpty(resourceEvent.ResourceId) ? name : resourceEvent.ResourceId;
                instances[instanceKey] = new ResourceStatusLine(
                    name,
                    resourceEvent.Snapshot.State?.Text ?? notReported,
                    resourceEvent.Snapshot.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

                // Retire the seed placeholder once real per-instance data
                // starts flowing for this name — otherwise a stale
                // "(not reported)" entry keyed under the bare name would sit
                // in the fold forever alongside the real per-replica entries
                // and permanently outrank a fully-Running resource.
                if (!string.Equals(instanceKey, name, StringComparison.Ordinal))
                {
                    instances.Remove(name);
                }

                WriteReport(path, FoldWorstPerName(instances.Values));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the application is shutting down.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Stack status watch loop ended unexpectedly; {Path} will not be updated further", path);
            WriteReport(path, FoldWorstPerName(instances.Values), watchEndedComment: $"# watch ended: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// One line per resource <b>name</b> even when several instances share
    /// it (replicas): picks the worst-ranked <see cref="Severity"/> among
    /// them, so a half-dead multi-replica resource is never reported as
    /// healthy because a sibling instance happened to write last.
    /// </summary>
    private static IEnumerable<ResourceStatusLine> FoldWorstPerName(IEnumerable<ResourceStatusLine> instances) =>
        instances
            .GroupBy(line => line.Name, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(line => Severity(line.State, line.ExitCode)).First());

    /// <summary>
    /// How bad a single instance's state is, worst-wins ordering for
    /// <see cref="FoldWorstPerName"/>. Deliberately coarser than the gate
    /// script's own classification — this only has to make sure a bad
    /// sibling instance is never hidden behind a healthy one, not restate
    /// scripts/wait-for-e2e-stack.sh's rules in C#.
    /// </summary>
    private static int Severity(string state, string exitCode)
    {
        if (state == "Running")
        {
            return 0;
        }

        if (state == "Finished" && exitCode == "0")
        {
            return 1;
        }

        if ((state is "Finished" or "Exited") && !string.IsNullOrEmpty(exitCode) && exitCode != "0")
        {
            return 3;
        }

        if (state is "FailedToStart" or "Terminated")
        {
            return 3;
        }

        return 2;
    }

    /// <summary>
    /// Writes atomically: a temp file in the same directory, then
    /// <see cref="File.Move(string, string, bool)"/> with overwrite. The gate
    /// polls this file every few seconds, so a half-written read must be
    /// impossible, not merely unlikely.
    /// </summary>
    private static void WriteReport(string path, IEnumerable<ResourceStatusLine> lines, string? watchEndedComment = null)
    {
        List<string> reportLines = [$"# stack-status v1 {DateTime.UtcNow:O}"];
        reportLines.AddRange(
            lines
                .OrderBy(line => line.Name, StringComparer.Ordinal)
                .Select(line => $"{line.Name}\t{line.State}\t{line.ExitCode}"));

        if (watchEndedComment is not null)
        {
            reportLines.Add(watchEndedComment);
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        File.WriteAllLines(tempPath, reportLines);
        File.Move(tempPath, path, overwrite: true);
    }

    private readonly record struct ResourceStatusLine(string Name, string State, string ExitCode);
}
