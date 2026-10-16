using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 389 / issue #2999, FR-008. <c>RtspUrl.FromStored</c> and <c>StreamSourceUrl.FromStored</c> read an address
/// back without the rule that refuses a raw '@' after the authority, so that a row stored before the rule stays
/// readable. That is only safe while nothing writes through them. The type system cannot say so (both are public
/// static members of a public type), so this test does: outside the declaring file, the only reference allowed is
/// the owning context's persistence configuration, which uses it as the value converter's read lambda.
///
/// <para>
/// Not vacuous: <see cref="The_two_persistence_configurations_are_the_only_references"/> insists on finding
/// exactly the two allowed references, and <see cref="A_reference_from_a_command_handler_is_a_violation"/> feeds
/// the same scan a handler that calls it. Written to compile before <c>FromStored</c> exists, by scanning source
/// text; until then the first of these is red because the two references are not there yet.
/// </para>
/// </summary>
public class FromStoredCallSiteTests
{
    private static readonly Regex Reference = new(
        @"\b(?<type>RtspUrl|StreamSourceUrl)\s*\.\s*FromStored\b",
        RegexOptions.Compiled);

    private static readonly string[] AllowedPaths =
    [
        "src/CameraCatalog/Domain/Camera/RtspUrl.cs",
        "src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs",
        "src/CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs",
        "src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs",
    ];

    [Fact]
    public void Nothing_else_in_the_source_tree_references_FromStored()
    {
        var violations = Violations(ReadSources());

        violations.ShouldBeEmpty("a validating-free factory reachable from a write path defeats the rule it exists beside");
    }

    [Fact]
    public void The_two_persistence_configurations_are_the_only_references()
    {
        var references = References(ReadSources());

        references.ShouldBe(
            [
                "src/CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs",
                "src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void A_reference_from_a_command_handler_is_a_violation()
    {
        Dictionary<string, string> sources = new()
        {
            ["src/CameraCatalog/Application/Commands/Handlers/ChangeCameraAddressCommandHandler.cs"] =
                "var url = RtspUrl.FromStored(command.Address);",
            ["src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs"] =
                ".HasConversion(url => url.Value, value => StreamSourceUrl.FromStored(value))",
        };

        Violations(sources).ShouldBe(
            ["src/CameraCatalog/Application/Commands/Handlers/ChangeCameraAddressCommandHandler.cs"]);
    }

    private static List<string> Violations(IReadOnlyDictionary<string, string> sources) =>
        [.. References(sources).Where(path => !AllowedPaths.Contains(path, StringComparer.Ordinal))];

    private static List<string> References(IReadOnlyDictionary<string, string> sources) =>
        [.. sources
            .Where(source => CodeLines(source.Value).Any(line => Reference.IsMatch(line)))
            .Where(source => !IsDeclaration(source.Key))
            .Select(source => source.Key)
            .Order(StringComparer.Ordinal)];

    private static bool IsDeclaration(string path) =>
        path.EndsWith("/Domain/Camera/RtspUrl.cs", StringComparison.Ordinal)
        || path.EndsWith("/Domain/Stream/StreamSourceUrl.cs", StringComparison.Ordinal);

    private static IEnumerable<string> CodeLines(string text) =>
        text.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

    private static Dictionary<string, string> ReadSources()
    {
        var root = RepositoryRoot();
        var source = Path.Combine(root.FullName, "src");

        return Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root.FullName, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
            .ToDictionary(path => path, path => File.ReadAllText(Path.Combine(root.FullName, path)), StringComparer.Ordinal);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
