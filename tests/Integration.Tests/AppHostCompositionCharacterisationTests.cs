using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Spec 354 (#2898) characterisation C1: pins the <b>whole composed application
/// model</b> of <c>src/AppHost/AppHost.cs</c> before it is split into extension
/// methods. The split must move no resource, no annotation, no environment
/// value and no condition, so the snapshot records every one of them, in the
/// order they were composed.
///
/// <para>
/// Each lane is built in memory (<c>CreateAsync</c> starts no resources, so this
/// needs no Docker and is safe beside a live <c>aspire run</c> — the footing of
/// <see cref="AppHostStackPersistenceTests"/> and
/// <see cref="AppHostGatewayRateBudgetTests"/>) and rendered to text: every
/// resource in <c>Resources</c> order, every annotation of it in list order with
/// its public scalar state, and for each environment / command-line callback the
/// entries it writes, in write order. Values are rendered by their manifest
/// expression where there is one (a secret parameter is never resolved to its
/// value). The result is compared line by line with the committed
/// <c>AppHostComposition/&lt;Lane&gt;.snapshot.txt</c>.
/// </para>
///
/// <para>
/// <b>The snapshots were captured once from the unrefactored tree and are
/// literals.</b> There is deliberately no update switch, environment flag or
/// regeneration path here: a snapshot line that has to change after a refactor
/// is evidence the composition moved, and the refactor is reverted, not the
/// snapshot adjusted.
/// </para>
///
/// <para>
/// <b>Publish mode is built in memory too</b> (<c>--operation publish</c> with the manifest
/// publisher; nothing is written), so the <c>isRunMode == false</c> branches are pinned by the
/// <c>Publish</c> lane and its callbacks are gathered under the publish execution context.
/// <b>Not rendered:</b> annotation members that are not scalars (delegates, nested graphs
/// deeper than three levels render as a type name) — their presence and position are pinned,
/// their content is not.
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostCompositionCharacterisationTests
{
    private const string SnapshotDirectory = "AppHostComposition";

    private static readonly string[] BaseArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
        // The testing builder loads the AppHost's user secrets, which on a developer machine
        // carry a generated OTLP and dashboard API key (and persisted ports); a CI runner has
        // none. A command-line value outranks every other source, so pinning both keys makes
        // the OTLP wiring present with deterministic values on every machine.
        "AppHost:OtlpApiKey=hermetic-otlp-api-key",
        "AppHost:DashboardApiKey=hermetic-dashboard-api-key",
        // The same builder also honours these process environment variables; pinned for the
        // same reason (a shell that exports either composes a different model).
        "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://localhost:4317",
        "--environment",
        "Development",
    ];

    public static TheoryData<string> Lanes =>
        ["Developer", "EndToEndJob", "IntegrationFixture", "Unparseable", "Publish"];

    [Theory]
    [MemberData(nameof(Lanes))]
    public async Task The_composed_model_matches_the_committed_snapshot_of_the_lane(string lane)
    {
        var actual = await RenderAsync(ArgumentsFor(lane));

        var expected = ReadSnapshot(lane);

        var actualLines = actual.Split('\n');
        var expectedLines = expected.Split('\n');

        var firstDifference = FirstDifference(expectedLines, actualLines);

        if (firstDifference is not { } index)
        {
            return;
        }

        var resource = ResourceAbove(expectedLines, actualLines, index);
        var expectedLine = index < expectedLines.Length ? expectedLines[index] : "<end of snapshot>";
        var actualLine = index < actualLines.Length ? actualLines[index] : "<end of model>";
        var differing = Enumerable
            .Range(0, Math.Min(expectedLines.Length, actualLines.Length))
            .Count(position => !string.Equals(expectedLines[position], actualLines[position], StringComparison.Ordinal));

        throw new Xunit.Sdk.XunitException(
            $"lane '{lane}', resource '{resource}': the composed model differs from "
            + $"{SnapshotDirectory}/{lane}.snapshot.txt at line {index + 1}.{Environment.NewLine}"
            + $"  snapshot: {expectedLine}{Environment.NewLine}"
            + $"  composed: {actualLine}{Environment.NewLine}"
            + $"  ({differing} lines differ position by position; snapshot {expectedLines.Length} lines, composed {actualLines.Length}){Environment.NewLine}"
            + "The split of AppHost.cs must not move the composition; revert the production change "
            + "rather than editing the snapshot (spec 354, ADR-0144).");
    }

    private static string[] ArgumentsFor(string lane) =>
        lane switch
        {
            "Publish" =>
            [
                .. BaseArguments,
                "--operation",
                "publish",
                "--publisher",
                "manifest",
                "--output-path",
                "pub.json",
            ],
            "Developer" => BaseArguments,
            "EndToEndJob" => [.. BaseArguments, "ScenarioSimulator=false", "PersistentStack=false"],
            "IntegrationFixture" => [.. BaseArguments, "E2ETests=true"],
            "Unparseable" =>
            [
                .. BaseArguments,
                "ScenarioSimulator=nope",
                "PersistentStack=nope",
                "E2ETests=nope",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "unknown lane"),
        };

    private static int? FirstDifference(string[] expected, string[] actual)
    {
        var shared = Math.Min(expected.Length, actual.Length);

        for (var index = 0; index < shared; index++)
        {
            if (!string.Equals(expected[index], actual[index], StringComparison.Ordinal))
            {
                return index;
            }
        }

        return expected.Length == actual.Length ? null : shared;
    }

    private static string ResourceAbove(string[] expected, string[] actual, int index)
    {
        var source = index < actual.Length ? actual : expected;

        for (var line = Math.Min(index, source.Length - 1); line >= 0; line--)
        {
            if (source[line].StartsWith("resource ", StringComparison.Ordinal))
            {
                return source[line];
            }
        }

        return "<before the first resource>";
    }

    private static string ReadSnapshot(string lane)
    {
        var path = System.IO.Path.Combine(
            RepositoryRoot(),
            "tests",
            "Integration.Tests",
            SnapshotDirectory,
            $"{lane}.snapshot.txt");

        return System.IO.File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }

    private static async Task<string> RenderAsync(string[] arguments)
    {
        using var builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(arguments);

        var root = RepositoryRoot();
        StringBuilder output = new();
        using var services = builder.Services.BuildServiceProvider();
        DistributedApplicationExecutionContext executionContext = new(
            new DistributedApplicationExecutionContextOptions(builder.ExecutionContext.Operation)
            {
                Services = services,
            });

        foreach (var resource in builder.Resources)
        {
            output.Append("resource ").Append(resource.Name).Append(" : ").Append(resource.GetType().Name).Append('\n');

            var index = 0;
            foreach (var annotation in resource.Annotations.Where(candidate => !IsPlatformDependent(candidate)))
            {
                output.Append("  [").Append(index++).Append("] ").Append(RenderAnnotation(annotation)).Append('\n');

                foreach (var entry in await CallbackEntriesAsync(resource, annotation, executionContext))
                {
                    output.Append("      ").Append(entry).Append('\n');
                }
            }
        }

        return Normalise(output.ToString(), root).TrimEnd('\n');
    }

    // Three values differ per machine or per build rather than per composition: the repository
    // location, the hash Aspire derives from the AppHost project path for volume names, and the
    // random image tag of a Dockerfile build. They are replaced by a marker so the snapshot is
    // the same on a developer machine and on CI; everything around them stays pinned.
    // Aspire adds this annotation to every project resource only on Windows (guarded by
    // OperatingSystem.IsWindows() in ProjectResourceBuilderExtensions), so a Linux CI runner and a
    // Windows developer machine compose a different annotation list from identical AppHost code.
    // It is the framework's own platform default, not composition this repository writes, so it is
    // left out of the snapshot (and out of the index numbering) rather than pinned per platform.
    private static bool IsPlatformDependent(IResourceAnnotation annotation) =>
        annotation is CertificateAuthorityCollectionAnnotation;

    private static string Normalise(string text, string root)
    {
        var slashed = text
            .Replace(root, "<repo>", StringComparison.OrdinalIgnoreCase)
            .Replace(root.Replace('\\', '/'), "<repo>", StringComparison.OrdinalIgnoreCase)
            .Replace('\\', '/');

        var volumes = Regex.Replace(
            slashed,
            @"smartsentineleye\.apphost-[0-9A-Fa-f]+-",
            "smartsentineleye.apphost-<hash>-",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        return Regex.Replace(
            volumes,
            "ImageTag=\"[0-9A-Fa-f]{40}\"",
            "ImageTag=\"<hash>\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
    }

    private static async Task<List<string>> CallbackEntriesAsync(
        IResource resource,
        IResourceAnnotation annotation,
        DistributedApplicationExecutionContext executionContext)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        List<string> entries = [];

        switch (annotation)
        {
            case EnvironmentCallbackAnnotation environment:
                Dictionary<string, object> variables = [];
                EnvironmentCallbackContext context = new(
                    executionContext,
                    resource,
                    variables,
                    deadline.Token)
                {
                    Logger = NullLogger.Instance,
                };

                await environment.Callback(context);

                entries.AddRange(variables.Select(pair => $"env {pair.Key}={RenderValue(pair.Value, 0)}"));
                break;

            case CommandLineArgsCallbackAnnotation arguments:
                List<object> values = [];
                CommandLineArgsCallbackContext argumentsContext = new(
                    values,
                    resource,
                    deadline.Token)
                {
                    Logger = NullLogger.Instance,
                    ExecutionContext = executionContext,
                };

                await arguments.Callback(argumentsContext);

                entries.AddRange(values.Select((value, position) => $"arg[{position}] {RenderValue(value, 0)}"));
                break;
        }

        return entries;
    }

    private static string RenderAnnotation(IResourceAnnotation annotation)
    {
        var type = annotation.GetType();
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => (property.Name, Rendered: RenderProperty(annotation, property)))
            .Where(pair => pair.Rendered is not null)
            .Select(pair => $"{pair.Name}={pair.Rendered}");

        return $"{type.Name} {{ {string.Join(", ", properties)} }}";
    }

    private static string? RenderProperty(object owner, PropertyInfo property)
    {
        if (typeof(Delegate).IsAssignableFrom(property.PropertyType))
        {
            return null;
        }

        try
        {
            return RenderValue(property.GetValue(owner), 1);
        }
        catch (TargetInvocationException exception)
        {
            return $"<throws {exception.InnerException?.GetType().Name}>";
        }
    }

    private static string RenderValue(object? value, int depth)
    {
        switch (value)
        {
            case null:
                return "null";
            case string text:
                return $"\"{text}\"";
            case IResource resource:
                return resource is IManifestExpressionProvider named
                    ? $"resource:{resource.Name}({named.ValueExpression})"
                    : $"resource:{resource.Name}";
            case IManifestExpressionProvider provider:
                return provider.ValueExpression;
            case Delegate:
                return "<delegate>";
            case Enum or bool or Guid or TimeSpan or Uri:
                return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }

        if (value.GetType().IsPrimitive)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }

        if (value is IEnumerable sequence && depth < 3)
        {
            return $"[{string.Join(", ", sequence.Cast<object?>().Select(item => RenderValue(item, depth + 1)))}]";
        }

        return value.GetType().Name;
    }

    private static string RepositoryRoot()
    {
        System.IO.DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
