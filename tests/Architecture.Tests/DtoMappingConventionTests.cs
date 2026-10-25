using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 459 / issue #3154 (ADR-0139). A DTO is built from its model in exactly one place: an
/// extension method in <c>src/&lt;Context&gt;/Application/DTOs/&lt;Name&gt;Mappings.cs</c>. A query handler, a command
/// handler or an endpoint that writes <c>new XDto(...)</c> itself, or keeps a private <c>ToDto</c>/<c>Map</c>
/// helper that returns one, puts the mapping where the next handler of the same DTO cannot find it, and
/// the two copies drift apart.
///
/// <para>
/// Reads source rather than metadata: an object creation is syntax and leaves no usable trace in the
/// assembly. Four rules, over every <c>*.cs</c> under <c>src/*/Application/</c> and <c>src/*/Api/</c>:
/// </para>
/// <list type="bullet">
/// <item><description>V1 - <c>new XDto(</c> or <c>new XDto {</c> outside a mappings file.</description></item>
/// <item><description>V2 - a target-typed local, <c>XDto name = new(</c>, outside a mappings file.</description></item>
/// <item><description>V3 - in a file under <c>/Handlers/</c>, a static member whose return type is a bare DTO
/// (<c>ToDto</c>, <c>ToPage</c>, <c>Map*</c>, <c>CredentialsOf</c>). <c>Task&lt;XDto&gt;</c>,
/// <c>Option&lt;XDto&gt;</c> and <c>Result&lt;...&gt;</c> are not matched.</description></item>
/// <item><description>V4 - a <c>*Mappings.cs</c> anywhere in that corpus other than
/// <c>Application/DTOs/</c>, which would otherwise be a rename that dodges V1-V3.</description></item>
/// </list>
///
/// <para>
/// Stated limit: a DTO built by a target-typed <c>new(</c> passed as an argument or placed in a collection
/// (<c>[new(a, b)]</c>) is not seen, because the type name is not written there. V3 covers the helper-method
/// form every current site used. Comments and string-literal interiors are masked
/// (<see cref="MaskStrictness.CommentsAndLiteralInteriors"/>), so prose and message text cannot trip a rule.
/// </para>
///
/// <para>
/// Not vacuous: <see cref="Every_context_that_serves_dtos_has_its_mappings_in_DTOs"/> insists on the exact set of
/// mappings files, <see cref="The_scan_reads_every_context_handler_folder"/> insists the scan reached each
/// context's handlers, and the counterfactual cases feed the same scan a source that breaks each rule.
/// </para>
/// </summary>
public class DtoMappingConventionTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex ExplicitConstruction = new(
        @"\bnew\s+[A-Z]\w*Dto\s*[({]",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex TargetTypedLocal = new(
        @"\b[A-Z]\w*Dto\s+\w+\s*=\s*new\s*\(",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex HandlerMappingHelper = new(
        @"\b(private|internal|public)\s+static\s+[A-Z]\w*Dto\s+\w+\s*\(",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex AllowedMappingsFile = new(
        @"^src/[^/]+/Application/DTOs/[^/]+Mappings\.cs$",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex CorpusPath = new(
        @"^src/[^/]+/(Application|Api)/",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly string[] Contexts =
    [
        "AuditObservability",
        "Automation",
        "CameraCatalog",
        "EventIngestion",
        "Identity",
        "LayoutComposition",
        "OverlayDesigner",
        "StreamDistribution",
        "SystemVariables",
    ];

    private static readonly string[] ExpectedMappingsFiles =
    [
        "src/AuditObservability/Application/DTOs/AuditEventMappings.cs",
        "src/Automation/Application/DTOs/DryRunResultMappings.cs",
        "src/Automation/Application/DTOs/RuleMappings.cs",
        "src/CameraCatalog/Application/DTOs/CameraMappings.cs",
        "src/EventIngestion/Application/DTOs/DeadLetterMappings.cs",
        "src/EventIngestion/Application/DTOs/EventMappings.cs",
        "src/EventIngestion/Application/DTOs/RegisteredEventTypeMappings.cs",
        "src/EventIngestion/Application/DTOs/SourceModeMappings.cs",
        "src/EventIngestion/Application/DTOs/WebhookIntegrationMappings.cs",
        "src/Identity/Application/DTOs/RegisteredClientMappings.cs",
        "src/Identity/Application/DTOs/ReplayedClientMappings.cs",
        "src/LayoutComposition/Application/DTOs/LayoutMappings.cs",
        "src/LayoutComposition/Application/DTOs/WallMappings.cs",
        "src/OverlayDesigner/Application/DTOs/OverlayMappings.cs",
        "src/StreamDistribution/Application/DTOs/StreamMappings.cs",
        "src/SystemVariables/Application/DTOs/PlaceholderResolutionMappings.cs",
        "src/SystemVariables/Application/DTOs/ResolvedTextPairMappings.cs",
        "src/SystemVariables/Application/DTOs/VariableMappings.cs",
    ];

    [Fact]
    public void No_handler_or_endpoint_builds_a_dto()
    {
        var violations = Violations(ReadSources());

        violations.ShouldBeEmpty(
            "a DTO is built only in src/<Context>/Application/DTOs/<Name>Mappings.cs as an extension method:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_context_that_serves_dtos_has_its_mappings_in_DTOs()
    {
        var mappings = ReadSources().Keys
            .Where(path => path.EndsWith("Mappings.cs", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        mappings.ShouldBe(ExpectedMappingsFiles);
    }

    [Fact]
    public void The_scan_reads_every_context_handler_folder()
    {
        var paths = ReadSources().Keys.ToList();

        foreach (var context in Contexts)
        {
            paths.ShouldContain(
                path => path.StartsWith($"src/{context}/Application/", StringComparison.Ordinal)
                    && path.Contains("/Queries/Handlers/", StringComparison.Ordinal),
                $"no query handler under src/{context}/Application/Queries/Handlers/ was scanned - the scan is broken, not the code");
        }
    }

    [Fact]
    public void An_explicit_construction_in_a_query_handler_is_a_violation()
    {
        var violations = Violations(Sources(
            (
                "src/CameraCatalog/Application/Queries/Handlers/GetCameraQueryHandler.cs",
                "return Success(new CameraDto(camera.Id.Value, camera.Name.Value));")));

        violations.ShouldBe(
            ["src/CameraCatalog/Application/Queries/Handlers/GetCameraQueryHandler.cs:1 V1 explicit construction"]);
    }

    [Fact]
    public void A_private_mapping_helper_in_a_handler_is_a_violation()
    {
        var violations = Violations(Sources(
            (
                "src/CameraCatalog/Application/Queries/Handlers/GetCameraQueryHandler.cs",
                "private static CameraDto ToDto(Camera camera) =>\n    new(camera.Id.Value, camera.Name.Value);")));

        violations.ShouldBe(
            ["src/CameraCatalog/Application/Queries/Handlers/GetCameraQueryHandler.cs:1 V3 mapping helper in a handler"]);
    }

    [Fact]
    public void A_target_typed_local_in_a_command_handler_is_a_violation()
    {
        var violations = Violations(Sources(
            (
                "src/Identity/Application/Commands/Handlers/RegisterDeviceCommandHandler.cs",
                "DeviceCredentialsDto credentials = new(clientId, secret);")));

        violations.ShouldBe(
            ["src/Identity/Application/Commands/Handlers/RegisterDeviceCommandHandler.cs:1 V2 target-typed local"]);
    }

    [Fact]
    public void An_endpoint_that_builds_a_dto_is_a_violation()
    {
        var violations = Violations(Sources(
            (
                "src/Identity/Api/DevicesEndpoints.cs",
                "return new DeviceCredentialsDto(replayed.ClientId, replayed.ClientSecret);")));

        violations.ShouldBe(
            ["src/Identity/Api/DevicesEndpoints.cs:1 V1 explicit construction"]);
    }

    [Fact]
    public void The_same_construction_inside_a_mappings_file_in_DTOs_is_allowed()
    {
        var violations = Violations(Sources(
            (
                "src/CameraCatalog/Application/DTOs/CameraMappings.cs",
                "private static CameraDto Build(Camera camera) => new CameraDto(camera.Id.Value);\n"
                + "public static CameraDto ToDto(this Camera camera) => new(camera.Id.Value);")));

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void A_construction_in_a_comment_or_a_string_literal_is_not_a_violation()
    {
        var violations = Violations(Sources(
            (
                "src/CameraCatalog/Application/Queries/Handlers/GetCameraQueryHandler.cs",
                "// new CameraDto(camera.Id.Value)\n"
                + "var text = \"new CameraDto(camera.Id.Value)\";")));

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void A_mappings_file_outside_DTOs_is_a_violation()
    {
        var violations = Violations(Sources(
            (
                "src/CameraCatalog/Application/Queries/Handlers/FooMappings.cs",
                "public static CameraDto ToDto(this Camera camera) => new CameraDto(camera.Id.Value);")));

        violations.ShouldContain(
            "src/CameraCatalog/Application/Queries/Handlers/FooMappings.cs:1 V4 mappings file outside Application/DTOs");
    }

    [Fact]
    public void A_handler_member_returning_a_task_or_a_result_of_a_dto_is_not_a_mapping_helper()
    {
        var violations = Violations(Sources(
            (
                "src/CameraCatalog/Application/Queries/Handlers/GetCameraQueryHandler.cs",
                "public static async Task<CameraDto> Load(int id) => await source.Find(id);\n"
                + "internal static Result<CameraDto, GetCameraError> Resolve(int id) => Find(id);")));

        violations.ShouldBeEmpty();
    }

    private static List<string> Violations(IReadOnlyDictionary<string, string> sources)
    {
        List<string> violations = [];

        foreach (var (path, text) in sources.OrderBy(source => source.Key, StringComparer.Ordinal))
        {
            var isMappingsFile = AllowedMappingsFile.IsMatch(path);
            var masked = SourceMask.Apply(text, MaskStrictness.CommentsAndLiteralInteriors);

            if (path.EndsWith("Mappings.cs", StringComparison.Ordinal) && !isMappingsFile)
            {
                violations.Add($"{path}:1 V4 mappings file outside Application/DTOs");
            }

            if (isMappingsFile)
            {
                continue;
            }

            Add(violations, path, masked, ExplicitConstruction, "V1 explicit construction");
            Add(violations, path, masked, TargetTypedLocal, "V2 target-typed local");

            if (path.Contains("/Handlers/", StringComparison.Ordinal))
            {
                Add(violations, path, masked, HandlerMappingHelper, "V3 mapping helper in a handler");
            }
        }

        return violations;
    }

    private static void Add(List<string> violations, string path, string masked, Regex rule, string description)
    {
        foreach (Match match in rule.Matches(masked))
        {
            var line = masked.AsSpan(0, match.Index).Count('\n') + 1;
            violations.Add($"{path}:{line} {description}");
        }
    }

    private static Dictionary<string, string> Sources(params (string Path, string Text)[] files) =>
        files.ToDictionary(file => file.Path, file => file.Text, StringComparer.Ordinal);

    private static Dictionary<string, string> ReadSources()
    {
        var root = RepositorySource.Root();
        var source = Path.Combine(root.FullName, "src");

        return Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Select(file => RepositorySource.RelativePath(root, file))
            .Where(path => CorpusPath.IsMatch(path))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
            .ToDictionary(path => path, path => File.ReadAllText(Path.Combine(root.FullName, path)), StringComparer.Ordinal);
    }
}
